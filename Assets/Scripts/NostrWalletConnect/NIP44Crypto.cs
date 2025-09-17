using System;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using NBitcoin.Secp256k1;
using HMACSHA256Crypto = System.Security.Cryptography.HMACSHA256;

namespace NostrWalletConnect
{
    public static class NIP44Crypto
    {
        // NIP-44 uses ChaCha20-Poly1305, but .NET doesn't have built-in support
        // We'll implement a simplified version that should work with most NIP-44 implementations

        public static string DecryptNIP44(string payload, string recipientPrivateKey, string senderPublicKey)
        {
            try
            {
                DebugLogger.LogToFile("=== NIP-44 DECRYPTION START ===");
                DebugLogger.LogToFile($"Payload: {payload}");

                // NIP-44 format: version (1 byte) + nonce (32 bytes) + ciphertext + mac (16 bytes)
                // But it's base64 encoded, so we need to decode first

                var payloadBytes = Convert.FromBase64String(payload);
                DebugLogger.LogToFile($"NIP-44 payload length: {payloadBytes.Length} bytes");
                DebugLogger.LogHexData("Full payload", payloadBytes, payloadBytes.Length);

                if (payloadBytes.Length < 65) // minimum: 1 + 32 + 32 = 65 bytes
                {
                    throw new ArgumentException("NIP-44 payload too short");
                }

                var version = payloadBytes[0];
                Debug.Log($"NIP-44 version: {version}");

                if (version != 1 && version != 2)
                {
                    throw new NotSupportedException($"NIP-44 version {version} not supported");
                }

                // Extract components
                var salt = new byte[32];
                Array.Copy(payloadBytes, 1, salt, 0, 32);

                var ciphertextLength = payloadBytes.Length - 1 - 32 - 32; // total - version - salt - mac
                var ciphertext = new byte[ciphertextLength];
                Array.Copy(payloadBytes, 33, ciphertext, 0, ciphertextLength);

                var mac = new byte[32];
                Array.Copy(payloadBytes, payloadBytes.Length - 32, mac, 0, 32);

                DebugLogger.LogHexData("Salt", salt);
                DebugLogger.LogToFile($"Ciphertext length: {ciphertextLength}");
                DebugLogger.LogHexData("Ciphertext", ciphertext, 32);
                DebugLogger.LogHexData("MAC", mac);

                // Compute conversation key using ECDH + HKDF-Extract
                var conversationKey = ComputeNIP44ConversationKey(recipientPrivateKey, senderPublicKey);

                // Derive message keys using HKDF-Expand
                var (enc, nonceKey, auth) = DeriveMessageKeys(conversationKey, salt);

                // Verify HMAC using auth key
                var expectedHmac = ComputeHmac(auth, ciphertext, salt);
                DebugLogger.LogHexData("Expected HMAC", expectedHmac);
                DebugLogger.LogHexData("Received MAC", mac);

                if (!ByteArraysEqual(mac, expectedHmac))
                {
                    DebugLogger.LogErrorToFile("HMAC verification failed - MAC mismatch!");
                    throw new Exception("HMAC verification failed");
                }

                DebugLogger.LogToFile("✅ HMAC verification passed!");

                // Decrypt using ChaCha20
                var paddedPlaintext = ChaCha20Decrypt(enc, nonceKey, ciphertext);
                DebugLogger.LogHexData("Padded plaintext", paddedPlaintext, 64);

                // Unpad the plaintext
                DebugLogger.LogToFile("Attempting to unpad plaintext...");
                var result = UnpadPlaintext(paddedPlaintext);
                DebugLogger.LogToFile($"✅ NIP-44 decryption successful: {result}");
                return result;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"NIP-44 decryption failed: {ex.Message}");
                throw;
            }
        }

