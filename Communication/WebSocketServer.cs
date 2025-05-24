using CocoroDock.Utilities;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CocoroDock.Communication
{
    /// <summary>
    /// WebSocketサーバー実装
    /// </summary>
    public class WebSocketServer : IDisposable
    {
        private readonly string _serverUrl;
        private readonly int _port;
        private HttpListener _httpListener;
        private CancellationTokenSource _cts;
        private Task? _listenTask;
        private bool _isRunning;
        private readonly ConcurrentDictionary<string, WebSocket> _clients = new ConcurrentDictionary<string, WebSocket>();

        public event EventHandler<(string ClientId, string Message)>? MessageReceived;
        public event EventHandler<string>? ConnectionError;
        public event EventHandler<string>? ClientConnected;
        public event EventHandler<string>? ClientDisconnected;

        public bool IsRunning => _isRunning;

        /// <summary>
        /// WebSocketサーバーのコンストラクタ
        /// </summary>
        /// <param name="host">ホスト名（例：127.0.0.1）</param>
        /// <param name="port">ポート番号（例：55600）</param>
        public WebSocketServer(string host, int port)
        {
            _serverUrl = $"http://{host}:{port}/";
            _port = port;
            _httpListener = new HttpListener();
            _httpListener.Prefixes.Add(_serverUrl);
            _cts = new CancellationTokenSource();
            _isRunning = false;
        }

        /// <summary>
        /// サーバーを起動
        /// </summary>
        public async Task StartAsync()
        {
            if (_isRunning) return;

            try
            {
                _httpListener.Start();
                _isRunning = true;

                // リッスンタスクを開始
                _listenTask = Task.Run(ListenForClientsAsync);
                // 非同期メソッドで await を使用するために形式的な待機を追加
                await Task.CompletedTask;

                Debug.WriteLine($"WebSocketサーバーを起動しました: {_serverUrl}");
            }
            catch (HttpListenerException httpEx)
            {
                Debug.WriteLine($"HttpListenerエラー: {httpEx.Message}");
                Debug.WriteLine($"エラーコード: {httpEx.ErrorCode}");
                Debug.WriteLine($"ネイティブエラーコード: {httpEx.NativeErrorCode}");
                ConnectionError?.Invoke(this, $"HttpListenerエラー: {httpEx.Message} (エラーコード: {httpEx.ErrorCode})");
                throw;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"サーバー起動エラー: {ex.GetType().Name}: {ex.Message}");
                Debug.WriteLine($"スタックトレース: {ex.StackTrace}");
                ConnectionError?.Invoke(this, $"サーバー起動エラー: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// サーバーを停止
        /// </summary>
        public async Task StopAsync()
        {
            if (!_isRunning) return;

            try
            {
                // まず実行フラグをオフにして新しいリクエスト処理を停止
                _isRunning = false;

                // キャンセルトークンをトリガー
                _cts.Cancel();

                // すべてのクライアント接続を閉じる
                await CloseAllClientConnectionsAsync();

                // リッスンタスクが完了するまで待機（必要に応じてタイムアウト設定も可能）
                if (_listenTask != null)
                {
                    await Task.WhenAny(_listenTask, Task.Delay(1000)); // 最大1秒待機
                }

                _httpListener.Stop();
                _httpListener.Close();

                Debug.WriteLine("WebSocketサーバーを停止しました");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"サーバー停止エラー: {ex.Message}");
            }
            finally
            {
                _clients.Clear();
                _cts = new CancellationTokenSource();
                _httpListener = new HttpListener();
                _httpListener.Prefixes.Add(_serverUrl);
            }
        }

        /// <summary>
        /// すべてのクライアント接続を閉じる
        /// </summary>
        private async Task CloseAllClientConnectionsAsync()
        {
            var closeTasks = new List<Task>();
            foreach (var client in _clients)
            {
                closeTasks.Add(CloseClientConnectionAsync(client.Key, client.Value));
            }

            if (closeTasks.Count > 0)
            {
                await Task.WhenAll(closeTasks);
            }
        }

        /// <summary>
        /// クライアント接続を待機するループ
        /// </summary>
        private async Task ListenForClientsAsync()
        {
            try
            {
                while (_isRunning && !_cts.Token.IsCancellationRequested)
                {
                    HttpListenerContext context = await GetContextSafelyAsync();
                    if (context == null) continue;

                    if (context.Request.IsWebSocketRequest)
                    {
                        ProcessWebSocketRequestAsync(context);
                    }
                    else
                    {
                        context.Response.StatusCode = 400;
                        context.Response.Close();
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // キャンセルされた場合は正常終了
                Debug.WriteLine("リッスンループがキャンセルされました");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"リッスンループエラー: {ex.Message}");
                ConnectionError?.Invoke(this, $"リッスンループエラー: {ex.Message}");
            }
        }

        /// <summary>
        /// 安全にHttpContextを取得する
        /// </summary>
        private async Task<HttpListenerContext> GetContextSafelyAsync()
        {
            try
            {
                return await _httpListener.GetContextAsync();
            }
            catch (HttpListenerException)
            {
                // HttpListenerが停止された場合
                return null;
            }
            catch (ObjectDisposedException)
            {
                // HttpListenerが破棄された場合
                return null;
            }
            catch (InvalidOperationException)
            {
                // HttpListenerがまだ起動していないか既に停止している場合
                return null;
            }
        }

        /// <summary>
        /// WebSocket接続リクエストを処理
        /// </summary>
        private async void ProcessWebSocketRequestAsync(HttpListenerContext context)
        {
            WebSocket? webSocket = null;
            string clientId = Guid.NewGuid().ToString();

            try
            {
                // WebSocket接続を確立
                var webSocketContext = await context.AcceptWebSocketAsync(subProtocol: null);
                webSocket = webSocketContext.WebSocket;

                // クライアント一覧に追加
                _clients.TryAdd(clientId, webSocket);

                // 接続イベントを発火
                ClientConnected?.Invoke(this, clientId);
                Debug.WriteLine($"新しいクライアントが接続しました: {clientId}");

                // クライアントからのメッセージ受信ループを開始
                await ReceiveMessagesAsync(clientId, webSocket);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"WebSocket処理エラー: {ex.Message}");
                ConnectionError?.Invoke(this, $"WebSocket処理エラー: {ex.Message}");
            }
            finally
            {
                if (_clients.TryRemove(clientId, out _))
                {
                    ClientDisconnected?.Invoke(this, clientId);
                    Debug.WriteLine($"クライアントが切断しました: {clientId}");
                }

                webSocket?.Dispose();
            }
        }

        /// <summary>
        /// クライアントからのメッセージを受信するループ
        /// </summary>
        private async Task ReceiveMessagesAsync(string clientId, WebSocket webSocket)
        {
            var buffer = new byte[4096];

            try
            {
                while (webSocket.State == WebSocketState.Open && !_cts.Token.IsCancellationRequested)
                {
                    var receiveBuffer = new ArraySegment<byte>(buffer);
                    WebSocketReceiveResult result;
                    var messageBuilder = new StringBuilder();

                    do
                    {
                        result = await webSocket.ReceiveAsync(receiveBuffer, _cts.Token);

                        if (result.MessageType == WebSocketMessageType.Text)
                        {
                            string receivedMessage = Encoding.UTF8.GetString(buffer, 0, result.Count);
                            messageBuilder.Append(receivedMessage);
                        }
                        else if (result.MessageType == WebSocketMessageType.Close)
                        {
                            await CloseClientConnectionAsync(clientId, webSocket);
                            return;
                        }
                    } while (!result.EndOfMessage);

                    string base64Text = messageBuilder.ToString();
                    if (!string.IsNullOrEmpty(base64Text))
                    {
                        ProcessReceivedMessage(clientId, base64Text);
                    }
                }
            }
            catch (WebSocketException ex) when (ex.WebSocketErrorCode == WebSocketError.ConnectionClosedPrematurely)
            {
                Debug.WriteLine($"クライアント接続が予期せず閉じられました: {clientId}");
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine($"クライアント接続処理がキャンセルされました: {clientId}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"メッセージ受信エラー: {ex.Message}");
            }
            finally
            {
                await CloseClientConnectionAsync(clientId, webSocket);
            }
        }

        /// <summary>
        /// 受信したメッセージを処理する
        /// </summary>
        private void ProcessReceivedMessage(string clientId, string base64Text)
        {
            try
            {
                string messageText = MessageHelper.DecodeFromBase64(base64Text);
                MessageReceived?.Invoke(this, (clientId, messageText));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"メッセージ処理エラー: {ex.Message}");
                // 処理できない場合は元のテキストをそのまま通知
                MessageReceived?.Invoke(this, (clientId, base64Text));
            }
        }

        /// <summary>
        /// クライアント接続を閉じる
        /// </summary>
        private async Task CloseClientConnectionAsync(string clientId, WebSocket webSocket)
        {
            try
            {
                if (webSocket.State == WebSocketState.Open || webSocket.State == WebSocketState.Connecting)
                {
                    await webSocket.CloseAsync(
                        WebSocketCloseStatus.NormalClosure,
                        "サーバーからの切断",
                        CancellationToken.None);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"クライアント接続クローズエラー: {ex.Message}");
            }
        }

        /// <summary>
        /// 特定のクライアントにメッセージを送信
        /// </summary>
        /// <param name="clientId">送信先クライアントID</param>
        /// <param name="message">送信するメッセージ</param>
        public async Task SendMessageToClientAsync(string clientId, string message)
        {
            if (_clients.TryGetValue(clientId, out WebSocket? client) && client != null && client.State == WebSocketState.Open)
            {
                try
                {
                    // メッセージをBase64エンコード
                    string base64String = MessageHelper.EncodeToBase64(message);
                    var base64Bytes = Encoding.UTF8.GetBytes(base64String);
                    
                    await client.SendAsync(
                        new ArraySegment<byte>(base64Bytes),
                        WebSocketMessageType.Text,
                        true,
                        _cts.Token);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"メッセージ送信エラー: {ex.Message}");
                    ConnectionError?.Invoke(this, $"メッセージ送信エラー ({clientId}): {ex.Message}");
                }
            }
        }

        /// <summary>
        /// すべてのクライアントにメッセージを送信（ブロードキャスト）
        /// </summary>
        /// <param name="message">送信するメッセージ</param>
        public async Task BroadcastMessageAsync(string message)
        {
            var tasks = new List<Task>();

            foreach (var client in _clients)
            {
                tasks.Add(SendMessageToClientAsync(client.Key, message));
            }

            if (tasks.Count > 0)
            {
                await Task.WhenAll(tasks);
            }
        }

        /// <summary>
        /// 指定されたタイプとペイロードのメッセージをすべてのクライアントに送信
        /// </summary>
        /// <param name="type">メッセージタイプ</param>
        /// <param name="payload">ペイロードデータ</param>
        public async Task SendMessageAsync(MessageType type, object payload)
        {
            try
            {
                var message = MessageHelper.CreateMessage(type, payload);
                string json = MessageHelper.SerializeToJson(message);
                await BroadcastMessageAsync(json);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"メッセージ送信エラー: {ex.Message}");
                ConnectionError?.Invoke(this, $"メッセージ送信エラー: {ex.Message}");
            }
        }

        /// <summary>
        /// 設定情報要求メッセージを送信
        /// </summary>
        public async Task SendRequestConfigAsync()
        {
            var payload = new ConfigRequestPayload
            {
                action = "get"
            };

            await SendMessageAsync(MessageType.config, payload);
        }

        /// <summary>
        /// 設定更新メッセージを送信
        /// </summary>
        /// <param name="settings">更新する設定</param>
        public async Task SendUpdateConfigAsync(ConfigSettings settings)
        {
            var payload = new ConfigUpdatePayload
            {
                action = "update",
                settings = settings
            };

            await SendMessageAsync(MessageType.config, payload);
        }

        /// <summary>
        /// リソースを解放
        /// </summary>
        public void Dispose()
        {
            try
            {
                // まず実行中のタスクをキャンセル
                _cts?.Cancel();

                // 実行フラグをリセット - これにより新しいリクエスト処理を停止
                _isRunning = false;

                // HttpListenerを確実に停止
                StopHttpListener();

                // 接続中のすべてのクライアントを強制切断
                AbortAllClients();

                // クライアントリストをクリア
                _clients.Clear();

                // キャンセルトークンを解放
                _cts?.Dispose();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"WebSocketServer破棄エラー: {ex.Message}");
            }
        }

        /// <summary>
        /// HttpListenerを停止
        /// </summary>
        private void StopHttpListener()
        {
            try
            {
                if (_httpListener.IsListening)
                {
                    _httpListener?.Stop();
                }
            }
            catch (ObjectDisposedException)
            {
                // 既に破棄されている場合は無視
            }
        }

        /// <summary>
        /// すべてのクライアントを強制切断
        /// </summary>
        private void AbortAllClients()
        {
            foreach (var client in _clients)
            {
                try
                {
                    // WebSocketを強制的に中断
                    if (client.Value.State == WebSocketState.Open ||
                        client.Value.State == WebSocketState.Connecting)
                    {
                        client.Value.Abort();
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"クライアント強制終了エラー: {ex.Message}");
                }
            }
        }
    }
}
