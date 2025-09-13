using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CocoroDock.Models;
using CocoroDock.Services;

namespace CocoroDock.Communication
{
    /// <summary>
    /// モバイルWebSocketサーバー
    /// PWAからのWebSocket接続を受け入れ、CocoreCoreM との橋渡しを行う
    /// </summary>
    public class MobileWebSocketServer : IDisposable
    {
        private HttpListener? _httpListener;
        private readonly int _port;
        private WebSocketChatClient? _cocoroClient;
        private VoicevoxClient? _voicevoxClient;
        private CancellationTokenSource? _cancellationTokenSource;
        private Task? _serverTask;
        private readonly IAppSettings _appSettings;

        // 接続管理（スマホ1台想定だが複数接続対応）
        private readonly ConcurrentDictionary<string, WebSocket> _connections = new();
        private readonly ConcurrentDictionary<string, string> _sessionMappings = new();

        public bool IsRunning => _httpListener?.IsListening == true;

        public MobileWebSocketServer(int port, IAppSettings appSettings)
        {
            _port = port;
            _appSettings = appSettings;
            Debug.WriteLine($"[MobileWebSocketServer] 初期化: ポート={port}");
        }

        /// <summary>
        /// サーバーを開始
        /// </summary>
        public async Task StartAsync()
        {
            if (_httpListener != null && _httpListener.IsListening)
            {
                Debug.WriteLine("[MobileWebSocketServer] 既に起動中です");
                return;
            }

            try
            {
                // HTTPリスナー初期化
                _httpListener = new HttpListener();
                _httpListener.Prefixes.Add($"http://0.0.0.0:{_port}/");
                _httpListener.Start();

                // CocoroCoreM クライアント初期化
                var cocoroPort = _appSettings.GetConfigSettings().cocoroCorePort;
                var clientId = $"mobile_{DateTime.Now:yyyyMMddHHmmss}";
                _cocoroClient = new WebSocketChatClient(cocoroPort, clientId);
                _cocoroClient.MessageReceived += OnCocoroCoreMessageReceived;
                _cocoroClient.ErrorOccurred += OnCocoroCoreError;

                await _cocoroClient.ConnectAsync();

                // VOICEVOX クライアント初期化
                var currentChar = _appSettings.GetCurrentCharacter();
                var voicevoxUrl = currentChar?.voicevoxConfig?.endpointUrl ?? "http://0.0.0.0:50021";
                _voicevoxClient = new VoicevoxClient(voicevoxUrl);

                // キャンセレーショントークン
                _cancellationTokenSource = new CancellationTokenSource();

                // サーバータスク開始
                _serverTask = Task.Run(ServerLoop);

                Debug.WriteLine($"[MobileWebSocketServer] サーバー開始: http://0.0.0.0:{_port}/");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MobileWebSocketServer] 開始エラー: {ex.Message}");
                await StopAsync();
                throw;
            }
        }

        /// <summary>
        /// サーバーを停止
        /// </summary>
        public async Task StopAsync()
        {
            try
            {
                _cancellationTokenSource?.Cancel();

                // 全接続を閉じる
                foreach (var kvp in _connections)
                {
                    try
                    {
                        if (kvp.Value.State == WebSocketState.Open)
                        {
                            await kvp.Value.CloseAsync(WebSocketCloseStatus.NormalClosure, "サーバー停止", CancellationToken.None);
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[MobileWebSocketServer] 接続クローズエラー: {ex.Message}");
                    }
                }
                _connections.Clear();
                _sessionMappings.Clear();

                // HTTPリスナー停止
                _httpListener?.Stop();
                _httpListener?.Close();

                // CocoroCoreM クライアント停止
                if (_cocoroClient != null)
                {
                    await _cocoroClient.DisconnectAsync();
                    _cocoroClient.Dispose();
                }

                // VOICEVOX クライアント停止
                _voicevoxClient?.Dispose();

                // サーバータスク完了待機
                if (_serverTask != null)
                {
                    await Task.WhenAny(_serverTask, Task.Delay(5000));
                }

                Debug.WriteLine("[MobileWebSocketServer] サーバー停止完了");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MobileWebSocketServer] 停止エラー: {ex.Message}");
            }
            finally
            {
                _httpListener = null;
                _cocoroClient = null;
                _voicevoxClient = null;
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
                _serverTask = null;
            }
        }

