using System;
using System.Collections.Concurrent;
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
        private VoicevoxClient? _voicevoxClient;
        private CancellationTokenSource? _cts;

        // 接続管理（スマホ1台想定だが複数接続対応）
        private readonly ConcurrentDictionary<string, WebSocket> _connections = new();
        private readonly ConcurrentDictionary<string, string> _sessionMappings = new();
        private readonly ConcurrentDictionary<string, string> _connectionAudioFiles = new(); // 接続IDごとの現在のオーディオファイル

        public bool IsRunning => _app != null;

        // モバイルチャットのイベント
        public event EventHandler<string>? MobileMessageReceived;
        public event EventHandler<string>? MobileResponseSent;

        public MobileWebSocketServer(int port, IAppSettings appSettings)
        {
            _port = port;
            _appSettings = appSettings;

            // 起動時に古い音声ファイルをクリーンアップ
            CleanupAudioFilesOnStartup();

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
                            if (_voicevoxClient != null && !string.IsNullOrWhiteSpace(textContent))
                            {
                                // 新しいファイル生成前に古いファイルを削除
                                DeleteAudioFileForConnection(connectionId);

                                var currentChar = _appSettings.GetCurrentCharacter();
                                var speakerId = currentChar?.voicevoxConfig?.speakerId ?? 3;
                                audioUrl = await _voicevoxClient.SynthesizeAsync(textContent, speakerId);

                                // 新しいファイルを記録
                                if (!string.IsNullOrEmpty(audioUrl))
                                {
                                    _connectionAudioFiles[connectionId] = audioUrl;
                                }
                            }

                            await SendPartialResponseToMobile(connectionId, textContent, audioUrl);
                            // CocoroDockに応答を通知
                            MobileResponseSent?.Invoke(this, textContent);
                        }
                        else
                        {
                            Debug.WriteLine($"[MobileWebSocketServer] Null or empty textContent received for connectionId: {connectionId}");
                        }
                    }
                    else if (response.type == "error")
                    {
                        await SendErrorToMobile(connectionId, MobileErrorCodes.CoreMError, "CocoreCoreM processing error");
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

                var fileStream = _voicevoxClient?.GetAudioFileStream(filename);
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
        }

        /// <summary>
        /// 起動時に古い音声ファイルをすべて削除
        /// </summary>
        private void CleanupAudioFilesOnStartup()
        {
            try
            {
                var audioDirectory = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "audio");

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

                var audioDirectory = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "audio");
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

        public void Dispose()
        {
            Task.Run(async () => await StopAsync()).Wait(TimeSpan.FromSeconds(10));
        }
    }
}