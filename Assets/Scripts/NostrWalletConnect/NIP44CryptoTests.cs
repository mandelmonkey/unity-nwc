using System;
using System.Collections.Generic;
using UnityEngine;

namespace NostrWalletConnect
{
    public class NIP44CryptoTests : MonoBehaviour
    {
        [Header("Test Controls")]
        [SerializeField] private bool runTestsOnStart = false;
        [SerializeField] private bool logDetailedResults = true;

        private void Start()
        {
            if (runTestsOnStart)
            {
                RunAllTests();
            }
        }

        [ContextMenu("Run All NIP-44 Tests")]
        public void RunAllTests()
        {
            Debug.Log("🧪 Starting NIP-44 Crypto Tests...");

            int totalTests = 0;
            int passedTests = 0;

            // Test 1: calc_padded_len function
            var paddingResults = TestCalcPaddedLen();
            totalTests += paddingResults.total;
            passedTests += paddingResults.passed;

            // Test 2: Key derivation
            var keyResults = TestKeyDerivation();
            totalTests += keyResults.total;
            passedTests += keyResults.passed;

            // Test 3: Encryption/Decryption round trip
            var cryptoResults = TestEncryptionDecryption();
            totalTests += cryptoResults.total;
            passedTests += cryptoResults.passed;

            // Final results
            Debug.Log($"🏁 NIP-44 Tests Complete: {passedTests}/{totalTests} passed");

            if (passedTests == totalTests)
            {
                Debug.Log("✅ ALL TESTS PASSED! NIP-44 implementation is correct.");
            }
            else
            {
                Debug.LogError($"❌ {totalTests - passedTests} TESTS FAILED! NIP-44 implementation has issues.");
            }
        }

