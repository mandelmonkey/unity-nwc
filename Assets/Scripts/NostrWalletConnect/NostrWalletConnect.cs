using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using Newtonsoft.Json;
using System.Collections;

namespace NostrWalletConnect
{
    public class NostrWalletConnect : MonoBehaviour
    {
        [SerializeField] private string connectionString;
        [SerializeField] private bool autoConnect = true;
        [SerializeField] private bool debugMode = true;

        private NWCConnectionString _connection;
        private NostrWebSocket _webSocket;
        private string _clientPrivateKey;
        private string _clientPublicKey;
        private string _currentSubscriptionId;
        private readonly Dictionary<string, TaskCompletionSource<NWCResponse>> _pendingRequests =
            new Dictionary<string, TaskCompletionSource<NWCResponse>>();
        private readonly Queue<NostrEvent> _pendingNWCResponses = new Queue<NostrEvent>();

        public event Action OnConnected;
        public event Action OnDisconnected;
        public event Action<string> OnError;
        public event Action<NWCResponse> OnResponse;

        public bool IsConnected => _webSocket?.IsConnected ?? false;
        public string WalletPubkey => _connection?.WalletPubkey;
        public string ClientPubkey => _clientPublicKey;

        private void Awake()
        {
            _webSocket = new NostrWebSocket();
            _webSocket.OnConnected += HandleConnected;
            _webSocket.OnDisconnected += HandleDisconnected;
            _webSocket.OnMessageReceived += HandleMessageReceived;
            _webSocket.OnError += HandleError;

            _clientPrivateKey = NostrCrypto.GeneratePrivateKey();
            _clientPublicKey = NostrCrypto.GetPublicKey(_clientPrivateKey);

            if (debugMode)
            {
                Debug.Log($"Client Private Key: {_clientPrivateKey}");
                Debug.Log($"Client Public Key: {_clientPublicKey}");
            }
        }

        private void Start()
        {
            if (autoConnect && !string.IsNullOrEmpty(connectionString))
            {
                _ = ConnectAsync(connectionString);
            }
        }

        private void Update()
        {
            // Process queued NWC responses on the main thread
            lock (_pendingNWCResponses)
            {
                while (_pendingNWCResponses.Count > 0)
                {
                    var response = _pendingNWCResponses.Dequeue();
                    try
                    {
                        HandleNWCResponse(response);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"Error processing NWC response on main thread: {ex.Message}");
                    }
                }
            }
        }

