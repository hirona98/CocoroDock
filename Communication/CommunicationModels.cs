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
        public string type { get; set; } = string.Empty;
        public string timestamp { get; set; } = string.Empty;
        public object? payload { get; set; }

        public WebSocketMessage(MessageType type, object payload)
        {
            this.type = type.ToString();
            timestamp = System.DateTime.Now.ToString("o"); // ISO 8601
            this.payload = payload;
        }
    }

    /// <summary>
    /// チャットメッセージペイロードクラス
    /// </summary>
    public class ChatMessagePayload
    {
        public string userId { get; set; } = string.Empty;
        public string sessionId { get; set; } = string.Empty;
        public string message { get; set; } = string.Empty;
    }

    /// <summary>
    /// チャットレスポンスペイロードクラス
    /// </summary>
    public class ChatResponsePayload
    {
        public string response { get; set; } = string.Empty;
    }

    /// <summary>
    /// 設定リクエストペイロードクラス
    /// </summary>
    public class ConfigRequestPayload
    {
        public string action { get; set; } = string.Empty;
    }

    /// <summary>
    /// 設定メッセージペイロードクラス
    /// </summary>
    public class ConfigMessagePayload
    {
        public string settingKey { get; set; } = string.Empty;
        public string value { get; set; } = string.Empty;
    }

    /// <summary>
    /// 設定更新ペイロードクラス
    /// </summary>
    public class ConfigUpdatePayload
    {
        public string action { get; set; } = string.Empty;
        public ConfigSettings settings { get; set; } = new ConfigSettings();
    }

    /// <summary>
    /// 設定レスポンスペイロードクラス
    /// </summary>
    public class ConfigResponsePayload
    {
        public string status { get; set; } = string.Empty;
        public string message { get; set; } = string.Empty;
        public ConfigSettings? settings { get; set; }
    }

    /// <summary>
    /// 設定レスポンスを含むメッセージクラス
    /// </summary>
    public class ConfigResponseWithSettings
    {
        public string type { get; set; } = string.Empty;
        public string timestamp { get; set; } = string.Empty;
        public ConfigResponsePayload? payload { get; set; }
    }

    /// <summary>
    /// キャラクター設定クラス
    /// </summary>
    public class CharacterSettings
    {
        public bool isReadOnly { get; set; }
        public string modelName { get; set; } = string.Empty;
        public string vrmFilePath { get; set; } = string.Empty;
        public bool isUseLLM { get; set; }
        public string apiKey { get; set; } = string.Empty;
        public string llmModel { get; set; } = string.Empty;
        public string systemPrompt { get; set; } = string.Empty;
        public bool isUseTTS { get; set; }
        public string ttsEndpointURL { get; set; } = string.Empty;
        public string ttsSperkerID { get; set; } = string.Empty;
    }

    /// <summary>
    /// アプリケーション設定クラス
    /// </summary>
    public class ConfigSettings
    {
        public int cocoroDockPort { get; set; }
        public int cocoroCorePort { get; set; }
        public bool isTopmost { get; set; }
        public bool isEscapeCursor { get; set; }
        public bool isInputVirtualKey { get; set; }
        public string virtualKeyString { get; set; } = string.Empty;
        public bool isAutoMove { get; set; }
        public bool isEnableAmbientOcclusion { get; set; }
        public int msaaLevel { get; set; }
        public int characterShadow { get; set; }
        public int characterShadowResolution { get; set; }
        public int backgroundShadow { get; set; }
        public int backgroundShadowResolution { get; set; }
        public float windowSize { get; set; }
        public int currentCharacterIndex { get; set; }
        public List<CharacterSettings> characterList { get; set; } = new List<CharacterSettings>();
    }

    /// <summary>
    /// 制御メッセージペイロードクラス
    /// </summary>
    public class ControlMessagePayload
    {
        public string command { get; set; } = string.Empty;
        public string reason { get; set; } = string.Empty;
    }

    /// <summary>
    /// 状態通知ペイロードクラス
    /// </summary>
    public class StatusMessagePayload
    {
        public int currentCPU { get; set; }
        public string status { get; set; } = string.Empty;
    }

    /// <summary>
    /// システムメッセージペイロードクラス
    /// </summary>
    public class SystemMessagePayload
    {
        public string level { get; set; } = string.Empty;
        public string message { get; set; } = string.Empty;
    }
}