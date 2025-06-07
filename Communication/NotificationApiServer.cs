using CocoroDock.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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
        public async Task StartAsync()
        {
            if (_host != null) return;

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

                // エンドポイントの設定
                app.MapPost("/api/v1/notification", async (HttpContext context, [FromBody] NotificationRequest request) =>
                {
                    try
                    {
                        // リクエストの検証
                        if (string.IsNullOrEmpty(request?.from) || string.IsNullOrEmpty(request?.message))
                        {
                            context.Response.StatusCode = 400;
                            await context.Response.WriteAsJsonAsync(new { error = "Invalid request format" });
                            return;
                        }

                        // 通知をChatメッセージとして転送
                        var chatPayload = new ChatMessagePayload
                        {
                            userId = request.from,
                            sessionId = $"notification_{DateTime.Now:yyyyMMddHHmmss}",
                            message = request.message
                        };

                        // WebSocket経由でメッセージ送信
                        await _communicationService.SendMessageAsync(MessageType.notification, chatPayload);

                        // 204 No Content を返す
                        context.Response.StatusCode = 204;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"通知処理エラー: {ex.Message}");
                        context.Response.StatusCode = 500;
                        await context.Response.WriteAsJsonAsync(new { error = "Internal server error" });
                    }
                });

                // ヘルスチェックエンドポイント（オプション）
                app.MapGet("/api/v1/health", () => Results.Ok(new { status = "healthy" }));

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
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"APIサーバー実行エラー: {ex.Message}");
                    }
                });

                Debug.WriteLine($"通知APIサーバーを起動しました: http://localhost:{_port}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"APIサーバー起動エラー: {ex.Message}");
                throw;
            }
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
                await _host.StopAsync(TimeSpan.FromSeconds(5));
                _host.Dispose();
                _host = null;
                _cts?.Dispose();
                _cts = null;

                Debug.WriteLine("通知APIサーバーを停止しました");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"APIサーバー停止エラー: {ex.Message}");
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