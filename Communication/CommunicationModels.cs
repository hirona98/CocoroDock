using System;
using System.Collections.Generic;

namespace CocoroDock.Communication
{

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
        public string sttEngine { get; set; } = "amivoice"; // STTエンジン ("amivoice" | "openai")
        public string sttWakeWord { get; set; } = string.Empty; // STT起動ワード
        public string sttApiKey { get; set; } = string.Empty; // STT用APIキー
        public string sttLanguage { get; set; } = "ja"; // STT言語設定
        public bool isConvertMToon { get; set; } = false; // UnlitをMToonに変換するかどうか
        public bool isEnableShadowOff { get; set; } = true; // 影オフ機能の有効/無効（デフォルト: true）
        public string shadowOffMesh { get; set; } = "Face, U_Char_1"; // 影を落とさないメッシュ名
    }

    /// <summary>
    /// スクリーンショット設定クラス
    /// </summary>
    public class ScreenshotSettings
    {
        public bool enabled { get; set; } = false;
        public int intervalMinutes { get; set; } = 10;
        public bool captureActiveWindowOnly { get; set; } = true;
        public bool includeContextAnalysis { get; set; } = true;
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
        public ScreenshotSettings screenshotSettings { get; set; } = new ScreenshotSettings();
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

    #region REST API ペイロードクラス

    /// <summary>
    /// CocoroDock API: チャットリクエスト
    /// </summary>
    public class ChatRequest
    {
        public string role { get; set; } = string.Empty; // "user" | "assistant"
        public string content { get; set; } = string.Empty;
        public DateTime timestamp { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// CocoroDock API: 制御コマンドリクエスト
    /// </summary>
    public class ControlRequest
    {
        public string command { get; set; } = string.Empty; // "shutdown" | "restart" | "reloadConfig"
        public Dictionary<string, object>? @params { get; set; }
        public string? reason { get; set; }
    }

    /// <summary>
    /// 標準レスポンス
    /// </summary>
    public class StandardResponse
    {
        public string status { get; set; } = "success"; // "success" | "error"
        public string message { get; set; } = string.Empty;
        public DateTime timestamp { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// エラーレスポンス
    /// </summary>
    public class ErrorResponse
    {
        public string status { get; set; } = "error";
        public string message { get; set; } = string.Empty;
        public string? errorCode { get; set; }
        public DateTime timestamp { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// CocoroShell API: チャットリクエスト
    /// </summary>
    public class ShellChatRequest
    {
        public string content { get; set; } = string.Empty;
        public VoiceParams? voiceParams { get; set; }
        public string? animation { get; set; } // "talk" | "idle" | null
        public string? characterName { get; set; }
    }

    /// <summary>
    /// 音声パラメータ
    /// </summary>
    public class VoiceParams
    {
        public int speaker_id { get; set; } = 1;
        public float speed { get; set; } = 1.0f;
        public float pitch { get; set; } = 0.0f;
        public float volume { get; set; } = 1.0f;
    }

    /// <summary>
    /// CocoroShell API: アニメーションリクエスト
    /// </summary>
    public class AnimationRequest
    {
        public string animationName { get; set; } = string.Empty;
    }

    /// <summary>
    /// CocoroShell API: 制御コマンドリクエスト
    /// </summary>
    public class ShellControlRequest
    {
        public string command { get; set; } = string.Empty;
        public Dictionary<string, object>? @params { get; set; }
    }

    /// <summary>
    /// CocoroCore API: チャットリクエスト (AIAvatarKit仕様準拠)
    /// </summary>
    public class CoreChatRequest
    {
        public string type { get; set; } = "invoke"; // AIAvatarKit: リクエストタイプ
        public string session_id { get; set; } = string.Empty; // セッションID
        public string user_id { get; set; } = string.Empty; // ユーザーID
        public string? context_id { get; set; } // コンテキストID（会話継続用）
        public string text { get; set; } = string.Empty; // テキストメッセージ
        public string? audio_data { get; set; } // Base64エンコードされた音声データ
        public List<object>? files { get; set; } // 添付ファイル
        public Dictionary<string, object>? system_prompt_params { get; set; } // システムプロンプトパラメータ
        public Dictionary<string, object>? metadata { get; set; } // メタデータ
    }

    /// <summary>
    /// CocoroCore API: 通知リクエスト (AIAvatarKit仕様準拠)
    /// </summary>
    public class CoreNotificationRequest
    {
        public string type { get; set; } = "invoke"; // AIAvatarKit: リクエストタイプ
        public string session_id { get; set; } = string.Empty; // セッションID
        public string user_id { get; set; } = string.Empty; // ユーザーID（送信者）
        public string? context_id { get; set; } // コンテキストID
        public string text { get; set; } = string.Empty; // 通知メッセージ
        public Dictionary<string, object>? metadata { get; set; } // メタデータ
    }

    /// <summary>
    /// CocoroDock API: ステータス更新リクエスト
    /// </summary>
    public class StatusUpdateRequest
    {
        public string message { get; set; } = string.Empty; // ステータスメッセージ
        public string? type { get; set; } // ステータスタイプ（"api_start", "api_end"など）
        public DateTime timestamp { get; set; } = DateTime.UtcNow;
    }

    #endregion
}