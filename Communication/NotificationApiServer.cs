using CocoroDock.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace CocoroDock.Communication
{
    /// <summary>
    /// 通知API用のRESTサーバー
    /// </summary>
    public class NotificationApiServer : IDisposable
    {
        private IHost? _host;
        private readonly int _port;
        private readonly ICommunicationService _communicationService;
        private CancellationTokenSource? _cts;

        public bool IsRunning => _host != null;

        public NotificationApiServer(int port, ICommunicationService communicationService)
        {
            _port = port;
            _communicationService = communicationService;
        }

        /// <summary>
        /// APIサーバーを開始
        /// </summary>
        public Task StartAsync()
        {
            if (_host != null) return Task.CompletedTask;

            try
            {
                _cts = new CancellationTokenSource();

                var builder = WebApplication.CreateBuilder();
                builder.WebHost.UseUrls($"http://*:{_port}");

                // Kestrelサーバーの設定
                builder.WebHost.ConfigureKestrel(serverOptions =>
                {
                    serverOptions.ListenAnyIP(_port);
                });

                // サービスの登録
                builder.Services.AddSingleton(_communicationService);

                var app = builder.Build();

                // グローバル例外ハンドラー
                app.UseExceptionHandler(appError =>
                {
                    appError.Run(async context =>
                    {
                        context.Response.StatusCode = 500;
                        context.Response.ContentType = "application/json";

                        var contextFeature = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>();
                        if (contextFeature != null)
                        {
                            Debug.WriteLine($"APIサーバー例外: {contextFeature.Error}");
                            await context.Response.WriteAsJsonAsync(new { error = "Internal server error" });
                        }
                    });
                });

                // エンドポイントの設定
                app.MapPost("/api/v1/notification", async (HttpContext context) =>
                {
                    try
                    {
                        // リクエストボディの読み取り
                        NotificationRequest? request = null;
                        try
                        {
                            request = await context.Request.ReadFromJsonAsync<NotificationRequest>();
                        }
                        catch (System.Text.Json.JsonException jsonEx)
                        {
                            Debug.WriteLine($"JSONパースエラー: {jsonEx.Message}");
                            context.Response.StatusCode = 400;
                            await context.Response.WriteAsJsonAsync(new { error = "Invalid JSON format" });
                            return;
                        }

                        // リクエストの検証
                        if (request == null)
                        {
                            context.Response.StatusCode = 400;
                            await context.Response.WriteAsJsonAsync(new { error = "Request body is required" });
                            return;
                        }

                        const int maxMessageLength = 5000;
                        if (request.message.Length + request.from.Length > maxMessageLength)
                        {
                            context.Response.StatusCode = 400;
                            await context.Response.WriteAsJsonAsync(new { error = $"Field 'message' exceeds maximum length of {maxMessageLength} characters" });
                            return;
                        }

                        if (string.IsNullOrWhiteSpace(request.from))
                        {
                            context.Response.StatusCode = 400;
                            await context.Response.WriteAsJsonAsync(new { error = "Field 'from' is required and cannot be empty" });
                            return;
                        }

                        if (string.IsNullOrWhiteSpace(request.message))
                        {
                            context.Response.StatusCode = 400;
                            await context.Response.WriteAsJsonAsync(new { error = "Field 'message' is required and cannot be empty" });
                            return;
                        }

                        // 通知をChatメッセージとして転送
                        var chatPayload = new ChatMessagePayload
                        {
                            userId = request.from.Trim(),
                            sessionId = $"notification_{DateTime.Now:yyyyMMddHHmmss}",
                            message = request.message.Trim()
                        };

                        // 通知メッセージを処理
                        try
                        {
                            // チャットウィンドウに表示
                            if (_communicationService is CommunicationService communicationService)
                            {
                                communicationService.RaiseNotificationMessageReceived(chatPayload);
                            }

                            // 即座にレスポンスを返す
                            context.Response.StatusCode = 204;
                            await context.Response.CompleteAsync();

                            // 通知を処理（CocoroCoreへの転送）をバックグラウンドで実行
                            _ = Task.Run(async () =>
                            {
                                try
                                {
                                    await _communicationService.ProcessNotificationAsync(chatPayload);
                                }
                                catch (Exception ex)
                                {
                                    Debug.WriteLine($"バックグラウンド通知処理エラー: {ex.Message}");
                                }
                            });
                        }
                        catch (InvalidOperationException ioEx)
                        {
                            Debug.WriteLine($"通知送信エラー: {ioEx.Message}");
                            context.Response.StatusCode = 503;
                            await context.Response.WriteAsJsonAsync(new { error = "Service temporarily unavailable" });
                            return;
                        }
                    }
                    catch (TaskCanceledException)
                    {
                        // タスクがキャンセルされた場合は何もしない
                        Debug.WriteLine("通知処理がキャンセルされました");
                        context.Response.StatusCode = 499; // Client Closed Request
                        await context.Response.CompleteAsync();
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"通知処理エラー: {ex.Message}");
                        Debug.WriteLine($"スタックトレース: {ex.StackTrace}");
                        context.Response.StatusCode = 500;
                        await context.Response.WriteAsJsonAsync(new { error = "Internal server error" });
                    }
                });

                _host = app;

                // バックグラウンドでサーバーを起動
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _host.RunAsync(_cts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        // 正常な終了
                    }
                    catch (System.Net.Sockets.SocketException sockEx) when (sockEx.Message.Contains("スレッドの終了") || sockEx.Message.Contains("thread exit"))
                    {
                        // サーバー停止時の正常なソケット終了
                        Debug.WriteLine("APIサーバーが正常に停止されました");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"APIサーバー実行エラー: {ex.Message}");
                    }
                });

                Debug.WriteLine($"通知APIサーバーを起動しました: http://127.0.0.1:{_port}");
            }
            catch (System.Net.Sockets.SocketException sockEx)
            {
                Debug.WriteLine($"ソケットエラー: {sockEx.Message}");
                Debug.WriteLine($"エラーコード: {sockEx.ErrorCode}");

                if (sockEx.ErrorCode == 10048) // WSAEADDRINUSE
                {
                    throw new InvalidOperationException($"ポート {_port} は既に使用されています。別のポートを指定するか、使用中のアプリケーションを終了してください。", sockEx);
                }
                else if (sockEx.ErrorCode == 10013) // WSAEACCES
                {
                    throw new InvalidOperationException($"ポート {_port} へのアクセスが拒否されました。管理者権限が必要な可能性があります。", sockEx);
                }
                else
                {
                    throw new InvalidOperationException($"ネットワークエラーが発生しました: {sockEx.Message}", sockEx);
                }
            }
            catch (System.IO.IOException ioEx)
            {
                Debug.WriteLine($"I/Oエラー: {ioEx.Message}");
                throw new InvalidOperationException("APIサーバーの起動中にI/Oエラーが発生しました。", ioEx);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"APIサーバー起動エラー: {ex.Message}");
                Debug.WriteLine($"エラータイプ: {ex.GetType().FullName}");
                throw new InvalidOperationException($"APIサーバーの起動に失敗しました: {ex.Message}", ex);
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// APIサーバーを停止
        /// </summary>
        public async Task StopAsync()
        {
            if (_host == null) return;

            try
            {
                _cts?.Cancel();

                var stopTask = _host.StopAsync(TimeSpan.FromSeconds(5));
                await stopTask.ConfigureAwait(false);

                _host.Dispose();
                _host = null;
                _cts?.Dispose();
                _cts = null;

                Debug.WriteLine("通知APIサーバーを停止しました");
            }
            catch (TaskCanceledException)
            {
                Debug.WriteLine("APIサーバーの停止がタイムアウトしました");
            }
            catch (ObjectDisposedException)
            {
                Debug.WriteLine("APIサーバーは既に破棄されています");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"APIサーバー停止エラー: {ex.Message}");
                Debug.WriteLine($"エラータイプ: {ex.GetType().FullName}");

                // エラーが発生してもリソースをクリーンアップ
                try
                {
                    _host?.Dispose();
                }
                catch { }

                _host = null;

                try
                {
                    _cts?.Dispose();
                }
                catch { }

                _cts = null;
            }
        }

        public void Dispose()
        {
            _cts?.Cancel();
            _host?.Dispose();
            _cts?.Dispose();
        }
    }

    /// <summary>
    /// 通知リクエストモデル
    /// </summary>
    public class NotificationRequest
    {
        public string from { get; set; } = string.Empty;
        public string message { get; set; } = string.Empty;
    }
}