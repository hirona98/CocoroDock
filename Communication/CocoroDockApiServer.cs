using CocoroDock.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
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
    /// CocoroDock用REST APIサーバー
    /// </summary>
    public class CocoroDockApiServer : IDisposable
    {
        private IHost? _host;
        private readonly int _port;
        private readonly IAppSettings _appSettings;
        private CancellationTokenSource? _cts;

        // イベント
        public event EventHandler<ChatRequest>? ChatMessageReceived;
        public event EventHandler<ControlRequest>? ControlCommandReceived;
        public event EventHandler<StatusUpdateRequest>? StatusUpdateReceived;
        public event EventHandler<LogMessage>? LogMessageReceived;

        public bool IsRunning => _host != null;

        public CocoroDockApiServer(int port, IAppSettings appSettings)
        {
            _port = port;
            _appSettings = appSettings;
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
                builder.WebHost.UseUrls($"http://127.0.0.1:{_port}");
                
                // ログレベルを設定してHTTPリクエストログを無効化
                builder.Logging.ClearProviders();
                builder.Logging.SetMinimumLevel(LogLevel.Warning);

                // Kestrelサーバーの設定
                builder.WebHost.ConfigureKestrel(serverOptions =>
                {
                    serverOptions.ListenLocalhost(_port);
                });

                // サービスの登録
                builder.Services.AddSingleton(_appSettings);
                builder.Services.AddSingleton(this);

                var app = builder.Build();

                // グローバル例外ハンドラー
                app.UseExceptionHandler(appError =>
                {
                    appError.Run(async context =>
                    {
                        context.Response.StatusCode = 500;
                        context.Response.ContentType = "application/json; charset=utf-8";

                        var contextFeature = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>();
                        if (contextFeature != null)
                        {
                            Debug.WriteLine($"APIサーバー例外: {contextFeature.Error}");

                            var errorResponse = new ErrorResponse
                            {
                                message = "Internal server error",
                                errorCode = "INTERNAL_ERROR"
                            };

                            await context.Response.WriteAsJsonAsync(errorResponse);
                        }
                    });
                });

                // エンドポイントの設定
                ConfigureEndpoints(app);

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

                Debug.WriteLine($"CocoroDock APIサーバーを起動しました: http://127.0.0.1:{_port}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"APIサーバー起動エラー: {ex.Message}");
                throw new InvalidOperationException($"APIサーバーの起動に失敗しました: {ex.Message}", ex);
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// エンドポイントを設定
        /// </summary>
        private void ConfigureEndpoints(WebApplication app)
        {
            // POST /api/addChatUi - チャットメッセージ受信
            app.MapPost("/api/addChatUi", async (HttpContext context) =>
            {
                try
                {
                    var request = await context.Request.ReadFromJsonAsync<ChatRequest>();
                    if (request == null)
                    {
                        context.Response.StatusCode = 400;
                        await context.Response.WriteAsJsonAsync(new ErrorResponse
                        {
                            message = "Request body is required",
                            errorCode = "INVALID_REQUEST"
                        });
                        return;
                    }

                    // 検証
                    if (string.IsNullOrWhiteSpace(request.content))
                    {
                        context.Response.StatusCode = 400;
                        await context.Response.WriteAsJsonAsync(new ErrorResponse
                        {
                            message = "Field 'content' is required and cannot be empty",
                            errorCode = "VALIDATION_ERROR"
                        });
                        return;
                    }

                    if (request.role != "user" && request.role != "assistant")
                    {
                        context.Response.StatusCode = 400;
                        await context.Response.WriteAsJsonAsync(new ErrorResponse
                        {
                            message = "Field 'role' must be 'user' or 'assistant'",
                            errorCode = "VALIDATION_ERROR"
                        });
                        return;
                    }

                    // イベント発火
                    ChatMessageReceived?.Invoke(this, request);

                    // 成功レスポンス
                    await context.Response.WriteAsJsonAsync(new StandardResponse
                    {
                        status = "success",
                        message = "Chat message received"
                    });
                }
                catch (System.Text.Json.JsonException)
                {
                    context.Response.StatusCode = 400;
                    await context.Response.WriteAsJsonAsync(new ErrorResponse
                    {
                        message = "Invalid JSON format",
                        errorCode = "JSON_ERROR"
                    });
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"チャット処理エラー: {ex.Message}");
                    context.Response.StatusCode = 500;
                    await context.Response.WriteAsJsonAsync(new ErrorResponse
                    {
                        message = "Internal server error",
                        errorCode = "INTERNAL_ERROR"
                    });
                }
            });

            // GET /api/config - 設定取得
            app.MapGet("/api/config", async (HttpContext context) =>
            {
                try
                {
                    var config = _appSettings.GetConfigSettings();
                    context.Response.ContentType = "application/json; charset=utf-8";
                    await context.Response.WriteAsJsonAsync(config);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"設定取得エラー: {ex.Message}");
                    context.Response.StatusCode = 500;
                    await context.Response.WriteAsJsonAsync(new ErrorResponse
                    {
                        message = "Failed to retrieve configuration",
                        errorCode = "CONFIG_ERROR"
                    });
                }
            });

            // PUT /api/config - 設定更新
            app.MapPut("/api/config", async (HttpContext context) =>
            {
                try
                {
                    var config = await context.Request.ReadFromJsonAsync<ConfigSettings>();
                    if (config == null)
                    {
                        context.Response.StatusCode = 400;
                        await context.Response.WriteAsJsonAsync(new ErrorResponse
                        {
                            message = "Request body is required",
                            errorCode = "INVALID_REQUEST"
                        });
                        return;
                    }

                    // 設定を更新
                    _appSettings.UpdateSettings(config);
                    _appSettings.SaveSettings();

                    await context.Response.WriteAsJsonAsync(new StandardResponse
                    {
                        status = "success",
                        message = "Configuration updated"
                    });
                }
                catch (System.Text.Json.JsonException)
                {
                    context.Response.StatusCode = 400;
                    await context.Response.WriteAsJsonAsync(new ErrorResponse
                    {
                        message = "Invalid JSON format",
                        errorCode = "JSON_ERROR"
                    });
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"設定更新エラー: {ex.Message}");
                    context.Response.StatusCode = 500;
                    await context.Response.WriteAsJsonAsync(new ErrorResponse
                    {
                        message = "Failed to update configuration",
                        errorCode = "CONFIG_ERROR"
                    });
                }
            });

            // PUT /api/config/patch - 設定部分更新
            app.MapPut("/api/config/patch", async (HttpContext context) =>
            {
                try
                {
                    var patch = await context.Request.ReadFromJsonAsync<ConfigPatchRequest>();
                    if (patch == null || patch.updates == null)
                    {
                        context.Response.StatusCode = 400;
                        await context.Response.WriteAsJsonAsync(new ErrorResponse
                        {
                            message = "Request body with updates is required",
                            errorCode = "INVALID_REQUEST"
                        });
                        return;
                    }

                    // 現在の設定を取得
                    var currentConfig = _appSettings.GetConfigSettings();
                    
                    // 部分更新を適用
                    ApplyConfigPatch(currentConfig, patch.updates);
                    
                    // 設定を更新・保存
                    _appSettings.UpdateSettings(currentConfig);
                    _appSettings.SaveSettings();

                    await context.Response.WriteAsJsonAsync(new StandardResponse
                    {
                        status = "success",
                        message = $"Configuration patch applied: {string.Join(", ", patch.changedFields ?? new string[0])}"
                    });
                }
                catch (System.Text.Json.JsonException)
                {
                    context.Response.StatusCode = 400;
                    await context.Response.WriteAsJsonAsync(new ErrorResponse
                    {
                        message = "Invalid JSON format",
                        errorCode = "JSON_ERROR"
                    });
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"設定部分更新エラー: {ex.Message}");
                    context.Response.StatusCode = 500;
                    await context.Response.WriteAsJsonAsync(new ErrorResponse
                    {
                        message = "Failed to apply configuration patch",
                        errorCode = "CONFIG_PATCH_ERROR"
                    });
                }
            });

            // POST /api/control - 制御コマンド
            app.MapPost("/api/control", async (HttpContext context) =>
            {
                try
                {
                    var request = await context.Request.ReadFromJsonAsync<ControlRequest>();
                    if (request == null)
                    {
                        context.Response.StatusCode = 400;
                        await context.Response.WriteAsJsonAsync(new ErrorResponse
                        {
                            message = "Request body is required",
                            errorCode = "INVALID_REQUEST"
                        });
                        return;
                    }

                    // コマンド検証
                    var validCommands = new[] { "shutdown", "restart", "reloadConfig" };
                    if (!Array.Exists(validCommands, cmd => cmd == request.command))
                    {
                        context.Response.StatusCode = 400;
                        await context.Response.WriteAsJsonAsync(new ErrorResponse
                        {
                            message = $"Invalid command. Must be one of: {string.Join(", ", validCommands)}",
                            errorCode = "INVALID_COMMAND"
                        });
                        return;
                    }

                    // イベント発火
                    ControlCommandReceived?.Invoke(this, request);

                    await context.Response.WriteAsJsonAsync(new StandardResponse
                    {
                        status = "success",
                        message = "Command executed"
                    });
                }
                catch (System.Text.Json.JsonException)
                {
                    context.Response.StatusCode = 400;
                    await context.Response.WriteAsJsonAsync(new ErrorResponse
                    {
                        message = "Invalid JSON format",
                        errorCode = "JSON_ERROR"
                    });
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"制御コマンド処理エラー: {ex.Message}");
                    context.Response.StatusCode = 500;
                    await context.Response.WriteAsJsonAsync(new ErrorResponse
                    {
                        message = "Internal server error",
                        errorCode = "INTERNAL_ERROR"
                    });
                }
            });

            // POST /api/status - ステータス更新
            app.MapPost("/api/status", async (HttpContext context) =>
            {
                try
                {
                    var request = await context.Request.ReadFromJsonAsync<StatusUpdateRequest>();
                    if (request == null)
                    {
                        context.Response.StatusCode = 400;
                        await context.Response.WriteAsJsonAsync(new ErrorResponse
                        {
                            message = "Request body is required",
                            errorCode = "INVALID_REQUEST"
                        });
                        return;
                    }

                    // ステータスメッセージの検証
                    if (string.IsNullOrWhiteSpace(request.message))
                    {
                        context.Response.StatusCode = 400;
                        await context.Response.WriteAsJsonAsync(new ErrorResponse
                        {
                            message = "Field 'message' is required and cannot be empty",
                            errorCode = "VALIDATION_ERROR"
                        });
                        return;
                    }

                    // イベント発火
                    StatusUpdateReceived?.Invoke(this, request);

                    await context.Response.WriteAsJsonAsync(new StandardResponse
                    {
                        status = "success",
                        message = "Status updated"
                    });
                }
                catch (System.Text.Json.JsonException)
                {
                    context.Response.StatusCode = 400;
                    await context.Response.WriteAsJsonAsync(new ErrorResponse
                    {
                        message = "Invalid JSON format",
                        errorCode = "JSON_ERROR"
                    });
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"ステータス更新処理エラー: {ex.Message}");
                    context.Response.StatusCode = 500;
                    await context.Response.WriteAsJsonAsync(new ErrorResponse
                    {
                        message = "Internal server error",
                        errorCode = "INTERNAL_ERROR"
                    });
                }
            });

            // POST /api/logs - ログメッセージ受信
            app.MapPost("/api/logs", async (HttpContext context) =>
            {
                try
                {
                    var request = await context.Request.ReadFromJsonAsync<LogMessage>();
                    if (request == null)
                    {
                        context.Response.StatusCode = 400;
                        await context.Response.WriteAsJsonAsync(new ErrorResponse
                        {
                            message = "Request body is required",
                            errorCode = "INVALID_REQUEST"
                        });
                        return;
                    }

                    // 検証
                    if (string.IsNullOrWhiteSpace(request.message))
                    {
                        context.Response.StatusCode = 400;
                        await context.Response.WriteAsJsonAsync(new ErrorResponse
                        {
                            message = "Field 'message' is required and cannot be empty",
                            errorCode = "VALIDATION_ERROR"
                        });
                        return;
                    }

                    // ログレベル検証
                    var validLevels = new[] { "DEBUG", "INFO", "WARNING", "ERROR" };
                    if (!Array.Exists(validLevels, level => level == request.level))
                    {
                        context.Response.StatusCode = 400;
                        await context.Response.WriteAsJsonAsync(new ErrorResponse
                        {
                            message = $"Invalid log level. Must be one of: {string.Join(", ", validLevels)}",
                            errorCode = "VALIDATION_ERROR"
                        });
                        return;
                    }

                    // コンポーネント検証
                    var validComponents = new[] { "CocoroCore", "CocoroMemory", "SEPARATOR" };
                    if (!Array.Exists(validComponents, comp => comp == request.component))
                    {
                        context.Response.StatusCode = 400;
                        await context.Response.WriteAsJsonAsync(new ErrorResponse
                        {
                            message = $"Invalid component. Must be one of: {string.Join(", ", validComponents)}",
                            errorCode = "VALIDATION_ERROR"
                        });
                        return;
                    }

                    // イベント発火
                    LogMessageReceived?.Invoke(this, request);

                    // 成功レスポンス
                    await context.Response.WriteAsJsonAsync(new StandardResponse
                    {
                        status = "success",
                        message = "Log message received"
                    });
                }
                catch (System.Text.Json.JsonException)
                {
                    context.Response.StatusCode = 400;
                    await context.Response.WriteAsJsonAsync(new ErrorResponse
                    {
                        message = "Invalid JSON format",
                        errorCode = "JSON_ERROR"
                    });
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"ログ受信処理エラー: {ex.Message}");
                    context.Response.StatusCode = 500;
                    await context.Response.WriteAsJsonAsync(new ErrorResponse
                    {
                        message = "Internal server error",
                        errorCode = "INTERNAL_ERROR"
                    });
                }
            });
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

                Debug.WriteLine("CocoroDock APIサーバーを停止しました");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"APIサーバー停止エラー: {ex.Message}");

                // エラーが発生してもリソースをクリーンアップ
                try { _host?.Dispose(); } catch { }
                _host = null;
                try { _cts?.Dispose(); } catch { }
                _cts = null;
            }
        }

        /// <summary>
        /// 設定に部分更新を適用
        /// </summary>
        private static void ApplyConfigPatch(ConfigSettings config, System.Collections.Generic.Dictionary<string, object> updates)
        {
            var configType = typeof(ConfigSettings);
            
            foreach (var kvp in updates)
            {
                var propertyInfo = configType.GetProperty(kvp.Key);
                if (propertyInfo != null && propertyInfo.CanWrite)
                {
                    try
                    {
                        object convertedValue;
                        
                        // 型に応じて変換処理
                        if (propertyInfo.PropertyType == typeof(float))
                        {
                            convertedValue = Convert.ToSingle(kvp.Value);
                        }
                        else if (propertyInfo.PropertyType == typeof(bool))
                        {
                            convertedValue = Convert.ToBoolean(kvp.Value);
                        }
                        else if (propertyInfo.PropertyType == typeof(int))
                        {
                            convertedValue = Convert.ToInt32(kvp.Value);
                        }
                        else if (propertyInfo.PropertyType.IsGenericType && 
                                propertyInfo.PropertyType.GetGenericTypeDefinition() == typeof(System.Collections.Generic.List<>))
                        {
                            // List型の場合はJSONから直接デシリアライズ
                            var json = System.Text.Json.JsonSerializer.Serialize(kvp.Value);
                            convertedValue = System.Text.Json.JsonSerializer.Deserialize(json, propertyInfo.PropertyType);
                        }
                        else
                        {
                            convertedValue = Convert.ChangeType(kvp.Value, propertyInfo.PropertyType);
                        }
                        
                        propertyInfo.SetValue(config, convertedValue);
                        Debug.WriteLine($"Applied config patch: {kvp.Key} = {convertedValue}");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Failed to apply patch for {kvp.Key}: {ex.Message}");
                    }
                }
                else
                {
                    Debug.WriteLine($"Property {kvp.Key} not found or not writable in ConfigSettings");
                }
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