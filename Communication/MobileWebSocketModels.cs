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
        [JsonPropertyName("image_data_base64")]
        public string ImageDataBase64 { get; set; } = "";

        [JsonPropertyName("encoding")]
        public string Encoding { get; set; } = "base64";

        [JsonPropertyName("format")]
        public string Format { get; set; } = "jpeg";

        [JsonPropertyName("width")]
        public int Width { get; set; }

        [JsonPropertyName("height")]
        public int Height { get; set; }

        [JsonPropertyName("camera_facing")]
        public string CameraFacing { get; set; } = "user";

        // 後方互換性のため既存のプロパティも保持
        [JsonPropertyName("image_data")]
        public string ImageData { get; set; } = "";

        [JsonPropertyName("source")]
        public string Source { get; set; } = "camera";

        // メッセージ付き画像送信用
        [JsonPropertyName("message")]
        public string Message { get; set; } = "";
    }

    /// <summary>
    /// 音声メッセージ（RNNoise統合版）
    /// </summary>
    public class MobileVoiceMessage : MobileWebSocketMessage
    {
        public MobileVoiceMessage()
        {
            Type = "voice";
        }

        [JsonPropertyName("data")]
        public MobileVoiceData Data { get; set; } = new();
    }

    /// <summary>
    /// 画像メッセージ
    /// </summary>
    public class MobileImageMessage : MobileWebSocketMessage
    {
        public MobileImageMessage()
        {
            Type = "image";
        }

        [JsonPropertyName("data")]
        public MobileImageData Data { get; set; } = new();
    }

    /// <summary>
    /// 音声データ（RNNoise統合版）
    /// </summary>
    public class MobileVoiceData
    {
        /// <summary>
        /// 既存のList<int>型（後方互換性のため残す）
        /// </summary>
        [JsonPropertyName("audio_data")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<int>? AudioData { get; set; }

        /// <summary>
        /// Base64エンコードされた音声データ（新規追加）
        /// </summary>
        [JsonPropertyName("audio_data_base64")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? AudioDataBase64 { get; set; }

        /// <summary>
        /// エンコーディング形式（新規追加）
        /// </summary>
        [JsonPropertyName("encoding")]
        public string? Encoding { get; set; }

        [JsonPropertyName("sample_rate")]
        public int SampleRate { get; set; } = 16000;

        [JsonPropertyName("channels")]
        public int Channels { get; set; } = 1;

        [JsonPropertyName("format")]
        public string Format { get; set; } = "wav";

        [JsonPropertyName("processing")]
        public string Processing { get; set; } = "rnnoise";

        [JsonPropertyName("session_id")]
        public string? SessionId { get; set; }
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
        public const string VoiceRecognitionError = "VOICE_RECOGNITION_ERROR";
        public const string AudioProcessingError = "AUDIO_PROCESSING_ERROR";
        public const string VoiceDataError = "VOICE_DATA_ERROR";
        public const string ImageProcessingError = "IMAGE_PROCESSING_ERROR";
    }
}