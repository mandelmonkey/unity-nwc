using System;
using UnityEngine;

namespace NostrWalletConnect
{
    [Serializable]
    public class NWCConnectionString
    {
        public string WalletPubkey { get; private set; }
        public string RelayUrl { get; private set; }
        public string Secret { get; private set; }
        public string LnUrlP { get; private set; }

        public static NWCConnectionString Parse(string connectionString)
        {
            if (string.IsNullOrEmpty(connectionString))
            {
                throw new ArgumentException("Connection string cannot be null or empty");
            }

            if (!connectionString.StartsWith("nostr+walletconnect://"))
            {
                throw new ArgumentException("Invalid NWC connection string format");
            }

            var result = new NWCConnectionString();
            var uri = new Uri(connectionString);

            result.WalletPubkey = uri.Host;

            var query = uri.Query;
            if (string.IsNullOrEmpty(query))
            {
                throw new ArgumentException("Missing query parameters");
            }

            var queryParams = ParseQueryString(query.TrimStart('?'));

            string relayUrl;
            if (!queryParams.TryGetValue("relay", out relayUrl))
            {
                throw new ArgumentException("Missing relay parameter");
            }
            result.RelayUrl = relayUrl;

            string secret;
            if (!queryParams.TryGetValue("secret", out secret))
            {
                throw new ArgumentException("Missing secret parameter");
            }
            result.Secret = secret;

            string lnUrlP;
            queryParams.TryGetValue("lnurlp", out lnUrlP);
            result.LnUrlP = lnUrlP;

            if (result.WalletPubkey.Length != 64)
            {
                throw new ArgumentException("Invalid wallet pubkey length");
            }

            if (result.Secret.Length != 64)
            {
                throw new ArgumentException("Invalid secret length");
            }

            return result;
        }

        private static System.Collections.Generic.Dictionary<string, string> ParseQueryString(string query)
        {
            var result = new System.Collections.Generic.Dictionary<string, string>();

            if (string.IsNullOrEmpty(query))
                return result;

            var pairs = query.Split('&');
            foreach (var pair in pairs)
            {
                var keyValue = pair.Split('=');
                if (keyValue.Length == 2)
                {
                    var key = Uri.UnescapeDataString(keyValue[0]);
                    var value = Uri.UnescapeDataString(keyValue[1]);
                    result[key] = value;
                }
            }

            return result;
        }
    }
}