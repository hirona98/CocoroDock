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

        // REST APIレスポンス用のクラス
        public class ChatStartResponse
        {
            public string? status { get; set; }
            public string? message { get; set; }
            public string? session_id { get; set; }
            public string? context_id { get; set; }
        }

        public CocoroCoreClient(int port)
        {
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(30) // REST API用のタイムアウト
            };
            _baseUrl = $"http://127.0.0.1:{port}";
        }

        /// <summary>
        /// チャットレスポンス結果（処理開始通知）
        /// </summary>
        public class ChatResponse : StandardResponse
        {
            public string? context_id { get; set; }
            public string? content { get; set; }  // 処理開始時はnull、実際のコンテンツは/api/addChatUiで受信
        }

        /// <summary>
        /// CocoroCoreにチャットメッセージを送信（REST対応）
        /// </summary>
        /// <param name="request">チャットリクエスト</param>
        public async Task<ChatResponse> SendChatMessageAsync(CoreChatRequest request)
        {
            try
            {
                var json = MessageHelper.SerializeToJson(request);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                Debug.WriteLine($"CocoroCoreにチャットメッセージを送信: {request.text}");

                using var response = await _httpClient.PostAsync($"{_baseUrl}/chat", content);

                var responseBody = await response.Content.ReadAsStringAsync();
                Debug.WriteLine($"CocoroCoreからの応答: {responseBody}");

                if (!response.IsSuccessStatusCode)
                {
                    var error = MessageHelper.DeserializeFromJson<ErrorResponse>(responseBody);
                    throw new HttpRequestException($"CocoroCoreエラー: {error?.message ?? responseBody}");
                }

                // JSON形式のレスポンスを解析
                var startResponse = MessageHelper.DeserializeFromJson<ChatStartResponse>(responseBody);
                if (startResponse == null)
                {
                    throw new InvalidOperationException("チャット開始応答の解析に失敗しました");
                }

                Debug.WriteLine($"チャット処理開始: status={startResponse.status}, context_id={startResponse.context_id}");

                // 処理開始応答を返す（実際のメッセージは/api/addChatUiで受信）
                return new ChatResponse 
                { 
                    status = startResponse.status ?? "success", 
                    message = startResponse.message ?? "Chat processing started",
                    timestamp = DateTime.UtcNow,
                    context_id = startResponse.context_id,
                    content = null  // 実際のコンテンツは後で/api/addChatUiで受信
                };
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
                Debug.WriteLine($"CocoroCoreへのチャット送信エラー: {ex.Message}");
                throw new InvalidOperationException($"CocoroCoreとの通信に失敗しました: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// CocoroCore2に統一APIでチャットメッセージを送信（新設計）
        /// </summary>
        /// <param name="request">統一チャットリクエスト</param>
        public async Task<UnifiedChatResponse> SendUnifiedChatMessageAsync(UnifiedChatRequest request)
        {
            try
            {
                var json = MessageHelper.SerializeToJson(request);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                Debug.WriteLine($"CocoroCore2に統一APIでチャットメッセージを送信: {request.message}");

                using var response = await _httpClient.PostAsync($"{_baseUrl}/api/chat/unified", content);

                var responseBody = await response.Content.ReadAsStringAsync();
                Debug.WriteLine($"CocoroCore2からの統一API応答: {responseBody}");

                if (!response.IsSuccessStatusCode)
                {
                    var error = MessageHelper.DeserializeFromJson<ErrorResponse>(responseBody);
                    throw new HttpRequestException($"CocoroCore2エラー: {error?.message ?? responseBody}");
                }

                // JSON形式のレスポンスを解析
                var unifiedResponse = MessageHelper.DeserializeFromJson<UnifiedChatResponse>(responseBody);
                if (unifiedResponse == null)
                {
                    throw new InvalidOperationException("統一API応答の解析に失敗しました");
                }

                Debug.WriteLine($"統一API応答受信: status={unifiedResponse.status}, length={unifiedResponse.response_length}");

                return unifiedResponse;
            }
            catch (TaskCanceledException)
            {
                throw new TimeoutException("CocoroCore2への統一APIリクエストがタイムアウトしました");
            }
            catch (HttpRequestException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"CocoroCore2への統一API送信エラー: {ex.Message}");
                throw new InvalidOperationException($"CocoroCore2との統一API通信に失敗しました: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// CocoroCoreに通知メッセージを送信（/chatエンドポイントを使用）
        /// </summary>
        /// <param name="request">通知リクエスト</param>
        public async Task<StandardResponse> SendNotificationAsync(CoreNotificationRequest request)
        {
            try
            {
                // 通知も/chatエンドポイントを使用（AIAvatarKit仕様）
                var chatRequest = new CoreChatRequest
                {
                    type = request.type,
                    session_id = request.session_id,
                    user_id = request.user_id,
                    context_id = request.context_id,
                    text = request.text,
                    audio_data = null,
                    files = null,
                    system_prompt_params = null,
                    metadata = request.metadata
                };

                // チャットメッセージとして送信
                var response = await SendChatMessageAsync(chatRequest);
                // ChatResponseからStandardResponseに変換
                return new StandardResponse
                {
                    status = response.status,
                    message = response.message,
                    timestamp = response.timestamp
                };
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"CocoroCoreへの通知送信エラー: {ex.Message}");
                throw;
            }
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

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }
}