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
        /// CocoroCoreのヘルスチェックを実行（MCP状態を含む）
        /// </summary>
        public async Task<HealthCheckResponse> GetHealthAsync()
        {
            try
            {
                using var response = await _httpClient.GetAsync($"{_baseUrl}/health");

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
        public async Task<UsersListResponse> GetUsersListAsync()
        {
            try
            {
                var requestUrl = $"{_baseUrl}/api/users";
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

                var result = MessageHelper.DeserializeFromJson<UsersListResponse>(responseBody)
                       ?? throw new InvalidOperationException("ユーザー一覧の解析に失敗しました");

                Debug.WriteLine($"[API Parsed] Users count: {result.data?.Count ?? 0}");
                if (result.data != null)
                {
                    foreach (var user in result.data)
                    {
                        Debug.WriteLine($"[API User] ID: {user.user_id}, Name: {user.user_name}, Role: {user.role}");
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
        /// ユーザー統計情報を取得
        /// </summary>
        public async Task<UserStatistics> GetUserStatisticsAsync(string userId)
        {
            try
            {
                var requestUrl = $"{_baseUrl}/api/users/{Uri.EscapeDataString(userId)}/statistics";
                Debug.WriteLine($"[API Request] GET {requestUrl}");
                Debug.WriteLine($"[API Param] UserId: {userId}");

                using var response = await _httpClient.GetAsync(requestUrl);

                var responseBody = await response.Content.ReadAsStringAsync();
                Debug.WriteLine($"[API Response] Status: {(int)response.StatusCode} {response.StatusCode}");
                Debug.WriteLine($"[API Response] Body: {responseBody}");

                if (!response.IsSuccessStatusCode)
                {
                    var error = MessageHelper.DeserializeFromJson<ErrorResponse>(responseBody);
                    throw new HttpRequestException($"ユーザー統計情報取得エラー: {error?.message ?? responseBody}");
                }

                var result = MessageHelper.DeserializeFromJson<UserStatistics>(responseBody)
                       ?? throw new InvalidOperationException("ユーザー統計情報の解析に失敗しました");

                Debug.WriteLine($"[API Parsed] UserStats - Total: {result.total_memories}, Text: {result.textual_memories}, Act: {result.activation_memories}, Para: {result.parametric_memories}");

                return result;
            }
            catch (TaskCanceledException)
            {
                Debug.WriteLine("[API Error] ユーザー統計情報取得がタイムアウトしました");
                throw new TimeoutException("ユーザー統計情報取得がタイムアウトしました");
            }
        }

        /// <summary>
        /// ユーザーの記憶統計情報を取得
        /// </summary>
        public async Task<MemoryStatsResponse> GetUserMemoryStatsAsync(string userId)
        {
            try
            {
                var requestUrl = $"{_baseUrl}/api/memory/user/{Uri.EscapeDataString(userId)}/stats";
                Debug.WriteLine($"[API Request] GET {requestUrl}");
                Debug.WriteLine($"[API Param] UserId: {userId}");

                using var response = await _httpClient.GetAsync(requestUrl);

                var responseBody = await response.Content.ReadAsStringAsync();
                Debug.WriteLine($"[API Response] Status: {(int)response.StatusCode} {response.StatusCode}");
                Debug.WriteLine($"[API Response] Body: {responseBody}");

                if (!response.IsSuccessStatusCode)
                {
                    if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                    {
                        Debug.WriteLine("[API Fallback] User not found, returning empty stats");
                        // ユーザーが存在しない場合は空の統計を返す
                        return new MemoryStatsResponse
                        {
                            user_id = userId,
                            total_memories = 0,
                            text_memories = 0,
                            activation_memories = 0,
                            parametric_memories = 0
                        };
                    }

                    var error = MessageHelper.DeserializeFromJson<ErrorResponse>(responseBody);
                    throw new HttpRequestException($"統計情報取得エラー: {error?.message ?? responseBody}");
                }

                var result = MessageHelper.DeserializeFromJson<MemoryStatsResponse>(responseBody)
                       ?? throw new InvalidOperationException("統計情報の解析に失敗しました");

                Debug.WriteLine($"[API Parsed] MemoryStats - Total: {result.total_memories}, Text: {result.text_memories}, Act: {result.activation_memories}, Para: {result.parametric_memories}");
                Debug.WriteLine($"[API Parsed] LastUpdated: {result.last_updated}, CubeId: {result.cube_id}");

                return result;
            }
            catch (TaskCanceledException)
            {
                Debug.WriteLine("[API Error] 統計情報取得がタイムアウトしました");
                throw new TimeoutException("統計情報取得がタイムアウトしました");
            }
        }

        /// <summary>
        /// ユーザーの全記憶を削除
        /// </summary>
        public async Task<MemoryDeleteResponse> DeleteUserMemoriesAsync(string userId)
        {
            try
            {
                var requestUrl = $"{_baseUrl}/api/memory/user/{Uri.EscapeDataString(userId)}/all";
                Debug.WriteLine($"[API Request] DELETE {requestUrl}");
                Debug.WriteLine($"[API Param] UserId: {userId}");

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
                        throw new InvalidOperationException($"ユーザーが見つかりません: {userId}");
                    }

                    Debug.WriteLine($"[API Error] 記憶削除に失敗: {error?.message ?? responseBody}");
                    throw new HttpRequestException($"記憶削除エラー: {error?.message ?? responseBody}");
                }

                var result = MessageHelper.DeserializeFromJson<MemoryDeleteResponse>(responseBody)
                       ?? throw new InvalidOperationException("削除結果の解析に失敗しました");

                Debug.WriteLine($"[API Parsed] DeleteResult - Status: {result.status}, DeletedCount: {result.deleted_count}");
                Debug.WriteLine($"[API Parsed] Details - Text: {result.details.text_memories}, Act: {result.details.activation_memories}, Para: {result.details.parametric_memories}");

                return result;
            }
            catch (TaskCanceledException)
            {
                Debug.WriteLine("[API Error] 記憶削除リクエストがタイムアウトしました");
                throw new TimeoutException("記憶削除リクエストがタイムアウトしました");
            }
        }

        /// <summary>
        /// MemOSストリーミングチャット送信
        /// </summary>
        /// <param name="request">MemOSチャットリクエスト</param>
        /// <param name="onStreamReceived">ストリーミングデータ受信時のコールバック</param>
        public async Task SendMemOSStreamingChatAsync(MemOSChatRequest request, Action<StreamingChatEventArgs> onStreamReceived)
        {
            try
            {
                var requestUrl = $"{_baseUrl}/api/memos/chat/stream";
                Debug.WriteLine($"[STREAMING API Request] POST {requestUrl}");
                Debug.WriteLine($"[STREAMING API Param] Query: {request.query}");
                Debug.WriteLine($"[STREAMING API Param] UserId: {request.user_id}");
                Debug.WriteLine($"[STREAMING API Param] Context: {request.context?.Count ?? 0} items");

                var json = MessageHelper.SerializeToJson(request);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                using var httpRequest = new HttpRequestMessage(HttpMethod.Post, requestUrl)
                {
                    Content = content
                };

                // text/event-streamを受信するためのヘッダー設定
                httpRequest.Headers.Add("Accept", "text/event-stream");
                httpRequest.Headers.Add("Cache-Control", "no-cache");

                using var response = await _httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead);

                Debug.WriteLine($"[STREAMING API Response] Status: {(int)response.StatusCode} {response.StatusCode}");
                Debug.WriteLine($"[STREAMING API Response] ContentType: {response.Content.Headers.ContentType?.MediaType}");

                if (!response.IsSuccessStatusCode)
                {
                    var errorBody = await response.Content.ReadAsStringAsync();
                    Debug.WriteLine($"[STREAMING API Error] Body: {errorBody}");

                    var errorEvent = new StreamingChatEventArgs
                    {
                        IsError = true,
                        ErrorMessage = $"ストリーミングチャットエラー (HTTP {(int)response.StatusCode}): {errorBody}",
                        IsFinished = true
                    };
                    onStreamReceived(errorEvent);
                    return;
                }

                // ストリーミングレスポンスを読み取り
                using var stream = await response.Content.ReadAsStreamAsync();
                using var reader = new StreamReader(stream, Encoding.UTF8);

                var fullResponse = new StringBuilder();
                string? line;
                while ((line = await reader.ReadLineAsync()) != null)
                {
                    Debug.WriteLine($"[STREAMING] Received line: {line}");

                    // Server-Sent Events形式の解析
                    if (line.StartsWith("data: "))
                    {
                        var dataContent = line.Substring(6); // "data: "を除去

                        if (string.IsNullOrWhiteSpace(dataContent))
                        {
                            continue; // 空のデータ行をスキップ
                        }

                        try
                        {
                            // JSON形式のデータを解析
                            using var jsonDoc = JsonDocument.Parse(dataContent);
                            var root = jsonDoc.RootElement;

                            if (root.TryGetProperty("type", out var typeElement))
                            {
                                var dataType = typeElement.GetString();

                                if (dataType == "error")
                                {
                                    // エラーメッセージ
                                    var errorMsg = root.TryGetProperty("data", out var dataElement)
                                        ? dataElement.GetString()
                                        : "Unknown streaming error";

                                    Debug.WriteLine($"[STREAMING Error] {errorMsg}");

                                    var errorEvent = new StreamingChatEventArgs
                                    {
                                        IsError = true,
                                        ErrorMessage = errorMsg,
                                        IsFinished = true
                                    };
                                    onStreamReceived(errorEvent);
                                    return;
                                }
                                else if (dataType == "data" || dataType == "response" || dataType == "text")
                                {
                                    // 通常の応答データ
                                    var responseData = root.TryGetProperty("data", out var dataElement)
                                        ? dataElement.GetString()
                                        : "";

                                    if (!string.IsNullOrEmpty(responseData))
                                    {
                                        fullResponse.Append(responseData);

                                        var contentEvent = new StreamingChatEventArgs
                                        {
                                            Content = responseData,
                                            IsFinished = false,
                                            IsError = false
                                        };
                                        onStreamReceived(contentEvent);
                                    }
                                }
                            }
                            else
                            {
                                // JSON構造でない場合は直接内容として扱う
                                var textContent = root.GetString() ?? dataContent;

                                if (!string.IsNullOrWhiteSpace(textContent))
                                {
                                    fullResponse.Append(textContent);

                                    var contentEvent = new StreamingChatEventArgs
                                    {
                                        Content = textContent,
                                        IsFinished = false,
                                        IsError = false
                                    };
                                    onStreamReceived(contentEvent);
                                }
                            }
                        }
                        catch (JsonException)
                        {
                            // JSON解析に失敗した場合は文字列として扱う
                            if (!string.IsNullOrWhiteSpace(dataContent))
                            {
                                fullResponse.Append(dataContent);

                                var contentEvent = new StreamingChatEventArgs
                                {
                                    Content = dataContent,
                                    IsFinished = false,
                                    IsError = false
                                };
                                onStreamReceived(contentEvent);
                            }
                        }
                    }
                }

                // ストリーミング完了
                Debug.WriteLine($"[STREAMING Completed] Total response length: {fullResponse.Length}");

                var finishedEvent = new StreamingChatEventArgs
                {
                    Content = "", // 最終イベントでは追加コンテンツなし
                    IsFinished = true,
                    IsError = false
                };
                onStreamReceived(finishedEvent);
            }
            catch (TaskCanceledException)
            {
                Debug.WriteLine("[STREAMING Error] ストリーミングリクエストがタイムアウトしました");
                var timeoutEvent = new StreamingChatEventArgs
                {
                    IsError = true,
                    ErrorMessage = "ストリーミングリクエストがタイムアウトしました",
                    IsFinished = true
                };
                onStreamReceived(timeoutEvent);
            }
            catch (HttpRequestException ex)
            {
                Debug.WriteLine($"[STREAMING Error] HTTP通信エラー: {ex.Message}");
                var httpErrorEvent = new StreamingChatEventArgs
                {
                    IsError = true,
                    ErrorMessage = $"HTTP通信エラー: {ex.Message}",
                    IsFinished = true
                };
                onStreamReceived(httpErrorEvent);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[STREAMING Error] 予期しないエラー: {ex.Message}");
                var generalErrorEvent = new StreamingChatEventArgs
                {
                    IsError = true,
                    ErrorMessage = $"予期しないエラー: {ex.Message}",
                    IsFinished = true
                };
                onStreamReceived(generalErrorEvent);
            }
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }
}