        public async Task<bool> ConnectAsync(string nwcConnectionString)
        {
            try
            {
                _connection = NWCConnectionString.Parse(nwcConnectionString);
                connectionString = nwcConnectionString;

                if (debugMode)
                {
                    Debug.Log($"Connecting to wallet: {_connection.WalletPubkey}");
                    Debug.Log($"Relay: {_connection.RelayUrl}");
                }

                var connected = await _webSocket.ConnectAsync(_connection.RelayUrl);
                if (connected)
                {
                    await SubscribeToResponses();

                    // Auto-detect preferred encryption version
                    try
                    {
                        await DetectPreferredEncryptionVersion();
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"Failed to detect encryption version: {ex.Message}");
                        // Continue with default version
                    }
                }

                return connected;
            }
            catch (Exception ex)
            {
                Debug.LogError($"Connection failed: {ex.Message}");
                OnError?.Invoke($"Connection failed: {ex.Message}");
                return false;
            }
        }

        public async Task DisconnectAsync()
        {
            try
            {
                if (!string.IsNullOrEmpty(_currentSubscriptionId))
                {
                    var closeMessage = NWCProtocol.CreateCloseMessage(_currentSubscriptionId);
                    await _webSocket.SendMessageAsync(closeMessage);
                }

                await _webSocket.DisconnectAsync();
            }
            catch (Exception ex)
            {
                Debug.LogError($"Disconnect error: {ex.Message}");
            }
        }

        public async Task<NWCResponse> PayInvoiceAsync(string invoice, long? amount = null)
        {
            var request = NWCProtocol.CreatePayInvoiceRequest(invoice, amount);
            return await SendRequestAsync(request);
        }

        public async Task<NWCResponse> MakeInvoiceAsync(long amount, string description = null, long? expiry = null)
        {
            var request = NWCProtocol.CreateMakeInvoiceRequest(amount, description, expiry);
            return await SendRequestAsync(request);
        }

        public async Task<NWCResponse> GetBalanceAsync()
        {
            var request = NWCProtocol.CreateGetBalanceRequest();
            return await SendRequestAsync(request);
        }

        public async Task<NWCResponse> GetInfoAsync()
        {
            var request = NWCProtocol.CreateGetInfoRequest();
            return await SendRequestAsync(request);
        }

        public async Task DetectPreferredEncryptionVersion()
        {
            DebugLogger.LogToFile("🔍 Detecting wallet's preferred encryption method...");

            // Analysis: wallet says "no initialization vector" for NIP-44 requests
            // But successfully sends NIP-44 responses - it uses hybrid encryption!
            DebugLogger.LogToFile("🔀 Wallet uses hybrid encryption: expects NIP-04 requests, sends NIP-44 responses");
            NostrCrypto.ForceNip04Only();  // Wallet expects NIP-04 requests but sends NIP-44 responses
        }

        private async Task<NWCResponse> SendRequestAsync(NWCRequest request)
        {
            if (_connection == null || !IsConnected)
            {
                throw new InvalidOperationException("Not connected to wallet");
            }

            try
            {
                var eventRequest = NWCProtocol.CreateRequestEvent(request, _connection.WalletPubkey, _connection.Secret, _clientPrivateKey);
                var message = NWCProtocol.CreateEventMessage(eventRequest);

                var tcs = new TaskCompletionSource<NWCResponse>();
                _pendingRequests[eventRequest.Id] = tcs;

                await _webSocket.SendMessageAsync(message);

                if (debugMode)
                {
                    Debug.Log($"Sent request: {request.Method}");
                }

                var timeoutTask = Task.Delay(TimeSpan.FromSeconds(30));
                var completedTask = await Task.WhenAny(tcs.Task, timeoutTask);

                if (completedTask == timeoutTask)
                {
                    _pendingRequests.Remove(eventRequest.Id);
                    throw new TimeoutException("Request timed out");
                }

                return await tcs.Task;
            }
            catch (Exception ex)
            {
                Debug.LogError($"Request failed: {ex.Message}");
                throw;
            }
        }

        private async Task SubscribeToResponses()
        {
            _currentSubscriptionId = Guid.NewGuid().ToString();

            // Use the connection secret's pubkey (same as used in requests)
            var connectionPubkey = NostrCrypto.GetPublicKey(_connection.Secret);

            var filters = new Dictionary<string, object>
            {
                ["kinds"] = new int[] { NWCProtocol.NWC_RESPONSE_KIND },
                ["#p"] = new string[] { connectionPubkey },
                ["authors"] = new string[] { _connection.WalletPubkey }
            };

            var subscriptionMessage = NWCProtocol.CreateSubscriptionMessage(_currentSubscriptionId, filters);
            await _webSocket.SendMessageAsync(subscriptionMessage);

            if (debugMode)
            {
                Debug.Log($"Subscribed with ID: {_currentSubscriptionId}");
            }
        }

        private void HandleConnected()
        {
            if (debugMode)
            {
                Debug.Log("Connected to Nostr relay");
            }
            OnConnected?.Invoke();
        }

        private void HandleDisconnected()
        {
            if (debugMode)
            {
                Debug.Log("Disconnected from Nostr relay");
            }
            OnDisconnected?.Invoke();

            foreach (var pendingRequest in _pendingRequests.Values)
            {
                pendingRequest.SetException(new InvalidOperationException("Connection lost"));
            }
            _pendingRequests.Clear();
        }

        private void HandleMessageReceived(string message)
        {
            try
            {
                if (debugMode)
                {
                    Debug.Log($"Raw message received: {message}");
                }

                var messageArray = JsonConvert.DeserializeObject<object[]>(message);
                if (messageArray == null || messageArray.Length < 2)
                    return;

                var messageType = messageArray[0].ToString();

                if (debugMode)
                {
                    Debug.Log($"Message type: {messageType}");
                }

                if (messageType == "EVENT")
                {
                    if (messageArray.Length >= 3)
                    {
                        var eventJson = JsonConvert.SerializeObject(messageArray[2]);
                        var nostrEvent = JsonConvert.DeserializeObject<NostrEvent>(eventJson);

                        if (debugMode)
                        {
                            Debug.Log($"Received event - Kind: {nostrEvent.Kind}, From: {nostrEvent.Pubkey}");
                        }

                        if (nostrEvent.Kind == NWCProtocol.NWC_RESPONSE_KIND)
                        {
                            if (debugMode)
                            {
                                Debug.Log("Queueing NWC response event for main thread processing");
                            }
                            lock (_pendingNWCResponses)
                            {
                                _pendingNWCResponses.Enqueue(nostrEvent);
                            }
                        }
                        else
                        {
                            if (debugMode)
                            {
                                Debug.Log($"Ignoring event with kind {nostrEvent.Kind} (expected {NWCProtocol.NWC_RESPONSE_KIND})");
                            }
                        }
                    }
                }
                else if (messageType == "EOSE")
                {
                    if (debugMode)
                    {
                        Debug.Log("End of stored events");
                    }
                }
                else if (messageType == "OK")
                {
                    if (debugMode && messageArray.Length >= 4)
                    {
                        Debug.Log($"OK response: EventId={messageArray[1]}, Success={messageArray[2]}, Message={messageArray[3]}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error handling message: {ex.Message}");
            }
        }

        private void HandleNWCResponse(NostrEvent responseEvent)
        {
            try
            {
                if (debugMode)
                {
                    Debug.Log($"Attempting to decrypt response with tags: {JsonConvert.SerializeObject(responseEvent.Tags)}");
                }

                DebugLogger.LogSeparator();
                DebugLogger.LogToFile("=== HANDLING NWC RESPONSE ===");
                DebugLogger.LogToFile($"Response event ID: {responseEvent.Id}");
                DebugLogger.LogToFile($"Response from pubkey: {responseEvent.Pubkey}");
                DebugLogger.LogToFile($"Response content length: {responseEvent.Content?.Length ?? 0}");
                DebugLogger.LogToFile($"Response content: {responseEvent.Content}");
                DebugLogger.LogToFile($"Connection secret (first 8): {_connection.Secret?.Substring(0, 8)}...");

                var response = NWCProtocol.ParseResponseEvent(responseEvent, _connection.Secret);

                if (debugMode)
                {
                    Debug.Log($"Decrypted response: {JsonConvert.SerializeObject(response)}");
                }

                OnResponse?.Invoke(response);

                var requestIdTag = FindTag(responseEvent.Tags, "e");
                if (debugMode)
                {
                    Debug.Log($"Looking for request ID tag 'e', found: {requestIdTag}");
                    Debug.Log($"Pending requests: {string.Join(", ", _pendingRequests.Keys)}");
                }

                if (requestIdTag != null && _pendingRequests.TryGetValue(requestIdTag, out var tcs))
                {
                    if (debugMode)
                    {
                        Debug.Log($"Found matching pending request for {requestIdTag}, resolving...");
                    }
                    _pendingRequests.Remove(requestIdTag);
                    tcs.SetResult(response);
                }
                else
                {
                    if (debugMode)
                    {
                        Debug.LogWarning($"No pending request found for response event with request ID: {requestIdTag}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error handling NWC response: {ex.Message}");
            }
        }

        private string FindTag(string[][] tags, string tagType)
        {
            foreach (var tag in tags)
            {
                if (tag.Length >= 2 && tag[0] == tagType)
                {
                    return tag[1];
                }
            }
            return null;
        }

        private void HandleError(string error)
        {
            if (debugMode)
            {
                Debug.LogError($"WebSocket error: {error}");
            }
            OnError?.Invoke(error);
        }

        public void SetConnectionString(string nwcConnectionString)
        {
            connectionString = nwcConnectionString;
        }

        public void SetDebugMode(bool enabled)
        {
            debugMode = enabled;
        }

        private void OnDestroy()
        {
            _ = DisconnectAsync();
            _webSocket?.Dispose();
        }

        #region Unity Inspector Helper Methods

        [ContextMenu("Show Log File Path")]
        public void ShowLogFilePath()
        {
            var logPath = DebugLogger.GetLogFilePath();
            Debug.Log($"NWC Debug log file: {logPath}");

            // Also log some system info (safe to call from main thread)
            DebugLogger.LogToFile($"Unity Application.persistentDataPath: {UnityEngine.Application.persistentDataPath}");
            DebugLogger.LogToFile($"Unity Application.dataPath: {UnityEngine.Application.dataPath}");

            // Open the containing folder (Windows/Mac compatible)
            try
            {
                var directory = System.IO.Path.GetDirectoryName(logPath);
                if (System.IO.Directory.Exists(directory))
                {
                    #if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
                    System.Diagnostics.Process.Start("explorer.exe", directory.Replace('/', '\\'));
                    #elif UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
                    System.Diagnostics.Process.Start("open", directory);
                    #endif
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"Could not open log directory: {ex.Message}");
            }
        }

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
            _ = DisconnectAsync();
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

        private async Task TestGetInfo()
        {
            try
            {
                var response = await GetInfoAsync();
                if (response.Error != null)
                {
                    Debug.LogError($"Get Info Error: {response.Error.Code} - {response.Error.Message}");
                }
                else
                {
                    Debug.Log($"Wallet Info: {JsonConvert.SerializeObject(response.Result, Formatting.Indented)}");
                }
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
                var response = await GetBalanceAsync();
                if (response.Error != null)
                {
                    Debug.LogError($"Get Balance Error: {response.Error.Code} - {response.Error.Message}");
                }
                else
                {
                    Debug.Log($"Balance: {JsonConvert.SerializeObject(response.Result, Formatting.Indented)}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Get Balance Exception: {ex.Message}");
            }
        }

        #endregion
    }
}