using CocoroDock.Utilities;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace CocoroDock.Communication
{
    /// <summary>
    /// CocoroCoreとの通信を行うクライアントクラス
    /// </summary>
    public class CocoroCoreClient : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;


        public CocoroCoreClient(int port)
        {
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(120) // REST API用のタイムアウト
            };
            _baseUrl = $"http://127.0.0.1:{port}";
        }





        /// <summary>
        /// CocoroCoreに制御コマンドを送信
        /// </summary>
        /// <param name="request">制御コマンドリクエスト</param>
        public async Task<StandardResponse> SendControlCommandAsync(CoreControlRequest request)
        {
            try
            {
                var json = MessageHelper.SerializeToJson(request);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                using var response = await _httpClient.PostAsync($"{_baseUrl}/api/control", content);

                var responseBody = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    var error = MessageHelper.DeserializeFromJson<ErrorResponse>(responseBody);
                    throw new HttpRequestException($"CocoroCoreエラー: {error?.message ?? responseBody}");
                }

                return MessageHelper.DeserializeFromJson<StandardResponse>(responseBody)
                       ?? new StandardResponse { status = "success", message = "Command sent successfully" };
            }
            catch (TaskCanceledException)
            {
                throw new TimeoutException("CocoroCoreへのリクエストがタイムアウトしました");
            }
            catch (HttpRequestException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"CocoroCoreへの制御コマンド送信エラー: {ex.Message}");
                throw new InvalidOperationException($"CocoroCoreとの通信に失敗しました: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// CocoroCoreのヘルスチェックを実行
        /// </summary>
        public async Task<HealthCheckResponse> GetHealthAsync()
        {
            try
            {
                using var response = await _httpClient.GetAsync($"{_baseUrl}/api/health");

                var responseBody = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    var error = MessageHelper.DeserializeFromJson<ErrorResponse>(responseBody);
                    throw new HttpRequestException($"CocoroCoreエラー: {error?.message ?? responseBody}");
                }

                return MessageHelper.DeserializeFromJson<HealthCheckResponse>(responseBody)
                       ?? throw new InvalidOperationException("ヘルスチェックレスポンスのパースに失敗しました");
            }
            catch (TaskCanceledException)
            {
                throw new TimeoutException("CocoroCoreへのリクエストがタイムアウトしました");
            }
            catch (HttpRequestException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"CocoroCoreのヘルスチェックエラー: {ex.Message}");
                throw new InvalidOperationException($"CocoroCoreとの通信に失敗しました: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// MCPツール登録ログを取得
        /// </summary>
        public async Task<McpToolRegistrationResponse> GetMcpToolRegistrationLogAsync()
        {
            try
            {
                using var response = await _httpClient.GetAsync($"{_baseUrl}/api/mcp/tool-registration-log");

                var responseBody = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    var error = MessageHelper.DeserializeFromJson<ErrorResponse>(responseBody);
                    throw new HttpRequestException($"CocoroCoreエラー: {error?.message ?? responseBody}");
                }

                return MessageHelper.DeserializeFromJson<McpToolRegistrationResponse>(responseBody)
                       ?? new McpToolRegistrationResponse { status = "success", message = "ログ取得完了", logs = new List<string>() };
            }
            catch (TaskCanceledException)
            {
                throw new TimeoutException("CocoroCoreへのリクエストがタイムアウトしました");
            }
            catch (HttpRequestException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MCPツール登録ログ取得エラー: {ex.Message}");
                throw new InvalidOperationException($"CocoroCoreとの通信に失敗しました: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// ユーザー一覧を取得
        /// </summary>
        public async Task<MemoryListResponse> GetMemoryListAsync()
        {
            try
            {
                var requestUrl = $"{_baseUrl}/api/memory/characters";
                Debug.WriteLine($"[API Request] GET {requestUrl}");

                using var response = await _httpClient.GetAsync(requestUrl);

                var responseBody = await response.Content.ReadAsStringAsync();
                Debug.WriteLine($"[API Response] Status: {(int)response.StatusCode} {response.StatusCode}");
                Debug.WriteLine($"[API Response] Body: {responseBody}");

                if (!response.IsSuccessStatusCode)
                {
                    var error = MessageHelper.DeserializeFromJson<ErrorResponse>(responseBody);
                    throw new HttpRequestException($"ユーザー一覧取得エラー: {error?.message ?? responseBody}");
                }

                var result = MessageHelper.DeserializeFromJson<MemoryListResponse>(responseBody)
                       ?? throw new InvalidOperationException("ユーザー一覧の解析に失敗しました");

                Debug.WriteLine($"[API Parsed] Users count: {result.data?.Count ?? 0}");
                if (result.data != null)
                {
                    foreach (var user in result.data)
                    {
                        Debug.WriteLine($"[API User] ID: {user.memory_id}, Name: {user.memory_name}, Role: {user.role}");
                    }
                }

                return result;
            }
            catch (TaskCanceledException)
            {
                Debug.WriteLine("[API Error] ユーザー一覧取得がタイムアウトしました");
                throw new TimeoutException("ユーザー一覧取得がタイムアウトしました");
            }
        }


        /// <summary>
        /// ユーザーの全記憶を削除
        /// </summary>
        public async Task<StandardResponse> DeleteUserMemoriesAsync(string memoryId)
        {
            try
            {
                var requestUrl = $"{_baseUrl}/api/memory/character/{Uri.EscapeDataString(memoryId)}/all";
                Debug.WriteLine($"[API Request] DELETE {requestUrl}");
                Debug.WriteLine($"[API Param] MemoryId: {memoryId}");

                using var response = await _httpClient.DeleteAsync(requestUrl);

                var responseBody = await response.Content.ReadAsStringAsync();
                Debug.WriteLine($"[API Response] Status: {(int)response.StatusCode} {response.StatusCode}");
                Debug.WriteLine($"[API Response] Body: {responseBody}");

                if (!response.IsSuccessStatusCode)
                {
                    var error = MessageHelper.DeserializeFromJson<ErrorResponse>(responseBody);

                    if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                    {
                        Debug.WriteLine("[API Error] ユーザーが見つかりません");
                        throw new InvalidOperationException($"ユーザーが見つかりません: {memoryId}");
                    }

                    Debug.WriteLine($"[API Error] 記憶削除に失敗: {error?.message ?? responseBody}");
                    throw new HttpRequestException($"記憶削除エラー: {error?.message ?? responseBody}");
                }

                var result = MessageHelper.DeserializeFromJson<StandardResponse>(responseBody)
                       ?? throw new InvalidOperationException("削除結果の解析に失敗しました");

                Debug.WriteLine($"[API Parsed] DeleteResult - Status: {result.status}, Message: {result.message}");

                return result;
            }
            catch (TaskCanceledException)
            {
                Debug.WriteLine("[API Error] 記憶削除リクエストがタイムアウトしました");
                throw new TimeoutException("記憶削除リクエストがタイムアウトしました");
            }
        }


        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }
}