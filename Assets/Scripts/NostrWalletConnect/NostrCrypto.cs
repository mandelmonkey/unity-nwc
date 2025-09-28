using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using NBitcoin.Secp256k1;
using SHA256Hash = System.Security.Cryptography.SHA256;

namespace NostrWalletConnect
{
    public static class NostrCrypto
    {
        public static string GeneratePrivateKey()
        {
            byte[] privateKey = new byte[32];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(privateKey);
            }
            return BitConverter.ToString(privateKey).Replace("-", "").ToLower();
        }

        public static string GetPublicKey(string privateKeyHex)
        {
            try
            {
                var privateKeyBytes = HexToBytes(privateKeyHex);
                if (privateKeyBytes.Length != 32)
                {
                    throw new ArgumentException("Private key must be 32 bytes");
                }

                var privKey = ECPrivKey.Create(privateKeyBytes);
                var pubKey = privKey.CreatePubKey();
                var xOnlyPubKey = pubKey.ToXOnlyPubKey();

                return BitConverter.ToString(xOnlyPubKey.ToBytes()).Replace("-", "").ToLower();
            }
            catch (Exception ex)
            {
                DebugLogger.LogErrorToFile($"Error generating public key: {ex.Message}");
                throw;
            }
        }


        private static int? _preferredNip44Version = null;
        private static bool _useNip04Only = false;

        public static void SetPreferredNip44Version(int version)
        {
            _preferredNip44Version = version;
            _useNip04Only = false;
            DebugLogger.LogToFile($"🔧 Set preferred NIP-44 version to: v{version}");
        }

        public static void ForceNip04Only()
        {
            _useNip04Only = true;
            DebugLogger.LogToFile($"🔧 Forcing NIP-04 only mode - wallet doesn't support NIP-44");
        }
 
        public static string GetCurrentEncryptionTag()
        {
            if (_useNip04Only)
            {
                return "nip04";
            }
            else if (_preferredNip44Version.HasValue)
            {
                return _preferredNip44Version.Value == 2 ? "nip44_v2" : "nip44";
            }
            else
            {
                // Hybrid mode - default to nip44_v2 for outgoing
                return "nip44_v2";
            }
        }

        /// <summary>
        /// Encrypts a message using the detected encryption standard from wallet info
        /// </summary>
        public static string EncryptForWallet(string message, string recipientPubkey, string senderPrivateKey)
        {
            if (_useNip04Only)
            {
                DebugLogger.LogToFile("🔐 Using NIP-04 encryption (wallet preference)");
                return EncryptNIP04Pure(message, recipientPubkey, senderPrivateKey);
            }
            else if (_preferredNip44Version != null)
            {
                DebugLogger.LogToFile($"🔐 Using NIP-44 v{_preferredNip44Version} encryption (wallet preference)");
                return NIP44Crypto.EncryptNIP44(message, recipientPubkey, senderPrivateKey, _preferredNip44Version.Value);
            }
            else
            {
                // Hybrid mode - try NIP-44 first, fallback to NIP-04
                DebugLogger.LogToFile("🔐 Using hybrid mode - trying NIP-44 first, fallback to NIP-04");
                return EncryptNIP04(message, recipientPubkey, senderPrivateKey); // Keep existing fallback logic
            }
        }

        /// <summary>
        /// Decrypts a message using auto-detection or the known encryption standard
        /// </summary>
        public static string DecryptFromWallet(string encryptedMessage, string senderPubkey, string recipientPrivateKey)
        {
            return DecryptNIP04(encryptedMessage, senderPubkey, recipientPrivateKey); // Keep existing auto-detection logic
        }

        /// <summary>
        /// Pure NIP-04 encryption without fallbacks
        /// </summary>
        public static string EncryptNIP04Pure(string message, string recipientPubkey, string senderPrivateKey)
        {
            DebugLogger.LogToFile("💡 Using pure NIP-04 encryption...");
            var sharedSecret = ComputeSharedSecret(recipientPubkey, senderPrivateKey);
            var iv = GenerateIV();
            DebugLogger.LogHexData("NIP-04 shared secret", sharedSecret);
            DebugLogger.LogHexData("NIP-04 IV", iv);
            var encrypted = AESEncrypt(message, sharedSecret, iv);
            DebugLogger.LogHexData("NIP-04 encrypted data", encrypted);
            var combined = Convert.ToBase64String(encrypted) + "?iv=" + Convert.ToBase64String(iv);
            DebugLogger.LogToFile($"✅ NIP-04 encryption complete: {combined.Substring(0, Math.Min(100, combined.Length))}...");
            return combined;
        }

