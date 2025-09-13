using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace NostrWalletConnect
{
    public class NostrWebSocket : IDisposable
    {
        private ClientWebSocket _webSocket;
        private CancellationTokenSource _cancellationTokenSource;
        private readonly Queue<string> _messageQueue = new Queue<string>();
        private readonly object _lockObject = new object();
        private bool _isConnected = false;

        public event Action OnConnected;
        public event Action OnDisconnected;
        public event Action<string> OnMessageReceived;
        public event Action<string> OnError;

        public bool IsConnected => _isConnected;

        public async Task<bool> ConnectAsync(string uri)
        {
            try
            {
                if (_webSocket?.State == WebSocketState.Open)
                {
                    await DisconnectAsync();
                }

                _webSocket = new ClientWebSocket();
                _cancellationTokenSource = new CancellationTokenSource();

                await _webSocket.ConnectAsync(new Uri(uri), _cancellationTokenSource.Token);
                _isConnected = true;

                OnConnected?.Invoke();
                Debug.Log($"Connected to relay: {uri}");

                _ = Task.Run(ReceiveLoop);

                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"WebSocket connection error: {ex.Message}");
                OnError?.Invoke($"Connection error: {ex.Message}");
                return false;
            }
        }

        public async Task DisconnectAsync()
        {
            try
            {
                _isConnected = false;
                _cancellationTokenSource?.Cancel();

                if (_webSocket?.State == WebSocketState.Open)
                {
                    await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing connection", CancellationToken.None);
                }

                _webSocket?.Dispose();
                _cancellationTokenSource?.Dispose();

                OnDisconnected?.Invoke();
                Debug.Log("Disconnected from relay");
            }
            catch (Exception ex)
            {
                Debug.LogError($"WebSocket disconnection error: {ex.Message}");
                OnError?.Invoke($"Disconnection error: {ex.Message}");
            }
        }

        public async Task SendMessageAsync(string message)
        {
            try
            {
                if (_webSocket?.State != WebSocketState.Open)
                {
                    throw new InvalidOperationException("WebSocket is not connected");
                }

                var buffer = Encoding.UTF8.GetBytes(message);
                await _webSocket.SendAsync(
                    new ArraySegment<byte>(buffer),
                    WebSocketMessageType.Text,
                    true,
                    _cancellationTokenSource.Token
                );

                Debug.Log($"Sent message: {message}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"Send message error: {ex.Message}");
                OnError?.Invoke($"Send error: {ex.Message}");
                throw;
            }
        }

        public string GetNextMessage()
        {
            lock (_lockObject)
            {
                if (_messageQueue.Count > 0)
                {
                    return _messageQueue.Dequeue();
                }
                return null;
            }
        }

        public List<string> GetAllMessages()
        {
            lock (_lockObject)
            {
                var messages = new List<string>(_messageQueue);
                _messageQueue.Clear();
                return messages;
            }
        }

        private async Task ReceiveLoop()
        {
            var buffer = new byte[4096];

            try
            {
                while (_webSocket.State == WebSocketState.Open && !_cancellationTokenSource.Token.IsCancellationRequested)
                {
                    var messageBuilder = new System.Text.StringBuilder();
                    WebSocketReceiveResult result;

                    // Keep reading until we get the complete message
                    do
                    {
                        result = await _webSocket.ReceiveAsync(
                            new ArraySegment<byte>(buffer),
                            _cancellationTokenSource.Token
                        );

                        if (result.MessageType == WebSocketMessageType.Text)
                        {
                            var chunk = Encoding.UTF8.GetString(buffer, 0, result.Count);
                            messageBuilder.Append(chunk);
                        }
                        else if (result.MessageType == WebSocketMessageType.Close)
                        {
                            Debug.Log("WebSocket closed by server");
                            return;
                        }
                    }
                    while (!result.EndOfMessage);

                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        var completeMessage = messageBuilder.ToString();
                        Debug.Log($"Received complete message ({completeMessage.Length} chars): {completeMessage.Substring(0, Math.Min(200, completeMessage.Length))}...");

                        lock (_lockObject)
                        {
                            _messageQueue.Enqueue(completeMessage);
                        }

                        OnMessageReceived?.Invoke(completeMessage);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                Debug.Log("WebSocket receive loop cancelled");
            }
            catch (Exception ex)
            {
                Debug.LogError($"WebSocket receive error: {ex.Message}");
                OnError?.Invoke($"Receive error: {ex.Message}");
            }
            finally
            {
                _isConnected = false;
                OnDisconnected?.Invoke();
            }
        }

        public void Dispose()
        {
            DisconnectAsync().GetAwaiter().GetResult();
        }
    }
}