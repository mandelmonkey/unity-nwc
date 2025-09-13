using System;
using System.Collections;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using Newtonsoft.Json;

namespace NostrWalletConnect
{
    [Serializable]
    public class ServerResponse<T>
    {
        public bool success;
        public T data;
        public string error;
        public string message;
    }

    [Serializable]
    public class ConnectionResponse
    {
        public string walletPubkey;
        public string clientPubkey;
    }

    [Serializable]
    public class WalletInfoResponse
    {
        public string alias;
        public string color;
        public string pubkey;
        public string network;
        public long block_height;
        public string block_hash;
        public string[] methods;
    }

    [Serializable]
    public class BalanceResponse
    {
        public long balance;
    }

    [Serializable]
    public class BalanceData
    {
        public long balance;
    }

    [Serializable]
    public class InvoiceResponse
    {
        public string type;
        public string invoice;
        public string description;
        public string description_hash;
        public string preimage;
        public string payment_hash;
        public long amount;
        public long fees_paid;
        public long created_at;
        public long expires_at;
    }

    [Serializable]
    public class PaymentResponse
    {
        public string preimage;
        public long fees_paid;
    }

    [Serializable]
    public class InvoiceData
    {
        public InvoiceResponse invoice;
    }

    [Serializable]
    public class PaymentData
    {
        public PaymentResponse payment;
    }

    [Serializable]
    public class GameInvoiceData
    {
        public InvoiceResponse gameInvoice;
        public InvoiceResponse entryInvoice;
    }

    public class ServerNWCClient : MonoBehaviour
    {
        [Header("Server Configuration")]
        [SerializeField] private string serverUrl = "http://localhost:3000";
        [SerializeField] private string apiKey = "";
        [SerializeField] private float requestTimeoutSeconds = 30f;

        [Header("Connection")]
        [SerializeField] private string connectionString = "";
        [SerializeField] private bool autoConnect = true;

        private bool _isConnected = false;
        private string _walletPubkey = "";
        private string _clientPubkey = "";

        public event Action OnConnected;
        public event Action OnDisconnected;
        public event Action<string> OnError;

        public bool IsConnected => _isConnected;
        public string WalletPubkey => _walletPubkey;
        public string ClientPubkey => _clientPubkey;
        public string ServerUrl => serverUrl;

        private void Start()
        {
            if (autoConnect && !string.IsNullOrEmpty(connectionString))
            {
                _ = ConnectAsync(connectionString);
            }
        }