        private static byte[] ComputeNIP44ConversationKey(string privateKeyHex, string publicKeyHex)
        {
            try
            {
                Debug.Log($"Computing conversation key from privkey: {privateKeyHex.Substring(0, 8)}... and pubkey: {publicKeyHex.Substring(0, 8)}...");

                var privateKeyBytes = NostrCrypto.HexToBytes(privateKeyHex);
                var publicKeyBytes = NostrCrypto.HexToBytes(publicKeyHex);

                // Handle different public key formats
                ECPubKey pubKey = null;
                if (publicKeyBytes.Length == 33)
                {
                    // Already has prefix (compressed format)
                    pubKey = ECPubKey.Create(publicKeyBytes);
                }
                else if (publicKeyBytes.Length == 32)
                {
                    // X-only public key - need to add prefix
                    // Following nostr-tools approach: try with 0x02 prefix first
                    var fullPubkeyBytes = new byte[33];
                    fullPubkeyBytes[0] = 0x02; // Even y-coordinate
                    Array.Copy(publicKeyBytes, 0, fullPubkeyBytes, 1, 32);

                    try
                    {
                        pubKey = ECPubKey.Create(fullPubkeyBytes);
                        Debug.Log("Using even y-coordinate (0x02 prefix)");
                    }
                    catch
                    {
                        // Try with odd y-coordinate if even fails
                        fullPubkeyBytes[0] = 0x03;
                        pubKey = ECPubKey.Create(fullPubkeyBytes);
                        Debug.Log("Using odd y-coordinate (0x03 prefix)");
                    }
                }
                else
                {
                    throw new ArgumentException($"Invalid public key length: {publicKeyBytes.Length}");
                }

                var privKey = ECPrivKey.Create(privateKeyBytes);

                // Compute ECDH shared secret and extract X coordinate
                // This matches the nostr-tools approach: getSharedSecret(privA, '02' + pubB).subarray(1, 33)
                var sharedPoint = pubKey.GetSharedPubkey(privKey);
                var sharedBytes = sharedPoint.ToBytes(); // This gives us the full compressed point

                // Extract X coordinate (skip the prefix byte, take next 32 bytes)
                var sharedX = new byte[32];
                Array.Copy(sharedBytes, 1, sharedX, 0, 32);
                Debug.Log($"Shared X coordinate ({sharedX.Length} bytes): {BitConverter.ToString(sharedX).Replace("-", "").Substring(0, 16)}...");

                // NIP-44: Use HKDF-Extract with "nip44-v2" salt
                // Following RFC 5869 HKDF-Extract: HMAC-Hash(salt, IKM)
                var salt = Encoding.UTF8.GetBytes("nip44-v2");
                byte[] conversationKey;
                using (var hmac = new HMACSHA256Crypto(salt))
                {
                    conversationKey = hmac.ComputeHash(sharedX);
                }

                Debug.Log($"Computed conversation key: {BitConverter.ToString(conversationKey).Replace("-", "").Substring(0, 16)}...");
                return conversationKey;
            }
            catch (Exception ex)
            {
                Debug.LogError($"NIP-44 conversation key computation failed: {ex.Message}");
                throw;
            }
        }

        private static (byte[] enc, byte[] nonce, byte[] auth) DeriveMessageKeys(byte[] conversationKey, byte[] salt)
        {
            // NIP-44: Use HKDF-Expand with salt as info parameter
            if (conversationKey.Length != 32)
                throw new ArgumentException("Conversation key must be 32 bytes");
            if (salt.Length != 32)
                throw new ArgumentException("Salt must be 32 bytes");

            DebugLogger.LogToFile("=== DERIVING MESSAGE KEYS ===");
            DebugLogger.LogHexData("Conversation key", conversationKey);
            DebugLogger.LogHexData("Salt for key derivation", salt);

            // Use salt as the info parameter for HKDF-Expand
            var keyMaterial = HkdfExpand(conversationKey, salt, 32 + 12 + 32); // 76 bytes total
            DebugLogger.LogHexData("HKDF key material", keyMaterial, keyMaterial.Length);

            var enc = new byte[32];
            var nonce = new byte[12];
            var auth = new byte[32];

            Array.Copy(keyMaterial, 0, enc, 0, 32);
            Array.Copy(keyMaterial, 32, nonce, 0, 12);
            Array.Copy(keyMaterial, 44, auth, 0, 32);

            DebugLogger.LogHexData("Derived enc key", enc);
            DebugLogger.LogHexData("Derived nonce", nonce);
            DebugLogger.LogHexData("Derived auth key", auth);

            return (enc, nonce, auth);
        }

