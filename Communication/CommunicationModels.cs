using System.Collections.Generic;

namespace CocoroDock.Communication
{
    /// <summary>
    /// WebSocketメッセージタイプ定義
    /// </summary>
    public enum MessageType
    {
        chat,
        config,
        control,
        status,
        system
    }

    /// <summary>
    /// WebSocketメッセージ基本構造
    /// </summary>
    public class WebSocketMessage
    {
        public string Type { get; set; } = string.Empty;
        public string Timestamp { get; set; } = string.Empty;
        public object? Payload { get; set; }

        public WebSocketMessage(MessageType type, object payload)
        {
            this.Type = type.ToString();
            Timestamp = System.DateTime.Now.ToString("o"); // ISO 8601
            this.Payload = payload;
        }
    }

    /// <summary>
    /// チャットメッセージペイロードクラス
    /// </summary>
    public class ChatMessagePayload
    {
        public string UserId { get; set; } = string.Empty;
        public string SessionId { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }

    /// <summary>
    /// チャットレスポンスペイロードクラス
    /// </summary>
    public class ChatResponsePayload
    {
        public string Response { get; set; } = string.Empty;
    }

    /// <summary>
    /// 設定リクエストペイロードクラス
    /// </summary>
    public class ConfigRequestPayload
    {
        public string Action { get; set; } = string.Empty;
    }

    /// <summary>
    /// 設定メッセージペイロードクラス
    /// </summary>
    public class ConfigMessagePayload
    {
        public string SettingKey { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
    }

    /// <summary>
    /// 設定更新ペイロードクラス
    /// </summary>
    public class ConfigUpdatePayload
    {
        public string Action { get; set; } = string.Empty;
        public ConfigSettings Settings { get; set; } = new ConfigSettings();
    }

    /// <summary>
    /// 設定レスポンスペイロードクラス
    /// </summary>
    public class ConfigResponsePayload
    {
        public string Status { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public ConfigSettings? Settings { get; set; }
    }

    /// <summary>
    /// 設定レスポンスを含むメッセージクラス
    /// </summary>
    public class ConfigResponseWithSettings
    {
        public string Type { get; set; } = string.Empty;
        public string Timestamp { get; set; } = string.Empty;
        public ConfigResponsePayload? Payload { get; set; }
    }

    /// <summary>
    /// キャラクター設定クラス
    /// </summary>
    public class CharacterSettings
    {
        public bool IsReadOnly { get; set; }
        public string ModelName { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string VrmFilePath { get; set; } = string.Empty;
        public bool IsUseLLM { get; set; }
        public string ApiKey { get; set; } = string.Empty;
        public string LlmModel { get; set; } = string.Empty;
        public string SystemPrompt { get; set; } = string.Empty;
        public double Temperature { get; set; } = 0.7;
        public int MaxTokens { get; set; } = 1000;
        public bool IsUseTTS { get; set; }
        public string TtsEndpointURL { get; set; } = string.Empty;
        public string TtsSperkerID { get; set; } = string.Empty;
    }

    /// <summary>
    /// アプリケーション設定クラス
    /// </summary>
    public class ConfigSettings
    {
        public int CocoroDockPort { get; set; }
        public int CocoroCorePort { get; set; }
        public string AiExecutablePath { get; set; } = string.Empty;
        public bool AutoStartAi { get; set; }
        public bool IsTopmost { get; set; }
        public bool IsEscapeCursor { get; set; }
        public bool IsInputVirtualKey { get; set; }
        public string VirtualKeyString { get; set; } = string.Empty;
        public bool IsAutoMove { get; set; }
        public bool IsEnableAmbientOcclusion { get; set; }
        public int MsaaLevel { get; set; }
        public int CharacterShadow { get; set; }
        public int CharacterShadowResolution { get; set; }
        public int BackgroundShadow { get; set; }
        public int BackgroundShadowResolution { get; set; }
        public float WindowSize { get; set; }
        public int CurrentCharacterIndex { get; set; }
        public List<CharacterSettings> CharacterList { get; set; } = new List<CharacterSettings>();
    }

    /// <summary>
    /// 制御メッセージペイロードクラス
    /// </summary>
    public class ControlMessagePayload
    {
        public string Command { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
    }

    /// <summary>
    /// 状態通知ペイロードクラス
    /// </summary>
    public class StatusMessagePayload
    {
        public int CurrentCPU { get; set; }
        public string Status { get; set; } = string.Empty;
    }

    /// <summary>
    /// システムメッセージペイロードクラス
    /// </summary>
    public class SystemMessagePayload
    {
        public string Level { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }
}
