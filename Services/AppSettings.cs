using CocoroDock.Communication;
using CocoroDock.Utilities;
using System;
using System.Collections.Generic;
using System.Diagnostics;
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
    public class AppSettings : IAppSettings
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
                    isReadOnly = false,
                    modelName = "model_name",
                    vrmFilePath = "vrm_file_path",
                    isUseLLM = false,
                    apiKey = "your_api_key",
                    llmModel = "gpt-3.5-turbo",
                    systemPrompt = "あなたは親切なアシスタントです。",
                    isUseTTS = false,
                    ttsEndpointURL = "http://localhost:50021",
                    ttsSperkerID = "1",
                    userId = "User01"
                }
            };
        }

        /// <summary>
        /// 設定値を更新
        /// </summary>
        /// <param name="config">サーバーから受信した設定値</param>
        public void UpdateSettings(ConfigSettings config)
        {
            CocoroDockPort = config.cocoroDockPort;
            CocoroCorePort = config.cocoroCorePort;
            IsTopmost = config.isTopmost;
            IsEscapeCursor = config.isEscapeCursor;
            IsInputVirtualKey = config.isInputVirtualKey;
            VirtualKeyString = config.virtualKeyString;
            IsAutoMove = config.isAutoMove;
            IsEnableAmbientOcclusion = config.isEnableAmbientOcclusion;
            MsaaLevel = config.msaaLevel;
            CharacterShadow = config.characterShadow;
            CharacterShadowResolution = config.characterShadowResolution;
            BackgroundShadow = config.backgroundShadow;
            BackgroundShadowResolution = config.backgroundShadowResolution;
            WindowSize = config.windowSize > 0 ? (int)config.windowSize : 650;
            CurrentCharacterIndex = config.currentCharacterIndex;

            // キャラクターリストを更新（もし受信したリストが空でなければ）
            if (config.characterList != null && config.characterList.Count > 0)
            {
                CharacterList = new List<CharacterSettings>(config.characterList);
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
                isTopmost = IsTopmost,
                isEscapeCursor = IsEscapeCursor,
                isInputVirtualKey = IsInputVirtualKey,
                virtualKeyString = VirtualKeyString,
                isAutoMove = IsAutoMove,
                isEnableAmbientOcclusion = IsEnableAmbientOcclusion,
                msaaLevel = MsaaLevel,
                characterShadow = CharacterShadow,
                characterShadowResolution = CharacterShadowResolution,
                backgroundShadow = BackgroundShadow,
                backgroundShadowResolution = BackgroundShadowResolution,
                windowSize = WindowSize,
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
                Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "",
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
            string json = File.ReadAllText(AppSettingsFilePath);
            bool isOldFormat = json.Contains("\"IsTopmost\"") || json.Contains("\"CharacterList\"");

            if (isOldFormat)
            {
                ProcessOldFormatSettings(defaultSettings);
            }
            else
            {
                ProcessCurrentFormatSettings(json);
            }
        }

        /// <summary>
        /// 古いフォーマットの設定ファイルを処理する
        /// </summary>
        private void ProcessOldFormatSettings(ConfigSettings defaultSettings)
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

                Debug.WriteLine("古い設定ファイルを新しい形式に変換しました");
            }
        }

        /// <summary>
        /// 現在のフォーマットの設定ファイルを処理する
        /// </summary>
        private void ProcessCurrentFormatSettings(string json)
        {
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
            var settings = MessageHelper.DeserializeFromJson<ConfigSettings>(json);

            if (settings != null)
            {
                UpdateLlmModelFormat(settings);
                UpdateSettings(settings);
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
                Debug.WriteLine($"古い設定ファイル読み込みエラー: {ex.Message}");
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
                defaultSettings.isTopmost = oldSettings.IsTopmost;
                defaultSettings.isEscapeCursor = oldSettings.IsEscapeCursor;
                defaultSettings.isInputVirtualKey = oldSettings.IsInputVirtualKey;
                defaultSettings.virtualKeyString = oldSettings.VirtualKeyString;
                defaultSettings.isAutoMove = oldSettings.IsAutoMove;
                defaultSettings.isEnableAmbientOcclusion = oldSettings.IsEnableAmbientOcclusion;
                defaultSettings.msaaLevel = oldSettings.MSAALevel;
                defaultSettings.characterShadow = oldSettings.CharacterShadow;
                defaultSettings.characterShadowResolution = oldSettings.CharacterShadowResolution;
                defaultSettings.backgroundShadow = oldSettings.BackgroundShadow;
                defaultSettings.backgroundShadowResolution = oldSettings.BackgroundShadowResolution;
                defaultSettings.windowSize = oldSettings.WindowSize;
                defaultSettings.currentCharacterIndex = oldSettings.CurrentCharacterIndex;

                // キャラクターリストの処理
                ConvertCharacterList(defaultSettings, oldSettings);

                // LLMモデル形式の更新
                UpdateLlmModelFormat(defaultSettings);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"設定マージエラー: {ex.Message}");
            }
        }

        /// <summary>
        /// 古いキャラクターリストを新しい形式に変換
        /// </summary>
        private void ConvertCharacterList(ConfigSettings defaultSettings, OldConfigSettings oldSettings)
        {
            if (oldSettings.CharacterList != null && oldSettings.CharacterList.Count > 0)
            {
                defaultSettings.characterList.Clear();

                foreach (var oldChar in oldSettings.CharacterList)
                {
                    var newChar = new CharacterSettings
                    {
                        isReadOnly = oldChar.IsReadOnly,
                        modelName = oldChar.ModelName,
                        vrmFilePath = oldChar.VRMFilePath,
                        isUseLLM = oldChar.IsUseLLM,
                        apiKey = oldChar.ApiKey,
                        llmModel = oldChar.LLMModel,
                        systemPrompt = oldChar.SystemPrompt,
                        isUseTTS = oldChar.IsUseTTS,
                        ttsEndpointURL = oldChar.TTSEndpointURL,
                        ttsSperkerID = oldChar.TTSSperkerID
                    };

                    defaultSettings.characterList.Add(newChar);
                }
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

        /// <summary>
        /// LLMモデル形式を更新する
        /// </summary>
        /// <param name="settings">更新する設定</param>
        private void UpdateLlmModelFormat(ConfigSettings settings)
        {
            if (settings.characterList == null || settings.characterList.Count == 0)
            {
                return;
            }

            foreach (var character in settings.characterList)
            {
                if (!string.IsNullOrEmpty(character.llmModel))
                {
                    if (character.llmModel.StartsWith("gpt-") && !character.llmModel.Contains("/"))
                    {
                        character.llmModel = "openai/" + character.llmModel;
                    }
                    else if (character.llmModel.StartsWith("gemini-") && !character.llmModel.Contains("/"))
                    {
                        character.llmModel = "gemini/" + character.llmModel;
                    }
                }
            }
        }
    }
}
