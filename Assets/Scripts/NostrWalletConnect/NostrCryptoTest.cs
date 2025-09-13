using System;
using UnityEngine;
using Newtonsoft.Json;

namespace NostrWalletConnect
{
    public class NostrCryptoTest : MonoBehaviour
    {
        [ContextMenu("Test Crypto Functions")]
        public void TestCryptoFunctions()
        {
            try
            {
                Debug.Log("=== Testing Nostr Crypto with NBitcoin.Secp256k1 ===");

                // Test 1: Key Generation
                Debug.Log("Test 1: Key Generation");
                var privateKey = NostrCrypto.GeneratePrivateKey();
                var publicKey = NostrCrypto.GetPublicKey(privateKey);

                Debug.Log($"Private Key: {privateKey}");
                Debug.Log($"Public Key: {publicKey}");

                if (privateKey.Length == 64 && publicKey.Length == 64)
                {
                    Debug.Log("✅ Key generation test passed");
                }
                else
                {
                    Debug.LogError("❌ Key generation test failed - incorrect lengths");
                    return;
                }

                // Test 2: Event Signing and Verification
                Debug.Log("\nTest 2: Event Signing and Verification");

                var testEvent = new
                {
                    pubkey = publicKey,
                    created_at = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    kind = 1,
                    tags = new string[0][],
                    content = "Hello, Nostr with NBitcoin!"
                };

                var eventJson = JsonConvert.SerializeObject(new object[]
                {
                    0,
                    testEvent.pubkey,
                    testEvent.created_at,
                    testEvent.kind,
                    testEvent.tags,
                    testEvent.content
                });

                var eventId = NostrCrypto.CreateEventId(eventJson);
                var signature = NostrCrypto.SignEvent(eventId, privateKey);

                Debug.Log($"Event JSON: {eventJson}");
                Debug.Log($"Event ID: {eventId}");
                Debug.Log($"Signature: {signature}");

                if (eventId.Length == 64 && signature.Length == 128)
                {
                    Debug.Log("✅ Event ID and signature generation test passed");
                }
                else
                {
                    Debug.LogError("❌ Event ID or signature has incorrect length");
                    return;
                }

                // Test 3: Signature Verification
                Debug.Log("\nTest 3: Signature Verification");
                bool isValid = NostrCrypto.VerifySignature(eventId, signature, publicKey);

                if (isValid)
                {
                    Debug.Log("✅ Signature verification test passed");
                }
                else
                {
                    Debug.LogError("❌ Signature verification failed");
                    return;
                }

                // Test 4: NIP-04 Encryption/Decryption
                Debug.Log("\nTest 4: NIP-04 Encryption/Decryption");

                var recipientPrivateKey = NostrCrypto.GeneratePrivateKey();
                var recipientPublicKey = NostrCrypto.GetPublicKey(recipientPrivateKey);

                var testMessage = "This is a secret message for NWC!";
                var encrypted = NostrCrypto.EncryptNIP04(testMessage, recipientPublicKey, privateKey);
                var decrypted = NostrCrypto.DecryptNIP04(encrypted, publicKey, recipientPrivateKey);

                Debug.Log($"Original Message: {testMessage}");
                Debug.Log($"Encrypted: {encrypted}");
                Debug.Log($"Decrypted: {decrypted}");

                if (testMessage == decrypted)
                {
                    Debug.Log("✅ NIP-04 encryption/decryption test passed");
                }
                else
                {
                    Debug.LogError("❌ NIP-04 encryption/decryption test failed");
                    return;
                }

                // Test 5: NWC Request Creation
                Debug.Log("\nTest 5: NWC Request Creation");

                var nwcRequest = new NWCRequest
                {
                    Method = "get_info",
                    Params = new System.Collections.Generic.Dictionary<string, object>()
                };

                var walletPubkey = NostrCrypto.GetPublicKey(NostrCrypto.GeneratePrivateKey());
                var clientPrivateKey = NostrCrypto.GeneratePrivateKey();

                var requestEvent = NWCProtocol.CreateRequestEvent(nwcRequest, walletPubkey, privateKey, clientPrivateKey);

                Debug.Log($"NWC Request Event:");
                Debug.Log($"  ID: {requestEvent.Id}");
                Debug.Log($"  Pubkey: {requestEvent.Pubkey}");
                Debug.Log($"  Kind: {requestEvent.Kind}");
                Debug.Log($"  Signature: {requestEvent.Signature}");

                // Verify the NWC request signature
                bool nwcSignatureValid = NostrCrypto.VerifySignature(requestEvent.Id, requestEvent.Signature, requestEvent.Pubkey);

                if (nwcSignatureValid)
                {
                    Debug.Log("✅ NWC request signature verification passed");
                }
                else
                {
                    Debug.LogError("❌ NWC request signature verification failed");
                    return;
                }

                Debug.Log("\n🎉 All tests passed! NBitcoin.Secp256k1 crypto is working correctly!");

            }
            catch (Exception ex)
            {
                Debug.LogError($"❌ Test failed with exception: {ex.Message}\n{ex.StackTrace}");
            }
        }

        [ContextMenu("Test Real NWC Connection")]
        public void TestRealNWCConnection()
        {
            var nwc = GetComponent<NostrWalletConnect>();
            if (nwc != null)
            {
                Debug.Log("Testing original NWC component with proper crypto...");
            }
            else
            {
                Debug.LogWarning("No NostrWalletConnect component found. Add it to test real connections.");
            }
        }

        [ContextMenu("Performance Test")]
        public void PerformanceTest()
        {
            var startTime = DateTime.UtcNow;

            Debug.Log("Starting performance test...");

            for (int i = 0; i < 10; i++)
            {
                var privateKey = NostrCrypto.GeneratePrivateKey();
                var publicKey = NostrCrypto.GetPublicKey(privateKey);

                var eventJson = JsonConvert.SerializeObject(new object[]
                {
                    0, publicKey, DateTimeOffset.UtcNow.ToUnixTimeSeconds(), 1, new string[0][], $"Test message {i}"
                });

                var eventId = NostrCrypto.CreateEventId(eventJson);
                var signature = NostrCrypto.SignEvent(eventId, privateKey);
                var verified = NostrCrypto.VerifySignature(eventId, signature, publicKey);

                if (!verified)
                {
                    Debug.LogError($"Signature verification failed on iteration {i}");
                    return;
                }
            }

            var endTime = DateTime.UtcNow;
            var duration = endTime - startTime;

            Debug.Log($"Performance test completed: 10 key generations, signings, and verifications in {duration.TotalMilliseconds:F2}ms");
        }
    }
}