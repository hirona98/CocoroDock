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
    /// アプリケーション設定を管理するクラス
    /// </summary>
    public class AppSettings : IAppSettings
    {
        private static readonly Lazy<AppSettings> _instance = new Lazy<AppSettings>(() => new AppSettings());

        public static AppSettings Instance => _instance.Value;

        /// <summary>
        /// 設定が保存されたときに発生するイベント
        /// </summary>
        public static event EventHandler? SettingsSaved;

        // UserData2ディレクトリのパスを取得
        private string UserDataDirectory => FindUserDataDirectory();

        // アプリケーション設定ファイルのパス
        private string AppSettingsFilePath => Path.Combine(UserDataDirectory, "Setting.json");

        // デフォルト設定ファイルのパス
        private string DefaultSettingsFilePath => Path.Combine(UserDataDirectory, "DefaultSetting.json");

        // アニメーション設定ファイルのパス
        private string AnimationSettingsFilePath => Path.Combine(UserDataDirectory, "AnimationSettings.json");

        // デフォルトアニメーション設定ファイルのパス
        private string DefaultAnimationSettingsFilePath => Path.Combine(UserDataDirectory, "DefaultAnimationSettings.json");

        // SystemPromptsディレクトリのパス
        public string SystemPromptsDirectory => Path.Combine(UserDataDirectory, "SystemPrompts");

        public int CocoroDockPort { get; set; } = 55600;
        public int CocoroCorePort { get; set; } = 55601;
        public int CocoroMemoryPort { get; set; } = 55602;
        public int CocoroMemoryDBPort { get; set; } = 55603;
        public int CocoroMemoryWebPort { get; set; } = 55606;
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
        public List<EscapePosition> EscapePositions { get; set; } = new List<EscapePosition>();
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

        // CocoroCore2用追加設定
        public bool EnableProMode { get; set; } = true;
        public bool EnableInternetRetrieval { get; set; } = true;
        public string GoogleApiKey { get; set; } = "GOOGLE_API_KEY";
        public string GoogleSearchEngineId { get; set; } = "GOOGLE_SERCH_ENGINE_ID";
        public int InternetMaxResults { get; set; } = 5;

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
        /// UserData2ディレクトリを探索して見つける
        /// </summary>
        /// <returns>UserData2ディレクトリのパス</returns>
        private string FindUserDataDirectory()
        {
            var baseDirectory = AppContext.BaseDirectory;

            // 探索するパスの配列
            string[] searchPaths = {
                Path.Combine(baseDirectory, "..", "UserData2"),
                Path.Combine(baseDirectory, "..", "..", "UserData2"),
                Path.Combine(baseDirectory, "..", "..", "..", "UserData2"),
                Path.Combine(baseDirectory, "..", "..", "..", "..", "UserData2")
            };

            foreach (var path in searchPaths)
            {
                var fullPath = Path.GetFullPath(path);
                if (Directory.Exists(fullPath))
                {
                    Debug.WriteLine($"UserData2ディレクトリ: {fullPath}");
                    return fullPath;
                }
            }

            // 見つからない場合は、最初のパスを使用してディレクトリを作成
            var defaultPath = Path.GetFullPath(searchPaths[0]);
            Debug.WriteLine($"UserData2が見つからないため作成: {defaultPath}");
            Directory.CreateDirectory(defaultPath);
            return defaultPath;
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
            CocoroMemoryWebPort = config.cocoroMemoryWebPort;
            CocoroShellPort = config.cocoroShellPort;
            NotificationApiPort = config.notificationApiPort;
            IsEnableNotificationApi = config.isEnableNotificationApi;
            IsEnableMcp = config.isEnableMcp;
            IsRestoreWindowPosition = config.isRestoreWindowPosition;
            IsTopmost = config.isTopmost;
            IsEscapeCursor = config.isEscapeCursor;
            EscapePositions = config.escapePositions != null ? new List<EscapePosition>(config.escapePositions) : new List<EscapePosition>();
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

            // CocoroCore2用追加設定を更新
            EnableProMode = config.enable_pro_mode;
            EnableInternetRetrieval = config.enable_internet_retrieval;
            GoogleApiKey = config.googleApiKey;
            GoogleSearchEngineId = config.googleSearchEngineId;
            InternetMaxResults = config.internetMaxResults;

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
                cocoroMemoryWebPort = CocoroMemoryWebPort,
                cocoroShellPort = CocoroShellPort,
                notificationApiPort = NotificationApiPort,
                isEnableNotificationApi = IsEnableNotificationApi,
                isEnableMcp = IsEnableMcp,
                isRestoreWindowPosition = IsRestoreWindowPosition,
                isTopmost = IsTopmost,
                isEscapeCursor = IsEscapeCursor,
                escapePositions = new List<EscapePosition>(EscapePositions),
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
                screenshotSettings = ScreenshotSettings,
                microphoneSettings = MicrophoneSettings,
                enable_pro_mode = EnableProMode,
                enable_internet_retrieval = EnableInternetRetrieval,
                googleApiKey = GoogleApiKey,
                googleSearchEngineId = GoogleSearchEngineId,
                internetMaxResults = InternetMaxResults,
                currentCharacterIndex = CurrentCharacterIndex,
                characterList = new List<CharacterSettings>(CharacterList)
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
                // アニメーション設定ファイルを読み込む
                LoadAnimationSettings();
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
            string userDataDir = UserDataDirectory;

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

                // イベント発生
                SettingsSaved?.Invoke(this, EventArgs.Empty);
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
            SaveAnimationSettings();
        }

        /// <summary>
        /// アニメーション設定をファイルから読み込む
        /// </summary>
        public void LoadAnimationSettings()
        {
            try
            {
                AnimationSettingsData animationData = null;

                // 設定ファイルが存在するか確認
                if (File.Exists(AnimationSettingsFilePath))
                {
                    string json = File.ReadAllText(AnimationSettingsFilePath);
                    animationData = MessageHelper.DeserializeFromJson<AnimationSettingsData>(json);
                }

                // 設定ファイルがない場合やデシリアライズに失敗した場合は、デフォルト設定を読み込む
                if (animationData == null)
                {
                    animationData = LoadDefaultAnimationSettings();

                    if (animationData != null)
                    {
                        // デフォルト設定をファイルに保存
                        SaveAnimationSettingsData(animationData);
                        Debug.WriteLine($"デフォルトアニメーション設定をファイルに保存しました: {AnimationSettingsFilePath}");
                    }
                }

                // 読み込んだ設定を適用
                if (animationData != null)
                {
                    CurrentAnimationSettingIndex = animationData.currentAnimationSettingIndex;
                    AnimationSettings = new List<AnimationSetting>(animationData.animationSettings);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"アニメーション設定ファイル読み込みエラー: {ex.Message}");
                // エラーが発生した場合はデフォルト設定を使用
                var defaultData = LoadDefaultAnimationSettings();
                if (defaultData != null)
                {
                    CurrentAnimationSettingIndex = defaultData.currentAnimationSettingIndex;
                    AnimationSettings = new List<AnimationSetting>(defaultData.animationSettings);
                }
            }
        }

        /// <summary>
        /// アニメーション設定をファイルに保存
        /// </summary>
        public void SaveAnimationSettings()
        {
            try
            {
                var animationData = new AnimationSettingsData
                {
                    currentAnimationSettingIndex = CurrentAnimationSettingIndex,
                    animationSettings = new List<AnimationSetting>(AnimationSettings)
                };

                SaveAnimationSettingsData(animationData);
                Debug.WriteLine($"アニメーション設定をファイルに保存しました: {AnimationSettingsFilePath}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"アニメーション設定ファイル保存エラー: {ex.Message}");
            }
        }

        /// <summary>
        /// デフォルトアニメーション設定を読み込む
        /// </summary>
        private AnimationSettingsData LoadDefaultAnimationSettings()
        {
            if (File.Exists(DefaultAnimationSettingsFilePath))
            {
                try
                {
                    string json = File.ReadAllText(DefaultAnimationSettingsFilePath);
                    var defaultData = MessageHelper.DeserializeFromJson<AnimationSettingsData>(json);

                    if (defaultData != null)
                    {
                        return defaultData;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"デフォルトアニメーション設定ファイル読み込みエラー: {ex.Message}");
                }
            }

            // 読み込みに失敗した場合は空の設定を返す
            return new AnimationSettingsData();
        }

        /// <summary>
        /// アニメーション設定データをファイルに保存する
        /// </summary>
        private void SaveAnimationSettingsData(AnimationSettingsData animationData)
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
            string json = JsonSerializer.Serialize(animationData, options);
            File.WriteAllText(AnimationSettingsFilePath, json);
        }

        /// <summary>
        /// キャラクターのsystemPromptをファイルから読み込む
        /// </summary>
        /// <param name="promptFilePath">プロンプトファイルのパス</param>
        /// <returns>プロンプトテキスト</returns>
        public string LoadSystemPrompt(string promptFilePath)
        {
            try
            {
                if (string.IsNullOrEmpty(promptFilePath))
                {
                    return string.Empty;
                }

                string fullPath = Path.Combine(SystemPromptsDirectory, promptFilePath);

                if (File.Exists(fullPath))
                {
                    return File.ReadAllText(fullPath);
                }
                else
                {
                    Debug.WriteLine($"SystemPromptファイルが見つかりません: {fullPath}");
                    return string.Empty;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SystemPrompt読み込みエラー: {ex.Message}");
                return string.Empty;
            }
        }

        /// <summary>
        /// キャラクターのsystemPromptをファイルに保存
        /// </summary>
        /// <param name="promptFilePath">プロンプトファイルのパス</param>
        /// <param name="promptText">プロンプトテキスト</param>
        public void SaveSystemPrompt(string promptFilePath, string promptText)
        {
            try
            {
                if (string.IsNullOrEmpty(promptFilePath))
                {
                    return;
                }

                // SystemPromptsディレクトリが存在しない場合は作成
                if (!Directory.Exists(SystemPromptsDirectory))
                {
                    Directory.CreateDirectory(SystemPromptsDirectory);
                }

                string fullPath = Path.Combine(SystemPromptsDirectory, promptFilePath);
                File.WriteAllText(fullPath, promptText ?? string.Empty);

                Debug.WriteLine($"SystemPromptを保存しました: {fullPath}");

                // SystemPrompt変更時もイベント発生
                SettingsSaved?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SystemPrompt保存エラー: {ex.Message}");
            }
        }

        /// <summary>
        /// 新しいsystemPromptファイル用のファイルパスを生成
        /// </summary>
        /// <param name="modelName">キャラクターのモデル名</param>
        /// <returns>モデル名_UUIDベースのファイルパス</returns>
        public string GenerateSystemPromptFilePath(string modelName)
        {
            return $"{modelName}_{Guid.NewGuid()}.txt";
        }

        /// <summary>
        /// UUID中間一致でsystemPromptファイルを検索
        /// </summary>
        /// <param name="uuid">検索するUUID</param>
        /// <returns>見つかったファイルパス、見つからない場合はnull</returns>
        public string? FindSystemPromptFileByUuid(string uuid)
        {
            try
            {
                if (string.IsNullOrEmpty(uuid) || !Directory.Exists(SystemPromptsDirectory))
                {
                    return null;
                }

                var files = Directory.GetFiles(SystemPromptsDirectory, "*.txt");
                foreach (var file in files)
                {
                    var fileName = Path.GetFileNameWithoutExtension(file);
                    if (fileName.Contains(uuid))
                    {
                        return Path.GetFileName(file);
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"UUID検索エラー: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// systemPromptファイル名からUUIDを抽出
        /// </summary>
        /// <param name="fileName">ファイル名</param>
        /// <returns>抽出されたUUID、抽出できない場合はnull</returns>
        public string? ExtractUuidFromFileName(string fileName)
        {
            try
            {
                if (string.IsNullOrEmpty(fileName))
                {
                    return null;
                }

                var nameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);
                var lastUnderscoreIndex = nameWithoutExtension.LastIndexOf('_');

                if (lastUnderscoreIndex >= 0 && lastUnderscoreIndex < nameWithoutExtension.Length - 1)
                {
                    var uuidPart = nameWithoutExtension.Substring(lastUnderscoreIndex + 1);
                    // UUID形式の検証（簡易）
                    if (Guid.TryParse(uuidPart, out _))
                    {
                        return uuidPart;
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"UUID抽出エラー: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// modelName変更時にsystemPromptファイル名を更新
        /// </summary>
        /// <param name="oldFileName">古いファイル名</param>
        /// <param name="newModelName">新しいモデル名</param>
        /// <returns>新しいファイル名</returns>
        public string UpdateSystemPromptFileName(string oldFileName, string newModelName)
        {
            try
            {
                var uuid = ExtractUuidFromFileName(oldFileName);
                if (uuid != null)
                {
                    var newFileName = $"{newModelName}_{uuid}.txt";
                    var oldFullPath = Path.Combine(SystemPromptsDirectory, oldFileName);
                    var newFullPath = Path.Combine(SystemPromptsDirectory, newFileName);

                    if (File.Exists(oldFullPath) && !File.Exists(newFullPath))
                    {
                        File.Move(oldFullPath, newFullPath);
                        Debug.WriteLine($"ファイル名を更新しました: {oldFileName} → {newFileName}");
                    }

                    return newFileName;
                }

                return oldFileName;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ファイル名更新エラー: {ex.Message}");
                return oldFileName;
            }
        }
    }

    /// <summary>
    /// アニメーション設定データクラス
    /// </summary>
    public class AnimationSettingsData
    {
        public int currentAnimationSettingIndex { get; set; } = 0;
        public List<AnimationSetting> animationSettings { get; set; } = new List<AnimationSetting>();
    }
}
