using System;
using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json;

namespace NostrWalletConnect
{
    [Serializable]
    public class NostrEvent
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("pubkey")]
        public string Pubkey { get; set; }

        [JsonProperty("created_at")]
        public long CreatedAt { get; set; }

        [JsonProperty("kind")]
        public int Kind { get; set; }

        [JsonProperty("tags")]
        public string[][] Tags { get; set; }

        [JsonProperty("content")]
        public string Content { get; set; }

        [JsonProperty("sig")]
        public string Signature { get; set; }

        public NostrEvent()
        {
            Tags = new string[0][];
        }
    }

    [Serializable]
    public class NWCRequest
    {
        [JsonProperty("method")]
        public string Method { get; set; }

        [JsonProperty("params")]
        public Dictionary<string, object> Params { get; set; }

        public NWCRequest()
        {
            Params = new Dictionary<string, object>();
        }
    }

    [Serializable]
    public class NWCResponse
    {
        [JsonProperty("result_type")]
        public string ResultType { get; set; }

        [JsonProperty("result")]
        public Dictionary<string, object> Result { get; set; }

        [JsonProperty("error")]
        public NWCError Error { get; set; }

        public NWCResponse()
        {
            Result = new Dictionary<string, object>();
        }

        /// <summary>
        /// Check if the response contains an error
        /// </summary>
        public bool HasError => Error != null;

        /// <summary>
        /// Check if the response is successful (no error)
        /// </summary>
        public bool IsSuccess => Error == null;

        /// <summary>
        /// Get a formatted error message, or null if no error
        /// </summary>
        public string ErrorMessage => Error != null ? $"{Error.Code}: {Error.Message}" : null;

        /// <summary>
        /// Throw an exception if this response contains an error
        /// </summary>
        public void ThrowIfError()
        {
            if (Error != null)
            {
                throw new InvalidOperationException($"NWC Error: {Error.Code} - {Error.Message}");
            }
        }

        /// <summary>
        /// For PayInvoice responses, verify that the preimage matches the original invoice
        /// </summary>
        /// <param name="originalInvoice">The original BOLT11 invoice that was paid</param>
        /// <returns>True if preimage is valid, false if invalid or not applicable</returns>
        public bool VerifyPaymentPreimage(string originalInvoice)
        {
            // Only applicable for successful pay_invoice responses
            if (HasError || ResultType != "pay_invoice" || string.IsNullOrEmpty(originalInvoice))
                return false;

            // Extract preimage from response
            if (!Result.TryGetValue("preimage", out var preimageObj) || preimageObj == null)
            {
                DebugLogger.LogWarning("No preimage found in pay_invoice response");
                return false;
            }

            string preimage = preimageObj.ToString();
            if (string.IsNullOrEmpty(preimage))
            {
                DebugLogger.LogWarning("Empty preimage in pay_invoice response");
                return false;
            }

            // Use BOLT11 decoder to verify preimage
            return BOLT11Decoder.VerifyPreimage(originalInvoice, preimage);
        }

        /// <summary>
        /// Get the preimage from a successful pay_invoice response
        /// </summary>
        public string GetPreimage()
        {
            if (HasError || !Result.TryGetValue("preimage", out var preimageObj))
                return null;

            return preimageObj?.ToString();
        }
    }

    [Serializable]
    public class NWCError
    {
        [JsonProperty("code")]
        public string Code { get; set; }

        [JsonProperty("message")]
        public string Message { get; set; }
    }

    [Serializable]
    public class PayInvoiceRequest
    {
        [JsonProperty("invoice")]
        public string Invoice { get; set; }

        [JsonProperty("amount")]
        public long? Amount { get; set; }
    }

    [Serializable]
    public class PayInvoiceResponse
    {
        [JsonProperty("preimage")]
        public string Preimage { get; set; }
    }

    [Serializable]
    public class MakeInvoiceRequest
    {
        [JsonProperty("amount")]
        public long Amount { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }

        [JsonProperty("description_hash")]
        public string DescriptionHash { get; set; }

        [JsonProperty("expiry")]
        public long? Expiry { get; set; }
    }

    [Serializable]
    public class MakeInvoiceResponse
    {
        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("invoice")]
        public string Invoice { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }

        [JsonProperty("description_hash")]
        public string DescriptionHash { get; set; }

        [JsonProperty("preimage")]
        public string Preimage { get; set; }

        [JsonProperty("payment_hash")]
        public string PaymentHash { get; set; }

        [JsonProperty("amount")]
        public long Amount { get; set; }

        [JsonProperty("fees_paid")]
        public long FeesPaid { get; set; }

        [JsonProperty("created_at")]
        public long CreatedAt { get; set; }

        [JsonProperty("expires_at")]
        public long? ExpiresAt { get; set; }

        [JsonProperty("metadata")]
        public Dictionary<string, object> Metadata { get; set; }

        public MakeInvoiceResponse()
        {
            Metadata = new Dictionary<string, object>();
        }
    }

    [Serializable]
    public class GetBalanceResponse
    {
        [JsonProperty("balance")]
        public long Balance { get; set; }
    }

    [Serializable]
    public class GetInfoResponse
    {
        [JsonProperty("alias")]
        public string Alias { get; set; }

        [JsonProperty("color")]
        public string Color { get; set; }

        [JsonProperty("pubkey")]
        public string Pubkey { get; set; }

        [JsonProperty("network")]
        public string Network { get; set; }

        [JsonProperty("block_height")]
        public long BlockHeight { get; set; }

        [JsonProperty("block_hash")]
        public string BlockHash { get; set; }

        [JsonProperty("methods")]
        public string[] Methods { get; set; }
    }

    [Serializable]
    public class WalletInfoData
    {
        [JsonProperty("alias")]
        public string Alias { get; set; }

        [JsonProperty("color")]
        public string Color { get; set; }

        [JsonProperty("pubkey")]
        public string Pubkey { get; set; }

        [JsonProperty("network")]
        public string Network { get; set; }

        [JsonProperty("block_height")]
        public long BlockHeight { get; set; }

        [JsonProperty("block_hash")]
        public string BlockHash { get; set; }

        [JsonProperty("methods")]
        public string[] Methods { get; set; }

        [JsonProperty("notifications")]
        public string[] Notifications { get; set; }

        [JsonProperty("encryption")]
        public string Encryption { get; set; }
    }

    public static class NWCProtocol
    {
        public const int NWC_REQUEST_KIND = 23194;
        public const int NWC_RESPONSE_KIND = 23195;
        public const int INFO_EVENT_KIND = 13194;

        public static class Methods
        {
            public const string PAY_INVOICE = "pay_invoice";
            public const string MAKE_INVOICE = "make_invoice";
            public const string GET_BALANCE = "get_balance";
            public const string GET_INFO = "get_info";
            public const string LIST_TRANSACTIONS = "list_transactions";
            public const string LOOKUP_INVOICE = "lookup_invoice";
            public const string MULTI_PAY_INVOICE = "multi_pay_invoice";
            public const string MULTI_PAY_KEYSEND = "multi_pay_keysend";
            public const string SIGN_MESSAGE = "sign_message";
        }

        public static class ErrorCodes
        {
            public const string RATE_LIMITED = "RATE_LIMITED";
            public const string NOT_IMPLEMENTED = "NOT_IMPLEMENTED";
            public const string INSUFFICIENT_BALANCE = "INSUFFICIENT_BALANCE";
            public const string QUOTA_EXCEEDED = "QUOTA_EXCEEDED";
            public const string RESTRICTED = "RESTRICTED";
            public const string UNAUTHORIZED = "UNAUTHORIZED";
            public const string INTERNAL = "INTERNAL";
            public const string OTHER = "OTHER";
        }

        public static NostrEvent CreateRequestEvent(NWCRequest request, string walletPubkey, string connectionSecret, string clientPrivateKey)
        {
            var clientPubkey = NostrCrypto.GetPublicKey(connectionSecret);
            var content = JsonConvert.SerializeObject(request);
            var encryptedContent = NostrCrypto.EncryptForWallet(content, walletPubkey, connectionSecret);

            // Get the current encryption method to tag the request
            var encryptionTag = NostrCrypto.GetCurrentEncryptionTag();

            var nostrEvent = new NostrEvent
            {
                Pubkey = clientPubkey,
                CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                Kind = NWC_REQUEST_KIND,
                Tags = new string[][]
                {
                    new string[] { "p", walletPubkey },
                    new string[] { "encryption", encryptionTag }
                },
                Content = encryptedContent
            };

            var eventJson = SerializeEventForSigning(nostrEvent);
            nostrEvent.Id = NostrCrypto.CreateEventId(eventJson);
            nostrEvent.Signature = NostrCrypto.SignEvent(nostrEvent.Id, connectionSecret);

            return nostrEvent;
        }

        public static NWCResponse ParseResponseEvent(NostrEvent responseEvent, string clientPrivateKey)
        {
            try
            {
                var decryptedContent = NostrCrypto.DecryptFromWallet(responseEvent.Content, responseEvent.Pubkey, clientPrivateKey);
                return JsonConvert.DeserializeObject<NWCResponse>(decryptedContent);
            }
            catch (Exception ex)
            {
                DebugLogger.LogErrorToFile($"Failed to parse NWC response: {ex.Message}");
                return new NWCResponse
                {
                    Error = new NWCError
                    {
                        Code = ErrorCodes.INTERNAL,
                        Message = "Failed to decrypt response"
                    }
                };
            }
        }

        public static string SerializeEventForSigning(NostrEvent nostrEvent)
        {
            var array = new object[]
            {
                0,
                nostrEvent.Pubkey,
                nostrEvent.CreatedAt,
                nostrEvent.Kind,
                nostrEvent.Tags,
                nostrEvent.Content
            };

            var settings = new JsonSerializerSettings
            {
                Formatting = Formatting.None,
                NullValueHandling = NullValueHandling.Include
            };

            return JsonConvert.SerializeObject(array, settings);
        }

        public static string CreateSubscriptionMessage(string subscriptionId, Dictionary<string, object> filters)
        {
            var message = new object[] { "REQ", subscriptionId, filters };
            return JsonConvert.SerializeObject(message, Formatting.None);
        }

        public static string CreateEventMessage(NostrEvent nostrEvent)
        {
            try
            {
                var message = new object[] { "EVENT", nostrEvent };
                var jsonResult = JsonConvert.SerializeObject(message, Formatting.None);
                DebugLogger.LogToFile($"✅ WebSocket message created successfully: {jsonResult.Length} chars");
                return jsonResult;
            }
            catch (Exception ex)
            {
                DebugLogger.LogErrorToFile($"❌ Failed to create WebSocket message: {ex.Message}");
                DebugLogger.LogErrorToFile($"Event content length: {nostrEvent.Content?.Length ?? 0}");
                DebugLogger.LogErrorToFile($"Event content preview: {nostrEvent.Content?.Substring(0, Math.Min(100, nostrEvent.Content?.Length ?? 0))}...");
                throw;
            }
        }

        public static string CreateCloseMessage(string subscriptionId)
        {
            var message = new object[] { "CLOSE", subscriptionId };
            return JsonConvert.SerializeObject(message, Formatting.None);
        }

        public static NWCRequest CreatePayInvoiceRequest(string invoice, long? amount = null)
        {
            var request = new PayInvoiceRequest
            {
                Invoice = invoice,
                Amount = amount
            };

            return new NWCRequest
            {
                Method = Methods.PAY_INVOICE,
                Params = new Dictionary<string, object>
                {
                    ["invoice"] = request.Invoice
                }
            };
        }

        public static NWCRequest CreateMakeInvoiceRequest(long amount, string description = null, long? expiry = null)
        {
            return new NWCRequest
            {
                Method = Methods.MAKE_INVOICE,
                Params = new Dictionary<string, object>
                {
                    ["amount"] = amount,
                    ["description"] = description ?? "",
                    ["expiry"] = expiry
                }
            };
        }

        public static NWCRequest CreateGetBalanceRequest()
        {
            return new NWCRequest
            {
                Method = Methods.GET_BALANCE
            };
        }

        public static NWCRequest CreateGetInfoRequest()
        {
            return new NWCRequest
            {
                Method = Methods.GET_INFO
            };
        }
    }
}