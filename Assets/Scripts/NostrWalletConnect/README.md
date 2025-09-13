# Nostr Wallet Connect (NWC) for Unity

A complete implementation of the Nostr Wallet Connect protocol for Unity, enabling Lightning Network wallet integration in your Unity applications.

## Features

- **NWC Connection String Parsing**: Parse and validate NWC connection strings
- **Cryptographic Functions**: secp256k1 key generation, NIP-04 encryption/decryption
- **WebSocket Communication**: Reliable Nostr relay communication
- **Protocol Implementation**: Full NWC protocol support including:
  - `pay_invoice` - Pay Lightning invoices
  - `make_invoice` - Create Lightning invoices
  - `get_balance` - Get wallet balance
  - `get_info` - Get wallet information
- **Unity Integration**: MonoBehaviour-based with Inspector support
- **Example UI**: Complete example implementation with UI

## Quick Start

1. **Add the NWC Component**: Add the `NostrWalletConnect` component to a GameObject in your scene

2. **Set Connection String**: Enter your NWC connection string in the format:
   ```
   nostr+walletconnect://WALLET_PUBKEY?relay=wss://relay.example.com&secret=SECRET_KEY
   ```

3. **Connect Programmatically**:
   ```csharp
   var nwc = GetComponent<NostrWalletConnect>();
   bool success = await nwc.ConnectAsync("your_connection_string_here");
   ```

4. **Use Wallet Functions**:
   ```csharp
   // Get wallet balance
   var balanceResponse = await nwc.GetBalanceAsync();

   // Create an invoice
   var invoiceResponse = await nwc.MakeInvoiceAsync(1000, "Test invoice");

   // Pay an invoice
   var paymentResponse = await nwc.PayInvoiceAsync("lnbc...");
   ```

## Classes Overview

### NostrWalletConnect (Main Class)
The primary MonoBehaviour that handles NWC connections and operations.

### NWCConnectionString
Parses and validates NWC connection strings.

### NostrCrypto
Handles all cryptographic operations including key generation, encryption, and signing.

### NostrWebSocket
Manages WebSocket connections to Nostr relays.

### NWCProtocol
Implements the NWC protocol message formats and methods.

### NWCExample
Complete example implementation with UI components.

## Dependencies

- Unity 2022.3 or later
- Newtonsoft.Json (automatically added to manifest.json)

## Connection String Format

```
nostr+walletconnect://[wallet_pubkey]?relay=[relay_url]&secret=[secret_key]&lnurlp=[lnurl_pay_url]
```

- `wallet_pubkey`: 64-character hex string (32 bytes)
- `relay_url`: WebSocket URL of the Nostr relay
- `secret_key`: 64-character hex string (32 bytes) for encryption
- `lnurlp`: (Optional) LNURL-pay URL

## Error Handling

All async methods throw exceptions on errors. The NWCResponse object also contains error information:

```csharp
try {
    var response = await nwc.GetBalanceAsync();
    if (response.Error != null) {
        Debug.LogError($"Error: {response.Error.Code} - {response.Error.Message}");
    } else {
        Debug.Log($"Balance: {response.Result["balance"]}");
    }
} catch (Exception ex) {
    Debug.LogError($"Request failed: {ex.Message}");
}
```

## Events

The NostrWalletConnect class provides several events for monitoring connection status:

```csharp
nwc.OnConnected += () => Debug.Log("Connected to wallet");
nwc.OnDisconnected += () => Debug.Log("Disconnected from wallet");
nwc.OnError += (error) => Debug.LogError($"Error: {error}");
nwc.OnResponse += (response) => Debug.Log("Received response");
```

## Inspector Methods

The NostrWalletConnect component includes context menu methods for testing:

- **Connect**: Connect using the connection string from the Inspector
- **Disconnect**: Disconnect from the wallet
- **Get Info**: Test getting wallet information
- **Get Balance**: Test getting wallet balance

## Example Usage

See the `NWCExample` class for a complete UI implementation showing how to:
- Connect/disconnect to/from wallets
- Create and pay invoices
- Check wallet balance and info
- Handle errors and responses

## Security Notes

- Private keys are generated locally and never transmitted
- All communication is encrypted using NIP-04 encryption
- Connection strings contain sensitive information and should be handled securely
- This is a client-side implementation and requires a compatible NWC wallet service

## Supported NWC Methods

- ✅ `pay_invoice` - Pay Lightning invoices
- ✅ `make_invoice` - Create Lightning invoices
- ✅ `get_balance` - Get wallet balance
- ✅ `get_info` - Get wallet information
- ❌ `list_transactions` - Not yet implemented
- ❌ `lookup_invoice` - Not yet implemented
- ❌ `multi_pay_invoice` - Not yet implemented
- ❌ `multi_pay_keysend` - Not yet implemented
- ❌ `sign_message` - Not yet implemented

## License

This implementation follows the NIP-47 specification and is provided as-is for educational and development purposes.