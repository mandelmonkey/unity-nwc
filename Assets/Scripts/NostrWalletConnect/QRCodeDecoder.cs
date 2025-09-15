using System;
using System.Collections.Generic;
using UnityEngine;

namespace NostrWalletConnect
{
    public static class QRCodeDecoder
    {
        private struct QRFinderPattern
        {
            public int x, y, estimatedModuleSize;
        }

        public static string DecodeQRFromPixels(Color32[] pixels, int width, int height)
        {
            try
            {
                var patterns = FindFinderPatterns(pixels, width, height);

                if (patterns.Count >= 3)
                {
                    return ExtractQRData(pixels, width, height, patterns);
                }

                return TrySimplePatternDetection(pixels, width, height);
            }
            catch (Exception ex)
            {
                Debug.LogError($"QR decoding error: {ex.Message}");
                return null;
            }
        }

        private static List<QRFinderPattern> FindFinderPatterns(Color32[] pixels, int width, int height)
        {
            var patterns = new List<QRFinderPattern>();

            for (int y = 0; y < height - 7; y += 4)
            {
                for (int x = 0; x < width - 7; x += 4)
                {
                    if (IsFinderPattern(pixels, width, height, x, y))
                    {
                        int moduleSize = EstimateModuleSize(pixels, width, height, x, y);
                        if (moduleSize > 0)
                        {
                            patterns.Add(new QRFinderPattern { x = x, y = y, estimatedModuleSize = moduleSize });
                        }
                    }
                }
            }

            return patterns;
        }

        private static bool IsFinderPattern(Color32[] pixels, int width, int height, int startX, int startY)
        {
            if (startX + 7 >= width || startY + 7 >= height) return false;

            int[] expectedPattern = { 1, 1, 3, 1, 1 };
            int[] horizontalCounts = new int[5];
            int[] verticalCounts = new int[5];

            for (int i = 0; i < 7; i++)
            {
                int pixelIndex = (startY + 3) * width + (startX + i);
                if (pixelIndex >= 0 && pixelIndex < pixels.Length)
                {
                    bool isDark = GetPixelBrightness(pixels[pixelIndex]) < 128;
                    int patternIndex = GetPatternIndex(i, 7);
                    if (patternIndex >= 0 && patternIndex < 5)
                    {
                        horizontalCounts[patternIndex] += isDark ? 1 : 0;
                    }
                }

                pixelIndex = (startY + i) * width + (startX + 3);
                if (pixelIndex >= 0 && pixelIndex < pixels.Length)
                {
                    bool isDark = GetPixelBrightness(pixels[pixelIndex]) < 128;
                    int patternIndex = GetPatternIndex(i, 7);
                    if (patternIndex >= 0 && patternIndex < 5)
                    {
                        verticalCounts[patternIndex] += isDark ? 1 : 0;
                    }
                }
            }

            return MatchesRatio(horizontalCounts, expectedPattern) && MatchesRatio(verticalCounts, expectedPattern);
        }

        private static int GetPatternIndex(int position, int size)
        {
            float ratio = (float)position / size;
            if (ratio < 0.15f) return 0;
            if (ratio < 0.3f) return 1;
            if (ratio < 0.7f) return 2;
            if (ratio < 0.85f) return 3;
            return 4;
        }

        private static bool MatchesRatio(int[] counts, int[] expected)
        {
            int totalCounts = 0;
            int totalExpected = 0;

            for (int i = 0; i < counts.Length; i++)
            {
                totalCounts += counts[i];
                totalExpected += expected[i];
            }

            if (totalCounts < 7) return false;

            float unitSize = (float)totalCounts / totalExpected;

            for (int i = 0; i < counts.Length; i++)
            {
                float expectedCount = expected[i] * unitSize;
                float variance = Math.Abs(counts[i] - expectedCount);
                if (variance > unitSize * 0.5f)
                    return false;
            }

            return true;
        }

        private static int EstimateModuleSize(Color32[] pixels, int width, int height, int x, int y)
        {
            return 3;
        }

        private static int GetPixelBrightness(Color32 pixel)
        {
            return (pixel.r + pixel.g + pixel.b) / 3;
        }

        private static string ExtractQRData(Color32[] pixels, int width, int height, List<QRFinderPattern> patterns)
        {
            return TrySimplePatternDetection(pixels, width, height);
        }

        private static string TrySimplePatternDetection(Color32[] pixels, int width, int height)
        {
            const string nwcPrefix = "nostr+walletconnect://";

            int centerX = width / 2;
            int centerY = height / 2;
            int scanRadius = Math.Min(width, height) / 4;

            var darkRegions = new List<Vector2Int>();

            for (int y = centerY - scanRadius; y <= centerY + scanRadius; y += 8)
            {
                for (int x = centerX - scanRadius; x <= centerX + scanRadius; x += 8)
                {
                    if (x >= 0 && x < width && y >= 0 && y < height)
                    {
                        int index = y * width + x;
                        if (index >= 0 && index < pixels.Length)
                        {
                            if (GetPixelBrightness(pixels[index]) < 100)
                            {
                                darkRegions.Add(new Vector2Int(x, y));
                            }
                        }
                    }
                }
            }

            if (darkRegions.Count > 50)
            {
                return TryExtractFromPattern(darkRegions, nwcPrefix);
            }

            return null;
        }

        private static string TryExtractFromPattern(List<Vector2Int> darkRegions, string prefix)
        {
            var testStrings = new[]
            {
                "nostr+walletconnect://b889ff5b1513b641e2a139f661a661364979c5beee91842f8f0e8dda6766a3b3?relay=wss://relay.primal.net&secret=71a8c14c1407c113601079c4302dab36460f0ccd0ad506f884658492666e639d",
                "nostr+walletconnect://example123456789012345678901234567890123456789012345678901234?relay=wss://relay.example.com&secret=abcdef1234567890abcdef1234567890abcdef1234567890abcdef1234567890"
            };

            if (darkRegions.Count > 30)
            {
                Debug.Log($"QR pattern detected with {darkRegions.Count} dark regions");
                return null;
            }

            return null;
        }

        public static bool IsValidNWCConnectionString(string connectionString)
        {
            if (string.IsNullOrEmpty(connectionString))
                return false;

            if (!connectionString.StartsWith("nostr+walletconnect://"))
                return false;

            try
            {
                var uri = new Uri(connectionString);
                return uri.Host.Length == 64 &&
                       !string.IsNullOrEmpty(uri.Query) &&
                       uri.Query.Contains("relay=") &&
                       uri.Query.Contains("secret=");
            }
            catch
            {
                return false;
            }
        }
    }
}