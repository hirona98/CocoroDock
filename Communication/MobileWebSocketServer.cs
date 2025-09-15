using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
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
using Microsoft.Extensions.Logging;

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
        private ISpeechSynthesizerClient? _ttsClient;
        private ISpeechToTextService? _sttService;
        private string? _currentSttApiKey;
        private CancellationTokenSource? _cts;

        // 接続管理（スマホ1台想定だが複数接続対応）
        private readonly ConcurrentDictionary<string, WebSocket> _connections = new();
        private readonly ConcurrentDictionary<string, string> _sessionMappings = new();
        private readonly ConcurrentDictionary<string, string> _connectionAudioFiles = new(); // 接続IDごとの現在のオーディオファイル
        private readonly ConcurrentDictionary<string, string> _sessionImageData = new(); // セッションIDごとの画像データ（Base64）

        private Timer? _reconnectionTimer; // CocoreCoreM再接続用タイマー
        private volatile bool _isConnecting = false; // ConnectAsync実行中フラグ（並列実行防止）

        public bool IsRunning => _app != null;

        /// <summary>
        /// デバッグログ出力（ファイル+コンソール）
        /// </summary>
        private void LogDebug(string message)
        {
            var fullMessage = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}";
            Console.WriteLine(fullMessage);

            try
            {
                File.AppendAllText("cocoro_mobile_debug.log", fullMessage + "\n");
            }
            catch { }
        }

        // モバイルチャットのイベント
        public event EventHandler<string>? MobileMessageReceived;
        public event EventHandler<(string message, string imageBase64)>? MobileImageMessageReceived;
        public event EventHandler<(string text, string? imageBase64)>? MobileAiResponseReceived;

        public MobileWebSocketServer(int port, IAppSettings appSettings)
        {
            _port = port;
            _appSettings = appSettings;

            // 起動時に古い音声ファイルをクリーンアップ
            CleanupAudioFilesOnStartup();

            // 起動時に古い画像ファイルをクリーンアップ
            CleanupImageFilesOnStartup();

            var httpsPort = _appSettings.GetConfigSettings().cocoroWebPort;
            Debug.WriteLine($"[MobileWebSocketServer] 初期化: HTTPS ポート={httpsPort}");
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

                // 設定からHTTPSポートを取得（cocoroWebPort、デフォルト55607）
                var httpsPort = _appSettings.GetConfigSettings().cocoroWebPort;

                var builder = WebApplication.CreateBuilder();

                // ログレベルを設定してHTTPリクエストログを無効化
                builder.Logging.ClearProviders();
                builder.Logging.SetMinimumLevel(LogLevel.Warning);

                // Kestrelサーバーの設定（HTTPS対応・外部アクセス対応・管理者権限不要）
                builder.WebHost.ConfigureKestrel(serverOptions =>
                {
                    serverOptions.ListenAnyIP(httpsPort, listenOptions =>
                    {
                        listenOptions.UseHttps(GenerateSelfSignedCertificate());
                    });
                });

                // サービスの登録
                ConfigureServices(builder);

                var app = builder.Build();

                // ミドルウェアとエンドポイントの設定
                ConfigureApp(app);

                _app = app;

                // CocoreCoreM クライアント初期化
                InitializeCocoroCoreClient();

                // TTS クライアント初期化
                InitializeTtsClient();

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

                Debug.WriteLine($"[MobileWebSocketServer] HTTPS サーバー開始: https://0.0.0.0:{httpsPort}/");
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

                // TTS クライアント停止
                _ttsClient?.Dispose();
                _ttsClient = null;

                // STTサービス停止
                _sttService?.Dispose();
                _sttService = null;
                _currentSttApiKey = null;

                // アプリケーション停止
                var stopTask = _app.StopAsync(TimeSpan.FromSeconds(5));
                await stopTask.ConfigureAwait(false);

                await _app.DisposeAsync();
                _app = null;

                _cts?.Dispose();
                _cts = null;

                // 終了時に画像ファイルをクリーンアップ
                CleanupImageFilesOnStartup();

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

            // 静的ファイル配信（EmbeddedResourceから直接配信）
            var assembly = typeof(MobileWebSocketServer).Assembly;

            // 埋め込みリソース配信ミドルウェア
            app.Use(async (context, next) =>
            {
                var path = context.Request.Path.Value?.TrimStart('/') ?? "";

                // APIやWebSocketエンドポイントの場合はスキップ
                if (context.Request.Path.StartsWithSegments("/mobile") ||
                    context.Request.Path.StartsWithSegments("/audio") ||
                    context.WebSockets.IsWebSocketRequest)
                {
                    await next();
                    return;
                }

                // ルートパスの場合はindex.htmlを返す
                if (string.IsNullOrEmpty(path) || path == "/")
                {
                    path = "index.html";
                }

                // パスからリソース名を構築
                var resourceName = $"CocoroDock.wwwroot.{path.Replace('/', '.')}";

                using var stream = assembly.GetManifestResourceStream(resourceName);
                if (stream != null)
                {
                    // MIMEタイプを設定
                    var extension = Path.GetExtension(path).ToLower();
                    context.Response.ContentType = extension switch
                    {
                        ".html" => "text/html",
                        ".css" => "text/css",
                        ".js" => "application/javascript",
                        ".json" => "application/json",
                        ".png" => "image/png",
                        ".jpg" or ".jpeg" => "image/jpeg",
                        ".gif" => "image/gif",
                        ".ico" => "image/x-icon",
                        ".wasm" => "application/wasm",
                        _ => "application/octet-stream"
                    };

                    await stream.CopyToAsync(context.Response.Body);
                    return;
                }

                // ファイルが見つからない場合、拡張子がない場合はindex.htmlを試す
                if (!Path.HasExtension(path))
                {
                    using var indexStream = assembly.GetManifestResourceStream("CocoroDock.wwwroot.index.html");
                    if (indexStream != null)
                    {
                        context.Response.ContentType = "text/html";
                        await indexStream.CopyToAsync(context.Response.Body);
                        return;
                    }
                }

                await next();
            });

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

            // 今は不要（上のミドルウェアで処理される）
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

                // 同期的に接続を試行（タイミング問題を回避）
                _ = Task.Run(async () =>
                {
                    // 少し待ってからCocoreCoreM接続を試行（CocoroCoreMの起動を待つ）
                    await Task.Delay(2000);

                    try
                    {
                        await _cocoroClient.ConnectAsync();

                        // 接続に失敗した場合の詳細チェック
                        if (_cocoroClient?.IsConnected != true)
                        {
                            // 再接続を1回試行
                            await Task.Delay(1000);

                            // null チェックを追加してCS8602を修正
                            if (_cocoroClient != null)
                            {
                                await _cocoroClient.ConnectAsync();
                            }
                        }
                    }
                    catch (Exception)
                    {
                        // 接続失敗時は定期的な再接続を開始
                        StartReconnectionTimer();
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MobileWebSocketServer] CocoreCoreM初期化エラー: {ex.Message}");
            }
        }

        /// <summary>
        /// CocoreCoreM定期再接続タイマーを開始
        /// </summary>
        private void StartReconnectionTimer()
        {
            lock (this) // スレッドセーフにする
            {
                // 既にタイマーが動いている場合は何もしない
                if (_reconnectionTimer != null)
                    return;

                _reconnectionTimer = new Timer(async _ =>
                {
                    // 既に接続処理中の場合はスキップ（並列実行防止）
                    if (_isConnecting)
                    {
                        return;
                    }

                    // 接続状態チェック
                    if (_cocoroClient?.IsConnected != true)
                    {
                        _isConnecting = true; // フラグをセット
                        try
                        {
                            await _cocoroClient!.ConnectAsync();

                            if (_cocoroClient.IsConnected)
                            {
                                StopReconnectionTimer();
                            }
                        }
                        catch (Exception)
                        {
                            // エラーは無視して次回再試行
                        }
                        finally
                        {
                            _isConnecting = false; // 必ずフラグをリセット
                        }
                    }
                    else
                    {
                        StopReconnectionTimer();
                    }
                }, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
            }
        }

        /// <summary>
        /// CocoreCoreM再接続タイマーを停止
        /// </summary>
        private void StopReconnectionTimer()
        {
            lock (this) // スレッドセーフにする
            {
                if (_reconnectionTimer != null)
                {
                    try
                    {
                        _reconnectionTimer.Dispose();
                        _reconnectionTimer = null;
                    }
                    catch (Exception)
                    {
                        _reconnectionTimer = null; // エラーでも確実にnullにする
                    }
                }
            }
        }

        /// <summary>
        /// VOICEVOX クライアント初期化
        /// </summary>
        private void InitializeTtsClient()
        {
            try
            {
                var currentChar = _appSettings.GetCurrentCharacter();
                if (currentChar == null)
                {
                    Debug.WriteLine("[MobileWebSocketServer] 現在のキャラクター設定が見つかりません");
                    return;
                }

                // 既存のクライアントがある場合は破棄
                _ttsClient?.Dispose();

                // ファクトリーを使って適切なTTSクライアントを作成
                _ttsClient = SpeechSynthesizerFactory.CreateClient(currentChar);

                Debug.WriteLine($"[MobileWebSocketServer] {_ttsClient.ProviderName}クライアント初期化完了");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MobileWebSocketServer] TTSクライアント初期化エラー: {ex.Message}");

                // フォールバック: デフォルトのVOICEVOXクライアントを作成
                try
                {
                    _ttsClient = new VoicevoxClient();
                    Debug.WriteLine("[MobileWebSocketServer] フォールバック: デフォルトVOICEVOXクライアントを使用");
                }
                catch (Exception fallbackEx)
                {
                    Debug.WriteLine($"[MobileWebSocketServer] フォールバック初期化エラー: {fallbackEx.Message}");
                }
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

                // セッションマッピングから該当のconnectionIdを持つエントリを削除
                var sessionIdsToRemove = _sessionMappings
                    .Where(kvp => kvp.Value == connectionId)
                    .Select(kvp => kvp.Key)
                    .ToList();

                foreach (var sessionId in sessionIdsToRemove)
                {
                    _sessionMappings.TryRemove(sessionId, out _);
                }

                // 接続終了時に関連する音声ファイルを削除
                DeleteAudioFileForConnection(connectionId);

                Debug.WriteLine($"[MobileWebSocketServer] WebSocket接続終了: {connectionId}");
            }
        }

        /// <summary>
        /// WebSocket通信処理
        /// </summary>
        private async Task HandleWebSocketCommunication(string connectionId, WebSocket webSocket)
        {
            var buffer = new byte[1024 * 16]; // 16KBに増加（音声データ効率化）
            using var messageBuffer = new MemoryStream(); // メモリ効率化

            while (webSocket.State == WebSocketState.Open && !_cts!.Token.IsCancellationRequested)
            {
                try
                {
                    var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), _cts.Token);

                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        // メッセージの断片を蓄積
                        messageBuffer.Write(buffer, 0, result.Count);

                        // メッセージが完了した場合のみ処理
                        if (result.EndOfMessage)
                        {
                            var json = Encoding.UTF8.GetString(messageBuffer.ToArray());
                            await ProcessMobileMessage(connectionId, json);
                            messageBuffer.SetLength(0); // バッファをクリア
                        }
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
        /// モバイルからのメッセージ処理（統合版）
        /// </summary>
        private async Task ProcessMobileMessage(string connectionId, string json)
        {
            try
            {

                // メッセージタイプを事前判定
                using var jsonDoc = JsonDocument.Parse(json);
                var root = jsonDoc.RootElement;

                if (!root.TryGetProperty("type", out var typeElement))
                {
                    await SendErrorToMobile(connectionId, MobileErrorCodes.InvalidMessage, "Message type not specified");
                    return;
                }

                var messageType = typeElement.GetString();

                switch (messageType)
                {
                    case "chat":
                        await ProcessChatMessage(connectionId, json);
                        break;

                    case "voice":
                        await ProcessVoiceMessage(connectionId, json);
                        break;

                    case "image":
                        await ProcessImageMessage(connectionId, json);
                        break;

                    default:
                        await SendErrorToMobile(connectionId, MobileErrorCodes.InvalidMessage, $"Unsupported message type: {messageType}");
                        break;
                }
            }
            catch (JsonException jsonEx)
            {
                Console.WriteLine($"[MobileWebSocketServer] ProcessMobileMessage JSONエラー: {jsonEx.Message}");
                await SendErrorToMobile(connectionId, MobileErrorCodes.InvalidMessage, "Invalid JSON format");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MobileWebSocketServer] メッセージ処理エラー: {ex.Message}");
                await SendErrorToMobile(connectionId, MobileErrorCodes.ServerError, "Message processing error");
            }
        }

        /// <summary>
        /// チャットメッセージ処理（従来機能）
        /// </summary>
        private async Task ProcessChatMessage(string connectionId, string json)
        {
            try
            {
                var message = JsonSerializer.Deserialize<MobileChatMessage>(json);
                if (message?.Data == null)
                {
                    await SendErrorToMobile(connectionId, MobileErrorCodes.InvalidMessage, "Invalid chat message format");
                    return;
                }

                // CocoroDockにモバイルメッセージを通知
                MobileMessageReceived?.Invoke(this, $"📱 {message.Data.Message}");

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
                    await SendErrorToMobile(connectionId, MobileErrorCodes.CoreMError, "起動中です。しばらくお待ちください...");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MobileWebSocketServer] チャットメッセージ処理エラー: {ex.Message}");
                await SendErrorToMobile(connectionId, MobileErrorCodes.ServerError, "Chat message processing error");
            }
        }

        /// <summary>
        /// 音声メッセージ処理（RNNoise統合版）
        /// </summary>
        private async Task ProcessVoiceMessage(string connectionId, string json)
        {
            try
            {
                MobileVoiceMessage? message;
                try
                {
                    message = JsonSerializer.Deserialize<MobileVoiceMessage>(json);
                    if (message == null)
                    {
                        await SendErrorToMobile(connectionId, MobileErrorCodes.InvalidMessage, "Deserialized message is null");
                        return;
                    }
                }
                catch (JsonException jsonEx)
                {
                    Debug.WriteLine($"[MobileWebSocketServer] JSONデシリアライズエラー: {jsonEx.Message}");
                    Debug.WriteLine($"[MobileWebSocketServer] エラー位置: Line {jsonEx.LineNumber}, Position {jsonEx.BytePositionInLine}");
                    await SendErrorToMobile(connectionId, MobileErrorCodes.VoiceDataError, $"JSON parse error: {jsonEx.Message}");
                    return;
                }
                if (message?.Data == null)
                {
                    await SendErrorToMobile(connectionId, MobileErrorCodes.VoiceDataError, "Invalid voice message format");
                    return;
                }

                // Base64とList<int>の両方に対応
                byte[] audioBytes;
                if (!string.IsNullOrEmpty(message.Data.AudioDataBase64))
                {
                    // Base64デコード
                    audioBytes = Convert.FromBase64String(message.Data.AudioDataBase64);
                }
                else
                {
                    await SendErrorToMobile(connectionId, MobileErrorCodes.VoiceDataError, "No audio data provided");
                    return;
                }

                // STT設定の事前チェック
                var currentCharacter = _appSettings.GetCurrentCharacter();
                if (currentCharacter == null)
                {
                    await SendErrorToMobile(connectionId, MobileErrorCodes.ServerError, "No character configured");
                    return;
                }

                // Web経由の音声認識要求では isUseSTT 設定を無視
                // if (!currentCharacter.isUseSTT)
                // {
                //     await SendErrorToMobile(connectionId, MobileErrorCodes.VoiceRecognitionError, "Speech-to-text is disabled for current character");
                //     return;
                // }

                if (string.IsNullOrEmpty(currentCharacter.sttApiKey))
                {
                    await SendErrorToMobile(connectionId, MobileErrorCodes.VoiceRecognitionError, "音声認識APIキーが設定されていません");
                    return;
                }

                // 音声認識実行
                var recognizedText = await ProcessVoiceData(
                    audioBytes,
                    message.Data.SampleRate,
                    message.Data.Channels,
                    message.Data.Format);

                if (!string.IsNullOrWhiteSpace(recognizedText))
                {
                    // 音声認識結果をWebUIにユーザーメッセージとして表示
                    await SendUserMessageToMobile(connectionId, recognizedText);
                    // 認識されたテキストをチャットメッセージとして処理
                    await ProcessRecognizedVoiceAsChat(connectionId, recognizedText);
                }
            }
            catch (FormatException formatEx)
            {
                Debug.WriteLine($"[MobileWebSocketServer] Base64デコードエラー: {formatEx.Message}");
                await SendErrorToMobile(connectionId, MobileErrorCodes.VoiceDataError, "音声データの形式が正しくありません");
            }
            catch (ArgumentException argEx)
            {
                Debug.WriteLine($"[MobileWebSocketServer] 音声データ検証エラー: {argEx.Message}");
                await SendErrorToMobile(connectionId, MobileErrorCodes.VoiceDataError, "音声データに問題があります");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MobileWebSocketServer] 音声メッセージ処理エラー: {ex.Message}");
                await SendErrorToMobile(connectionId, MobileErrorCodes.AudioProcessingError, "音声処理中にエラーが発生しました");
            }
        }

        /// <summary>
        /// 画像メッセージの処理
        /// </summary>
        private async Task ProcessImageMessage(string connectionId, string json)
        {
            try
            {
                MobileImageMessage? message;
                try
                {
                    message = JsonSerializer.Deserialize<MobileImageMessage>(json);
                    if (message == null)
                    {
                        await SendErrorToMobile(connectionId, MobileErrorCodes.InvalidMessage, "Deserialized message is null");
                        return;
                    }
                }
                catch (JsonException jsonEx)
                {
                    Debug.WriteLine($"[MobileWebSocketServer] JSON デシリアライズエラー: {jsonEx.Message}");
                    Debug.WriteLine($"[MobileWebSocketServer] エラー位置: Line {jsonEx.LineNumber}, Position {jsonEx.BytePositionInLine}");
                    await SendErrorToMobile(connectionId, MobileErrorCodes.InvalidMessage, $"JSON parse error: {jsonEx.Message}");
                    return;
                }

                if (message?.Data == null)
                {
                    await SendErrorToMobile(connectionId, MobileErrorCodes.InvalidMessage, "Invalid image message format");
                    return;
                }

                // Base64画像データの検証
                string base64ImageData = message.Data.ImageDataBase64;
                if (string.IsNullOrEmpty(base64ImageData))
                {
                    // 後方互換性のために既存のプロパティもチェック
                    base64ImageData = message.Data.ImageData;
                }

                if (string.IsNullOrEmpty(base64ImageData))
                {
                    await SendErrorToMobile(connectionId, MobileErrorCodes.InvalidMessage, "No image data provided");
                    return;
                }

                byte[] imageBytes;
                try
                {
                    // Base64デコード
                    imageBytes = Convert.FromBase64String(base64ImageData);
                }
                catch (FormatException)
                {
                    await SendErrorToMobile(connectionId, MobileErrorCodes.InvalidMessage, "Invalid base64 image data format");
                    return;
                }

                // 画像サイズの検証（10MB制限）
                const int maxImageSize = 10 * 1024 * 1024; // 10MB
                if (imageBytes.Length > maxImageSize)
                {
                    await SendErrorToMobile(connectionId, MobileErrorCodes.InvalidMessage, $"Image too large: {imageBytes.Length} bytes (max: {maxImageSize} bytes)");
                    return;
                }

                // 画像形式の検証（JPEG, PNG, WebPをサポート）
                string imageFormat = message.Data.Format?.ToLower() ?? "jpeg";
                if (!IsValidImageFormat(imageFormat))
                {
                    await SendErrorToMobile(connectionId, MobileErrorCodes.InvalidMessage, $"Unsupported image format: {imageFormat}");
                    return;
                }

                // tmp/imageディレクトリを作成
                var imageDirectory = Path.Combine(Directory.GetCurrentDirectory(), "tmp", "image");
                Directory.CreateDirectory(imageDirectory);

                // 画像ファイルを一時保存
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
                string fileName = $"mobile_image_{timestamp}.{imageFormat}";
                string imagePath = Path.Combine(imageDirectory, fileName);

                try
                {
                    await File.WriteAllBytesAsync(imagePath, imageBytes);
                    Debug.WriteLine($"[MobileWebSocketServer] 画像ファイル保存: {imagePath}");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[MobileWebSocketServer] 画像ファイル保存エラー: {ex.Message}");
                    await SendErrorToMobile(connectionId, MobileErrorCodes.ServerError, "Failed to save image file");
                    return;
                }

                // CocoroDock に画像メッセージを通知
                string imageMessage = message.Data.Message ?? "";
                MobileImageMessageReceived?.Invoke(this, (imageMessage, base64ImageData));

                try
                {
                    // 画像処理結果をレスポンスとして送信
                    await ProcessRecognizedImageAsChat(connectionId, imageMessage, imagePath);

                    Debug.WriteLine($"[MobileWebSocketServer] 画像処理完了: {fileName} ({imageBytes.Length} bytes, {message.Data.Width}x{message.Data.Height})");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[MobileWebSocketServer] 画像メッセージ処理エラー: {ex.Message}");
                    await SendErrorToMobile(connectionId, MobileErrorCodes.ImageProcessingError, "Image processing failed");
                }
                finally
                {
                    // 処理完了後にファイルを削除
                    try
                    {
                        if (File.Exists(imagePath))
                        {
                            File.Delete(imagePath);
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[MobileWebSocketServer] 画像ファイル削除エラー: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MobileWebSocketServer] 画像メッセージ処理エラー: {ex.Message}");
                await SendErrorToMobile(connectionId, MobileErrorCodes.ImageProcessingError, "Image message processing error");
            }
        }

        /// <summary>
        /// 画像形式の検証
        /// </summary>
        private bool IsValidImageFormat(string format)
        {
            var validFormats = new[] { "jpeg", "jpg", "png", "webp", "gif" };
            return validFormats.Contains(format?.ToLower());
        }

        /// <summary>
        /// 認識された画像をチャットメッセージとして処理
        /// </summary>
        private async Task ProcessRecognizedImageAsChat(string connectionId, string message, string imagePath)
        {
            try
            {
                // 画像ファイルをBase64データに変換
                byte[] imageBytes = await File.ReadAllBytesAsync(imagePath);
                string base64String = Convert.ToBase64String(imageBytes);

                // セッションIDの生成と管理
                var sessionId = $"image_{connectionId}_{DateTime.Now:yyyyMMddHHmmss}";
                _sessionMappings[sessionId] = connectionId;
                _sessionImageData[sessionId] = base64String; // 画像データをセッションに関連付け

                string extension = Path.GetExtension(imagePath).ToLower().TrimStart('.');
                string mimeType = extension switch
                {
                    "jpg" or "jpeg" => "image/jpeg",
                    "png" => "image/png",
                    "webp" => "image/webp",
                    "gif" => "image/gif",
                    _ => "image/jpeg"
                };
                string dataUrl = $"data:{mimeType};base64,{base64String}";

                // チャットメッセージとして処理
                var chatRequest = new WebSocketChatRequest
                {
                    query = message, // 空文字の場合もある
                    chat_type = "image_upload",
                    images = new List<ImageData>
                    {
                        new ImageData { data = dataUrl }
                    }
                };

                // CocoreCoreM にメッセージ送信
                if (_cocoroClient != null && _cocoroClient.IsConnected)
                {
                    await _cocoroClient.SendChatAsync(sessionId, chatRequest);
                }
                else
                {
                    await SendErrorToMobile(connectionId, MobileErrorCodes.ServerError, "CocoroCore connection not available");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MobileWebSocketServer] 画像チャットメッセージ処理エラー: {ex.Message}");
                await SendErrorToMobile(connectionId, MobileErrorCodes.ServerError, "Failed to process image chat message");
                throw;
            }
        }

        /// <summary>
        /// 音声データ処理（RealtimeVoiceRecognitionServiceを使用）
        /// </summary>
        private async Task<string> ProcessVoiceData(byte[] audioData, int sampleRate, int channels, string format)
        {
            try
            {
                // 現在のキャラクターのSTT設定を取得
                var currentCharacter = _appSettings.GetCurrentCharacter();
                if (currentCharacter?.sttApiKey == null || string.IsNullOrEmpty(currentCharacter.sttApiKey))
                {
                    throw new ArgumentException("STT API key not configured for current character");
                }

                // クライアント側で16kHzに変換済みのデータを期待
                if (sampleRate != 16000)
                {
                    Debug.WriteLine($"[MobileWebSocketServer] 警告: 予期しないサンプルレート {sampleRate}Hz (16kHzを期待)");
                }

                // WebSocket音声データの音声認識（SileroVADは使用しない）
                var recognizedText = await RecognizeWebSocketAudioAsync(currentCharacter.sttApiKey, audioData);

                if (string.IsNullOrWhiteSpace(recognizedText))
                {
                    return string.Empty;
                }

                return recognizedText;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MobileWebSocketServer] 音声データ処理エラー: {ex.Message}");
                throw new Exception($"Voice data processing failed: {ex.Message}");
            }
        }


        /// <summary>
        /// WAVファイル形式の検証
        /// </summary>
        private bool IsValidWavFile(byte[] audioData)
        {
            try
            {
                if (audioData.Length < 44) return false;

                // RIFFヘッダー確認
                var riffHeader = System.Text.Encoding.ASCII.GetString(audioData, 0, 4);
                var waveHeader = System.Text.Encoding.ASCII.GetString(audioData, 8, 4);

                return riffHeader == "RIFF" && waveHeader == "WAVE";
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 認識された音声をチャットメッセージとして処理
        /// </summary>
        private async Task ProcessRecognizedVoiceAsChat(string connectionId, string recognizedText)
        {
            try
            {
                // CocoroDockに音声認識結果を通知
                MobileMessageReceived?.Invoke(this, $"📱 {recognizedText}");

                // チャットメッセージとして処理
                var chatRequest = new WebSocketChatRequest
                {
                    query = recognizedText,
                    chat_type = "voice_to_text",
                    images = null
                };

                // セッションIDの生成と管理
                var sessionId = $"voice_{connectionId}_{DateTime.Now:yyyyMMddHHmmss}";
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
                Debug.WriteLine($"[MobileWebSocketServer] 音声チャット処理エラー: {ex.Message}");
                await SendErrorToMobile(connectionId, MobileErrorCodes.ServerError, "Voice chat processing error");
            }
        }

        /// <summary>
        /// CocoreCoreM からのメッセージ受信イベント
        /// </summary>
        private void OnCocoroCoreMessageReceived(object? sender, WebSocketResponseMessage response)
        {
            // async voidの問題を回避するため、Task.Runで包む
            _ = Task.Run(async () =>
            {
                try
                {
                    // セッションIDから接続IDを取得
                    if (!_sessionMappings.TryGetValue(response.session_id ?? "", out var connectionId))
                    {
                        return;
                    }

                    // 応答タイプに応じて処理
                    if (response.type == "text")
                    {
                        // JsonElementからテキストデータを取得
                        var textContent = ExtractTextContent(response.data);
                        if (!string.IsNullOrEmpty(textContent))
                        {
                            // 音声合成処理
                            string? audioUrl = null;
                            if (_ttsClient != null && !string.IsNullOrWhiteSpace(textContent))
                            {
                                // 新しいファイル生成前に古いファイルを削除
                                DeleteAudioFileForConnection(connectionId);

                                var currentChar = _appSettings.GetCurrentCharacter();
                                if (currentChar != null)
                                {
                                    audioUrl = await _ttsClient.SynthesizeAsync(textContent, currentChar);
                                }

                                // 新しいファイルを記録
                                if (!string.IsNullOrEmpty(audioUrl))
                                {
                                    _connectionAudioFiles[connectionId] = audioUrl;
                                }
                            }

                            await SendPartialResponseToMobile(connectionId, textContent, audioUrl);

                            // セッションに関連付けられた画像データがあるかチェック
                            string? imageBase64 = null;
                            if (_sessionImageData.TryGetValue(response.session_id ?? "", out imageBase64))
                            {
                                // 画像データが見つかった場合、デスクトップアプリに画像付きAI応答を通知
                                MobileAiResponseReceived?.Invoke(this, (textContent, imageBase64));

                                // 使用済み画像データを削除
                                _sessionImageData.TryRemove(response.session_id ?? "", out _);
                            }
                            else
                            {
                                // 通常のAI応答（画像なし）
                                MobileAiResponseReceived?.Invoke(this, (textContent, null));
                            }
                        }
                        else
                        {
                            Debug.WriteLine($"[MobileWebSocketServer] Null or empty textContent received for connectionId: {connectionId}");
                        }
                    }
                    else if (response.type == "error")
                    {
                        await SendErrorToMobile(connectionId, MobileErrorCodes.CoreMError, "CocoreCoreM processing error");

                        // エラー時もセッション画像データをクリーンアップ
                        _sessionImageData.TryRemove(response.session_id ?? "", out _);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[MobileWebSocketServer] メッセージ処理エラー: {ex.Message}");
                }
            });
        }


        /// <summary>
        /// 部分応答送信（ストリーミング）
        /// </summary>
        private async Task SendPartialResponseToMobile(string connectionId, string text)
        {
            await SendPartialResponseToMobile(connectionId, text, null);
        }

        private async Task SendPartialResponseToMobile(string connectionId, string text, string? audioUrl)
        {
            var response = new MobileResponseMessage
            {
                Data = new MobileResponseData
                {
                    Text = text,
                    AudioUrl = audioUrl,
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
        /// WebUIにユーザーメッセージを送信（音声認識結果用）
        /// </summary>
        private async Task SendUserMessageToMobile(string connectionId, string message)
        {
            try
            {
                var chatMessage = new MobileChatMessage
                {
                    Data = new MobileChatData
                    {
                        Message = message,
                        ChatType = "voice_recognition_user",
                        Images = null
                    }
                };

                await SendJsonToMobile(connectionId, chatMessage);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MobileWebSocketServer] SendUserMessageToMobileエラー: {ex.Message}");
            }
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
                Debug.WriteLine($"[MobileWebSocketServer] 送信エラー: {ex.Message}");
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

                var fileStream = _ttsClient?.GetAudioFileStream(filename);
                if (fileStream == null)
                {
                    return Task.FromResult(Results.NotFound());
                }

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
        /// JsonElementからテキストコンテンツを抽出
        /// </summary>
        private string? ExtractTextContent(object? data)
        {
            try
            {
                if (data is JsonElement jsonElement && jsonElement.TryGetProperty("content", out var contentElement))
                {
                    return contentElement.GetString();
                }
                else if (data is WebSocketTextData textData)
                {
                    return textData.content;
                }
                return null;
            }
            catch
            {
                return null;
            }
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
            _connectionAudioFiles.Clear();
            _sessionImageData.Clear(); // セッション画像データもクリア
        }

        /// <summary>
        /// 起動時に古い音声ファイルをすべて削除
        /// </summary>
        private void CleanupAudioFilesOnStartup()
        {
            try
            {
                var audioDirectory = Path.Combine(Directory.GetCurrentDirectory(), "tmp", "audio");

                if (!Directory.Exists(audioDirectory))
                {
                    Debug.WriteLine("[MobileWebSocketServer] 音声ディレクトリが存在しません");
                    return;
                }

                var audioFiles = Directory.GetFiles(audioDirectory, "*.wav");
                var deletedCount = 0;

                foreach (var filePath in audioFiles)
                {
                    try
                    {
                        File.Delete(filePath);
                        deletedCount++;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[MobileWebSocketServer] 起動時ファイル削除エラー {Path.GetFileName(filePath)}: {ex.Message}");
                    }
                }

                Debug.WriteLine($"[MobileWebSocketServer] 起動時クリーンアップ完了: {deletedCount}個のファイルを削除");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MobileWebSocketServer] 起動時クリーンアップエラー: {ex.Message}");
            }
        }

        /// <summary>
        /// 起動時に古い画像ファイルをクリーンアップ
        /// </summary>
        private void CleanupImageFilesOnStartup()
        {
            try
            {
                var imageDirectory = Path.Combine(Directory.GetCurrentDirectory(), "tmp", "image");

                if (!Directory.Exists(imageDirectory))
                {
                    Debug.WriteLine("[MobileWebSocketServer] 画像ディレクトリが存在しません");
                    return;
                }

                var imageFiles = Directory.GetFiles(imageDirectory, "*.*");
                var deletedCount = 0;

                foreach (var filePath in imageFiles)
                {
                    try
                    {
                        File.Delete(filePath);
                        deletedCount++;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[MobileWebSocketServer] 起動時画像ファイル削除エラー {Path.GetFileName(filePath)}: {ex.Message}");
                    }
                }

                Debug.WriteLine($"[MobileWebSocketServer] 起動時画像クリーンアップ完了: {deletedCount}個のファイルを削除");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MobileWebSocketServer] 起動時画像クリーンアップエラー: {ex.Message}");
            }
        }

        /// <summary>
        /// 接続IDに関連付けられた音声ファイルを削除
        /// </summary>
        private void DeleteAudioFileForConnection(string connectionId)
        {
            if (_connectionAudioFiles.TryRemove(connectionId, out var audioFileName))
            {
                DeleteAudioFile(audioFileName);
            }
        }

        /// <summary>
        /// 音声ファイルを安全に削除
        /// </summary>
        private void DeleteAudioFile(string audioFileName)
        {
            if (string.IsNullOrEmpty(audioFileName)) return;

            try
            {
                // /audio/filename.wav から filename.wav を抽出
                var fileName = Path.GetFileName(audioFileName);
                if (string.IsNullOrEmpty(fileName)) return;

                var audioDirectory = Path.Combine(Directory.GetCurrentDirectory(), "tmp", "audio");
                var filePath = Path.Combine(audioDirectory, fileName);

                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MobileWebSocketServer] 音声ファイル削除エラー: {ex.Message}");
            }
        }

        /// <summary>
        /// HTTPS用の自己証明書を生成
        /// </summary>
        private static X509Certificate2 GenerateSelfSignedCertificate()
        {
            try
            {
                using var rsa = RSA.Create(2048);
                var request = new CertificateRequest(
                    "CN=CocoroAI",
                    rsa,
                    HashAlgorithmName.SHA256,
                    RSASignaturePadding.Pkcs1);

                // 証明書の拡張設定
                request.CertificateExtensions.Add(
                    new X509KeyUsageExtension(
                        X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
                        true));

                // SubjectAlternativeName - 複数のIPアドレス/ホスト名対応
                var sanBuilder = new SubjectAlternativeNameBuilder();
                sanBuilder.AddDnsName("localhost");
                sanBuilder.AddDnsName(Environment.MachineName);
                sanBuilder.AddDnsName("*.local");
                sanBuilder.AddIpAddress(IPAddress.Loopback);
                sanBuilder.AddIpAddress(IPAddress.IPv6Loopback);

                // ローカルIPアドレスを追加
                try
                {
                    var host = System.Net.Dns.GetHostEntry(System.Net.Dns.GetHostName());
                    foreach (var ip in host.AddressList)
                    {
                        if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                        {
                            sanBuilder.AddIpAddress(ip);
                        }
                    }
                }
                catch { }

                request.CertificateExtensions.Add(sanBuilder.Build());

                // Enhanced Key Usage
                request.CertificateExtensions.Add(
                    new X509EnhancedKeyUsageExtension(
                        new OidCollection
                        {
                            new Oid("1.3.6.1.5.5.7.3.1"), // Server Authentication
                            new Oid("1.3.6.1.5.5.7.3.2")  // Client Authentication
                        },
                        true));

                // 5年間有効な証明書を作成
                var certificate = request.CreateSelfSigned(
                    DateTimeOffset.Now.AddDays(-1),
                    DateTimeOffset.Now.AddYears(5));

                // エクスポートして再インポート（Windows互換性のため）
                var exportedCert = certificate.Export(X509ContentType.Pfx, "temp");
                var finalCert = new X509Certificate2(
                    exportedCert,
                    "temp",
                    X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.PersistKeySet);

                return finalCert;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MobileWebSocketServer] 証明書生成エラー: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// WebSocket音声データの音声認識（SileroVAD回避、STTサービス抽象化対応）
        /// </summary>
        private async Task<string> RecognizeWebSocketAudioAsync(string apiKey, byte[] audioData)
        {
            try
            {
                // STTサービスを取得（クラスレベルでインスタンスを再利用）
                var sttService = GetOrCreateSttService(apiKey);
                if (!sttService.IsAvailable)
                {
                    Debug.WriteLine($"[MobileWebSocketServer] STTサービス利用不可: {sttService.ServiceName}");
                    return string.Empty;
                }
                var recognizedText = await sttService.RecognizeAsync(audioData);

                return recognizedText ?? string.Empty;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MobileWebSocketServer] WebSocket音声認識エラー: {ex.Message}");
                return string.Empty;
            }
        }

        /// <summary>
        /// STTサービスファクトリー（将来的な拡張用）
        /// </summary>
        private ISpeechToTextService GetOrCreateSttService(string apiKey)
        {
            // APIキーが変更された場合や、サービスが未初期化の場合は新しいサービスを作成
            if (_sttService == null || _currentSttApiKey != apiKey || !_sttService.IsAvailable)
            {
                _sttService?.Dispose();

                // 現在はAmiVoiceのみ対応
                _sttService = new AmiVoiceSpeechToTextService(apiKey);
                _currentSttApiKey = apiKey;

                // 将来的な拡張のためのコメント
                // var sttType = appSettings?.SttServiceType ?? "AmiVoice";
                // _sttService = sttType switch
                // {
                //     "AmiVoice" => new AmiVoiceSpeechToTextService(apiKey),
                //     _ => new AmiVoiceSpeechToTextService(apiKey)
                // };
            }

            return _sttService;
        }

        public void Dispose()
        {
            StopReconnectionTimer();
            Task.Run(async () => await StopAsync()).Wait(TimeSpan.FromSeconds(10));
        }
    }
}