        public static string EncryptNIP04(string message, string recipientPubkey, string senderPrivateKey)
        {
            try
            {
                // Check if we should skip NIP-44 entirely
                if (_useNip04Only)
                {
                    DebugLogger.LogToFile("💡 Using NIP-04 only (wallet doesn't support NIP-44)...");
                    DebugLogger.LogToFile("Using NIP-04 (legacy) encryption for outgoing message...");
                    var nip04SharedSecret = ComputeSharedSecret(recipientPubkey, senderPrivateKey);
                    var nip04Iv = GenerateIV();
                    DebugLogger.LogHexData("NIP-04 shared secret", nip04SharedSecret);
                    DebugLogger.LogHexData("NIP-04 IV", nip04Iv);
                    var nip04Encrypted = AESEncrypt(message, nip04SharedSecret, nip04Iv);
                    DebugLogger.LogHexData("NIP-04 encrypted data", nip04Encrypted);
                    var nip04Combined = Convert.ToBase64String(nip04Encrypted) + "?iv=" + Convert.ToBase64String(nip04Iv);
                    DebugLogger.LogToFile($"✅ NIP-04 encryption complete: {nip04Combined.Substring(0, Math.Min(100, nip04Combined.Length))}...");
                    DebugLogger.LogToFile($"NIP-04 result length: {nip04Combined.Length}");

                    // Validate for JSON safety
                    var hasControlChars = nip04Combined.Any(c => char.IsControl(c) && c != '\t' && c != '\n' && c != '\r');
                    if (hasControlChars)
                    {
                        DebugLogger.LogToFile("⚠️ WARNING: NIP-04 result contains control characters that may break JSON");
                    }

                    // Test JSON serialization
                    try
                    {
                        var testJson = Newtonsoft.Json.JsonConvert.SerializeObject(nip04Combined);
                        DebugLogger.LogToFile($"✅ NIP-04 result JSON serialization test passed: {testJson.Length} chars");
                    }
                    catch (Exception jsonEx)
                    {
                        DebugLogger.LogToFile($"❌ NIP-04 result failed JSON serialization test: {jsonEx.Message}");
                    }

                    return nip04Combined;
                }

                // Try NIP-44 first (modern encryption)
                try
                {
                    DebugLogger.LogToFile($"💡 Trying NIP-44 encryption for outgoing message (preferred version: {_preferredNip44Version ?? 1})...");
                    DebugLogger.LogToFile("Using NIP-44 encryption for outgoing message...");
                    var result = NIP44Crypto.EncryptNIP44(message, recipientPubkey, senderPrivateKey, _preferredNip44Version ?? 1);
                    DebugLogger.LogToFile($"✅ NIP-44 encryption succeeded, returning result");
                    return result;
                }
                catch (Exception nip44Ex)
                {
                    DebugLogger.LogErrorToFile($"❌ NIP-44 encryption failed: {nip44Ex.Message}");
                    DebugLogger.LogErrorToFile($"Stack trace: {nip44Ex.StackTrace}");
                    DebugLogger.LogWarningToFile($"NIP-44 encryption failed: {nip44Ex.Message}, falling back to NIP-04...");
                }

                // Fall back to NIP-04 (legacy)
                DebugLogger.LogToFile("⚠️ Falling back to NIP-04 (legacy) encryption...");
                DebugLogger.LogToFile("Using NIP-04 (legacy) encryption for outgoing message...");
                var sharedSecret = ComputeSharedSecret(recipientPubkey, senderPrivateKey);
                var iv = GenerateIV();
                var encrypted = AESEncrypt(message, sharedSecret, iv);
                var combined = Convert.ToBase64String(encrypted) + "?iv=" + Convert.ToBase64String(iv);
                return combined;
            }
            catch (Exception ex)
            {
                DebugLogger.LogErrorToFile($"Encryption error: {ex.Message}");
                throw;
            }
        }

