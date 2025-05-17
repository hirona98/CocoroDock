using CocoroDock.Communication;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Encodings.Web;
using System.Text.Json.Serialization;

namespace CocoroDock.Services
{
    /// <summary>
    /// 古いバージョンのsetting.jsonのキャラクター設定クラス
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
    /// 古いバージョンのsetting.jsonの設定クラス
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
    public class AppSettings
    {
        private static readonly Lazy<AppSettings> _instance = new Lazy<AppSettings>(() => new AppSettings());

        public static AppSettings Instance => _instance.Value;

        // アプリケーション設定ファイルのパス
        private string AppSettingsFilePath => Path.Combine(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "",
            "UserData", "setting.json");

        // デフォルト設定ファイルのパス
        private string DefaultSettingsFilePath => Path.Combine(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "",
            "UserData", "defaultSetting.json");

        public int CocoroDockPort { get; set; } = 55600;
        public int CocoroCorePort { get; set; } = 55601;
        // アプリケーション設定
        public string AiExecutablePath { get; set; } = string.Empty;
        public bool AutoStartAi { get; set; } = false;
        // UI設定
        public bool IsTopmost { get; set; } = false;
        public bool IsEscapeCursor { get; set; } = false;
        public bool IsInputVirtualKey { get; set; } = false;
        public string VirtualKeyString { get; set; } = string.Empty;
        public bool IsAutoMove { get; set; } = false;
        public bool IsEnableAmbientOcclusion { get; set; } = false;
        public int MsaaLevel { get; set; } = 0;
        public int CharacterShadow { get; set; } = 0;
        public int CharacterShadowResolution { get; set; } = 0;
        public int BackgroundShadow { get; set; } = 0;
        public int BackgroundShadowResolution { get; set; } = 0;
        public int WindowSize { get; set; } = 650;

        // キャラクター設定
        public int CurrentCharacterIndex { get; set; } = 0;
        public List<CharacterSettings> CharacterList { get; set; } = new List<CharacterSettings>();

        public bool IsLoaded { get; set; } = false;

        // コンストラクタはprivate（シングルトンパターン）
        private AppSettings()
        {
            // デフォルト設定を初期化
            InitializeDefaultSettings();

            // 設定ファイルから読み込み
            LoadSettings();
        }

        /// <summary>
        /// デフォルト設定を初期化
        /// </summary>
        private void InitializeDefaultSettings()
        {
            // デフォルトのキャラクター設定を初期化
            CharacterList = new List<CharacterSettings>
            {
                new CharacterSettings
                {
                    IsReadOnly = false,
                    ModelName = "model_name",
                    VrmFilePath = "vrm_file_path",
                    IsUseLLM = false,
                    ApiKey = "your_api_key",
                    LlmModel = "gpt-3.5-turbo",
                    SystemPrompt = "あなたは親切なアシスタントです。",
                    IsUseTTS = false,
                    TtsEndpointURL = "http://localhost:50021",
                    TtsSperkerID = "1",
                }
            };
        }

