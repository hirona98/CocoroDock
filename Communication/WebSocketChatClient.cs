using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CocoroDock.Utilities;

namespace CocoroDock.Communication
{
    /// <summary>
    /// WebSocketチャットクライアント
    /// </summary>
    public class WebSocketChatClient : IDisposable
    {
        private ClientWebSocket? _webSocket;
        private readonly string _clientId;
        private readonly Uri _webSocketUri;
        private CancellationTokenSource? _cancellationTokenSource;
        private Task? _receiveTask;
        private bool _isConnected = false;
        private readonly object _connectionLock = new object();

        /// <summary>
        /// WebSocketメッセージ受信イベント
        /// </summary>
        public event EventHandler<WebSocketResponseMessage>? MessageReceived;

        /// <summary>
        /// WebSocket接続状態変更イベント
        /// </summary>
        public event EventHandler<bool>? ConnectionStateChanged;

        /// <summary>
        /// WebSocketエラーイベント
        /// </summary>
        public event EventHandler<string>? ErrorOccurred;

        public WebSocketChatClient(int port, string clientId)
        {
            _clientId = clientId;
            _webSocketUri = new Uri($"ws://127.0.0.1:{port}/ws/chat/{clientId}");
        }

        /// <summary>
        /// WebSocket接続を確立（リトライ機能付き）
        /// </summary>
        public async Task<bool> ConnectAsync()
        {
            lock (_connectionLock)
            {
                if (_isConnected)
                {
                    return true;
                }
            }


            while (true)
            {
                try
                {
                    // 既存のリソースをクリーンアップ
                    await DisconnectAsync();

                    _webSocket = new ClientWebSocket();
                    _cancellationTokenSource = new CancellationTokenSource();

                    // 接続タイムアウト設定（30秒）
                    _webSocket.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);

                    await _webSocket.ConnectAsync(_webSocketUri, _cancellationTokenSource.Token);

                    lock (_connectionLock)
                    {
                        _isConnected = true;
                    }

                    // 受信ループを開始
                    _receiveTask = ReceiveLoopAsync();

                    ConnectionStateChanged?.Invoke(this, true);

                    return true;
                }
                catch (Exception ex) when (!(ex is WebSocketException) && !(ex is AggregateException))
                {
                    const int retryDelayMs = 1000;
                    await Task.Delay(retryDelayMs);
                    await DisconnectAsync();
                }
            }
        }

        /// <summary>
        /// WebSocket切断
        /// </summary>
        public async Task DisconnectAsync()
        {
            bool wasConnected;
            lock (_connectionLock)
            {
                wasConnected = _isConnected;
                _isConnected = false;
            }

            try
            {
                // キャンセレーションを要求
                _cancellationTokenSource?.Cancel();

                // 受信タスクの完了を待機
                if (_receiveTask != null)
                {
                    try
                    {
                        await Task.WhenAny(_receiveTask, Task.Delay(TimeSpan.FromSeconds(5)));
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[WebSocket] 受信タスク停止エラー: {ex.Message}");
                    }
                }

                // WebSocket接続をクローズ
                if (_webSocket?.State == WebSocketState.Open)
                {
                    try
                    {
                        await _webSocket.CloseAsync(
                            WebSocketCloseStatus.NormalClosure,
                            "正常切断",
                            CancellationToken.None
                        );
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[WebSocket] クローズエラー: {ex.Message}");
                    }
                }
            }
            finally
            {
                // リソースを解放
                _webSocket?.Dispose();
                _webSocket = null;

                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;

                _receiveTask = null;

                if (wasConnected)
                {
                    ConnectionStateChanged?.Invoke(this, false);
                }
            }
        }