        public static string DecryptNIP04(string encryptedMessage, string senderPubkey, string recipientPrivateKey)
        {
            try
            {
                DebugLogger.Initialize();
                DebugLogger.LogSeparator();
                DebugLogger.LogToFile("STARTING NIP-04/44 DECRYPTION ATTEMPT");
                DebugLogger.LogToFile($"Sender pubkey: {senderPubkey}");
                DebugLogger.LogToFile($"Recipient privkey (first 8 chars): {recipientPrivateKey.Substring(0, 8)}...");
                DebugLogger.LogToFile($"Encrypted message length: {encryptedMessage.Length}");
                DebugLogger.LogToFile($"Encrypted message (first 100 chars): {encryptedMessage.Substring(0, Math.Min(100, encryptedMessage.Length))}...");

                // First detect the encryption format by examining the message structure
                var encryptionFormat = DetectEncryptionFormat(encryptedMessage);
                DebugLogger.LogToFile($"Detected encryption format: {encryptionFormat}");

                if (encryptionFormat == "NIP-44")
                {
                    try
                    {
                        DebugLogger.LogToFile("Attempting NIP-44 decryption...");
                        var nip44Result = NIP44Crypto.DecryptNIP44(encryptedMessage, recipientPrivateKey, senderPubkey);
                        DebugLogger.LogToFile("✅ NIP-44 decryption succeeded!");
                        DebugLogger.LogToFile($"Decrypted result: {nip44Result}");
                        return nip44Result;
                    }
                    catch (Exception nip44Ex)
                    {
                        DebugLogger.LogErrorToFile($"NIP-44 decryption failed: {nip44Ex.Message}");
                        DebugLogger.LogToFile("Falling back to NIP-04 (legacy)...");
                    }
                }
                else
                {
                    DebugLogger.LogToFile("Message appears to be NIP-04 format, skipping NIP-44 attempt...");
                }

                // Fall back to NIP-04 (legacy encryption)
                DebugLogger.LogToFile("Attempting NIP-04 (legacy) decryption...");
                var sharedSecret = ComputeSharedSecret(senderPubkey, recipientPrivateKey);
                DebugLogger.LogToFile($"Computed shared secret: {BitConverter.ToString(sharedSecret).Replace("-", "").ToLower().Substring(0, 16)}...");

                // Try standard format first: "encrypted?iv=base64iv"
                var parts = encryptedMessage.Split(new[] { "?iv=" }, StringSplitOptions.None);
                if (parts.Length == 2)
                {
                    DebugLogger.LogToFile("Found standard NIP-04 format with ?iv= separator");
                    var encrypted = Convert.FromBase64String(parts[0]);
                    var iv = Convert.FromBase64String(parts[1]);
                    return AESDecrypt(encrypted, sharedSecret, iv);
                }

                // Try alternative format: IV might be embedded in the base64 data
                DebugLogger.LogWarningToFile("Standard NIP-04 format not detected, trying alternative decryption methods...");

                var encryptedBytes = Convert.FromBase64String(encryptedMessage);
                DebugLogger.LogToFile($"Decoded base64 data length: {encryptedBytes.Length} bytes");

                // Method 1: First 16 bytes are IV, rest is encrypted data
                if (encryptedBytes.Length > 16)
                {
                    DebugLogger.LogToFile("Trying Method 1: IV prefix");
                    var iv1 = new byte[16];
                    var encrypted1 = new byte[encryptedBytes.Length - 16];
                    Array.Copy(encryptedBytes, 0, iv1, 0, 16);
                    Array.Copy(encryptedBytes, 16, encrypted1, 0, encrypted1.Length);

                    try
                    {
                        var result = AESDecrypt(encrypted1, sharedSecret, iv1);
                        DebugLogger.LogToFile("Method 1 succeeded!");
                        return result;
                    }
                    catch (Exception ex1)
                    {
                        DebugLogger.LogWarningToFile($"Method 1 (IV prefix) failed: {ex1.Message}");
                    }
                }

                // Method 2: Last 16 bytes are IV, rest is encrypted data
                if (encryptedBytes.Length > 16)
                {
                    DebugLogger.LogToFile("Trying Method 2: IV suffix");
                    var encrypted2 = new byte[encryptedBytes.Length - 16];
                    var iv2 = new byte[16];
                    Array.Copy(encryptedBytes, 0, encrypted2, 0, encrypted2.Length);
                    Array.Copy(encryptedBytes, encryptedBytes.Length - 16, iv2, 0, 16);

                    try
                    {
                        var result = AESDecrypt(encrypted2, sharedSecret, iv2);
                        DebugLogger.LogToFile("Method 2 succeeded!");
                        return result;
                    }
                    catch (Exception ex2)
                    {
                        DebugLogger.LogWarningToFile($"Method 2 (IV suffix) failed: {ex2.Message}");
                    }
                }

                // Method 3: Try with zero IV (some implementations might not use random IV)
                DebugLogger.LogToFile("Trying Method 3: Zero IV");
                try
                {
                    var zeroIV = new byte[16];
                    var result = AESDecrypt(encryptedBytes, sharedSecret, zeroIV);
                    DebugLogger.LogToFile("Method 3 succeeded!");
                    return result;
                }
                catch (Exception ex3)
                {
                    DebugLogger.LogWarningToFile($"Method 3 (zero IV) failed: {ex3.Message}");
                }

                // Method 4: Try raw decryption with shared secret as key directly
                DebugLogger.LogToFile("Trying Method 4: Direct decryption without IV");
                try
                {
                    using (var aes = Aes.Create())
                    {
                        aes.Key = sharedSecret;
                        aes.Mode = CipherMode.ECB; // Try ECB mode
                        aes.Padding = PaddingMode.PKCS7;

                        using (var decryptor = aes.CreateDecryptor())
                        {
                            var decrypted = decryptor.TransformFinalBlock(encryptedBytes, 0, encryptedBytes.Length);
                            var result = Encoding.UTF8.GetString(decrypted);
                            DebugLogger.LogToFile("Method 4 succeeded!");
                            return result;
                        }
                    }
                }
                catch (Exception ex4)
                {
                    DebugLogger.LogWarningToFile($"Method 4 (ECB mode) failed: {ex4.Message}");
                }

                DebugLogger.LogErrorToFile("All decryption methods failed");
                throw new ArgumentException("Could not decrypt message with any known NIP-04 format");
            }
            catch (Exception ex)
            {
                DebugLogger.LogErrorToFile($"NIP-04 decryption error: {ex.Message}");
                throw;
            }
        }