        /// <summary>
        /// 設定値を更新
        /// </summary>
        /// <param name="config">サーバーから受信した設定値</param>
        public void UpdateSettings(ConfigSettings config)
        {
            CocoroDockPort = config.CocoroDockPort;
            CocoroCorePort = config.CocoroCorePort;
            IsTopmost = config.IsTopmost;
            IsEscapeCursor = config.IsEscapeCursor;
            IsInputVirtualKey = config.IsInputVirtualKey;
            VirtualKeyString = config.VirtualKeyString;
            IsAutoMove = config.IsAutoMove;
            IsEnableAmbientOcclusion = config.IsEnableAmbientOcclusion;
            MsaaLevel = config.MsaaLevel;
            CharacterShadow = config.CharacterShadow;
            CharacterShadowResolution = config.CharacterShadowResolution;
            BackgroundShadow = config.BackgroundShadow;
            BackgroundShadowResolution = config.BackgroundShadowResolution;
            WindowSize = config.WindowSize > 0 ? (int)config.WindowSize : 650;
            CurrentCharacterIndex = config.CurrentCharacterIndex;

            // キャラクターリストを更新（もし受信したリストが空でなければ）
            if (config.CharacterList != null && config.CharacterList.Count > 0)
            {
                CharacterList = new List<CharacterSettings>(config.CharacterList);
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
                CocoroDockPort = CocoroDockPort,
                CocoroCorePort = CocoroCorePort,
                IsTopmost = IsTopmost,
                IsEscapeCursor = IsEscapeCursor,
                IsInputVirtualKey = IsInputVirtualKey,
                VirtualKeyString = VirtualKeyString,
                IsAutoMove = IsAutoMove,
                IsEnableAmbientOcclusion = IsEnableAmbientOcclusion,
                MsaaLevel = MsaaLevel,
                CharacterShadow = CharacterShadow,
                CharacterShadowResolution = CharacterShadowResolution,
                BackgroundShadow = BackgroundShadow,
                BackgroundShadowResolution = BackgroundShadowResolution,
                WindowSize = WindowSize,
                CurrentCharacterIndex = CurrentCharacterIndex,
                CharacterList = new List<CharacterSettings>(CharacterList)
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
                ErrorHandlingService.Instance.LogError(
                    ErrorHandlingService.ErrorLevel.Error,
                    "設定ファイル読み込みエラー",
                    ex);
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
                string userDataDir = Path.Combine(
                    Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "",
                    "UserData");

                if (!Directory.Exists(userDataDir))
                {
                    Directory.CreateDirectory(userDataDir);
                }

                // まずデフォルト設定を読み込む
                ConfigSettings defaultSettings = LoadDefaultSettings();

                // 設定ファイルが存在するか確認
                if (File.Exists(AppSettingsFilePath))
                {
                    // 古いバージョンのsetting.jsonかどうかを確認
                    string json = File.ReadAllText(AppSettingsFilePath);
                    bool isOldFormat = json.Contains("\"IsTopmost\"") || json.Contains("\"CharacterList\"");

                    if (isOldFormat)
                    {
                        // 古いバージョンの設定を読み込んで変換
                        var oldSettings = LoadOldSettings();
                        if (oldSettings != null)
                        {
                            // デフォルト設定をベースに古い設定で上書き
                            MergeSettings(defaultSettings, oldSettings);

                            // 設定を適用
                            UpdateSettings(defaultSettings);

                            // 新しい形式で保存
                            SaveAppSettings();

                            Console.WriteLine("古い設定ファイルを新しい形式に変換しました");
                        }
                    }
                    else
                    {
                        // 通常の読み込み処理
                        var options = new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true,
                            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                        };
                        var settings = JsonSerializer.Deserialize<ConfigSettings>(json, options);

                        if (settings != null)
                        {
                            UpdateLlmModelFormat(settings);
                            UpdateSettings(settings);
                        }
                    }
                }
                else
                {
                    // 設定ファイルがない場合はデフォルト設定を適用して保存
                    UpdateSettings(defaultSettings);
                    SaveAppSettings();
                    Console.WriteLine($"デフォルト設定をファイルに保存しました: {AppSettingsFilePath}");
                }
            }
            catch (Exception ex)
            {
                ErrorHandlingService.Instance.LogError(
                    ErrorHandlingService.ErrorLevel.Error,
                    "アプリケーション設定ファイル読み込みエラー",
                    ex);
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
                    var defaultSettings = JsonSerializer.Deserialize<ConfigSettings>(json, options);

                    if (defaultSettings != null)
                    {
                        return defaultSettings;
                    }
                }
                catch (Exception ex)
                {
                    ErrorHandlingService.Instance.LogError(
                        ErrorHandlingService.ErrorLevel.Error,
                        "デフォルト設定ファイル読み込みエラー",
                        ex);
                }
            }

