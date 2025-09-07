using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace CocoroDock.Services
{
    public class AmiVoiceSyncClient
    {
        private const string ENDPOINT = "https://acp-api.amivoice.com/v1/recognize";
        private readonly string _apiKey;
        private static readonly HttpClient _httpClient;

        static AmiVoiceSyncClient()
        {
            var handler = new HttpClientHandler()
            {
                MaxConnectionsPerServer = 100,
                UseCookies = false
            };

            _httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(30)
            };

            // Keep-Alive設定
            _httpClient.DefaultRequestHeaders.Connection.Add("keep-alive");
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "CocoroAI/4.1.2");
        }

        public AmiVoiceSyncClient(string apiKey)
        {
            _apiKey = apiKey ?? throw new ArgumentNullException(nameof(apiKey));
        }

        public async Task<string> RecognizeAsync(byte[] audioData)
        {
            if (audioData == null || audioData.Length == 0)
                return string.Empty;

            try
            {
                using var content = new MultipartFormDataContent();
                content.Add(new StringContent(_apiKey), "u");
                content.Add(new StringContent("grammarFileNames=-a2-ja-general"), "d");

                using var audioContent = new ByteArrayContent(audioData);
                audioContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("audio/wav");
                content.Add(audioContent, "a", "audio.wav");

                var response = await _httpClient.PostAsync(ENDPOINT, content).ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    var errorText = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    System.Diagnostics.Debug.WriteLine($"AmiVoice API Error: {response.StatusCode} - {errorText}");
                    return string.Empty;
                }

                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                // 直接textフィールドを取得（aiavatarkitと同じ方式）
                using var document = JsonDocument.Parse(json);
                if (document.RootElement.TryGetProperty("results", out var results) &&
                    results.GetArrayLength() > 0 &&
                    results[0].TryGetProperty("text", out var textElement))
                {
                    return textElement.GetString() ?? string.Empty;
                }

                return string.Empty;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"AmiVoice Recognition Error: {ex.Message}");
                return string.Empty;
            }
        }

        public void Dispose()
        {
            // 静的HttpClientは破棄しない（アプリケーション終了まで再利用）
        }
    }

    public class AmiVoiceResult
    {
        public AmiVoiceResultItem[]? results { get; set; }
        public string? code { get; set; }
        public string? message { get; set; }
    }

    public class AmiVoiceResultItem
    {
        public string text { get; set; } = string.Empty;
        public float confidence { get; set; }
        public AmiVoiceToken[]? tokens { get; set; }
    }

    public class AmiVoiceToken
    {
        public string written { get; set; } = string.Empty;
        public string spoken { get; set; } = string.Empty;
        public int starttime { get; set; }
        public int endtime { get; set; }
    }
}