using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CocoroDock.Communication
{
    /// <summary>
    /// WebSocketチャットリクエスト（最新仕様対応）
    /// </summary>
    public class WebSocketChatRequest
    {
        [JsonPropertyName("query")]
        public string query { get; set; } = "";

        [JsonPropertyName("chat_type")]
        public string chat_type { get; set; } = "text"; // "text" | "text_image" | "notification" | "desktop_watch"

        [JsonPropertyName("images")]
        public List<ImageData>? images { get; set; }

        [JsonPropertyName("notification")]
        public NotificationData? notification { get; set; }

        [JsonPropertyName("desktop_context")]
        public DesktopContext? desktop_context { get; set; }

        [JsonPropertyName("history")]
        public List<HistoryMessage>? history { get; set; }

        [JsonPropertyName("internet_search")]
        public bool internet_search { get; set; } = false;

        [JsonPropertyName("request_id")]
        public string? request_id { get; set; }
    }

    /// <summary>
    /// WebSocketメッセージ基本形式
    /// </summary>
    public class WebSocketMessage
    {
        [JsonPropertyName("action")]
        public string action { get; set; } = "";

        [JsonPropertyName("session_id")]
        public string session_id { get; set; } = "";

        [JsonPropertyName("request")]
        public WebSocketChatRequest? request { get; set; }
    }

    /// <summary>
    /// WebSocketレスポンスメッセージ
    /// </summary>
    public class WebSocketResponseMessage
    {
        [JsonPropertyName("session_id")]
        public string session_id { get; set; } = "";

        [JsonPropertyName("type")]
        public string type { get; set; } = "";

        [JsonPropertyName("data")]
        public object? data { get; set; }
    }

    /// <summary>
    /// WebSocketテキストデータ
    /// </summary>
    public class WebSocketTextData
    {
        [JsonPropertyName("content")]
        public string content { get; set; } = "";

        [JsonPropertyName("is_incremental")]
        public bool is_incremental { get; set; } = true;
    }

    /// <summary>
    /// WebSocketエラーデータ
    /// </summary>
    public class WebSocketErrorData
    {
        [JsonPropertyName("message")]
        public string message { get; set; } = "";

        [JsonPropertyName("code")]
        public string code { get; set; } = "";
    }

    /// <summary>
    /// WebSocket参照データ
    /// </summary>
    public class WebSocketReferenceData
    {
        [JsonPropertyName("references")]
        public List<MemoryReference>? references { get; set; }
    }

    /// <summary>
    /// 記憶参照情報
    /// </summary>
    public class MemoryReference
    {
        [JsonPropertyName("memory_id")]
        public string memory_id { get; set; } = "";

        [JsonPropertyName("content")]
        public string content { get; set; } = "";

        [JsonPropertyName("relevance_score")]
        public double relevance_score { get; set; } = 0.0;
    }

    /// <summary>
    /// WebSocket時間データ
    /// </summary>
    public class WebSocketTimeData
    {
        [JsonPropertyName("total_time")]
        public double total_time { get; set; } = 0.0;

        [JsonPropertyName("speed_improvement")]
        public string speed_improvement { get; set; } = "";
    }

    /// <summary>
    /// WebSocket完了データ
    /// </summary>
    public class WebSocketEndData
    {
        [JsonPropertyName("total_tokens")]
        public int total_tokens { get; set; } = 0;

        [JsonPropertyName("final_text")]
        public string final_text { get; set; } = "";
    }

}