        private static byte[] ComputeSharedSecret(string pubkey, string privateKey)
        {
            try
            {
                DebugLogger.LogToFile($"Computing NIP-04 shared secret");
                DebugLogger.LogToFile($"Pubkey: {pubkey}");
                DebugLogger.LogToFile($"Privkey (first 8 chars): {privateKey.Substring(0, 8)}...");

                var pubkeyBytes = HexToBytes(pubkey);
                var privateKeyBytes = HexToBytes(privateKey);

                DebugLogger.LogHexData("Pubkey bytes", pubkeyBytes);
                DebugLogger.LogHexData("Privkey bytes", privateKeyBytes);

                if (pubkeyBytes.Length != 32 || privateKeyBytes.Length != 32)
                {
                    throw new ArgumentException("Invalid key lengths");
                }

                var privKey = ECPrivKey.Create(privateKeyBytes);

                // Create a full pubkey from x-only by trying both even and odd y coordinates
                ECPubKey pubKey = null;
                try
                {
                    // Try to create pubkey assuming even y coordinate (0x02 prefix)
                    var fullPubkeyBytes = new byte[33];
                    fullPubkeyBytes[0] = 0x02;
                    Array.Copy(pubkeyBytes, 0, fullPubkeyBytes, 1, 32);
                    pubKey = ECPubKey.Create(fullPubkeyBytes);
                    DebugLogger.LogToFile("NIP-04: Using even y-coordinate (0x02 prefix)");
                }
                catch
                {
                    try
                    {
                        // Try odd y coordinate (0x03 prefix)
                        var fullPubkeyBytes = new byte[33];
                        fullPubkeyBytes[0] = 0x03;
                        Array.Copy(pubkeyBytes, 0, fullPubkeyBytes, 1, 32);
                        pubKey = ECPubKey.Create(fullPubkeyBytes);
                        DebugLogger.LogToFile("NIP-04: Using odd y-coordinate (0x03 prefix)");
                    }
                    catch
                    {
                        throw new Exception("Could not reconstruct public key from x-coordinate");
                    }
                }

                // Compute ECDH shared secret - use the X coordinate only (standard for NIP-04)
                var sharedPoint = pubKey.GetSharedPubkey(privKey);

                // Log both compressed and X-only formats to compare with JS reference
                var compressedPoint = sharedPoint.ToBytes(true); // Compressed format (33 bytes with prefix)
                var xCoord = sharedPoint.ToXOnlyPubKey().ToBytes(); // 32-byte X coordinate

                DebugLogger.LogHexData("Shared point (compressed, 33 bytes)", compressedPoint);
                DebugLogger.LogHexData("ECDH X-coordinate (32 bytes)", xCoord);

                // Try the JavaScript approach: skip first byte of compressed point
                if (compressedPoint.Length == 33)
                {
                    var jsStyleSecret = new byte[32];
                    Array.Copy(compressedPoint, 1, jsStyleSecret, 0, 32);
                    DebugLogger.LogHexData("JS-style secret (bytes 1-32 of compressed)", jsStyleSecret);

                    // Compare if they're the same
                    bool areSame = System.Linq.Enumerable.SequenceEqual(xCoord, jsStyleSecret);
                    DebugLogger.LogToFile($"X-coord vs JS-style match: {areSame}");

                    if (!areSame)
                    {
                        DebugLogger.LogToFile("🔍 Using JS-style secret (bytes 1-32) instead of direct X-coordinate");
                        return jsStyleSecret;
                    }
                }

                // NIP-04 uses the raw X coordinate without hashing (unlike some ECDH implementations)
                DebugLogger.LogToFile("Using raw X-coordinate as shared secret for NIP-04 (no hashing)");
                return xCoord;
            }
            catch (Exception ex)
            {
                DebugLogger.LogErrorToFile($"ECDH error, trying alternative approaches: {ex.Message}");

                // Alternative approach 1: Use the shared secret directly without hashing
                try
                {
                    var pubkeyBytes = HexToBytes(pubkey);
                    var privateKeyBytes = HexToBytes(privateKey);
                    var privKey = ECPrivKey.Create(privateKeyBytes);

                    ECPubKey pubKey = null;
                    try
                    {
                        var fullPubkeyBytes = new byte[33];
                        fullPubkeyBytes[0] = 0x02;
                        Array.Copy(pubkeyBytes, 0, fullPubkeyBytes, 1, 32);
                        pubKey = ECPubKey.Create(fullPubkeyBytes);
                    }
                    catch
                    {
                        var fullPubkeyBytes = new byte[33];
                        fullPubkeyBytes[0] = 0x03;
                        Array.Copy(pubkeyBytes, 0, fullPubkeyBytes, 1, 32);
                        pubKey = ECPubKey.Create(fullPubkeyBytes);
                    }

                    var sharedPoint = pubKey.GetSharedPubkey(privKey);
                    var xCoord = sharedPoint.ToXOnlyPubKey().ToBytes();

                    DebugLogger.LogToFile($"Alternative: Direct X-coordinate as shared secret: {BitConverter.ToString(xCoord).Replace("-", "").Substring(0, 16)}...");
                    return xCoord; // Return unhashed
                }
                catch (Exception ex2)
                {
                    DebugLogger.LogErrorToFile($"Alternative ECDH also failed: {ex2.Message}, falling back to simple method");

                    // Fallback to simple hash-based approach
                    var pubkeyBytes = HexToBytes(pubkey);
                    var privateKeyBytes = HexToBytes(privateKey);

                    using (var sha256 = SHA256Hash.Create())
                    {
                        var combined = new byte[pubkeyBytes.Length + privateKeyBytes.Length];
                        Array.Copy(pubkeyBytes, 0, combined, 0, pubkeyBytes.Length);
                        Array.Copy(privateKeyBytes, 0, combined, pubkeyBytes.Length, privateKeyBytes.Length);
                        var fallbackSecret = sha256.ComputeHash(combined);
                        DebugLogger.LogToFile($"Fallback shared secret: {BitConverter.ToString(fallbackSecret).Replace("-", "").Substring(0, 16)}...");
                        return fallbackSecret;
                    }
                }
            }
        }