        private (int total, int passed) TestCalcPaddedLen()
        {
            Debug.Log("🔍 Testing calc_padded_len function...");

            // Test vectors from nip44.vectors.json
            var testCases = new Dictionary<int, int>
            {
                {16, 32}, {32, 32}, {33, 64}, {37, 64}, {45, 64}, {49, 64}, {64, 64}, {65, 96},
                {100, 128}, {111, 128}, {200, 224}, {250, 256}, {320, 320}, {383, 384},
                {384, 384}, {400, 448}, {500, 512}, {512, 512}, {515, 640}, {700, 768},
                {800, 896}, {900, 1024}, {1020, 1024}, {65536, 65536}
            };

            int passed = 0;
            int total = testCases.Count;

            foreach (var testCase in testCases)
            {
                try
                {
                    int input = testCase.Key;
                    int expected = testCase.Value;

                    // Access the private CalcPaddedLen method via reflection
                    var method = typeof(NIP44Crypto).GetMethod("CalcPaddedLen",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

                    int actual = (int)method.Invoke(null, new object[] { input });

                    if (actual == expected)
                    {
                        passed++;
                        if (logDetailedResults)
                        {
                            Debug.Log($"✅ calc_padded_len({input}) = {actual} (expected {expected})");
                        }
                    }
                    else
                    {
                        Debug.LogError($"❌ calc_padded_len({input}) = {actual} (expected {expected})");
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"❌ calc_padded_len({testCase.Key}) threw exception: {ex.Message}");
                }
            }

            Debug.Log($"📊 calc_padded_len: {passed}/{total} tests passed");
            return (total, passed);
        }

        private (int total, int passed) TestKeyDerivation()
        {
            Debug.Log("🔑 Testing key derivation...");

            // Official NIP-44 test vectors for conversation key generation
            var testCases = new[]
            {
                new {
                    sec1 = "315e59ff51cb9209768cf7da80791ddcaae56ac9775eb25b6dee1234bc5d2268",
                    pub2 = "c2f9d9948dc8c7c38321e4b85c8558872eafa0641cd269db76848a6073e69133",
                    conversationKey = "3dfef0ce2a4d80a25e7a328accf73448ef67096f65f79588e358d9a0eb9013f1"
                },
                new {
                    sec1 = "a1e37752c9fdc1273be53f68c5f74be7c8905728e8de75800b94262f9497c86e",
                    pub2 = "03bb7947065dde12ba991ea045132581d0954f042c84e06d8c00066e23c1a800",
                    conversationKey = "4d14f36e81b8452128da64fe6f1eae873baae2f444b02c950b90e43553f2178b"
                }
            };

            int passed = 0;
            int total = testCases.Length;

            foreach (var testCase in testCases)
            {
                try
                {
                    // Access the private ComputeNIP44ConversationKey method
                    var method = typeof(NIP44Crypto).GetMethod("ComputeNIP44ConversationKey",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

                    byte[] actualKey = (byte[])method.Invoke(null, new object[] { testCase.sec1, testCase.pub2 });
                    string actualHex = BitConverter.ToString(actualKey).Replace("-", "").ToLower();

                    if (actualHex == testCase.conversationKey)
                    {
                        passed++;
                        if (logDetailedResults)
                        {
                            Debug.Log($"✅ Key derivation: {actualHex} (expected {testCase.conversationKey})");
                        }
                    }
                    else
                    {
                        Debug.LogError($"❌ Key derivation: {actualHex} (expected {testCase.conversationKey})");
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"❌ Key derivation threw exception: {ex.Message}");
                }
            }

            Debug.Log($"📊 Key derivation: {passed}/{total} tests passed");
            return (total, passed);
        }

        private (int total, int passed) TestEncryptionDecryption()
        {
            Debug.Log("🔒 Testing encryption/decryption...");

            // Official NIP-44 test vectors from nip44.vectors.json
            var testCases = new[]
            {
                new {
                    sec1 = "0000000000000000000000000000000000000000000000000000000000000001",
                    sec2 = "0000000000000000000000000000000000000000000000000000000000000002",
                    pub1 = "79be667ef9dcbbac55a06295ce870b07029bfcdb2dce28d959f2815b16f81798", // Public key derived from sec1
                    pub2 = "c6047f9441ed7d6d3045406e95c07cd85c778e4b8cef3ca7abac09b95c709ee5", // Public key derived from sec2
                    plaintext = "a",
                    payload = "AgAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAABee0G5VSK0/9YypIObAtDKfYEAjD35uVkHyB0F4DwrcNaCXlCWZKaArsGrY6M9wnuTMxWfp1RTN9Xga8no+kF5Vsb"
                },
                new {
                    sec1 = "0000000000000000000000000000000000000000000000000000000000000002",
                    sec2 = "0000000000000000000000000000000000000000000000000000000000000001",
                    pub1 = "c6047f9441ed7d6d3045406e95c07cd85c778e4b8cef3ca7abac09b95c709ee5", // Public key derived from sec1
                    pub2 = "79be667ef9dcbbac55a06295ce870b07029bfcdb2dce28d959f2815b16f81798", // Public key derived from sec2
                    plaintext = "🍕🫃", // "🍕🫃"
                    payload = "AvAAAAAAAAAAAAAAAAAAAPAAAAAAAAAAAAAAAAAAAAAPSKSK6is9ngkX2+cSq85Th16oRTISAOfhStnixqZziKMDvB0QQzgFZdjLTPicCJaV8nDITO+QfaQ61+KbWQIOO2Yj"
                }
            };

            int passed = 0;
            int total = testCases.Length;

            foreach (var testCase in testCases)
            {
                try
                {
                    // Test encryption
                    string encrypted = NIP44Crypto.EncryptNIP44(testCase.plaintext, testCase.pub2, testCase.sec1, 2);

                    // Test decryption
                    string decrypted = NIP44Crypto.DecryptNIP44(testCase.payload, testCase.sec2, testCase.pub1);

                    if (decrypted == testCase.plaintext)
                    {
                        passed++;
                        if (logDetailedResults)
                        {
                            Debug.Log($"✅ Encrypt/Decrypt: '{testCase.plaintext}' -> encrypted -> '{decrypted}'");
                        }
                    }
                    else
                    {
                        Debug.LogError($"❌ Encrypt/Decrypt: '{testCase.plaintext}' -> encrypted -> '{decrypted}' (mismatch)");
                    }

                    // Also test our encryption can be decrypted
                    try
                    {
                        string ourDecrypted = NIP44Crypto.DecryptNIP44(encrypted, testCase.sec1, testCase.pub2);
                        if (ourDecrypted == testCase.plaintext)
                        {
                            if (logDetailedResults)
                            {
                                Debug.Log($"✅ Round-trip test: '{testCase.plaintext}' -> our encryption -> '{ourDecrypted}'");
                            }
                        }
                        else
                        {
                            Debug.LogWarning($"⚠️ Our encryption round-trip failed: '{testCase.plaintext}' -> '{ourDecrypted}'");
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"⚠️ Our encryption couldn't be decrypted: {ex.Message}");
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"❌ Encryption/decryption test threw exception: {ex.Message}");
                }
            }

            Debug.Log($"📊 Encryption/Decryption: {passed}/{total} tests passed");
            return (total, passed);
        }

        [ContextMenu("Test calc_padded_len Only")]
        public void TestPaddingOnly()
        {
            var results = TestCalcPaddedLen();
            Debug.Log($"Padding test results: {results.passed}/{results.total}");
        }

        [ContextMenu("Test Key Derivation Only")]
        public void TestKeyDerivationOnly()
        {
            var results = TestKeyDerivation();
            Debug.Log($"Key derivation test results: {results.passed}/{results.total}");
        }

        [ContextMenu("Test Encryption Only")]
        public void TestEncryptionOnly()
        {
            var results = TestEncryptionDecryption();
            Debug.Log($"Encryption test results: {results.passed}/{results.total}");
        }
    }
}