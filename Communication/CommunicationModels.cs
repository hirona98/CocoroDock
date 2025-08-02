using System;
using System.Collections.Generic;

namespace CocoroDock.Communication
{

    /// <summary>
    /// チャットメッセージペイロードクラス
    /// </summary>
    public class ChatMessagePayload
    {
        public string from { get; set; } = string.Empty;
        public string sessionId { get; set; } = string.Empty;
        public string message { get; set; } = string.Empty;
    }

    /// <summary>
    /// Style-Bert-VITS2の設定を保持するクラス
    /// </summary>
    public class StyleBertVits2Config
    {
        public string endpointUrl { get; set; } = "http://localhost:5000";
        public string modelName { get; set; } = "amitaro";
        public int modelId { get; set; } = 0;
        public string speakerName { get; set; } = "あみたろ";
        public int speakerId { get; set; } = 0;
        public string style { get; set; } = "Neutral";
        public float styleWeight { get; set; } = 1.0f;
        public float sdpRatio { get; set; } = 0.2f;
        public float noise { get; set; } = 0.6f;
        public float noiseW { get; set; } = 0.8f;
        public float length { get; set; } = 1.0f;
        public string language { get; set; } = "JP";
        public bool autoSplit { get; set; } = true;
        public float splitInterval { get; set; } = 0.5f;
        public string assistText { get; set; } = "";
        public float assistTextWeight { get; set; } = 0f;
        public string referenceAudioPath { get; set; } = "";
    }

    /// <summary>
    /// AivisCloudの設定を保持するクラス
    /// </summary>
    public class AivisCloudConfig
    {
        public string apiKey { get; set; } = "";
        public string endpointUrl { get; set; } = "";
        public string modelUuid { get; set; } = "a59cb814-0083-4369-8542-f51a29e72af7";
        public string speakerUuid { get; set; } = "";
        public int styleId { get; set; } = 0;
        public string styleName { get; set; } = "";
        public bool useSSML { get; set; } = false;
        public string language { get; set; } = "ja";
        public float speakingRate { get; set; } = 1f;
        public float emotionalIntensity { get; set; } = 1f;
        public float tempoDynamics { get; set; } = 1f;
        public float pitch { get; set; } = 0f;
        public float volume { get; set; } = 1f;
        public string outputFormat { get; set; } = "wav";
        public int outputBitrate { get; set; } = 0;
        public int outputSamplingRate { get; set; } = 16000;
        public string outputAudioChannels { get; set; } = "mono";
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
        public string localLLMBaseUrl { get; set; } = string.Empty; // ローカルLLMのベースURL
        public string systemPromptFilePath { get; set; } = string.Empty;
        public bool isUseTTS { get; set; }
        public string ttsEndpointURL { get; set; } = string.Empty;
        public string ttsSperkerID { get; set; } = string.Empty;
        public string ttsType { get; set; } = "voicevox"; // "voicevox" or "style-bert-vits2" or "aivis-cloud"

        // Style-Bert-VITS2用設定
        public StyleBertVits2Config styleBertVits2Config { get; set; } = new StyleBertVits2Config();

        // AivisCloud用設定
        public AivisCloudConfig aivisCloudConfig { get; set; } = new AivisCloudConfig();
        public bool isEnableMemory { get; set; } = false; // メモリ機能の有効/無効
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
        public int idleTimeoutMinutes { get; set; } = 10;
    }

    /// <summary>
    /// マイク設定クラス
    /// </summary>
    public class MicrophoneSettings
    {
        public int inputThreshold { get; set; } = -45;
    }

    /// <summary>
    /// 逃げ先座標設定クラス
    /// </summary>
    public class EscapePosition
    {
        public float x { get; set; } = 0f;
        public float y { get; set; } = 0f;
        public bool enabled { get; set; } = true;
    }

    /// <summary>
    /// 位置情報レスポンス
    /// </summary>
    public class PositionResponse
    {
        public string status { get; set; } = "success";
        public string message { get; set; } = string.Empty;
        public string timestamp { get; set; } = string.Empty;
        public PositionData position { get; set; } = new PositionData();
    }

    /// <summary>
    /// 位置情報データ
    /// </summary>
    public class PositionData
    {
        public float x { get; set; } = 0f;
        public float y { get; set; } = 0f;
        public SizeData windowSize { get; set; } = new SizeData();
    }

    /// <summary>
    /// サイズ情報データ
    /// </summary>
    public class SizeData
    {
        public float width { get; set; } = 0f;
        public float height { get; set; } = 0f;
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
        public int cocoroMemoryWebPort { get; set; } = 55606;
        public int cocoroShellPort { get; set; } = 55605;
        public int notificationApiPort { get; set; } = 55604;
        public bool isEnableNotificationApi { get; set; } = false;
        public bool isEnableMcp { get; set; } = false;
        public bool isRestoreWindowPosition { get; set; } = false;
        public bool isTopmost { get; set; }
        public bool isEscapeCursor { get; set; }
        public List<EscapePosition> escapePositions { get; set; } = new List<EscapePosition>();
        public bool isInputVirtualKey { get; set; }
        public string virtualKeyString { get; set; } = string.Empty;
        public bool isAutoMove { get; set; }
        public bool showMessageWindow { get; set; } = true;
        public bool isEnableAmbientOcclusion { get; set; }
        public int msaaLevel { get; set; }
        public int characterShadow { get; set; }
        public int characterShadowResolution { get; set; }
        public int backgroundShadow { get; set; }
        public int backgroundShadowResolution { get; set; }
        public float windowSize { get; set; }
        public float windowPositionX { get; set; } = 0.0f;
        public float windowPositionY { get; set; } = 0.0f;
        public ScreenshotSettings screenshotSettings { get; set; } = new ScreenshotSettings();
        public MicrophoneSettings microphoneSettings { get; set; } = new MicrophoneSettings();
        