        /// <summary>
        /// チャットメッセージを送信
        /// </summary>
        public async Task<bool> SendChatAsync(string sessionId, WebSocketChatRequest request)
        {
            if (!_isConnected || _webSocket?.State != WebSocketState.Open)
            {
                return false;
            }

            try
            {
                var message = new WebSocketMessage
                {
                    action = "chat",
                    session_id = sessionId,
                    request = request
                };

                var json = MessageHelper.SerializeToJson(message);
                var bytes = Encoding.UTF8.GetBytes(json);

                await _webSocket.SendAsync(
                    new ArraySegment<byte>(bytes),
                    WebSocketMessageType.Text,
                    true,
                    _cancellationTokenSource?.Token ?? CancellationToken.None
                );

                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WebSocket] 送信エラー: {ex.Message}");
                ErrorOccurred?.Invoke(this, $"送信エラー: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 受信ループ（完全メッセージ受信対応）
        /// </summary>
        private async Task ReceiveLoopAsync()
        {
            var buffer = new byte[1024 * 8]; // 8KB バッファ（フレーム単位）

            try
            {
                while (_isConnected &&
                       _webSocket?.State == WebSocketState.Open &&
                       !(_cancellationTokenSource?.Token.IsCancellationRequested ?? true))
                {
                    var messageBuffer = new List<byte>();
                    WebSocketReceiveResult result;

                    // 完全なメッセージを受信するまでループ
                    do
                    {
                        result = await _webSocket.ReceiveAsync(
                            new ArraySegment<byte>(buffer),
                            _cancellationTokenSource?.Token ?? CancellationToken.None
                        );

                        if (result.MessageType == WebSocketMessageType.Text)
                        {
                            // 受信したデータをメッセージバッファに追加
                            messageBuffer.AddRange(buffer.Take(result.Count));
                        }
                        else if (result.MessageType == WebSocketMessageType.Close)
                        {
                            return;
                        }

                    } while (!result.EndOfMessage && result.MessageType == WebSocketMessageType.Text);

                    // 完全なメッセージを受信したら処理
                    if (result.MessageType == WebSocketMessageType.Text && messageBuffer.Count > 0)
                    {
                        try
                        {
                            var json = Encoding.UTF8.GetString(messageBuffer.ToArray());
                            // Debug.WriteLine($"[WebSocket] 完全メッセージ受信: Size={messageBuffer.Count} bytes");
                            ProcessReceivedMessage(json);
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[WebSocket] メッセージ処理エラー: {ex.Message}");
                            // JSONパースエラーの場合は続行（エラーイベントは ProcessReceivedMessage 内で発火）
                        }
                    }
                }
            }
            catch (WebSocketException ex)
            {
                Debug.WriteLine($"[WebSocket] 受信エラー: {ex.Message}");
                ErrorOccurred?.Invoke(this, $"受信エラー: {ex.Message}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WebSocket] 予期しない受信エラー: {ex.Message}");
                ErrorOccurred?.Invoke(this, $"予期しない受信エラー: {ex.Message}");
            }
            finally
            {
                await DisconnectAsync();
            }
        }

        /// <summary>
        /// 受信メッセージ処理
        /// </summary>
        private void ProcessReceivedMessage(string json)
        {
            try
            {
                var message = MessageHelper.DeserializeFromJson<WebSocketResponseMessage>(json);
                if (message != null)
                {
                    // Debug.WriteLine($"[WebSocket] メッセージ受信: type={message.type}, session={message.session_id}");
                    MessageReceived?.Invoke(this, message);
                }
                else
                {
                    Debug.WriteLine($"[WebSocket] メッセージデシリアライズ失敗: {json.Substring(0, Math.Min(100, json.Length))}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WebSocket] メッセージ処理エラー: {ex.Message}");
                ErrorOccurred?.Invoke(this, $"メッセージ処理エラー: {ex.Message}");
            }
        }

        /// <summary>
        /// 接続状態確認
        /// </summary>
        public bool IsConnected
        {
            get
            {
                lock (_connectionLock)
                {
                    return _isConnected && _webSocket?.State == WebSocketState.Open;
                }
            }
        }

        /// <summary>
        /// リソース解放
        /// </summary>
        public void Dispose()
        {
            try
            {
                // 非同期メソッドを同期的に呼び出し（Dispose内なので例外的に許可）
                DisconnectAsync().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WebSocket] Dispose時エラー: {ex.Message}");
            }
        }
    }
}