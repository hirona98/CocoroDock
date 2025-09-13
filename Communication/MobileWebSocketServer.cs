using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CocoroDock.Models;
using CocoroDock.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace CocoroDock.Communication
{
    /// <summary>
    /// モバイルWebSocketサーバー（ASP.NET Core実装）
    /// PWAからのWebSocket接続を受け入れ、CocoreCoreM との橋渡しを行う
    /// </summary>
    public class MobileWebSocketServer : IDisposable
    {
        private WebApplication? _app;
        private readonly int _port;
        private readonly IAppSettings _appSettings;
        private WebSocketChatClient? _cocoroClient;
        private VoicevoxClient? _voicevoxClient;
        private CancellationTokenSource? _cts;

        // 接続管理（スマホ1台想定だが複数接続対応）
        private readonly ConcurrentDictionary<string, WebSocket> _connections = new();
        private readonly ConcurrentDictionary<string, string> _sessionMappings = new();

        public bool IsRunning => _app != null;

        public MobileWebSocketServer(int port, IAppSettings appSettings)
        {
            _port = port;
            _appSettings = appSettings;
            Debug.WriteLine($"[MobileWebSocketServer] 初期化: ポート={port}");
        }

        /// <summary>
        /// サーバーを開始
        /// </summary>
        public Task StartAsync()
        {
            if (_app != null)
            {
                Debug.WriteLine("[MobileWebSocketServer] 既に起動中です");
                return Task.CompletedTask;
            }

            try
            {
                _cts = new CancellationTokenSource();

                var builder = WebApplication.CreateBuilder();

                // Kestrelサーバーの設定（外部アクセス対応・管理者権限不要）
                builder.WebHost.ConfigureKestrel(serverOptions =>
                {
                    serverOptions.ListenAnyIP(_port);
                });

                // サービスの登録
                ConfigureServices(builder);

                var app = builder.Build();

                // ミドルウェアとエンドポイントの設定
                ConfigureApp(app);

                _app = app;

                // CocoreCoreM クライアント初期化
                InitializeCocoroCoreClient();

                // VOICEVOX クライアント初期化
                InitializeVoicevoxClient();

                // バックグラウンドでサーバーを起動
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _app.RunAsync(_cts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        // 正常な終了
                        Debug.WriteLine("[MobileWebSocketServer] サーバーが正常に停止されました");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[MobileWebSocketServer] サーバー実行エラー: {ex.Message}");
                    }
                });

                Debug.WriteLine($"[MobileWebSocketServer] サーバー開始: http://0.0.0.0:{_port}/");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MobileWebSocketServer] 開始エラー: {ex.Message}");
                _ = StopAsync();
                throw;
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// サーバーを停止
        /// </summary>
        public async Task StopAsync()
        {
            if (_app == null) return;

            try
            {
                _cts?.Cancel();

                // 全WebSocket接続を閉じる
                await CloseAllConnectionsAsync();

                // CocoroCoreM クライアント停止
                if (_cocoroClient != null)
                {
                    await _cocoroClient.DisconnectAsync();
                    _cocoroClient.Dispose();
                    _cocoroClient = null;
                }

                // VOICEVOX クライアント停止
                _voicevoxClient?.Dispose();
                _voicevoxClient = null;

                // アプリケーション停止
                var stopTask = _app.StopAsync(TimeSpan.FromSeconds(5));
                await stopTask.ConfigureAwait(false);

                await _app.DisposeAsync();
                _app = null;

                _cts?.Dispose();
                _cts = null;

                Debug.WriteLine("[MobileWebSocketServer] サーバー停止完了");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MobileWebSocketServer] 停止エラー: {ex.Message}");
            }
        }

        /// <summary>
        /// サービスの設定
        /// </summary>
        private void ConfigureServices(WebApplicationBuilder builder)
        {
            // 必要に応じてサービスを追加
        }

        /// <summary>
        /// ミドルウェアとエンドポイントの設定
        /// </summary>
        private void ConfigureApp(WebApplication app)
        {
            // WebSocketサポートを有効化
            app.UseWebSockets();

            // 静的ファイル配信（PWA用）
            var wwwrootPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
            if (Directory.Exists(wwwrootPath))
            {
                app.UseStaticFiles(new StaticFileOptions
                {
                    FileProvider = new PhysicalFileProvider(wwwrootPath),
                    RequestPath = ""
                });
            }

            // WebSocketエンドポイント
            app.Map("/mobile", async context =>
            {
                if (context.WebSockets.IsWebSocketRequest)
                {
                    await HandleWebSocketAsync(context);
                }
                else
                {
                    context.Response.StatusCode = 400;
                    await context.Response.WriteAsync("WebSocket request expected");
                }
            });

            // 音声ファイル配信エンドポイント
            app.MapGet("/audio/{filename}", async (HttpContext context) =>
            {
                return await HandleAudioFileAsync(context);
            });

            // ルートパス処理（index.htmlにリダイレクト）
            app.MapGet("/", context =>
            {
                context.Response.Redirect("/index.html");
                return Task.CompletedTask;
            });
        }

        /// <summary>
        /// CocoreCoreM クライアント初期化
        /// </summary>
        private void InitializeCocoroCoreClient()
        {
            try
            {
                var cocoroPort = _appSettings.GetConfigSettings().cocoroCorePort;
                var clientId = $"mobile_{DateTime.Now:yyyyMMddHHmmss}";
                _cocoroClient = new WebSocketChatClient(cocoroPort, clientId);
                _cocoroClient.MessageReceived += OnCocoroCoreMessageReceived;
                _cocoroClient.ErrorOccurred += OnCocoroCoreError;

                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _cocoroClient.ConnectAsync();
                        Debug.WriteLine("[MobileWebSocketServer] CocoreCoreM接続完了");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[MobileWebSocketServer] CocoreCoreM接続エラー: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MobileWebSocketServer] CocoreCoreM初期化エラー: {ex.Message}");
            }
        }

        /// <summary>
        /// VOICEVOX クライアント初期化
        /// </summary>
        private void InitializeVoicevoxClient()
        {
            try
            {
                var currentChar = _appSettings.GetCurrentCharacter();
                var voicevoxUrl = currentChar?.voicevoxConfig?.endpointUrl ?? "http://0.0.0.0:50021";
                _voicevoxClient = new VoicevoxClient(voicevoxUrl);
                Debug.WriteLine("[MobileWebSocketServer] VOICEVOX初期化完了");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MobileWebSocketServer] VOICEVOX初期化エラー: {ex.Message}");
            }
        }

        /// <summary>
        /// WebSocket接続処理
        /// </summary>
        private async Task HandleWebSocketAsync(HttpContext context)
        {
            var webSocket = await context.WebSockets.AcceptWebSocketAsync();
            var connectionId = Guid.NewGuid().ToString();
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

            while (webSocket.State == WebSocketState.Open && !_cts!.Token.IsCancellationRequested)
            {
                try
                {
                    var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), _cts.Token);

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
                catch (WebSocketException wsEx)
                {
                    Debug.WriteLine($"[MobileWebSocketServer] WebSocket例外: {wsEx.Message}");
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
                await webSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, _cts!.Token);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MobileWebSocketServer] JSON送信エラー: {ex.Message}");
            }
        }

        /// <summary>
        /// 音声ファイル配信処理
        /// </summary>
        private Task<IResult> HandleAudioFileAsync(HttpContext context)
        {
            try
            {
                var filename = context.Request.RouteValues["filename"]?.ToString();
                if (string.IsNullOrEmpty(filename))
                {
                    return Task.FromResult(Results.NotFound());
                }

                using var fileStream = _voicevoxClient?.GetAudioFileStream(filename);
                if (fileStream == null)
                {
                    return Task.FromResult(Results.NotFound());
                }

                Debug.WriteLine($"[MobileWebSocketServer] 音声ファイル配信: {filename}");
                return Task.FromResult(Results.File(fileStream, "audio/wav"));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MobileWebSocketServer] 音声ファイル配信エラー: {ex.Message}");
                return Task.FromResult(Results.Problem("Audio file delivery error"));
            }
        }

        /// <summary>
        /// CocoreCoreM エラーイベント
        /// </summary>
        private void OnCocoroCoreError(object? sender, string error)
        {
            Debug.WriteLine($"[MobileWebSocketServer] CocoreCoreエラー: {error}");
            // 全接続にエラーを通知
            _ = Task.Run(async () =>
            {
                foreach (var connectionId in _connections.Keys)
                {
                    await SendErrorToMobile(connectionId, MobileErrorCodes.CoreMError, error);
                }
            });
        }

        /// <summary>
        /// 全WebSocket接続を閉じる
        /// </summary>
        private async Task CloseAllConnectionsAsync()
        {
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
        }

        public void Dispose()
        {
            Task.Run(async () => await StopAsync()).Wait(TimeSpan.FromSeconds(10));
        }
    }
}