        private static byte[] HkdfExpand(byte[] prk, byte[] info, int length)
        {
            // Simplified HKDF-Expand using HMAC-SHA256
            var hashLength = 32; // SHA256 output length
            var n = (int)Math.Ceiling((double)length / hashLength);
            var okm = new byte[n * hashLength];

            using (var hmac = new HMACSHA256Crypto(prk))
            {
                var t = new byte[0];
                for (int i = 1; i <= n; i++)
                {
                    var input = new byte[t.Length + info.Length + 1];
                    Array.Copy(t, 0, input, 0, t.Length);
                    Array.Copy(info, 0, input, t.Length, info.Length);
                    input[input.Length - 1] = (byte)i;

                    t = hmac.ComputeHash(input);
                    Array.Copy(t, 0, okm, (i - 1) * hashLength, t.Length);
                }
            }

            var result = new byte[length];
            Array.Copy(okm, 0, result, 0, length);
            return result;
        }

        private static byte[] ComputeHmac(byte[] key, byte[] ciphertext, byte[] aad)
        {
            // NIP-44: HMAC(key, aad || ciphertext)
            Debug.Log($"Computing HMAC with key: {BitConverter.ToString(key).Replace("-", "").Substring(0, 16)}...");
            Debug.Log($"AAD (salt) length: {aad.Length}, first bytes: {BitConverter.ToString(aad).Replace("-", "").Substring(0, 16)}...");
            Debug.Log($"Ciphertext length: {ciphertext.Length}, first bytes: {BitConverter.ToString(ciphertext).Replace("-", "").Substring(0, Math.Min(32, ciphertext.Length * 2))}...");

            using (var hmac = new HMACSHA256Crypto(key))
            {
                var input = new byte[aad.Length + ciphertext.Length];
                Array.Copy(aad, 0, input, 0, aad.Length);
                Array.Copy(ciphertext, 0, input, aad.Length, ciphertext.Length);

                var result = hmac.ComputeHash(input);
                Debug.Log($"Computed HMAC: {BitConverter.ToString(result).Replace("-", "")}");
                return result;
            }
        }