            // 読み込みに失敗した場合は空の設定を返す
            return new ConfigSettings();
        }        /// <summary>
                 /// 古いバージョンの設定ファイルを読み込む
                 /// </summary>
        private OldConfigSettings? LoadOldSettings()
        {
            try
            {
                string json = File.ReadAllText(AppSettingsFilePath);

                // 古いバージョンの設定ファイルかどうかを判断（大文字始まりのプロパティ名を持つ）
                if (json.Contains("\"IsTopmost\"") || json.Contains("\"CharacterList\""))
                {
                    var options = new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = false, // 大文字小文字を区別
                        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                    };
                    return JsonSerializer.Deserialize<OldConfigSettings>(json, options);
                }
            }
            catch (Exception ex)
            {
                ErrorHandlingService.Instance.LogError(
                    ErrorHandlingService.ErrorLevel.Error,
                    "古い設定ファイル読み込みエラー",
                    ex);
            }

            return null;
        }

        /// <summary>
        /// 古い設定をデフォルト設定にマージする
        /// </summary>
        private void MergeSettings(ConfigSettings defaultSettings, OldConfigSettings oldSettings)
        {
            try
            {
                // 基本設定をマージ
                defaultSettings.IsTopmost = oldSettings.IsTopmost;
                defaultSettings.IsEscapeCursor = oldSettings.IsEscapeCursor;
                defaultSettings.IsInputVirtualKey = oldSettings.IsInputVirtualKey;
                defaultSettings.VirtualKeyString = oldSettings.VirtualKeyString;
                defaultSettings.IsAutoMove = oldSettings.IsAutoMove;
                defaultSettings.IsEnableAmbientOcclusion = oldSettings.IsEnableAmbientOcclusion;
                defaultSettings.MsaaLevel = oldSettings.MSAALevel;
                defaultSettings.CharacterShadow = oldSettings.CharacterShadow;
                defaultSettings.CharacterShadowResolution = oldSettings.CharacterShadowResolution;
                defaultSettings.BackgroundShadow = oldSettings.BackgroundShadow;
                defaultSettings.BackgroundShadowResolution = oldSettings.BackgroundShadowResolution;
                defaultSettings.WindowSize = oldSettings.WindowSize;
                defaultSettings.CurrentCharacterIndex = oldSettings.CurrentCharacterIndex;

                // キャラクターリストの処理
                if (oldSettings.CharacterList != null && oldSettings.CharacterList.Count > 0)
                {
                    defaultSettings.CharacterList.Clear();

                    foreach (var oldChar in oldSettings.CharacterList)
                    {
                        var newChar = new CharacterSettings
                        {
                            IsReadOnly = oldChar.IsReadOnly,
                            ModelName = oldChar.ModelName,
                            VrmFilePath = oldChar.VRMFilePath,
                            IsUseLLM = oldChar.IsUseLLM,
                            ApiKey = oldChar.ApiKey,
                            LlmModel = oldChar.LLMModel,
                            SystemPrompt = oldChar.SystemPrompt,
                            IsUseTTS = oldChar.IsUseTTS,
                            TtsEndpointURL = oldChar.TTSEndpointURL,
                            TtsSperkerID = oldChar.TTSSperkerID
                        };

                        defaultSettings.CharacterList.Add(newChar);
                    }
                }

                // LLMモデル形式の更新
                UpdateLlmModelFormat(defaultSettings);
            }
            catch (Exception ex)
            {
                ErrorHandlingService.Instance.LogError(
                    ErrorHandlingService.ErrorLevel.Error,
                    "設定マージエラー",
                    ex);
            }
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
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping // 日本語などの非ASCII文字をエスケープせずに出力
                };
                string json = JsonSerializer.Serialize(settings, options);

                // ファイルに保存
                File.WriteAllText(AppSettingsFilePath, json);

                Console.WriteLine($"設定をファイルに保存しました: {AppSettingsFilePath}");
            }
            catch (Exception ex)
            {
                ErrorHandlingService.Instance.LogError(
                    ErrorHandlingService.ErrorLevel.Error,
                    "アプリケーション設定ファイル保存エラー",
                    ex);
            }
        }

        /// <summary>
        /// 全設定をファイルに保存
        /// </summary>
        public void SaveSettings()
        {
            SaveAppSettings();
        }

        /// <summary>
        /// </summary>
        /// <param name="settings">更新する設定</param>
        private void UpdateLlmModelFormat(ConfigSettings settings)
        {
            if (settings.CharacterList == null || settings.CharacterList.Count == 0)
            {
                return;
            }

            foreach (var character in settings.CharacterList)
            {
                if (!string.IsNullOrEmpty(character.LlmModel))
                {
                    if (character.LlmModel.StartsWith("gpt-"))
                    {
                        character.LlmModel = "openai/" + character.LlmModel;
                    }
                    else if (character.LlmModel.StartsWith("gemini-"))
                    {
                        character.LlmModel = "gemini/" + character.LlmModel;
                    }
                }
            }
        }
    }
}
