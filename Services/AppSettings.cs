using CocoroDock.Communication;
using CocoroDock.Utilities;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CocoroDock.Services
{
    /// <summary>
    /// 古いバージョンのSettingのキャラクター設定クラス
    /// </summary>
    public class OldCharacterSettings
    {
        [JsonPropertyName("IsReadOnly")]
        public bool IsReadOnly { get; set; }

        [JsonPropertyName("ModelName")]
        public string ModelName { get; set; } = string.Empty;

        [JsonPropertyName("VRMFilePath")]
        public string VRMFilePath { get; set; } = string.Empty;

        [JsonPropertyName("IsUseLLM")]
        public bool IsUseLLM { get; set; }

        [JsonPropertyName("ApiKey")]
        public string ApiKey { get; set; } = string.Empty;

        [JsonPropertyName("LLMModel")]
        public string LLMModel { get; set; } = string.Empty;

        [JsonPropertyName("SystemPrompt")]
        public string SystemPrompt { get; set; } = string.Empty;

        [JsonPropertyName("IsUseTTS")]
        public bool IsUseTTS { get; set; }

        [JsonPropertyName("TTSEndpointURL")]
        public string TTSEndpointURL { get; set; } = string.Empty;

        [JsonPropertyName("TTSSperkerID")]
        public string TTSSperkerID { get; set; } = string.Empty;
    }

    /// <summary>
    /// 古いバージョンのSettingの設定クラス
    /// </summary>
    public class OldConfigSettings
    {
        [JsonPropertyName("IsTopmost")]
        public bool IsTopmost { get; set; }

        [JsonPropertyName("IsEscapeCursor")]
        public bool IsEscapeCursor { get; set; }

        [JsonPropertyName("IsInputVirtualKey")]
        public bool IsInputVirtualKey { get; set; }

        [JsonPropertyName("VirtualKeyString")]
        public string VirtualKeyString { get; set; } = string.Empty;

        [JsonPropertyName("IsAutoMove")]
        public bool IsAutoMove { get; set; }

        [JsonPropertyName("showMessageWindow")]
        public bool ShowMessageWindow { get; set; }

        [JsonPropertyName("IsEnableAmbientOcclusion")]
        public bool IsEnableAmbientOcclusion { get; set; }

        [JsonPropertyName("WindowSize")]
        public float WindowSize { get; set; }

        [JsonPropertyName("MSAALevel")]
        public int MSAALevel { get; set; }

        [JsonPropertyName("CharacterShadow")]
        public int CharacterShadow { get; set; }

        [JsonPropertyName("CharacterShadowResolution")]
        public int CharacterShadowResolution { get; set; }

        [JsonPropertyName("BackgroundShadow")]
        public int BackgroundShadow { get; set; }

        [JsonPropertyName("BackgroundShadowResolution")]
        public int BackgroundShadowResolution { get; set; }

        [JsonPropertyName("CurrentCharacterIndex")]
        public int CurrentCharacterIndex { get; set; }

        [JsonPropertyName("CharacterList")]
        public List<OldCharacterSettings> CharacterList { get; set; } = new List<OldCharacterSettings>();
    }

    /// <summary>
    /// アプリケーション設定を管理するクラス
    /// </summary>
    public class AppSettings : IAppSettings
    {
        private static readonly Lazy<AppSettings> _instance = new Lazy<AppSettings>(() => new AppSettings());

        public static AppSettings Instance => _instance.Value;

        // アプリケーション設定ファイルのパス
        private string AppSettingsFilePath => Path.Combine(
            AppContext.BaseDirectory,
            "UserData", "Setting.json");

        // デフォルト設定ファイルのパス
        private string DefaultSettingsFilePath => Path.Combine(
            AppContext.BaseDirectory,
            "UserData", "DefaultSetting.json");

        public int CocoroDockPort { get; set; } = 55600;
        public int CocoroCorePort { get; set; } = 55601;
        public int CocoroMemoryPort { get; set; } = 55602;
        public int CocoroMemoryDBPort { get; set; } = 55603;
        public int CocoroShellPort { get; set; } = 55605;
        public int NotificationApiPort { get; set; } = 55604;
        // 通知API設定
        public bool IsEnableNotificationApi { get; set; } = true;
        // MCP設定
        public bool IsEnableMcp { get; set; } = false;
        // UI設定
        public bool IsRestoreWindowPosition { get; set; } = false;
        public bool IsTopmost { get; set; } = false;
        public bool IsEscapeCursor { get; set; } = false;
        public bool IsInputVirtualKey { get; set; } = false;
        public string VirtualKeyString { get; set; } = string.Empty;
        public bool IsAutoMove { get; set; } = false;
        public bool ShowMessageWindow { get; set; } = true;
        public bool IsEnableAmbientOcclusion { get; set; } = false;
        public int MsaaLevel { get; set; } = 0;
        public int CharacterShadow { get; set; } = 0;
        public int CharacterShadowResolution { get; set; } = 0;
        public int BackgroundShadow { get; set; } = 0;
        public int BackgroundShadowResolution { get; set; } = 0;
        public int WindowSize { get; set; } = 650;
        public float WindowPositionX { get; set; } = 0.0f;
        public float WindowPositionY { get; set; } = 0.0f;

        // キャラクター設定
        public int CurrentCharacterIndex { get; set; } = 0;
        public List<CharacterSettings> CharacterList { get; set; } = new List<CharacterSettings>();

        // アニメーション設定
        public int CurrentAnimationSettingIndex { get; set; } = 0;
        public List<AnimationSetting> AnimationSettings { get; set; } = new List<AnimationSetting>();

        // スクリーンショット設定
        public ScreenshotSettings ScreenshotSettings { get; set; } = new ScreenshotSettings();

        // マイク設定
        public MicrophoneSettings MicrophoneSettings { get; set; } = new MicrophoneSettings();

        public bool IsLoaded { get; set; } = false;

        // コンストラクタはprivate（シングルトンパターン）
        private AppSettings()
        {
            // 設定ファイルから読み込み
            LoadSettings();
        }

        /// <summary>
        /// 設定値を更新
        /// </summary>
        /// <param name="config">サーバーから受信した設定値</param>
        public void UpdateSettings(ConfigSettings config)
        {
            CocoroDockPort = config.cocoroDockPort;
            CocoroCorePort = config.cocoroCorePort;
            CocoroMemoryPort = config.cocoroMemoryPort;
            CocoroMemoryDBPort = config.cocoroMemoryDBPort;
            CocoroShellPort = config.cocoroShellPort;
            NotificationApiPort = config.notificationApiPort;
            IsEnableNotificationApi = config.isEnableNotificationApi;
            IsEnableMcp = config.isEnableMcp;
            IsRestoreWindowPosition = config.isRestoreWindowPosition;
            IsTopmost = config.isTopmost;
            IsEscapeCursor = config.isEscapeCursor;
            IsInputVirtualKey = config.isInputVirtualKey;
            VirtualKeyString = config.virtualKeyString;
            IsAutoMove = config.isAutoMove;
            ShowMessageWindow = config.showMessageWindow;
            IsEnableAmbientOcclusion = config.isEnableAmbientOcclusion;
            MsaaLevel = config.msaaLevel;
            CharacterShadow = config.characterShadow;
            CharacterShadowResolution = config.characterShadowResolution;
            BackgroundShadow = config.backgroundShadow;
            BackgroundShadowResolution = config.backgroundShadowResolution;
            WindowSize = config.windowSize > 0 ? (int)config.windowSize : 650;
            WindowPositionX = config.windowPositionX;
            WindowPositionY = config.windowPositionY;
            CurrentCharacterIndex = config.currentCharacterIndex;

            // キャラクターリストを更新（もし受信したリストが空でなければ）
            if (config.characterList != null && config.characterList.Count > 0)
            {
                CharacterList = new List<CharacterSettings>(config.characterList);
            }

            // アニメーション設定を更新
            CurrentAnimationSettingIndex = config.currentAnimationSettingIndex;
            if (config.animationSettings != null && config.animationSettings.Count > 0)
            {
                AnimationSettings = new List<AnimationSetting>(config.animationSettings);
            }

            // スクリーンショット設定を更新
            if (config.screenshotSettings != null)
            {
                ScreenshotSettings = config.screenshotSettings;
            }

            // マイク設定を更新
            if (config.microphoneSettings != null)
            {
                MicrophoneSettings = config.microphoneSettings;
            }

            // 設定読み込み完了フラグを設定
            IsLoaded = true;
        }

        /// <summary>
        /// 現在の設定からConfigSettingsオブジェクトを作成
        /// </summary>
        /// <returns>ConfigSettings オブジェクト</returns>
        public ConfigSettings GetConfigSettings()
        {
            return new ConfigSettings
            {
                cocoroDockPort = CocoroDockPort,
                cocoroCorePort = CocoroCorePort,
                cocoroMemoryPort = CocoroMemoryPort,
                cocoroMemoryDBPort = CocoroMemoryDBPort,
                cocoroShellPort = CocoroShellPort,
                notificationApiPort = NotificationApiPort,
                isEnableNotificationApi = IsEnableNotificationApi,
                isEnableMcp = IsEnableMcp,
                isRestoreWindowPosition = IsRestoreWindowPosition,
                isTopmost = IsTopmost,
                isEscapeCursor = IsEscapeCursor,
                isInputVirtualKey = IsInputVirtualKey,
                virtualKeyString = VirtualKeyString,
                isAutoMove = IsAutoMove,
                showMessageWindow = ShowMessageWindow,
                isEnableAmbientOcclusion = IsEnableAmbientOcclusion,
                msaaLevel = MsaaLevel,
                characterShadow = CharacterShadow,
                characterShadowResolution = CharacterShadowResolution,
                backgroundShadow = BackgroundShadow,
                backgroundShadowResolution = BackgroundShadowResolution,
                windowSize = WindowSize,
                windowPositionX = WindowPositionX,
                windowPositionY = WindowPositionY,
                currentCharacterIndex = CurrentCharacterIndex,
                characterList = new List<CharacterSettings>(CharacterList),
                currentAnimationSettingIndex = CurrentAnimationSettingIndex,
                animationSettings = new List<AnimationSetting>(AnimationSettings),
                screenshotSettings = ScreenshotSettings,
                microphoneSettings = MicrophoneSettings
            };
        }

        /// <summary>
        /// 設定ファイルから設定を読み込む
        /// </summary>
        public void LoadSettings()
        {
            try
            {
                // アプリケーション設定ファイルを読み込む
                LoadAppSettings();
                // 設定読み込み完了フラグを設定
                IsLoaded = true;
            }
            catch (Exception ex)
            {
                // エラーが発生した場合はデフォルト設定を使用
                Debug.WriteLine($"設定ファイル読み込みエラー: {ex.Message}");
            }
        }

        /// <summary>
        /// アプリケーション設定ファイルを読み込む
        /// </summary>
        public void LoadAppSettings()
        {
            try
            {
                // ディレクトリの存在確認とない場合は作成
                EnsureUserDataDirectoryExists();

                // まずデフォルト設定を読み込む
                ConfigSettings defaultSettings = LoadDefaultSettings();

                // 設定ファイルが存在するか確認
                if (File.Exists(AppSettingsFilePath))
                {
                    LoadExistingSettingsFile(defaultSettings);
                }
                else
                {
                    // 設定ファイルがない場合はデフォルト設定を適用して保存
                    UpdateSettings(defaultSettings);
                    SaveAppSettings();
                    Debug.WriteLine($"デフォルト設定をファイルに保存しました: {AppSettingsFilePath}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"アプリケーション設定ファイル読み込みエラー: {ex.Message}");
            }
        }

        /// <summary>
        /// UserDataディレクトリの存在を確認し、必要なら作成する
        /// </summary>
        private void EnsureUserDataDirectoryExists()
        {
            string userDataDir = Path.Combine(
                AppContext.BaseDirectory,
                "UserData");

            if (!Directory.Exists(userDataDir))
            {
                Directory.CreateDirectory(userDataDir);
            }
        }

        /// <summary>
        /// 既存の設定ファイルを読み込む
        /// </summary>
        private void LoadExistingSettingsFile(ConfigSettings defaultSettings)
        {
            string configJson = File.ReadAllText(AppSettingsFilePath);
            ProcessCurrentFormatSettings(configJson, defaultSettings);
        }

        /// <summary>
        /// 現在のフォーマットの設定ファイルを処理する
        /// </summary>
        private void ProcessCurrentFormatSettings(string configJson, ConfigSettings defaultSettings)
        {
            var userSettings = MessageHelper.DeserializeFromJson<ConfigSettings>(configJson);
            if (userSettings != null)
            {
                // デシリアライズされた設定をそのまま使用（デフォルト値は自動適用済み）
                UpdateSettings(userSettings);
                SaveAppSettings();
            }
        }


        /// <summary>
        /// デフォルト設定ファイルを読み込む
        /// </summary>
        private ConfigSettings LoadDefaultSettings()
        {
            if (File.Exists(DefaultSettingsFilePath))
            {
                try
                {
                    string json = File.ReadAllText(DefaultSettingsFilePath);
                    var options = new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true,
                        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                    };
                    var defaultSettings = MessageHelper.DeserializeFromJson<ConfigSettings>(json);

                    if (defaultSettings != null)
                    {
                        return defaultSettings;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"デフォルト設定ファイル読み込みエラー: {ex.Message}");
                }
            }

            // 読み込みに失敗した場合は空の設定を返す
            return new ConfigSettings();
        }

        /// <summary>
        /// アプリケーション設定をファイルに保存
        /// </summary>
        public void SaveAppSettings()
        {
            try
            {
                // 現在の設定からConfigSettingsオブジェクトを取得
                var settings = GetConfigSettings();

                // JSONにシリアライズ
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true, // 整形されたJSONを出力
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping // 日本語などの非ASCII文字をエスケープせずに出力
                };
                string json = JsonSerializer.Serialize(settings, options);

                // ファイルに保存
                File.WriteAllText(AppSettingsFilePath, json);

                Debug.WriteLine($"設定をファイルに保存しました: {AppSettingsFilePath}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"アプリケーション設定ファイル保存エラー: {ex.Message}");
            }
        }

        /// <summary>
        /// 全設定をファイルに保存
        /// </summary>
        public void SaveSettings()
        {
            SaveAppSettings();
        }
    }
}
