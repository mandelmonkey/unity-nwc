using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using Newtonsoft.Json;
using System.Collections;
using System.Linq;

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
        private readonly Dictionary<string, long> _requestTimestamps =
            new Dictionary<string, long>();
        private readonly Queue<NostrEvent> _pendingNWCResponses = new Queue<NostrEvent>();

        public event Action OnConnected;
        public event Action OnDisconnected;
        public event Action<string> OnError;
        public event Action<NWCResponse> OnResponse;
        public event Action<NWCResponse, string> OnResponseWithCache;

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

                    // Get wallet info and capabilities
                    try
                    {
                        await GetWalletInfoAsync();
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"Failed to get wallet info: {ex.Message}");
                        // Continue with default settings
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

        public async Task RequestWalletInfoEvent()
        {
            DebugLogger.LogToFile("📡 Requesting wallet info event (kind 13194)...");

            if (_connection == null || !IsConnected)
            {
                DebugLogger.LogToFile("❌ Not connected to relay - cannot request info event");
                OnError?.Invoke("Not connected to relay");
                return;
            }

            try
            {
                // Subscribe to kind 13194 events from the wallet
                var filter = new
                {
                    kinds = new[] { 13194 },
                    authors = new[] { _connection.WalletPubkey },
                    limit = 1
                };

                var subscriptionId = "info_event_" + Guid.NewGuid().ToString("N")[..8];
                var subscribeMessage = new object[]
                {
                    "REQ",
                    subscriptionId,
                    filter
                };

                var subscribeJson = JsonConvert.SerializeObject(subscribeMessage);
                DebugLogger.LogToFile($"📡 Subscribing to wallet info events: {subscribeJson}");

                await _webSocket.SendMessageAsync(subscribeJson);

                // Note: The wallet should respond with a kind 13194 event containing:
                // {
                //   "kind": 13194,
                //   "tags": [
                //     ["encryption", "nip44_v2 nip04"],
                //     ["notifications", "payment_received payment_sent"]
                //   ],
                //   "content": "pay_invoice get_balance make_invoice lookup_invoice list_transactions get_info notifications"
                // }

                DebugLogger.LogToFile("✅ Info event subscription sent - watch for kind 13194 events in logs");
            }
            catch (Exception ex)
            {
                DebugLogger.LogToFile($"❌ Failed to request wallet info event: {ex.Message}");
                OnError?.Invoke($"Failed to request info event: {ex.Message}");
            }
        }

        private TaskCompletionSource<bool> _walletInfoComplete;

        public async Task GetWalletInfoAsync()
        {
            DebugLogger.LogToFile("🔍 Getting wallet info and capabilities by requesting info event...");

            _walletInfoComplete = new TaskCompletionSource<bool>();

            try
            {
                // Request wallet info event to get capabilities, methods, and encryption preferences
                await RequestWalletInfoEvent();

                // Wait for the info event response to be processed
                DebugLogger.LogToFile("📡 Info event requested - waiting for wallet response with capabilities and encryption preferences");

                // Wait up to 10 seconds for wallet info to be received
                var timeout = Task.Delay(10000);
                var completed = await Task.WhenAny(_walletInfoComplete.Task, timeout);

                if (completed == timeout)
                {
                    DebugLogger.LogToFile("⏰ Timeout waiting for wallet info event response");
                    throw new TimeoutException("Timeout waiting for wallet info");
                }

                DebugLogger.LogToFile("✅ Wallet info retrieval completed successfully");
            }
            catch (Exception ex)
            {
                DebugLogger.LogToFile($"⚠️ Failed to get wallet info: {ex.Message}"); 
            }
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

                // Log the full JSON payload being sent to relay
                DebugLogger.LogToFile($"📡 Sending {request.Method} request to relay:");
                DebugLogger.LogToFile($"Event ID: {eventRequest.Id}");
                DebugLogger.LogToFile($"Event Tags: {string.Join(", ", eventRequest.Tags?.Select(tag => $"[{string.Join(", ", tag)}]") ?? new string[0])}");
                DebugLogger.LogToFile($"Full JSON payload: {message}");

                var tcs = new TaskCompletionSource<NWCResponse>();
                _pendingRequests[eventRequest.Id] = tcs;
                _requestTimestamps[eventRequest.Id] = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

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
                    _requestTimestamps.Remove(eventRequest.Id);
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
            _requestTimestamps.Clear();
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
                        else if (nostrEvent.Kind == NWCProtocol.INFO_EVENT_KIND)
                        {
                            if (debugMode)
                            {
                                Debug.Log("Received wallet info event (kind 13194) - processing encryption standards");
                            }
                            ProcessInfoEvent(nostrEvent);
                        }
                        else
                        {
                            if (debugMode)
                            {
                                Debug.Log($"Ignoring event with kind {nostrEvent.Kind} (expected {NWCProtocol.NWC_RESPONSE_KIND} or {NWCProtocol.INFO_EVENT_KIND})");
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

                // Determine if this response is cached/old or fresh
                var requestIdTag = FindTag(responseEvent.Tags, "e");
                bool isCached = false;
                string cacheInfo = "";

                if (requestIdTag != null && _requestTimestamps.TryGetValue(requestIdTag, out var requestTime))
                {
                    var responseTime = responseEvent.CreatedAt;
                    var timeDiff = responseTime - requestTime;

                    // If response was created before our request, it's definitely cached
                    if (timeDiff < 0)
                    {
                        isCached = true;
                        cacheInfo = $"📦 CACHED (created {Math.Abs(timeDiff)}s before request)";
                    }
                    else if (timeDiff < 2) // Very quick response, likely fresh
                    {
                        cacheInfo = $"🆕 FRESH (responded in {timeDiff}s)";
                    }
                    else
                    {
                        cacheInfo = $"⏱️ DELAYED (responded after {timeDiff}s)";
                    }
                }
                else
                {
                    // No matching request found, likely cached from previous session
                    isCached = true;
                    cacheInfo = "📦 CACHED (no matching request found)";
                }

                DebugLogger.LogToFile($"🏷️ Response status: {cacheInfo}");
                DebugLogger.LogToFile($"Response created_at: {responseEvent.CreatedAt} ({DateTimeOffset.FromUnixTimeSeconds(responseEvent.CreatedAt):yyyy-MM-dd HH:mm:ss} UTC)");

                if (debugMode)
                {
                    Debug.Log($"Decrypted response ({cacheInfo}): {JsonConvert.SerializeObject(response)}");
                    Debug.Log($"Looking for request ID tag 'e', found: {requestIdTag}");
                    Debug.Log($"Pending requests: {string.Join(", ", _pendingRequests.Keys)}");
                }

                OnResponse?.Invoke(response);
                OnResponseWithCache?.Invoke(response, cacheInfo);

                if (requestIdTag != null && _pendingRequests.TryGetValue(requestIdTag, out var tcs))
                {
                    if (debugMode)
                    {
                        Debug.Log($"Found matching pending request for {requestIdTag}, resolving...");
                    }
                    _pendingRequests.Remove(requestIdTag);
                    _requestTimestamps.Remove(requestIdTag); // Clean up timestamp tracking
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
            if (string.IsNullOrEmpty(connectionString))
            {
                connectionString = PlayerPrefs.GetString("NWC_ConnectionString");
                
                Debug.Log($"NWC Connection string: {connectionString}");
            }
        
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

        private void ProcessInfoEvent(NostrEvent infoEvent)
        {
            try
            {
                DebugLogger.LogToFile($"🔍 Processing wallet info event from {infoEvent.Pubkey}");
                DebugLogger.LogToFile($"Info event content: {infoEvent.Content}");

                // Parse supported methods from content (space-separated string)
                string[] methods = infoEvent.Content.Split(new char[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                DebugLogger.LogToFile($"✅ Wallet supports {methods.Length} methods: {string.Join(", ", methods)}");

                // Parse wallet capabilities from tags (encryption standards and notifications)
                string encryptionStandards = null;
                string[] notifications = null;

                if (infoEvent.Tags != null)
                {
                    foreach (var tag in infoEvent.Tags)
                    {
                        if (tag != null && tag.Length >= 2)
                        {
                            if (tag[0] == "encryption")
                            {
                                encryptionStandards = tag[1];
                                DebugLogger.LogToFile($"🔍 Found encryption tag: {encryptionStandards}");
                            }
                            else if (tag[0] == "notifications")
                            {
                                notifications = tag[1].Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                                DebugLogger.LogToFile($"🔔 Wallet notifications: {string.Join(", ", notifications)}");
                            }
                        }
                    }
                }

                // Determine supported encryption methods and set preferences
                bool supportsNip44v2 = false;
                bool supportsNip44 = false;
                bool supportsNip04 = false;

                if (!string.IsNullOrEmpty(encryptionStandards))
                {
                    var encryptionLower = encryptionStandards.ToLower();

                    // Check for specific versions first
                    supportsNip44v2 = encryptionLower.Contains("nip44_v2") || encryptionLower.Contains("nip-44_v2") ||
                                     encryptionLower.Contains("nip44v2") || encryptionLower.Contains("nip-44v2");

                    // Check for generic NIP-44 (version 1)
                    supportsNip44 = (encryptionLower.Contains("nip44") || encryptionLower.Contains("nip-44")) && !supportsNip44v2;

                    // Check for NIP-04
                    supportsNip04 = encryptionLower.Contains("nip04") || encryptionLower.Contains("nip-04");

                    DebugLogger.LogToFile($"🔐 Encryption detection - NIP-44 v2: {supportsNip44v2}, NIP-44 v1: {supportsNip44}, NIP-04: {supportsNip04}");
                }

                // Configure encryption mode based on wallet capabilities
                // Priority order: NIP-44 v2 > NIP-44 v1 > NIP-04
                if (supportsNip44v2)
                {
                    DebugLogger.LogToFile("🔐 Wallet supports NIP-44 v2 - using NIP-44 v2 (highest priority)");
                    NostrCrypto.SetPreferredNip44Version(2);
                }
                else if (supportsNip44)
                {
                    DebugLogger.LogToFile("🔐 Wallet supports NIP-44 v1 - using NIP-44 v1");
                    NostrCrypto.SetPreferredNip44Version(1);
                }
                else if (supportsNip04)
                {
                    DebugLogger.LogToFile("🔐 Wallet supports NIP-04 only - using NIP-04 (fallback)");
                    NostrCrypto.ForceNip04Only();
                }
                else
                {
                    DebugLogger.LogToFile("⚠️ No encryption standards found in info event - using hybrid mode"); 
                }

                // Signal that wallet info retrieval is complete
                _walletInfoComplete?.TrySetResult(true);
            }
            catch (Exception ex)
            {
                DebugLogger.LogErrorToFile($"❌ Error processing info event: {ex.Message}");
                DebugLogger.LogToFile("🔀 Falling back to hybrid encryption mode"); 

                // Signal completion even on error
                _walletInfoComplete?.TrySetResult(false);
            }
        }
    }
}