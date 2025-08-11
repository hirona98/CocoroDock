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
        /// CocoroCore2にAPIでチャットメッセージを送信
        /// </summary>
        /// <param name="request">チャットリクエスト</param>
        public async Task<UnifiedChatResponse> SendUnifiedChatMessageAsync(UnifiedChatRequest request)
        {
            try
            {
                var json = MessageHelper.SerializeToJson(request);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                using var response = await _httpClient.PostAsync($"{_baseUrl}/api/chat/unified", content);

                var responseBody = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    var error = MessageHelper.DeserializeFromJson<ErrorResponse>(responseBody);
                    throw new HttpRequestException($"CocoroCore2エラー: {error?.message ?? responseBody}");
                }

                // JSON形式のレスポンスを解析
                var unifiedResponse = MessageHelper.DeserializeFromJson<UnifiedChatResponse>(responseBody);
                if (unifiedResponse == null)
                {
                    throw new InvalidOperationException("API応答の解析に失敗しました");
                }

                return unifiedResponse;
            }
            catch (TaskCanceledException)
            {
                throw new TimeoutException("CocoroCore2へのAPIリクエストがタイムアウトしました");
            }
            catch (HttpRequestException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"CocoroCore2へのAPI送信エラー: {ex.Message}");
                throw new InvalidOperationException($"CocoroCore2とのAPI通信に失敗しました: {ex.Message}", ex);
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