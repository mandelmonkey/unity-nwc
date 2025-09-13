# Unity Nostr Wallet Connect (NWC) Implementation

A Unity implementation for connecting to Bitcoin Lightning wallets via the Nostr Wallet Connect protocol.

## Table of Contents
- [What is Nostr Wallet Connect?](#what-is-nostr-wallet-connect)
- [High-Level Architecture](#high-level-architecture)
- [How It Works](#how-it-works)
- [Key Components](#key-components)
- [Encryption & Security](#encryption--security)
- [Message Flow](#message-flow)
- [Technical Deep Dive](#technical-deep-dive)
- [Usage Examples](#usage-examples)
- [Troubleshooting](#troubleshooting)

## What is Nostr Wallet Connect?

Nostr Wallet Connect (NWC) is a protocol that allows applications to connect to Lightning wallets remotely and securely. Instead of managing Bitcoin/Lightning keys directly in your app, you connect to an external wallet that handles all the cryptographic operations.

### Key Benefits:
- **Security**: Your app never touches Lightning keys
- **Simplicity**: No need to implement Lightning Network complexity
- **Interoperability**: Works with any NWC-compatible wallet
- **Real-time**: Uses Nostr for instant communication

## High-Level Architecture

```
Unity App  ←→  Nostr Relay  ←→  Lightning Wallet
    ↑              ↑                 ↑
Encrypted      Message         Handles Bitcoin
Requests       Routing         Operations
```

### The Flow:
1. **Unity App** creates Lightning requests (invoices, payments)
2. **Encrypts** them using shared secrets
3. **Sends** via Nostr relay to the wallet
4. **Wallet** processes Lightning operations
5. **Responds** back through the same encrypted channel

## How It Works

### 1. Connection Setup
```
User provides connection string:
nostr+walletconnect://WALLET_PUBKEY?relay=wss://relay.url&secret=SHARED_SECRET
```

This contains:
- **Wallet's public key**: Who to send messages to
- **Relay URL**: Which Nostr relay to use for communication
- **Shared secret**: For encrypting messages between app and wallet

### 2. Encrypted Communication
- **Outgoing (App → Wallet)**: Uses NIP-04 encryption (AES-256-CBC + IV)
- **Incoming (Wallet → App)**: Uses NIP-44 encryption (ChaCha20 + HMAC)
- **Hybrid approach**: Your wallet expects different formats for requests vs responses

### 3. Lightning Operations
Once connected, you can:
- **Create invoices**: `MakeInvoiceAsync(amount, description)`
- **Pay invoices**: `PayInvoiceAsync(bolt11_invoice)`
- **Check balance**: `GetBalanceAsync()`
- **Get wallet info**: `GetInfoAsync()`

## Key Components

### Core Classes

#### `NostrWalletConnect.cs`
The main controller that orchestrates everything:
- Manages WebSocket connections to Nostr relays
- Handles encryption/decryption of messages
- Provides high-level Lightning operation methods
- Manages threading (background networking, main thread UI updates)

#### `NostrWebSocket.cs`
WebSocket client for real-time Nostr communication:
- Handles multi-part message assembly (fixes truncation issues)
- Manages connection lifecycle
- Processes incoming Nostr events

#### `NostrCrypto.cs`
Cryptographic operations:
- **NIP-04 encryption**: For outgoing requests to wallet
- **NIP-44 decryption**: For incoming responses from wallet
- **Key derivation**: ECDH shared secrets, HKDF key expansion
- **Hybrid compatibility**: Supports both encryption standards

#### `NIP44Crypto.cs`
Specialized NIP-44 implementation:
- **ChaCha20 stream cipher**: Proper implementation (not AES simulation)
- **HMAC authentication**: Message integrity verification
- **Padding/unpadding**: Message format compliance

## Encryption & Security

### Why Two Different Encryption Standards?

Your wallet uses a **hybrid approach**:
- **Requests (Unity → Wallet)**: Expects NIP-04 format
- **Responses (Wallet → Unity)**: Sends NIP-44 format

This is wallet-specific behavior that we discovered during development.

### NIP-04 (Outgoing Requests)
```csharp
// Shared secret from ECDH (raw X-coordinate, no hashing)
var sharedSecret = ComputeSharedSecret(walletPubkey, clientPrivateKey);

// AES-256-CBC encryption with random IV
var encrypted = AESEncrypt(message, sharedSecret, randomIV);

// Format: "base64_encrypted_data?iv=base64_iv"
var content = Convert.ToBase64String(encrypted) + "?iv=" + Convert.ToBase64String(iv);
```

### NIP-44 (Incoming Responses)
```csharp
// Same shared secret, different key derivation
var conversationKey = HKDF(sharedSecret, salt="nip44-v2");

// ChaCha20 decryption with HMAC verification
var plaintext = ChaCha20Decrypt(ciphertext, derivedKey, nonce);
var isValid = HMAC_Verify(ciphertext, derivedAuthKey, receivedMAC);
```

## Message Flow

### Making an Invoice Request

1. **User calls** `MakeInvoiceAsync(1000, "Test invoice")`

2. **Request creation**:
   ```json
   {
     "method": "make_invoice",
     "params": {
       "amount": 1000,
       "description": "Test invoice"
     }
   }
   ```

3. **NIP-04 encryption**:
   ```
   plaintext → AES-256-CBC → base64 → "encrypted?iv=random"
   ```

4. **Nostr event creation**:
   ```json
   {
     "kind": 23194,
     "pubkey": "client_pubkey",
     "content": "encrypted_content",
     "tags": [["p", "wallet_pubkey"]]
   }
   ```

5. **WebSocket send** to Nostr relay

6. **Relay forwards** to wallet

7. **Wallet processes** Lightning invoice creation

8. **Wallet responds** with NIP-44 encrypted result

9. **Unity decrypts** and displays result

### Threading Model

- **Background Thread**: WebSocket communication, message receiving
- **Main Thread**: UI updates, user interactions
- **Queue System**: Background thread queues responses for main thread processing

```csharp
// Background thread (WebSocket)
lock (_pendingNWCResponses) {
    _pendingNWCResponses.Enqueue(response);
}

// Main thread (Update loop)
while (_pendingNWCResponses.Count > 0) {
    var response = _pendingNWCResponses.Dequeue();
    HandleNWCResponse(response); // Safe for Unity UI calls
}
```

## Technical Deep Dive

### WebSocket Message Handling

**Challenge**: WebSocket messages can be split into multiple frames for large messages.

**Solution**: Proper multi-frame assembly:
```csharp
do {
    result = await _webSocket.ReceiveAsync(buffer, cancellationToken);
    if (result.MessageType == WebSocketMessageType.Text) {
        messageBuilder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
    }
} while (!result.EndOfMessage); // Keep reading until complete message

var completeMessage = messageBuilder.ToString();
```

### Shared Secret Computation

**Critical detail**: The JavaScript reference implementation uses:
```javascript
let sharedPoint = secp.getSharedSecret(privateKey, '02' + publicKey)
let sharedSecret = sharedPoint.slice(1, 33) // Skip first byte, take next 32
```

**Our C# equivalent**:
```csharp
var sharedPoint = pubKey.GetSharedPubkey(privKey);
var xCoord = sharedPoint.ToXOnlyPubKey().ToBytes(); // 32-byte X coordinate
// No hashing - use raw X coordinate as shared secret
```

### ChaCha20 Implementation

**Not just any stream cipher** - proper ChaCha20 with:
- Quarter-round function with specific bit rotations
- 20 rounds of the quarter-round function
- Proper state matrix initialization
- Block counter increment

```csharp
private static void QuarterRound(uint[] state, int a, int b, int c, int d) {
    state[a] += state[b]; state[d] ^= state[a]; state[d] = RotateLeft(state[d], 16);
    state[c] += state[d]; state[b] ^= state[c]; state[b] = RotateLeft(state[b], 12);
    // ... continue with proper ChaCha20 quarter round
}
```

### Error Handling & Diagnostics

**Comprehensive logging** throughout the pipeline:
- Hex dumps of cryptographic operations
- WebSocket message assembly details
- Event processing flow
- Error context and recovery attempts

**Example debugging output**:
```
[14:43:46] === NIP-44 DECRYPTION START ===
[14:43:46] Payload length: 195 bytes
[14:43:46] Salt (32 bytes): 148FA262BCFA5C94...
[14:43:46] Derived enc key (32 bytes): 8CF203C3870FD9E9...
[14:43:46] ✅ HMAC verification passed!
[14:43:46] ✅ NIP-44 decryption successful
```

## Usage Examples

### Basic Setup
```csharp
public class MyWalletApp : MonoBehaviour {
    [SerializeField] private NostrWalletConnect nwc;

    async void Start() {
        // Connection string from your wallet
        string connectionString = "nostr+walletconnect://...";

        // Set up event handlers
        nwc.OnConnected += () => Debug.Log("Wallet connected!");
        nwc.OnResponse += HandleWalletResponse;

        // Connect
        bool success = await nwc.ConnectAsync(connectionString);
        if (success) {
            Debug.Log("Connected to wallet!");
        }
    }

    void HandleWalletResponse(NWCResponse response) {
        if (response.Error != null) {
            Debug.LogError($"Wallet error: {response.Error.Message}");
        } else {
            Debug.Log($"Success: {JsonConvert.SerializeObject(response.Result)}");
        }
    }
}
```

### Creating Lightning Invoices
```csharp
public async Task CreateInvoice() {
    try {
        await nwc.MakeInvoiceAsync(1000, "Payment for premium features");
        // Response will come through OnResponse event
    } catch (Exception ex) {
        Debug.LogError($"Failed to create invoice: {ex.Message}");
    }
}
```

### Paying Lightning Invoices
```csharp
public async Task PayInvoice(string bolt11Invoice) {
    try {
        await nwc.PayInvoiceAsync(bolt11Invoice);
        // Response will confirm payment status
    } catch (Exception ex) {
        Debug.LogError($"Failed to pay invoice: {ex.Message}");
    }
}
```

## Troubleshooting

### Common Issues

#### "Failed to decrypt" errors
**Cause**: Old cached responses from failed connection attempts
**Solution**: These are normal and will stop once the Nostr relay finishes sending cached events

#### "Unterminated string" JSON errors
**Cause**: WebSocket messages being truncated
**Solution**: Fixed with proper multi-frame message assembly

#### "get_isActiveAndEnabled can only be called from the main thread"
**Cause**: WebSocket callbacks trying to update UI from background thread
**Solution**: Queue system processes responses on main thread in Update()

#### Connection hangs or timeouts
**Cause**: Incorrect encryption preventing wallet from reading requests
**Solution**: Ensure proper NIP-04/NIP-44 hybrid implementation

### Debug Logs

Enable detailed logging in `DebugLogger.cs` to see:
- Complete message encryption/decryption flow
- WebSocket message assembly
- Cryptographic operation details
- Event processing timeline

### Testing Your Implementation

Use the included test functions:
```csharp
[ContextMenu("Test Crypto Functions")]
private void TestCryptoFunctions() {
    // Tests NIP-04 encryption/decryption roundtrip
}

[ContextMenu("Test Connection String Parser")]
private void TestConnectionStringParser() {
    // Validates connection string parsing
}
```

## Architecture Decisions

### Why Hybrid Encryption?
Different wallets implement NWC slightly differently. Some expect NIP-04, others NIP-44. Your specific wallet expects NIP-04 requests but sends NIP-44 responses. This hybrid approach ensures compatibility.

### Why Custom ChaCha20?
Unity doesn't have built-in ChaCha20. Many implementations online are incorrect or use AES as a substitute. We implemented proper ChaCha20 from the specification for authentic NIP-44 support.

### Why Threading Model?
Unity's main thread handles UI and MonoBehaviour operations. WebSocket operations must run on background threads. The queue system safely bridges these two worlds.

### Why Detailed Logging?
Cryptographic protocols are complex and debugging encryption issues requires visibility into every step. The comprehensive logging helps diagnose exactly where issues occur.

---

This implementation provides a complete, production-ready NWC client for Unity that handles the complexities of Nostr communication, hybrid encryption standards, and Unity's threading requirements.