        public async Task<bool> ConnectAsync(string nwcConnectionString)
        {
            try
            {
                connectionString = nwcConnectionString;

                var requestData = new
                {
                    connectionString = nwcConnectionString
                };

                var response = await PostRequestAsync<ConnectionResponse>("/api/wallet/connect", requestData);

                if (response.success)
                {
                    _walletPubkey = response.data.walletPubkey;
                    _clientPubkey = response.data.clientPubkey;
                    _isConnected = true;

                    OnConnected?.Invoke();
                    Debug.Log($"Connected to wallet via server. Wallet: {_walletPubkey}");
                    return true;
                }
                else
                {
                    OnError?.Invoke(response.error ?? "Unknown connection error");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Connection failed: {ex.Message}");
                OnError?.Invoke($"Connection failed: {ex.Message}");
                return false;
            }
        }

        public void Disconnect()
        {
            _isConnected = false;
            _walletPubkey = "";
            _clientPubkey = "";
            OnDisconnected?.Invoke();
        }

        public async Task<WalletInfoResponse> GetWalletInfoAsync()
        {
            if (!_isConnected)
                throw new InvalidOperationException("Not connected to wallet");

            var requestData = new { connectionString };
            var response = await PostRequestAsync<WalletInfoResponse>("/api/wallet/info", requestData);

            if (response.success)
            {
                return response.data;
            }
            else
            {
                throw new Exception(response.error ?? "Failed to get wallet info");
            }
        }

        public async Task<long> GetBalanceAsync()
        {
            if (!_isConnected)
                throw new InvalidOperationException("Not connected to wallet");

            var requestData = new { connectionString };
            var response = await PostRequestAsync<BalanceData>("/api/wallet/balance", requestData);

            if (response.success)
            {
                return response.data.balance;
            }
            else
            {
                throw new Exception(response.error ?? "Failed to get balance");
            }
        }

        public async Task<InvoiceResponse> CreateInvoiceAsync(long amount, string description = "", long? expiry = null)
        {
            if (!_isConnected)
                throw new InvalidOperationException("Not connected to wallet");

            var requestData = new
            {
                connectionString,
                amount,
                description,
                expiry
            };

            var response = await PostRequestAsync<InvoiceData>("/api/wallet/invoice/create", requestData);

            if (response.success)
            {
                return response.data.invoice;
            }
            else
            {
                throw new Exception(response.error ?? "Failed to create invoice");
            }
        }

        public async Task<PaymentResponse> PayInvoiceAsync(string invoice, long? amount = null)
        {
            if (!_isConnected)
                throw new InvalidOperationException("Not connected to wallet");

            var requestData = new
            {
                connectionString,
                invoice,
                amount
            };

            var response = await PostRequestAsync<PaymentData>("/api/wallet/invoice/pay", requestData);

            if (response.success)
            {
                return response.data.payment;
            }
            else
            {
                throw new Exception(response.error ?? "Failed to pay invoice");
            }
        }

        // Game-specific methods
        public async Task<InvoiceResponse> CreateGamePrizeInvoiceAsync(string gameId, string playerId, long prizeAmount, string gameType = "")
        {
            var requestData = new
            {
                gameId,
                playerId,
                prizeAmount,
                gameType
            };

            var response = await PostRequestAsync<GameInvoiceData>("/api/lightning/game/prize-invoice", requestData);

            if (response.success)
            {
                return response.data.gameInvoice;
            }
            else
            {
                throw new Exception(response.error ?? "Failed to create game prize invoice");
            }
        }

        public async Task<InvoiceResponse> CreateGameEntryInvoiceAsync(string gameId, string playerId, long entryFee, string gameType = "")
        {
            var requestData = new
            {
                gameId,
                playerId,
                entryFee,
                gameType
            };

            var response = await PostRequestAsync<GameInvoiceData>("/api/lightning/game/entry-invoice", requestData);

            if (response.success)
            {
                return response.data.entryInvoice;
            }
            else
            {
                throw new Exception(response.error ?? "Failed to create game entry invoice");
            }
        }

        private async Task<ServerResponse<T>> PostRequestAsync<T>(string endpoint, object data)
        {
            using (var request = new UnityWebRequest($"{serverUrl}{endpoint}", "POST"))
            {
                string jsonData = JsonConvert.SerializeObject(data);
                byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonData);
                request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");

                if (!string.IsNullOrEmpty(apiKey))
                {
                    request.SetRequestHeader("X-API-Key", apiKey);
                }

                request.timeout = (int)requestTimeoutSeconds;

                var operation = request.SendWebRequest();

                while (!operation.isDone)
                {
                    await Task.Yield();
                }

                if (request.result == UnityWebRequest.Result.Success)
                {
                    string responseText = request.downloadHandler.text;
                    try
                    {
                        return JsonConvert.DeserializeObject<ServerResponse<T>>(responseText);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"Failed to parse server response: {ex.Message}\nResponse: {responseText}");
                        return new ServerResponse<T>
                        {
                            success = false,
                            error = $"Failed to parse response: {ex.Message}"
                        };
                    }
                }
                else
                {
                    string errorResponse = request.downloadHandler?.text ?? request.error;
                    Debug.LogError($"Server request failed: {request.error}\nResponse: {errorResponse}");

                    try
                    {
                        var errorObj = JsonConvert.DeserializeObject<ServerResponse<T>>(errorResponse);
                        return errorObj;
                    }
                    catch
                    {
                        return new ServerResponse<T>
                        {
                            success = false,
                            error = $"Server error: {request.error}"
                        };
                    }
                }
            }
        }

        public void SetConnectionString(string nwcConnectionString)
        {
            connectionString = nwcConnectionString;
        }

        public void SetServerUrl(string url)
        {
            serverUrl = url;
        }

        public void SetApiKey(string key)
        {
            apiKey = key;
        }

        #region Unity Inspector Helper Methods

        [ContextMenu("Connect")]
        public void ConnectFromInspector()
        {
            if (!string.IsNullOrEmpty(connectionString))
            {
                _ = ConnectAsync(connectionString);
            }
            else
            {
                Debug.LogWarning("Connection string is empty");
            }
        }

        [ContextMenu("Disconnect")]
        public void DisconnectFromInspector()
        {
            Disconnect();
        }

        [ContextMenu("Get Info")]
        public void GetInfoFromInspector()
        {
            _ = TestGetInfo();
        }

        [ContextMenu("Get Balance")]
        public void GetBalanceFromInspector()
        {
            _ = TestGetBalance();
        }

        [ContextMenu("Test Server Connection")]
        public void TestServerConnection()
        {
            _ = TestServerHealth();
        }

        private async Task TestGetInfo()
        {
            try
            {
                var info = await GetWalletInfoAsync();
                Debug.Log($"Wallet Info: {JsonConvert.SerializeObject(info, Formatting.Indented)}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"Get Info Exception: {ex.Message}");
            }
        }

        private async Task TestGetBalance()
        {
            try
            {
                var balance = await GetBalanceAsync();
                Debug.Log($"Balance: {balance} millisatoshis");
            }
            catch (Exception ex)
            {
                Debug.LogError($"Get Balance Exception: {ex.Message}");
            }
        }

        private async Task TestServerHealth()
        {
            try
            {
                using (var request = UnityWebRequest.Get($"{serverUrl}/health"))
                {
                    var operation = request.SendWebRequest();

                    while (!operation.isDone)
                    {
                        await Task.Yield();
                    }

                    if (request.result == UnityWebRequest.Result.Success)
                    {
                        Debug.Log($"Server Health: {request.downloadHandler.text}");
                    }
                    else
                    {
                        Debug.LogError($"Server Health Check Failed: {request.error}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Server Health Check Exception: {ex.Message}");
            }
        }

        #endregion
    }
}