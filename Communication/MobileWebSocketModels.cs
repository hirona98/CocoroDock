using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CocoroDock.Communication
{
    /// <summary>
    /// モバイルWebSocket通信用メッセージの基底クラス
    /// </summary>
    public abstract class MobileWebSocketMessage
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = "";

        [JsonPropertyName("timestamp")]
        public string Timestamp { get; set; } = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
    }

    /// <summary>
    /// スマートフォンからCocoroDockへのチャットメッセージ
    /// </summary>
    public class MobileChatMessage : MobileWebSocketMessage
    {
        public MobileChatMessage()
        {
            Type = "chat";
        }

        [JsonPropertyName("data")]
        public MobileChatData Data { get; set; } = new();
    }

    /// <summary>
    /// チャットメッセージのデータ部分
    /// </summary>
    public class MobileChatData
    {
        [JsonPropertyName("message")]
        public string Message { get; set; } = "";

        [JsonPropertyName("chat_type")]
        public string? ChatType { get; set; }

        [JsonPropertyName("images")]
        public List<MobileImageData>? Images { get; set; }
    }

    /// <summary>
    /// 画像データ
    /// </summary>
    public class MobileImageData
    {
        [JsonPropertyName("image_data")]
        public string ImageData { get; set; } = "";

        [JsonPropertyName("source")]
        public string Source { get; set; } = "camera";
    }

    /// <summary>
    /// CocoroDockからスマートフォンへの応答メッセージ
    /// </summary>
    public class MobileResponseMessage : MobileWebSocketMessage
    {
        public MobileResponseMessage()
        {
            Type = "response";
        }

        [JsonPropertyName("data")]
        public MobileResponseData Data { get; set; } = new();
    }

    /// <summary>
    /// 応答メッセージのデータ部分
    /// </summary>
    public class MobileResponseData
    {
        [JsonPropertyName("text")]
        public string Text { get; set; } = "";

        [JsonPropertyName("audio_url")]
        public string? AudioUrl { get; set; }

        [JsonPropertyName("speaker_id")]
        public int SpeakerId { get; set; } = 3;

        [JsonPropertyName("source")]
        public string Source { get; set; } = "cocoro_core_m";
    }

    /// <summary>
    /// エラーメッセージ
    /// </summary>
    public class MobileErrorMessage : MobileWebSocketMessage
    {
        public MobileErrorMessage()
        {
            Type = "error";
        }

        [JsonPropertyName("data")]
        public MobileErrorData Data { get; set; } = new();
    }

    /// <summary>
    /// エラーメッセージのデータ部分
    /// </summary>
    public class MobileErrorData
    {
        [JsonPropertyName("code")]
        public string Code { get; set; } = "";

        [JsonPropertyName("message")]
        public string Message { get; set; } = "";
    }

    /// <summary>
    /// エラーコード定数
    /// </summary>
    public static class MobileErrorCodes
    {
        public const string VoicevoxError = "VOICEVOX_ERROR";
        public const string CoreMError = "CORE_M_ERROR";
        public const string NetworkError = "NETWORK_ERROR";
        public const string InvalidMessage = "INVALID_MESSAGE";
        public const string ServerError = "SERVER_ERROR";
    }
}