using System.Collections.Generic;

namespace CocoroDock.Communication
{
    /// <summary>
    /// WebSocketメッセージタイプ定義
    /// </summary>
    public enum MessageType
    {
        chat,
        notification,
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
        public string userId { get; set; } = string.Empty; // 現状未使用
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
        public bool isEnableMemory { get; set; } = true; // メモリ機能の有効/無効（デフォルト: true）
        public string userId { get; set; } = "";
        public string embeddedApiKey { get; set; } = string.Empty; // 埋め込みモデル用APIキー
        public string embeddedModel { get; set; } = "openai/text-embedding-3-small"; // 埋め込みモデル名
        public bool isUseSTT { get; set; } = false; // STT（音声認識）機能の有効/無効
        public string sttWakeWord { get; set; } = string.Empty; // STT起動ワード
        public string sttApiKey { get; set; } = string.Empty; // STT用APIキー
        public bool isConvertMToon { get; set; } = false; // UnlitをMToonに変換するかどうか
        public bool isEnableShadowOff { get; set; } = true; // 影オフ機能の有効/無効（デフォルト: true）
        public string shadowOffMesh { get; set; } = "Face, U_Char_1"; // 影を落とさないメッシュ名
    }

    /// <summary>
    /// アプリケーション設定クラス
    /// </summary>
    public class ConfigSettings
    {
        public int cocoroDockPort { get; set; } = 55600;
        public int cocoroCorePort { get; set; } = 55601;
        public int cocoroMemoryPort { get; set; } = 55602;
        public int cocoroMemoryDBPort { get; set; } = 55603;
        public int notificationApiPort { get; set; } = 55604;
        public bool isEnableNotificationApi { get; set; } = false;
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
        public int currentAnimationSettingIndex { get; set; } = 0;
        public List<CharacterSettings> characterList { get; set; } = new List<CharacterSettings>();
        public List<AnimationSetting> animationSettings { get; set; } = new List<AnimationSetting>();
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
    /// アニメーション設定クラス
    /// </summary>
    public class AnimationSetting
    {
        public string animeSetName { get; set; } = "デフォルト"; // 設定セット名
        public int postureChangeLoopCountStanding { get; set; } = 30; // 立ち姿勢の変更ループ回数
        public int postureChangeLoopCountSittingFloor { get; set; } = 30; // 座り姿勢の変更ループ回数
        public List<AnimationConfig> animations { get; set; } = new List<AnimationConfig>(); // 個別アニメーション設定
    }

    /// <summary>
    /// 個別アニメーション設定クラス
    /// </summary>
    public class AnimationConfig
    {
        public string displayName { get; set; } = ""; // UI表示名（例：「立ち_手を振る」）
        public int animationType { get; set; } = 0; // 0:Standing, 1:SittingFloor (2:LyingDownは非表示)
        public string animationName { get; set; } = ""; // Animator内での名前（例：「DT_01_wait_natural_F_001_FBX」）
        public bool isEnabled { get; set; } = true; // 有効/無効
    }
}