        private static bool ByteArraysEqual(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i]) return false;
            }
            return true;
        }

        private static byte[] ChaCha20Decrypt(byte[] key, byte[] nonce, byte[] ciphertext)
        {
            // Use XChaCha20-Poly1305 compatible stream cipher
            return ChaCha20Stream(key, nonce, ciphertext);
        }

        private static byte[] ChaCha20Stream(byte[] key, byte[] nonce, byte[] data)
        {
            // Proper ChaCha20 stream cipher implementation
            var result = new byte[data.Length];

            // ChaCha20 uses 64-byte (16 word) state
            var state = new uint[16];

            // ChaCha20 constants: "expand 32-byte k"
            state[0] = 0x61707865;
            state[1] = 0x3320646e;
            state[2] = 0x79622d32;
            state[3] = 0x6b206574;

            // Key (8 words)
            for (int i = 0; i < 8; i++)
            {
                state[4 + i] = BitConverter.ToUInt32(key, i * 4);
            }

            // Counter (1 word) + Nonce (3 words for ChaCha20)
            state[12] = 0; // Counter starts at 0
            for (int i = 0; i < 3 && i * 4 < nonce.Length; i++)
            {
                if (i * 4 + 4 <= nonce.Length)
                    state[13 + i] = BitConverter.ToUInt32(nonce, i * 4);
                else
                {
                    // Handle partial word at end of nonce
                    uint word = 0;
                    for (int j = 0; j < 4 && i * 4 + j < nonce.Length; j++)
                    {
                        word |= ((uint)nonce[i * 4 + j]) << (j * 8);
                    }
                    state[13 + i] = word;
                }
            }

            // Generate keystream blocks
            for (int blockNum = 0; blockNum * 64 < data.Length; blockNum++)
            {
                state[12] = (uint)blockNum; // Update counter

                var block = ChaCha20Block(state);

                // XOR with data
                int start = blockNum * 64;
                int length = Math.Min(64, data.Length - start);

                for (int i = 0; i < length; i++)
                {
                    result[start + i] = (byte)(data[start + i] ^ block[i]);
                }
            }

            return result;
        }

        private static byte[] ChaCha20Block(uint[] state)
        {
            // Copy state for this block (ChaCha20 doesn't modify original state)
            var work = new uint[16];
            Array.Copy(state, work, 16);

            // ChaCha20 quarter round function
            void QuarterRound(int a, int b, int c, int d)
            {
                work[a] = work[a] + work[b]; work[d] ^= work[a]; work[d] = RotateLeft(work[d], 16);
                work[c] = work[c] + work[d]; work[b] ^= work[c]; work[b] = RotateLeft(work[b], 12);
                work[a] = work[a] + work[b]; work[d] ^= work[a]; work[d] = RotateLeft(work[d], 8);
                work[c] = work[c] + work[d]; work[b] ^= work[c]; work[b] = RotateLeft(work[b], 7);
            }

            // 20 rounds (10 double rounds)
            for (int i = 0; i < 10; i++)
            {
                // Column rounds
                QuarterRound(0, 4, 8, 12);
                QuarterRound(1, 5, 9, 13);
                QuarterRound(2, 6, 10, 14);
                QuarterRound(3, 7, 11, 15);

                // Diagonal rounds
                QuarterRound(0, 5, 10, 15);
                QuarterRound(1, 6, 11, 12);
                QuarterRound(2, 7, 8, 13);
                QuarterRound(3, 4, 9, 14);
            }

            // Add original state
            for (int i = 0; i < 16; i++)
            {
                work[i] += state[i];
            }

            // Convert to byte array (little-endian)
            var result = new byte[64];
            for (int i = 0; i < 16; i++)
            {
                var bytes = BitConverter.GetBytes(work[i]);
                Array.Copy(bytes, 0, result, i * 4, 4);
            }

            return result;
        }

        private static uint RotateLeft(uint value, int bits)
        {
            return (value << bits) | (value >> (32 - bits));
        }

        private static string UnpadPlaintext(byte[] paddedData)
        {
            // NIP-44 padding format: [length:2][data][padding...]
            DebugLogger.LogToFile("=== UNPADDING PLAINTEXT ===");
            DebugLogger.LogToFile($"Padded data length: {paddedData.Length}");
            DebugLogger.LogHexData("First 16 bytes of padded data", paddedData, 16);

            if (paddedData.Length < 2)
            {
                DebugLogger.LogErrorToFile("Padded data too short (< 2 bytes)");
                throw new ArgumentException("Padded data too short");
            }

            // Read length (big-endian)
            int length = (paddedData[0] << 8) | paddedData[1];
            DebugLogger.LogToFile($"Decoded length from first 2 bytes: {length} (0x{length:X4})");
            DebugLogger.LogToFile($"Available data after length bytes: {paddedData.Length - 2}");

            if (length < 1)
            {
                DebugLogger.LogErrorToFile($"Length is too small: {length}");
                throw new ArgumentException($"Invalid padding length: {length} (too small)");
            }

            if (length > paddedData.Length - 2)
            {
                DebugLogger.LogErrorToFile($"Length {length} exceeds available data {paddedData.Length - 2}");
                throw new ArgumentException($"Invalid padding length: {length} exceeds available data {paddedData.Length - 2}");
            }

            var unpadded = new byte[length];
            Array.Copy(paddedData, 2, unpadded, 0, length);

            DebugLogger.LogHexData("Unpadded data", unpadded, Math.Min(32, length));
            var result = Encoding.UTF8.GetString(unpadded);
            DebugLogger.LogToFile($"Unpadded UTF-8 string: {result}");

            return result;
        }

        private static string TryMultipleDecryptionMethods(byte[] ciphertext, byte[] key, byte[] nonce, byte[] expectedMac)
        {
            Exception lastException = null;

            // Method 1: Try improved ChaCha20 simulation first
            try
            {
                Debug.Log("Trying improved ChaCha20 simulation...");
                var result = DecryptWithXOR(ciphertext, key, nonce);

                // Check if result looks like valid JSON/text
                if (IsValidUtf8Text(result))
                {
                    Debug.Log("✅ Improved ChaCha20 simulation succeeded!");
                    return result;
                }
                else
                {
                    Debug.LogWarning("ChaCha20 simulation produced invalid UTF-8");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"❌ Improved ChaCha20 simulation failed: {ex.Message}");
                lastException = ex;
            }

            // Method 2: Try AES-GCM
            try
            {
                Debug.Log("Trying AES-GCM...");
                var result = DecryptWithAESGCM(ciphertext, key, nonce, expectedMac);
                if (IsValidUtf8Text(result))
                {
                    Debug.Log("✅ AES-GCM succeeded!");
                    return result;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"❌ AES-GCM failed: {ex.Message}");
                lastException = ex;
            }

            // Method 3: Try AES-CBC fallback
            try
            {
                Debug.Log("Trying AES-CBC fallback...");
                var result = DecryptWithAESCBC(ciphertext, key, nonce);
                if (IsValidUtf8Text(result))
                {
                    Debug.Log("✅ AES-CBC succeeded!");
                    return result;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"❌ AES-CBC failed: {ex.Message}");
                lastException = ex;
            }

            // Method 4: Try simplified CTR mode
            try
            {
                Debug.Log("Trying simplified CTR mode...");
                var result = DecryptWithSimplifiedCTR(ciphertext, key, nonce);
                if (IsValidUtf8Text(result))
                {
                    Debug.Log("✅ Simplified CTR succeeded!");
                    return result;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"❌ Simplified CTR failed: {ex.Message}");
                lastException = ex;
            }

            throw new Exception($"All NIP-44 decryption methods failed. Last error: {lastException?.Message}");
        }

        private static bool IsValidUtf8Text(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;

            // Check if string contains mostly printable characters or valid JSON structure
            int printableCount = 0;
            int totalCount = text.Length;

            foreach (char c in text)
            {
                if (char.IsControl(c) && c != '\n' && c != '\r' && c != '\t')
                {
                    // Too many control characters indicate garbled text
                    if (totalCount > 10 && printableCount < totalCount * 0.7)
                        return false;
                }
                else
                {
                    printableCount++;
                }
            }

            // Additional check: if it starts with '{' it might be JSON
            return printableCount > totalCount * 0.7 || text.TrimStart().StartsWith("{");
        }

        private static string DecryptWithAESCBC(byte[] ciphertext, byte[] key, byte[] nonce)
        {
            using (var aes = Aes.Create())
            {
                aes.Key = key;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;

                var iv = new byte[16];
                Array.Copy(nonce, 0, iv, 0, Math.Min(16, nonce.Length));
                aes.IV = iv;

                using (var decryptor = aes.CreateDecryptor())
                {
                    var decrypted = decryptor.TransformFinalBlock(ciphertext, 0, ciphertext.Length);
                    return Encoding.UTF8.GetString(decrypted);
                }
            }
        }

        private static string DecryptWithAESGCM(byte[] ciphertext, byte[] key, byte[] nonce, byte[] expectedMac)
        {
            Debug.Log($"Attempting AES decryption with key length: {key.Length}, ciphertext length: {ciphertext.Length}, nonce length: {nonce.Length}");

            try
            {
                // Try AES-GCM if available
                using (var aes = new AesGcm(key))
                {
                    var iv = new byte[12]; // AES-GCM uses 12-byte IV
                    Array.Copy(nonce, 0, iv, 0, Math.Min(12, nonce.Length));

                    var plaintext = new byte[ciphertext.Length];
                    var tag = new byte[16];
                    Array.Copy(expectedMac, 0, tag, 0, Math.Min(16, expectedMac.Length));

                    Debug.Log($"AES-GCM: IV={BitConverter.ToString(iv).Replace("-", "")}, Tag={BitConverter.ToString(tag).Replace("-", "")}");

                    aes.Decrypt(iv, ciphertext, tag, plaintext);
                    var result = Encoding.UTF8.GetString(plaintext);
                    Debug.Log("✅ AES-GCM decryption succeeded!");
                    return result;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"❌ AES-GCM decryption failed: {ex.Message}");
            }

            throw new Exception("AES-GCM not available and all fallback methods failed");
        }

        private static string DecryptWithXOR(byte[] ciphertext, byte[] key, byte[] nonce)
        {
            // Improved ChaCha20-like stream cipher simulation
            var keyStream = new byte[ciphertext.Length];

            // ChaCha20 uses a 16-byte counter + nonce setup
            var chachaInput = new byte[64]; // ChaCha20 block size

            // Initialize ChaCha20-like state (simplified)
            // Constants: "expand 32-byte k"
            var constants = new byte[] { 0x65, 0x78, 0x70, 0x61, 0x6e, 0x64, 0x20, 0x33, 0x32, 0x2d, 0x62, 0x79, 0x74, 0x65, 0x20, 0x6b };
            Array.Copy(constants, 0, chachaInput, 0, 16);

            // Key (32 bytes)
            Array.Copy(key, 0, chachaInput, 16, 32);

            using (var sha256 = System.Security.Cryptography.SHA256.Create())
            {
                for (int blockNum = 0; blockNum * 64 < ciphertext.Length; blockNum++)
                {
                    // Counter (4 bytes) + Nonce (12 bytes)
                    var counter = BitConverter.GetBytes((uint)blockNum);
                    Array.Copy(counter, 0, chachaInput, 48, 4);
                    Array.Copy(nonce, 0, chachaInput, 52, Math.Min(12, nonce.Length));

                    // Generate keystream block using multiple hash rounds (ChaCha20 simulation)
                    var block = chachaInput;
                    for (int round = 0; round < 4; round++) // Simplified from ChaCha20's 20 rounds
                    {
                        block = sha256.ComputeHash(block);
                        if (block.Length < 64)
                        {
                            var extended = new byte[64];
                            Array.Copy(block, extended, block.Length);
                            Array.Copy(chachaInput, block.Length, extended, block.Length, 64 - block.Length);
                            block = extended;
                        }
                    }

                    // Copy keystream block
                    int startIndex = blockNum * 64;
                    int length = Math.Min(64, ciphertext.Length - startIndex);
                    Array.Copy(block, 0, keyStream, startIndex, length);
                }
            }

            var plaintext = new byte[ciphertext.Length];
            for (int i = 0; i < ciphertext.Length; i++)
            {
                plaintext[i] = (byte)(ciphertext[i] ^ keyStream[i]);
            }

            return Encoding.UTF8.GetString(plaintext);
        }

        private static string DecryptWithSimplifiedCTR(byte[] ciphertext, byte[] key, byte[] nonce)
        {
            using (var aes = Aes.Create())
            {
                aes.Key = key;
                aes.Mode = CipherMode.ECB; // Use ECB to manually implement CTR
                aes.Padding = PaddingMode.None;

                var plaintext = new byte[ciphertext.Length];
                var counter = new byte[16];
                Array.Copy(nonce, 0, counter, 0, Math.Min(16, nonce.Length));

                using (var encryptor = aes.CreateEncryptor())
                {
                    for (int i = 0; i < ciphertext.Length; i += 16)
                    {
                        var keyStream = encryptor.TransformFinalBlock(counter, 0, 16);

                        for (int j = 0; j < 16 && i + j < ciphertext.Length; j++)
                        {
                            plaintext[i + j] = (byte)(ciphertext[i + j] ^ keyStream[j]);
                        }

                        // Increment counter
                        for (int k = 15; k >= 0; k--)
                        {
                            if (++counter[k] != 0) break;
                        }
                    }
                }

                return Encoding.UTF8.GetString(plaintext);
            }
        }

        // Simplified NIP-44 encryption for sending requests
        public static string EncryptNIP44(string message, string recipientPublicKey, string senderPrivateKey, int version = 1)
        {
            try
            {
                DebugLogger.LogToFile("=== ATTEMPTING NIP-44 ENCRYPTION ===");
                DebugLogger.LogToFile($"Message length: {message.Length}");
                DebugLogger.LogToFile($"Message preview: {message.Substring(0, Math.Min(50, message.Length))}...");
                DebugLogger.LogToFile($"Recipient pubkey: {recipientPublicKey}");
                DebugLogger.LogToFile($"Sender privkey: {senderPrivateKey.Substring(0, 8)}...");
                Debug.Log("Attempting NIP-44 encryption...");

                // Compute conversation key
                var conversationKey = ComputeNIP44ConversationKey(senderPrivateKey, recipientPublicKey);

                // Generate random salt (32 bytes)
                var salt = new byte[32];
                using (var rng = RandomNumberGenerator.Create())
                {
                    rng.GetBytes(salt);
                }

                // Derive message keys
                var (enc, nonce, auth) = DeriveMessageKeys(conversationKey, salt);

                // Pad the message
                var paddedMessage = PadPlaintext(message);

                // Encrypt with ChaCha20 stream
                var ciphertext = ChaCha20Stream(enc, nonce, paddedMessage);

                // Compute HMAC
                var hmac = ComputeHmac(auth, ciphertext, salt);

                // Construct payload: version + salt + ciphertext + hmac
                var payload = new byte[1 + 32 + ciphertext.Length + 32];
                payload[0] = (byte)version; // use specified version
                DebugLogger.LogToFile($"Using NIP-44 version: {version}");
                Array.Copy(salt, 0, payload, 1, 32);
                Array.Copy(ciphertext, 0, payload, 33, ciphertext.Length);
                Array.Copy(hmac, 0, payload, 33 + ciphertext.Length, 32);

                var result = Convert.ToBase64String(payload);
                DebugLogger.LogToFile($"✅ NIP-44 encryption successful! Result length: {result.Length}");
                DebugLogger.LogToFile($"Encrypted result (first 100 chars): {result.Substring(0, Math.Min(100, result.Length))}...");
                return result;
            }
            catch (Exception ex)
            {
                DebugLogger.LogErrorToFile($"❌ NIP-44 encryption failed: {ex.Message}");
                DebugLogger.LogErrorToFile($"Stack trace: {ex.StackTrace}");
                Debug.LogError($"NIP-44 encryption failed: {ex.Message}");
                throw;
            }
        }

        private static byte[] PadPlaintext(string plaintext)
        {
            var messageBytes = Encoding.UTF8.GetBytes(plaintext);
            var messageLength = messageBytes.Length;

            if (messageLength < 1 || messageLength > 0xFFFF)
                throw new ArgumentException("Message length out of range");

            // NIP-44 official padding: uses powers-of-two algorithm
            // calcPaddedLen should take the message length only (not including 2-byte prefix)
            var paddedLength = CalcPaddedLen(messageLength);
            var totalPaddedLength = 2 + paddedLength; // 2-byte length prefix + padded message

            DebugLogger.LogToFile($"📦 Padding: message={messageLength}B, paddedMessage={paddedLength}B, totalPadded={totalPaddedLength}B");

            // Create padded message: [length:2][message][zero padding...]
            var padded = new byte[totalPaddedLength];
            padded[0] = (byte)(messageLength >> 8);    // length high byte
            padded[1] = (byte)(messageLength & 0xFF);  // length low byte
            Array.Copy(messageBytes, 0, padded, 2, messageLength);
            // Remaining bytes (from messageLength+2 to totalPaddedLength) are already zero-initialized

            return padded;
        }

        /// <summary>
        /// Official NIP-44 calc_padded_len algorithm using powers-of-two
        /// Based on: https://github.com/nbd-wtf/nostr-tools/blob/master/nip44.ts
        /// </summary>
        private static int CalcPaddedLen(int unpaddedLen)
        {
            // Input validation (matching the working TypeScript implementation)
            if (unpaddedLen < 1)
            {
                throw new ArgumentException("Expected positive integer for padded length calculation");
            }

            if (unpaddedLen <= 32)
            {
                return 32; // Minimum padded message size
            }

            // Calculate next power of 2 greater than (unpaddedLen - 1)
            // Using Math.Log(x) / Math.Log(2) since Math.Log2 is not available in Unity's .NET version
            var nextPower = 1 << (int)(Math.Floor(Math.Log(unpaddedLen - 1) / Math.Log(2)) + 1);

            int chunk;
            if (nextPower <= 256)
            {
                chunk = 32;
            }
            else
            {
                chunk = nextPower / 8;
            }

            return chunk * (int)(Math.Floor((double)(unpaddedLen - 1) / chunk) + 1);
        }
    }
}