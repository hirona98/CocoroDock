using CocoroDock.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System.Windows.Media.Imaging;

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

                        const int maxMessageLength = 10 * 1024 * 1024; // 10MB
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

                        // 複数画像データの検証
                        List<BitmapSource> imageSources = new List<BitmapSource>();
                        if (request.images != null && request.images.Length > 0)
                        {
                            // 最大枚数制限
                            const int maxImageCount = 5;
                            if (request.images.Length > maxImageCount)
                            {
                                context.Response.StatusCode = 400;
                                await context.Response.WriteAsJsonAsync(new { error = $"Too many images. Maximum {maxImageCount} images allowed" });
                                return;
                            }

                            // 各画像を検証・デコード
                            for (int i = 0; i < request.images.Length; i++)
                            {
                                if (string.IsNullOrWhiteSpace(request.images[i]))
                                {
                                    context.Response.StatusCode = 400;
                                    await context.Response.WriteAsJsonAsync(new { error = $"Image {i + 1} is empty" });
                                    return;
                                }

                                try
                                {
                                    var imageSource = ValidateAndDecodeImage(request.images[i]);
                                    imageSources.Add(imageSource);
                                }
                                catch (ArgumentException ex)
                                {
                                    context.Response.StatusCode = 400;
                                    await context.Response.WriteAsJsonAsync(new { error = $"Image {i + 1}: {ex.Message}" });
                                    return;
                                }
                                catch (Exception ex)
                                {
                                    Debug.WriteLine($"画像{i + 1}デコードエラー: {ex.Message}");
                                    context.Response.StatusCode = 400;
                                    await context.Response.WriteAsJsonAsync(new { error = $"Invalid image data in image {i + 1}" });
                                    return;
                                }
                            }

                            // 全体サイズ制限チェック
                            long totalSize = 0;
                            foreach (var imageData in request.images)
                            {
                                var base64Data = imageData.Split(',').Last();
                                totalSize += base64Data.Length;
                            }
                            const long maxTotalSize = 15 * 1024 * 1024; // 15MB (Base64)
                            if (totalSize > maxTotalSize)
                            {
                                context.Response.StatusCode = 400;
                                await context.Response.WriteAsJsonAsync(new { error = $"Total image size exceeds maximum limit of {maxTotalSize / (1024 * 1024)}MB" });
                                return;
                            }
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
                                communicationService.RaiseNotificationMessageReceived(chatPayload, imageSources);
                            }

                            // 即座にレスポンスを返す
                            context.Response.StatusCode = 204;
                            await context.Response.CompleteAsync();

                            // 通知を処理（CocoroCoreへの転送）をバックグラウンドで実行
                            _ = Task.Run(async () =>
                            {
                                try
                                {
                                    await _communicationService.ProcessNotificationAsync(chatPayload, request.images);
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

        /// <summary>
        /// 画像データの検証とデコード
        /// </summary>
        /// <param name="imageData">Base64エンコードされた画像データ（data URL形式）</param>
        /// <returns>デコードされた画像</returns>
        private BitmapSource ValidateAndDecodeImage(string imageData)
        {
            // data URL形式の検証
            var dataUrlPattern = @"^data:image\/(png|jpeg|jpg|gif|webp);base64,([A-Za-z0-9+\/]+(=|==)?)";  
            var match = Regex.Match(imageData, dataUrlPattern);
            
            if (!match.Success)
            {
                throw new ArgumentException("Invalid image format. Expected data URL format (data:image/type;base64,data)");
            }
            
            var mimeType = match.Groups[1].Value;
            var base64Data = match.Groups[2].Value;
            
            // サイズ制限（5MB）
            const int maxSizeBytes = 5 * 1024 * 1024;
            if (base64Data.Length > maxSizeBytes * 4 / 3) // Base64は約1.33倍になる
            {
                throw new ArgumentException($"Image size exceeds maximum limit of {maxSizeBytes / (1024 * 1024)}MB");
            }
            
            // Base64デコード
            byte[] imageBytes;
            try
            {
                imageBytes = Convert.FromBase64String(base64Data);
            }
            catch (FormatException)
            {
                throw new ArgumentException("Invalid Base64 image data");
            }
            
            // 画像として読み込み
            try
            {
                using (var stream = new MemoryStream(imageBytes))
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.StreamSource = stream;
                    bitmap.EndInit();
                    bitmap.Freeze();
                    return bitmap;
                }
            }
            catch (Exception ex)
            {
                throw new ArgumentException($"Failed to decode image: {ex.Message}");
            }
        }

        public void Dispose()
        {
            _cts?.Cancel();
            _host?.Dispose();
            _cts?.Dispose();
        }
    }
}