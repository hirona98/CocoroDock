using System;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace CocoroDock.Communication
{
    /// <summary>
    /// CocoroShell REST APIクライアント
    /// </summary>
    public class CocoroShellClient : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;
        private bool _disposed;

        /// <summary>
        /// コンストラクタ
        /// </summary>
        /// <param name="baseUrl">ベースURL（例: http://127.0.0.1:55605）</param>
        public CocoroShellClient(string baseUrl)
        {
            _baseUrl = baseUrl;
            _httpClient = new HttpClient
            {
                BaseAddress = new Uri(baseUrl),
                Timeout = TimeSpan.FromSeconds(30)
            };
            _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
        }

        /// <summary>
        /// コンストラクタ（ポート番号指定）
        /// </summary>
        /// <param name="port">ポート番号</param>
        public CocoroShellClient(int port) : this($"http://127.0.0.1:{port}")
        {
        }

        /// <summary>
        /// チャットメッセージを送信（音声合成付き）
        /// </summary>
        public async Task<StandardResponse> SendChatMessageAsync(ShellChatRequest request)
        {
            try
            {
                var response = await _httpClient.PostAsJsonAsync("/api/chat", request);

                if (response.IsSuccessStatusCode)
                {
                    var result = await response.Content.ReadFromJsonAsync<StandardResponse>();
                    return result ?? new StandardResponse
                    {
                        status = "success",
                        message = "Chat message sent"
                    };
                }
                else
                {
                    var errorResponse = await TryReadErrorResponse(response);
                    throw new HttpRequestException($"API error: {errorResponse?.message ?? response.ReasonPhrase}");
                }
            }
            catch (TaskCanceledException)
            {
                throw new TimeoutException("Request to CocoroShell timed out");
            }
            catch (HttpRequestException)
            {
                throw; // そのまま再スロー
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"チャットメッセージ送信エラー: {ex.Message}");
                throw new InvalidOperationException($"Failed to send chat message: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// アニメーションコマンドを送信
        /// </summary>
        public async Task<StandardResponse> SendAnimationCommandAsync(AnimationRequest request)
        {
            try
            {
                var response = await _httpClient.PostAsJsonAsync("/api/animation", request);

                if (response.IsSuccessStatusCode)
                {
                    var result = await response.Content.ReadFromJsonAsync<StandardResponse>();
                    return result ?? new StandardResponse
                    {
                        status = "success",
                        message = "Animation command sent"
                    };
                }
                else
                {
                    var errorResponse = await TryReadErrorResponse(response);
                    throw new HttpRequestException($"API error: {errorResponse?.message ?? response.ReasonPhrase}");
                }
            }
            catch (TaskCanceledException)
            {
                throw new TimeoutException("Request to CocoroShell timed out");
            }
            catch (HttpRequestException)
            {
                throw; // そのまま再スロー
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"アニメーションコマンド送信エラー: {ex.Message}");
                throw new InvalidOperationException($"Failed to send animation command: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 制御コマンドを送信
        /// </summary>
        public async Task<StandardResponse> SendControlCommandAsync(ShellControlRequest request)
        {
            try
            {
                var response = await _httpClient.PostAsJsonAsync("/api/control", request);

                if (response.IsSuccessStatusCode)
                {
                    var result = await response.Content.ReadFromJsonAsync<StandardResponse>();
                    return result ?? new StandardResponse
                    {
                        status = "success",
                        message = "Control command sent"
                    };
                }
                else
                {
                    var errorResponse = await TryReadErrorResponse(response);
                    throw new HttpRequestException($"API error: {errorResponse?.message ?? response.ReasonPhrase}");
                }
            }
            catch (TaskCanceledException)
            {
                throw new TimeoutException("Request to CocoroShell timed out");
            }
            catch (HttpRequestException)
            {
                throw; // そのまま再スロー
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"制御コマンド送信エラー: {ex.Message}");
                throw new InvalidOperationException($"Failed to send control command: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// ヘルスチェック（接続確認）
        /// </summary>
        public async Task<bool> CheckHealthAsync()
        {
            try
            {
                // CocoroShellにヘルスチェックエンドポイントがない場合は、
                // 軽量なコマンドで代用する
                var request = new ShellControlRequest
                {
                    command = "ping",
                    @params = new System.Collections.Generic.Dictionary<string, object>()
                };

                var response = await _httpClient.PostAsJsonAsync("/api/control", request);
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 現在のキャラクター位置を取得
        /// </summary>
        public async Task<PositionResponse> GetPositionAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync("/api/position");

                if (response.IsSuccessStatusCode)
                {
                    var result = await response.Content.ReadFromJsonAsync<PositionResponse>();
                    return result ?? throw new InvalidOperationException("Failed to deserialize position response");
                }
                else
                {
                    var errorResponse = await TryReadErrorResponse(response);
                    throw new HttpRequestException($"API error: {errorResponse?.message ?? response.ReasonPhrase}");
                }
            }
            catch (TaskCanceledException)
            {
                throw new TimeoutException("Request to CocoroShell timed out");
            }
            catch (HttpRequestException)
            {
                throw; // そのまま再スロー
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"位置取得エラー: {ex.Message}");
                throw new InvalidOperationException($"Failed to get position: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 設定の部分更新を送信
        /// </summary>
        public async Task<StandardResponse> UpdateConfigPatchAsync(ConfigPatchRequest request)
        {
            try
            {
                var response = await _httpClient.PostAsJsonAsync("/api/config/patch", request);

                if (response.IsSuccessStatusCode)
                {
                    var result = await response.Content.ReadFromJsonAsync<StandardResponse>();
                    return result ?? new StandardResponse
                    {
                        status = "success",
                        message = "Config patch applied"
                    };
                }
                else
                {
                    var errorResponse = await TryReadErrorResponse(response);
                    throw new HttpRequestException($"API error: {errorResponse?.message ?? response.ReasonPhrase}");
                }
            }
            catch (TaskCanceledException)
            {
                throw new TimeoutException("Request to CocoroShell timed out");
            }
            catch (HttpRequestException)
            {
                throw; // そのまま再スロー
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"設定部分更新エラー: {ex.Message}");
                throw new InvalidOperationException($"Failed to update config patch: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// エラーレスポンスの読み取りを試みる
        /// </summary>
        private async Task<ErrorResponse?> TryReadErrorResponse(HttpResponseMessage response)
        {
            try
            {
                var content = await response.Content.ReadAsStringAsync();
                if (!string.IsNullOrWhiteSpace(content))
                {
                    return JsonSerializer.Deserialize<ErrorResponse>(content, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"エラーレスポンス読み取りエラー: {ex.Message}");
            }
            return null;
        }

        /// <summary>
        /// リソースの解放
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// リソースの解放（内部実装）
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _httpClient?.Dispose();
                }
                _disposed = true;
            }
        }
    }
}