        // CocoroCore2用追加設定
        public bool enable_pro_mode { get; set; } = true;
        public bool enable_internet_retrieval { get; set; } = true;
        public string googleApiKey { get; set; } = "GOOGLE_API_KEY";
        public string googleSearchEngineId { get; set; } = "GOOGLE_SERCH_ENGINE_ID";
        public int internetMaxResults { get; set; } = 5;
        
        public int currentCharacterIndex { get; set; }
        public List<CharacterSettings> characterList { get; set; } = new List<CharacterSettings>();
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
        public string userId { get; set; } = string.Empty;
        public string sessionId { get; set; } = string.Empty;
        public string message { get; set; } = string.Empty;
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
    /// 通知リクエスト
    /// </summary>
    public class NotificationRequest
    {
        public string from { get; set; } = string.Empty;
        public string message { get; set; } = string.Empty;
        public string[]? images { get; set; } // Base64エンコードされた画像データ配列（data URL形式、最大5枚）
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
    /// CocoroShell API: 設定部分更新リクエスト
    /// </summary>
    public class ConfigPatchRequest
    {
        public Dictionary<string, object> updates { get; set; } = new Dictionary<string, object>();
        public string[] changedFields { get; set; } = Array.Empty<string>();
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
    /// CocoroCore2 API: チャットリクエスト
    /// </summary>
    public class UnifiedChatRequest
    {
        public string user_id { get; set; } = string.Empty; // ユーザーID（固定値）
        public string session_id { get; set; } = string.Empty; // セッションID
        public string message { get; set; } = string.Empty; // メッセージテキスト
        public string? character_name { get; set; } // キャラクター名
        public string? system_prompt { get; set; } // システムプロンプト
        public string? context_id { get; set; } // コンテキストID
        public List<Dictionary<string, object>>? files { get; set; } // 添付ファイル
        public Dictionary<string, object>? metadata { get; set; } // メタデータ
    }

    /// <summary>
    /// CocoroCore2 API: チャットレスポンス
    /// </summary>
    public class UnifiedChatResponse
    {
        public string status { get; set; } = string.Empty; // 処理状態
        public string message { get; set; } = string.Empty; // 処理結果メッセージ
        public string? response { get; set; } // AIの応答
        public string? context_id { get; set; } // 新しいコンテキストID
        public string? session_id { get; set; } // セッションID
        public int? response_length { get; set; } // レスポンス文字数
        public DateTime timestamp { get; set; } // タイムスタンプ
    }

    /// <summary>
    /// CocoroCore API: 制御コマンドリクエスト
    /// </summary>
    public class CoreControlRequest
    {
        public string command { get; set; } = string.Empty;
        public Dictionary<string, object>? @params { get; set; }
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

    /// <summary>
    /// CocoroCore: ヘルスチェックレスポンス
    /// </summary>
    public class HealthCheckResponse
    {
        public string status { get; set; } = string.Empty;
        public string version { get; set; } = string.Empty;
        public string character { get; set; } = string.Empty;
        public bool memory_enabled { get; set; }
        public string llm_model { get; set; } = string.Empty;
        public int active_sessions { get; set; }
        public McpStatus? mcp_status { get; set; }
    }

    /// <summary>
    /// MCPステータス情報
    /// </summary>
    public class McpStatus
    {
        public int total_servers { get; set; }
        public int connected_servers { get; set; }
        public int total_tools { get; set; }
        public Dictionary<string, McpServerInfo>? servers { get; set; }
        public string? error { get; set; }
    }

    /// <summary>
    /// MCPサーバー情報
    /// </summary>
    public class McpServerInfo
    {
        public bool connected { get; set; }
        public int tool_count { get; set; }
        public string connection_type { get; set; } = string.Empty;
    }

    /// <summary>
    /// MCP診断結果
    /// </summary>
    public class McpDiagnosticResult
    {
        public List<McpServerDiagnostic>? servers { get; set; }
        public string summary { get; set; } = string.Empty;
        public string? error { get; set; }
    }

    /// <summary>
    /// MCPサーバー診断詳細
    /// </summary>
    public class McpServerDiagnostic
    {
        public string name { get; set; } = string.Empty;
        public string command { get; set; } = string.Empty;
        public List<string>? args { get; set; }
        public Dictionary<string, object>? env { get; set; }
        public List<string>? issues { get; set; }
        public List<string>? checks { get; set; }
    }

    /// <summary>
    /// MCP診断レスポンス
    /// </summary>
    public class McpDiagnosticResponse : StandardResponse
    {
        public McpDiagnosticResult? diagnostic_result { get; set; }
    }

    /// <summary>
    /// MCPツール登録ログレスポンス
    /// </summary>
    public class McpToolRegistrationResponse : StandardResponse
    {
        public List<string>? logs { get; set; }
    }

    /// <summary>
    /// ログメッセージ
    /// </summary>
    public class LogMessage
    {
        public DateTime timestamp { get; set; } = DateTime.UtcNow;
        public string level { get; set; } = string.Empty; // "DEBUG", "INFO", "WARNING", "ERROR"
        public string component { get; set; } = string.Empty; // "CocoroCore", "CocoroMemory"
        public string message { get; set; } = string.Empty;
    }

    #endregion
}