        /// <summary>
        /// サーバーメインループ
        /// </summary>
        private async Task ServerLoop()
        {
            while (!_cancellationTokenSource!.Token.IsCancellationRequested && _httpListener!.IsListening)
            {
                try
                {
                    var context = await _httpListener.GetContextAsync();
                    _ = Task.Run(() => HandleRequestAsync(context), _cancellationTokenSource.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[MobileWebSocketServer] サーバーループエラー: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// HTTPリクエスト処理
        /// </summary>
        private async Task HandleRequestAsync(HttpListenerContext context)
        {
            try
            {
                var request = context.Request;
                var response = context.Response;

                Debug.WriteLine($"[MobileWebSocketServer] リクエスト: {request.HttpMethod} {request.Url?.PathAndQuery}");

                // WebSocket接続の場合
                if (context.Request.IsWebSocketRequest)
                {
                    await HandleWebSocketRequestAsync(context);
                    return;
                }

                // 音声ファイル配信
                if (request.HttpMethod == "GET" && request.Url?.AbsolutePath?.StartsWith("/audio/") == true)
                {
                    await HandleAudioFileRequest(context);
                    return;
                }

                // その他のリクエストは404
                response.StatusCode = 404;
                response.Close();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MobileWebSocketServer] リクエスト処理エラー: {ex.Message}");
                try
                {
                    context.Response.StatusCode = 500;
                    context.Response.Close();
                }
                catch { }
            }
        }

        /// <summary>
        /// WebSocket接続処理
        /// </summary>
        private async Task HandleWebSocketRequestAsync(HttpListenerContext context)
        {
            WebSocketContext webSocketContext;
            try
            {
                webSocketContext = await context.AcceptWebSocketAsync(null);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MobileWebSocketServer] WebSocket受け入れエラー: {ex.Message}");
                context.Response.StatusCode = 400;
                context.Response.Close();
                return;
            }

            var connectionId = Guid.NewGuid().ToString();
            var webSocket = webSocketContext.WebSocket;
            _connections[connectionId] = webSocket;

            Debug.WriteLine($"[MobileWebSocketServer] WebSocket接続確立: {connectionId}");

            try
            {
                await HandleWebSocketCommunication(connectionId, webSocket);
            }
            finally
            {
                _connections.TryRemove(connectionId, out _);
                _sessionMappings.TryRemove(connectionId, out _);
                Debug.WriteLine($"[MobileWebSocketServer] WebSocket接続終了: {connectionId}");
            }
        }

        /// <summary>
        /// WebSocket通信処理
        /// </summary>
        private async Task HandleWebSocketCommunication(string connectionId, WebSocket webSocket)
        {
            var buffer = new byte[1024 * 4];

            while (webSocket.State == WebSocketState.Open && !_cancellationTokenSource!.Token.IsCancellationRequested)
            {
                try
                {
                    var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), _cancellationTokenSource.Token);

                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                        await ProcessMobileMessage(connectionId, json);
                    }
                    else if (result.MessageType == WebSocketMessageType.Close)
                    {
                        break;
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[MobileWebSocketServer] WebSocket通信エラー: {ex.Message}");
                    break;
                }
            }
        }

        /// <summary>
        /// モバイルからのメッセージ処理
        /// </summary>
        private async Task ProcessMobileMessage(string connectionId, string json)
        {
            try
            {
                var message = JsonSerializer.Deserialize<MobileChatMessage>(json);
                if (message?.Data == null)
                {
                    await SendErrorToMobile(connectionId, MobileErrorCodes.InvalidMessage, "Invalid message format");
                    return;
                }

                Debug.WriteLine($"[MobileWebSocketServer] モバイルメッセージ受信: {message.Data.Message.Substring(0, Math.Min(50, message.Data.Message.Length))}...");

                // CocoreCoreM に送信するためのリクエスト作成
                var chatRequest = new WebSocketChatRequest
                {
                    query = message.Data.Message,
                    chat_type = message.Data.ChatType ?? "text",
                    images = message.Data.Images?.Select(img => new ImageData
                    {
                        data = img.ImageData
                    }).ToList()
                };

                // セッションIDの生成と管理
                var sessionId = $"mobile_{connectionId}_{DateTime.Now:yyyyMMddHHmmss}";
                _sessionMappings[sessionId] = connectionId;

                // CocoreCoreM にメッセージ送信
                if (_cocoroClient != null && _cocoroClient.IsConnected)
                {
                    await _cocoroClient.SendChatAsync(sessionId, chatRequest);
                }
                else
                {
                    await SendErrorToMobile(connectionId, MobileErrorCodes.CoreMError, "CocoreCoreM connection not available");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MobileWebSocketServer] メッセージ処理エラー: {ex.Message}");
                await SendErrorToMobile(connectionId, MobileErrorCodes.ServerError, "Message processing error");
            }
        }

        /// <summary>
        /// CocoreCoreM からのメッセージ受信イベント
        /// </summary>
        private async void OnCocoroCoreMessageReceived(object? sender, WebSocketResponseMessage response)
        {
            try
            {
                // セッションIDから接続IDを取得
                var connectionId = _sessionMappings.Keys.FirstOrDefault(sessionId =>
                    response.session_id?.StartsWith($"mobile_{sessionId}") == true);

                if (connectionId == null)
                {
                    Debug.WriteLine($"[MobileWebSocketServer] 対応する接続が見つかりません: {response.session_id}");
                    return;
                }

                // 応答タイプに応じて処理
                if (response.type == "end" && response.data is WebSocketEndData endData)
                {
                    // 最終応答: 音声合成して送信
                    await HandleFinalResponse(connectionId, endData.final_text);
                }
                else if (response.type == "text" && response.data is WebSocketTextData textData)
                {
                    // ストリーミング応答: テキストのみ送信
                    await SendPartialResponseToMobile(connectionId, textData.content);
                }
                else if (response.type == "error")
                {
                    // エラー応答
                    await SendErrorToMobile(connectionId, MobileErrorCodes.CoreMError, "CocoreCoreM processing error");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MobileWebSocketServer] CocoreCoreメッセージ処理エラー: {ex.Message}");
            }
        }

