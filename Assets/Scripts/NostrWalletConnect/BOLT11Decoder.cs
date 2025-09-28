using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace NostrWalletConnect
{
    public static class BOLT11Decoder
    {
        private const string CHARSET = "qpzry9x8gf2tvdw0s3jn54khce6mua7l";

        public class DecodedInvoice
        {
            public string Network { get; set; }
            public long? AmountSats { get; set; }
            public string PaymentHash { get; set; }
            public string Description { get; set; }
            public long Timestamp { get; set; }
            public long Expiry { get; set; }
            public string PayeePublicKey { get; set; }

            public bool IsValid => !string.IsNullOrEmpty(PaymentHash);
        }

        public static DecodedInvoice DecodeInvoice(string invoice)
        {
            try
            {
                if (string.IsNullOrEmpty(invoice))
                    throw new ArgumentException("Invoice cannot be null or empty");

                DebugLogger.Log($"🔍 Starting BOLT11 decode for: {invoice.Substring(0, Math.Min(50, invoice.Length))}...");

                // Convert to lowercase for processing
                invoice = invoice.ToLower().Trim();
                DebugLogger.Log($"📝 Cleaned invoice: {invoice}");

                // Check for valid BOLT11 prefix
                if (!invoice.StartsWith("lnbc") && !invoice.StartsWith("lntb") && !invoice.StartsWith("lnbcrt"))
                {
                    throw new ArgumentException($"Invalid BOLT11 invoice format - got prefix: {invoice.Substring(0, Math.Min(6, invoice.Length))}");
                }

                var result = new DecodedInvoice();

                // Extract network
                if (invoice.StartsWith("lnbc")) result.Network = "mainnet";
                else if (invoice.StartsWith("lntb")) result.Network = "testnet";
                else if (invoice.StartsWith("lnbcrt")) result.Network = "regtest";

                // Find the last '1' separator before the signature
                int lastSeparator = invoice.LastIndexOf('1');
                if (lastSeparator == -1)
                    throw new ArgumentException("Invalid BOLT11 format: no separator found");

                DebugLogger.Log($"📍 Found separator at position {lastSeparator}");

                // Extract human readable part and data part
                string hrp = invoice.Substring(0, lastSeparator);
                string dataString = invoice.Substring(lastSeparator + 1);

                DebugLogger.Log($"🏷️ HRP: {hrp}");
                DebugLogger.Log($"📊 Data: {dataString} (length: {dataString.Length})");

                // Extract amount from HRP
                result.AmountSats = ExtractAmount(hrp);

                // Decode the data part using bech32
                byte[] data = Bech32Decode(dataString);
                if (data == null || data.Length == 0)
                    throw new ArgumentException("Failed to decode bech32 data");

                DebugLogger.Log($"🔧 Decoded data: {BitConverter.ToString(data).Replace("-", "").ToLower()} ({data.Length} bytes)");

                // Parse tagged fields
                ParseTaggedFields(data, result);

                DebugLogger.Log($"🧾 Decoded BOLT11 invoice:");
                DebugLogger.Log($"  Network: {result.Network}");
                DebugLogger.Log($"  Amount: {result.AmountSats} sats");
                DebugLogger.Log($"  Payment Hash: {result.PaymentHash}");
                DebugLogger.Log($"  Description: {result.Description}");
                DebugLogger.Log($"  Expiry: {result.Expiry}s");

                return result;
            }
            catch (Exception ex)
            {
                DebugLogger.LogError($"Failed to decode BOLT11 invoice: {ex.Message}");
                return new DecodedInvoice(); // Return invalid invoice
            }
        }

        private static long? ExtractAmount(string hrp)
        {
            // Remove prefix (lnbc, lntb, etc.)
            string amountPart = "";
            if (hrp.StartsWith("lnbc")) amountPart = hrp.Substring(4);
            else if (hrp.StartsWith("lntb")) amountPart = hrp.Substring(4);
            else if (hrp.StartsWith("lnbcrt")) amountPart = hrp.Substring(6);

            if (string.IsNullOrEmpty(amountPart))
                return null;

            // Parse amount with multiplier
            char multiplier = amountPart[amountPart.Length - 1];
            string numberPart = amountPart.Substring(0, amountPart.Length - 1);

            if (!long.TryParse(numberPart, out long baseAmount))
                return null;

            // Apply multipliers according to BOLT11 spec
            switch (multiplier)
            {
                case 'm': return baseAmount * 100_000; // milli-bitcoin
                case 'u': return baseAmount * 100;     // micro-bitcoin
                case 'n': return baseAmount / 10;      // nano-bitcoin
                case 'p': return baseAmount / 10_000;  // pico-bitcoin
                default:
                    // If no multiplier, treat as base amount
                    if (char.IsDigit(multiplier))
                    {
                        // The multiplier is actually part of the number
                        if (long.TryParse(amountPart, out long fullAmount))
                            return fullAmount * 100_000_000; // Treat as bitcoin and convert to sats
                    }
                    return baseAmount;
            }
        }

        private static byte[] Bech32Decode(string data)
        {
            try
            {
                DebugLogger.Log($"🔍 Decoding bech32 data: {data}");

                // Convert bech32 characters to 5-bit values
                var values = new List<byte>();
                foreach (char c in data)
                {
                    int index = CHARSET.IndexOf(c);
                    if (index == -1)
                    {
                        DebugLogger.LogError($"Invalid bech32 character: {c} at position {data.IndexOf(c)}");
                        throw new ArgumentException($"Invalid bech32 character: {c}");
                    }
                    values.Add((byte)index);
                }

                DebugLogger.Log($"Converted to {values.Count} 5-bit values");

                // Remove checksum (last 6 characters)
                if (values.Count < 6)
                    throw new ArgumentException("Data too short for bech32");

                values.RemoveRange(values.Count - 6, 6);
                DebugLogger.Log($"After removing checksum: {values.Count} values");

                // Return the 5-bit values directly - DON'T convert to 8-bit yet
                // The tagged field parsing will handle the 5-bit to 8-bit conversion per field
                return values.ToArray();
            }
            catch (Exception ex)
            {
                DebugLogger.LogError($"Bech32 decode error: {ex.Message}");
                DebugLogger.LogError($"Stack trace: {ex.StackTrace}");
                return null;
            }
        }

        private static byte[] ConvertBits(byte[] data, int fromBits, int toBits, bool pad)
        {
            try
            {
                DebugLogger.Log($"Converting {data.Length} values from {fromBits}-bit to {toBits}-bit");

                int acc = 0;
                int bits = 0;
                var result = new List<byte>();

                foreach (byte value in data)
                {
                    if ((value >> fromBits) != 0)
                    {
                        DebugLogger.LogError($"Invalid input value: {value} (exceeds {fromBits} bits)");
                        return null; // Invalid input
                    }

                    acc = (acc << fromBits) | value;
                    bits += fromBits;

                    while (bits >= toBits)
                    {
                        bits -= toBits;
                        result.Add((byte)((acc >> bits) & ((1 << toBits) - 1)));
                    }
                }

                if (pad && bits > 0)
                {
                    result.Add((byte)((acc << (toBits - bits)) & ((1 << toBits) - 1)));
                }
                else if (bits >= fromBits || ((acc << (toBits - bits)) & ((1 << toBits) - 1)) != 0)
                {
                    DebugLogger.LogError($"Invalid padding: bits={bits}, fromBits={fromBits}");
                    return null; // Invalid padding
                }

                DebugLogger.Log($"Conversion successful: {result.Count} bytes output");
                return result.ToArray();
            }
            catch (Exception ex)
            {
                DebugLogger.LogError($"ConvertBits failed: {ex.Message}");
                return null;
            }
        }

        private static void ParseTaggedFields(byte[] fiveBitData, DecodedInvoice result)
        {
            // Set defaults
            result.Expiry = 3600; // 1 hour default

            DebugLogger.Log($"🏷️ Parsing tagged fields from {fiveBitData.Length} 5-bit values");

            // Debug: Show first 15 values
            var first15 = string.Join(",", fiveBitData.Take(Math.Min(15, fiveBitData.Length)));
            DebugLogger.Log($"🔍 First 15 5-bit values: {first15}");

            // BOLT11 data structure after bech32 decode:
            // 1. Timestamp (35 bits = 7 five-bit groups)
            // 2. Tagged fields (variable)
            // 3. Signature (65 bytes = 520 bits = 104 five-bit groups)

            int pos = 0;

            // Extract timestamp (7 five-bit values = 35 bits)
            if (fiveBitData.Length < 7)
            {
                DebugLogger.LogError("Not enough data for timestamp");
                return;
            }

            long timestamp = 0;
            for (int i = 0; i < 7; i++)
            {
                timestamp = (timestamp << 5) | fiveBitData[pos++];
            }
            result.Timestamp = timestamp;
            DebugLogger.Log($"⏰ Parsed timestamp: {timestamp} ({DateTimeOffset.FromUnixTimeSeconds(timestamp):yyyy-MM-dd HH:mm:ss} UTC)");

            // Calculate where signature starts (last 104 five-bit values)
            int signatureStart = fiveBitData.Length - 104;
            DebugLogger.Log($"🔏 Signature starts at position {signatureStart}, current pos: {pos}");

            // Parse tagged fields between timestamp and signature
            while (pos + 3 <= signatureStart) // Need at least type (1) + data_length (2) = 3 elements minimum
            {
                // Extract type (1 element = 5 bits)
                int tag = fiveBitData[pos];
                pos++;

                // Extract data_length (2 elements = 10 bits)
                if (pos + 2 > fiveBitData.Length)
                {
                    DebugLogger.LogWarning($"Not enough data for length field at position {pos}");
                    break;
                }

                // BOLT11 spec: data_length is encoded as 10 bits across 2 five-bit values
                // First 5-bit value contains bits 9-5, second 5-bit value contains bits 4-0
                int dataLength = (fiveBitData[pos] << 5) + fiveBitData[pos + 1];
                pos += 2;

                DebugLogger.Log($"🔍 Length calculation: fiveBitData[{pos-2}]={fiveBitData[pos-2]}, fiveBitData[{pos-1}]={fiveBitData[pos-1]}, combined={dataLength}");

                DebugLogger.Log($"📋 Found tag {tag}, data length {dataLength}");

                // Check if we have enough data
                if (pos + dataLength > fiveBitData.Length)
                {
                    DebugLogger.LogWarning($"Not enough data for tag {tag}, expected {dataLength} elements, have {fiveBitData.Length - pos}");
                    break;
                }

                // Extract field data (dataLength elements of 5 bits each)
                byte[] fiveBitFieldData = new byte[dataLength];
                Array.Copy(fiveBitData, pos, fiveBitFieldData, 0, dataLength);
                pos += dataLength;

                // Convert 5-bit field data to 8-bit bytes
                byte[] fieldData = ConvertBits(fiveBitFieldData, 5, 8, false);

                if (fieldData == null)
                {
                    DebugLogger.LogError($"Failed to convert field data for tag {tag}");
                    continue;
                }

                DebugLogger.Log($"📦 Tag {tag}: {fieldData.Length} bytes extracted");

                switch (tag)
                {
                    case 1: // Payment hash (p)
                        if (fieldData.Length == 32)
                        {
                            result.PaymentHash = BitConverter.ToString(fieldData).Replace("-", "").ToLower();
                            DebugLogger.Log($"💳 Payment hash: {result.PaymentHash}");
                        }
                        else
                        {
                            DebugLogger.LogWarning($"Invalid payment hash length: {fieldData.Length} (expected 32)");
                        }
                        break;

                    case 13: // Description (d)
                        try
                        {
                            result.Description = Encoding.UTF8.GetString(fieldData);
                            DebugLogger.Log($"📝 Description: {result.Description}");
                        }
                        catch (Exception ex)
                        {
                            DebugLogger.LogWarning($"Failed to decode description: {ex.Message}");
                        }
                        break;

                    case 6: // Expiry time (x)
                        if (fieldData.Length <= 8)
                        {
                            result.Expiry = 0;
                            for (int i = 0; i < fieldData.Length; i++)
                            {
                                result.Expiry = (result.Expiry << 8) | fieldData[i];
                            }
                            DebugLogger.Log($"⏰ Expiry: {result.Expiry}s");
                        }
                        break;

                    case 19: // Payee public key (n)
                        if (fieldData.Length == 33)
                        {
                            result.PayeePublicKey = BitConverter.ToString(fieldData).Replace("-", "").ToLower();
                            DebugLogger.Log($"🔑 Payee pubkey: {result.PayeePublicKey}");
                        }
                        break;

                    default:
                        DebugLogger.Log($"🤷 Unknown tag {tag}, skipping {fieldData.Length} bytes");
                        break;
                }
            }
        }


        public static bool VerifyPreimage(string invoice, string preimage)
        {
            try
            {
                var decoded = DecodeInvoice(invoice);
                if (!decoded.IsValid || string.IsNullOrEmpty(preimage))
                {
                    DebugLogger.LogWarning("Invalid invoice or preimage for verification");
                    return false;
                }

                // Convert preimage from hex to bytes
                byte[] preimageBytes = HexToBytes(preimage);

                // Calculate SHA256 hash of preimage
                using (var sha256 = SHA256.Create())
                {
                    byte[] hashBytes = sha256.ComputeHash(preimageBytes);
                    string calculatedHash = BitConverter.ToString(hashBytes).Replace("-", "").ToLower();

                    bool isValid = calculatedHash.Equals(decoded.PaymentHash, StringComparison.OrdinalIgnoreCase);

                    DebugLogger.Log($"🔐 Preimage verification:");
                    DebugLogger.Log($"  Expected hash: {decoded.PaymentHash}");
                    DebugLogger.Log($"  Calculated hash: {calculatedHash}");
                    DebugLogger.Log($"  Result: {(isValid ? "✅ VALID" : "❌ INVALID")}");

                    return isValid;
                }
            }
            catch (Exception ex)
            {
                DebugLogger.LogError($"Preimage verification failed: {ex.Message}");
                return false;
            }
        }

        private static byte[] HexToBytes(string hex)
        {
            if (hex.Length % 2 == 1)
                throw new ArgumentException("Invalid hex string length");

            byte[] bytes = new byte[hex.Length / 2];
            for (int i = 0; i < hex.Length; i += 2)
            {
                bytes[i / 2] = Convert.ToByte(hex.Substring(i, 2), 16);
            }
            return bytes;
        }
    }
}