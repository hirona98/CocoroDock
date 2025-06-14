using CocoroDock.Utilities;
using System;
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

        // SSEデータ用のクラス
        public class SseData
        {
            public string? type { get; set; }
            public string? content { get; set; }
            public string? role { get; set; }
            public string? session_id { get; set; }
            public string? context_id { get; set; }
        }

        public CocoroCoreClient(int port)
        {
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromMinutes(5) // SSE用に長めのタイムアウトを設定
            };
            _baseUrl = $"http://127.0.0.1:{port}";
        }

        /// <summary>
        /// SSEレスポンス結果
        /// </summary>
        public class ChatResponse : StandardResponse
        {
            public string? context_id { get; set; }
        }

        /// <summary>
        /// CocoroCoreにチャットメッセージを送信（SSEストリーミング対応）
        /// </summary>
        /// <param name="request">チャットリクエスト</param>
        public async Task<ChatResponse> SendChatMessageAsync(CoreChatRequest request)
        {
            try
            {
                var json = MessageHelper.SerializeToJson(request);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                // SSEストリームを受信するため、特別なリクエストを構成
                var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/chat")
                {
                    Content = content
                };
                
                using var response = await _httpClient.SendAsync(
                    httpRequest, 
                    HttpCompletionOption.ResponseHeadersRead
                );

                if (!response.IsSuccessStatusCode)
                {
                    var errorBody = await response.Content.ReadAsStringAsync();
                    var error = MessageHelper.DeserializeFromJson<ErrorResponse>(errorBody);
                    throw new HttpRequestException($"CocoroCoreエラー: {error?.message ?? errorBody}");
                }

                // Server-Sent Eventsの処理
                await using var stream = await response.Content.ReadAsStreamAsync();
                using var reader = new StreamReader(stream);
                
                string? finalContextId = null;
                var responseReceived = false;

                while (!reader.EndOfStream)
                {
                    var line = await reader.ReadLineAsync();
                    if (line == null) break;

                    if (line.StartsWith("data: "))
                    {
                        var jsonData = line.Substring(6);
                        if (jsonData == "[DONE]") break;

                        try
                        {
                            var data = MessageHelper.DeserializeFromJson<SseData>(jsonData);
                            if (data != null)
                            {
                                // context_idを保存（今後の会話継続用）
                                if (!string.IsNullOrEmpty(data.context_id))
                                {
                                    finalContextId = data.context_id;
                                }

                                // チャンクごとの処理（必要に応じて）
                                Debug.WriteLine($"SSE チャンク受信: type={data.type}, content={data.content?.Length ?? 0} chars");
                                
                                responseReceived = true;
                            }
                        }
                        catch (JsonException ex)
                        {
                            Debug.WriteLine($"SSEデータのパースエラー: {ex.Message}");
                        }
                    }
                }

                // 最終的なレスポンスはCocoroCoreから/api/addChatUiに送信されるため、
                // ここでは成功レスポンスとcontext_idを返す
                return new ChatResponse 
                { 
                    status = "success", 
                    message = responseReceived ? "AI response received via SSE" : "No response received",
                    timestamp = DateTime.UtcNow,
                    context_id = finalContextId
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

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }
}