        private static byte[] GenerateIV()
        {
            var iv = new byte[16];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(iv);
            }
            return iv;
        }

        private static byte[] AESEncrypt(string message, byte[] key, byte[] iv)
        {
            using (var aes = Aes.Create())
            {
                aes.Key = key;
                aes.IV = iv;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;

                using (var encryptor = aes.CreateEncryptor())
                {
                    var messageBytes = Encoding.UTF8.GetBytes(message);
                    return encryptor.TransformFinalBlock(messageBytes, 0, messageBytes.Length);
                }
            }
        }

        private static string AESDecrypt(byte[] encrypted, byte[] key, byte[] iv)
        {
            using (var aes = Aes.Create())
            {
                aes.Key = key;
                aes.IV = iv;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;

                using (var decryptor = aes.CreateDecryptor())
                {
                    var decrypted = decryptor.TransformFinalBlock(encrypted, 0, encrypted.Length);
                    return Encoding.UTF8.GetString(decrypted);
                }
            }
        }

        public static string CreateEventId(string eventJson)
        {
            using (var sha256 = SHA256Hash.Create())
            {
                var bytes = Encoding.UTF8.GetBytes(eventJson);
                var hash = sha256.ComputeHash(bytes);
                return BitConverter.ToString(hash).Replace("-", "").ToLower();
            }
        }

        public static string SignEvent(string eventId, string privateKey)
        {
            try
            {
                var eventIdBytes = HexToBytes(eventId);
                var privateKeyBytes = HexToBytes(privateKey);

                if (eventIdBytes.Length != 32)
                {
                    throw new ArgumentException("Event ID must be 32 bytes");
                }

                if (privateKeyBytes.Length != 32)
                {
                    throw new ArgumentException("Private key must be 32 bytes");
                }

                var privKey = ECPrivKey.Create(privateKeyBytes);

                // Generate auxiliary randomness for BIP-340 (recommended)
                var auxRand32 = new byte[32];
                using (var rng = RandomNumberGenerator.Create())
                {
                    rng.GetBytes(auxRand32);
                }

                // Sign with BIP-340 Schnorr signature
                var signature = privKey.SignBIP340(eventIdBytes, auxRand32);

                return BitConverter.ToString(signature.ToBytes()).Replace("-", "").ToLower();
            }
            catch (Exception ex)
            {
                DebugLogger.LogErrorToFile($"Event signing error: {ex.Message}");
                throw;
            }
        }

        public static byte[] HexToBytes(string hex)
        {
            if (hex.Length % 2 == 1)
                throw new ArgumentException("Invalid hex string length");

            var bytes = new byte[hex.Length / 2];
            for (int i = 0; i < hex.Length; i += 2)
            {
                bytes[i / 2] = Convert.ToByte(hex.Substring(i, 2), 16);
            }
            return bytes;
        }

        public static string BytesToHex(byte[] bytes)
        {
            return BitConverter.ToString(bytes).Replace("-", "").ToLower();
        }

        private static string DetectEncryptionFormat(string encryptedMessage)
        {
            try
            {
                // NIP-44 detection: Try to decode as base64 and check format
                var decoded = Convert.FromBase64String(encryptedMessage);

                // NIP-44 format: version(1) + salt(32) + ciphertext + mac(32)
                // Minimum length: 1 + 32 + 1 + 32 = 66 bytes
                if (decoded.Length >= 66)
                {
                    var version = decoded[0];
                    if (version == 2) // NIP-44 v2
                    {
                        DebugLogger.LogToFile($"Detected NIP-44 v{version} format (payload length: {decoded.Length})");
                        return "NIP-44";
                    }
                }

                // NIP-04 detection: Look for "?iv=" pattern or try base64 decode
                if (encryptedMessage.Contains("?iv="))
                {
                    DebugLogger.LogToFile("Detected NIP-04 format (contains ?iv= separator)");
                    return "NIP-04";
                }

                // If it's short base64 without version byte, likely NIP-04
                if (decoded.Length < 66)
                {
                    DebugLogger.LogToFile($"Detected likely NIP-04 format (short payload length: {decoded.Length})");
                    return "NIP-04";
                }

                DebugLogger.LogToFile("Format unclear, defaulting to NIP-04");
                return "NIP-04";
            }
            catch
            {
                // If base64 decode fails, probably NIP-04 with ?iv= format
                DebugLogger.LogToFile("Base64 decode failed, assuming NIP-04");
                return "NIP-04";
            }
        }

        public static bool VerifySignature(string eventId, string signature, string publicKey)
        {
            try
            {
                var eventIdBytes = HexToBytes(eventId);
                var signatureBytes = HexToBytes(signature);
                var publicKeyBytes = HexToBytes(publicKey);

                if (eventIdBytes.Length != 32 || signatureBytes.Length != 64 || publicKeyBytes.Length != 32)
                {
                    return false;
                }

                var xOnlyPubKey = ECXOnlyPubKey.Create(publicKeyBytes);

                // Try different ways to create the signature based on NBitcoin.Secp256k1 API
                try
                {
                    if (SecpSchnorrSignature.TryCreate(signatureBytes, out var schnorrSig))
                    {
                        return xOnlyPubKey.SigVerifyBIP340(schnorrSig, eventIdBytes);
                    }
                    else
                    {
                        return false;
                    }
                }
                catch
                {
                    // Fallback: if signature parsing fails, return false
                    return false;
                }
            }
            catch (Exception ex)
            {
                DebugLogger.LogErrorToFile($"Signature verification error: {ex.Message}");
                return false;
            }
        }
    }
}