        /// <summary>
        /// 最終応答処理（音声合成含む）
        /// </summary>
        private async Task HandleFinalResponse(string connectionId, string text)
        {
            try
            {
                var currentChar = _appSettings.GetCurrentCharacter();
                var speakerId = currentChar?.voicevoxConfig?.speakerId ?? 3;

                // 音声合成
                string? audioUrl = null;
                if (_voicevoxClient != null && !string.IsNullOrWhiteSpace(text))
                {
                    audioUrl = await _voicevoxClient.SynthesizeAsync(text, speakerId);
                }

                // 応答メッセージ作成
                var response = new MobileResponseMessage
                {
                    Data = new MobileResponseData
                    {
                        Text = text,
                        AudioUrl = audioUrl,
                        SpeakerId = speakerId,
                        Source = "cocoro_core_m"
                    }
                };

                await SendJsonToMobile(connectionId, response);
                Debug.WriteLine($"[MobileWebSocketServer] 最終応答送信完了: audioUrl={audioUrl}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MobileWebSocketServer] 最終応答処理エラー: {ex.Message}");
                await SendErrorToMobile(connectionId, MobileErrorCodes.ServerError, "Final response processing error");
            }
        }

        /// <summary>
        /// 部分応答送信（ストリーミング）
        /// </summary>
        private async Task SendPartialResponseToMobile(string connectionId, string text)
        {
            var response = new MobileResponseMessage
            {
                Data = new MobileResponseData
                {
                    Text = text,
                    AudioUrl = null,
                    Source = "cocoro_core_m"
                }
            };

            await SendJsonToMobile(connectionId, response);
        }

        /// <summary>
        /// エラーメッセージ送信
        /// </summary>
        private async Task SendErrorToMobile(string connectionId, string errorCode, string errorMessage)
        {
            var error = new MobileErrorMessage
            {
                Data = new MobileErrorData
                {
                    Code = errorCode,
                    Message = errorMessage
                }
            };

            await SendJsonToMobile(connectionId, error);
        }

        /// <summary>
        /// JSONメッセージ送信
        /// </summary>
        private async Task SendJsonToMobile(string connectionId, object message)
        {
            if (!_connections.TryGetValue(connectionId, out var webSocket) || webSocket.State != WebSocketState.Open)
            {
                return;
            }

            try
            {
                var json = JsonSerializer.Serialize(message);
                var bytes = Encoding.UTF8.GetBytes(json);
                await webSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, _cancellationTokenSource!.Token);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MobileWebSocketServer] JSON送信エラー: {ex.Message}");
            }
        }

        /// <summary>
        /// 音声ファイル配信処理
        /// </summary>
        private async Task HandleAudioFileRequest(HttpListenerContext context)
        {
            try
            {
                var fileName = Path.GetFileName(context.Request.Url?.AbsolutePath);
                if (string.IsNullOrEmpty(fileName))
                {
                    context.Response.StatusCode = 404;
                    context.Response.Close();
                    return;
                }

                using var fileStream = _voicevoxClient?.GetAudioFileStream(fileName);
                if (fileStream == null)
                {
                    context.Response.StatusCode = 404;
                    context.Response.Close();
                    return;
                }

                context.Response.ContentType = "audio/wav";
                context.Response.ContentLength64 = fileStream.Length;
                context.Response.StatusCode = 200;

                await fileStream.CopyToAsync(context.Response.OutputStream);
                context.Response.Close();

                Debug.WriteLine($"[MobileWebSocketServer] 音声ファイル配信: {fileName}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MobileWebSocketServer] 音声ファイル配信エラー: {ex.Message}");
                try
                {
                    context.Response.StatusCode = 500;
                    context.Response.Close();
                }
                catch { }
            }
        }

        /// <summary>
        /// CocoreCoreM エラーイベント
        /// </summary>
        private void OnCocoroCoreError(object? sender, string error)
        {
            Debug.WriteLine($"[MobileWebSocketServer] CocoreCoreエラー: {error}");
            // 全接続にエラーを通知
            Task.Run(async () =>
            {
                foreach (var connectionId in _connections.Keys)
                {
                    await SendErrorToMobile(connectionId, MobileErrorCodes.CoreMError, error);
                }
            });
        }

        public void Dispose()
        {
            Task.Run(async () => await StopAsync()).Wait(TimeSpan.FromSeconds(10));
        }
    }
}