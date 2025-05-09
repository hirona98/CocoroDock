using CocoroDock.Communication;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;

namespace CocoroDock.Services
{
    /// <summary>
    /// アプリケーション設定を管理するクラス
    /// </summary>
    public class AppSettings
    {
        private static readonly Lazy<AppSettings> _instance = new Lazy<AppSettings>(() => new AppSettings());

        public static AppSettings Instance => _instance.Value;

        // 設定ファイルのパス
        private string SettingsFilePath => Path.Combine(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "",
            "ConnectSetting.json");

        // 接続設定
        public string WebSocketHost { get; set; } = "127.0.0.1";
        public int WebSocketPort { get; set; } = 55600;
        public string UserId { get; set; } = "user01";

        // 下位互換性のためのプロパティ
        public string WebSocketUrl
        {
            get => $"ws://{WebSocketHost}:{WebSocketPort}/";
            set
            {
                // ws://127.0.0.1:55600/ 形式から分解して設定
                if (Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) && uri != null)
                {
                    WebSocketHost = uri.Host;
                    WebSocketPort = uri.Port;
                }
            }
        }

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
                    VRMFilePath = "vrm_file_path",
                    IsUseLLM = false,
                    ApiKey = "your_api_key",
                    LLMModel = "gpt-3.5-turbo",
                    SystemPrompt = "あなたは親切なアシスタントです。",
                    IsUseTTS = false,
                    TTSEndpointURL = "http://localhost:50021",
                    TTSSperkerID = "1",
                }
            };
        }

        /// <summary>
        /// 設定値を更新
        /// </summary>
        /// <param name="config">サーバーから受信した設定値</param>
        public void UpdateSettings(ConfigSettings config)
        {
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
                // 設定ファイルが存在するか確認
                if (File.Exists(SettingsFilePath))
                {
                    // ファイルからJSONを読み込む
                    string json = File.ReadAllText(SettingsFilePath);

                    // JSONをデシリアライズ
                    var settings = JsonSerializer.Deserialize<ConnectionSettings>(json);

                    if (settings != null)
                    {
                        // 設定を適用
                        WebSocketHost = settings.WebSocketHost;
                        WebSocketPort = settings.WebSocketPort;
                        UserId = settings.UserId;

                        // 設定読み込み完了フラグを設定
                        IsLoaded = true;
                    }
                }
                else
                {
                    // 設定ファイルが存在しない場合は、デフォルト設定でファイルを作成
                    SaveSettings();
                }
            }
            catch (Exception ex)
            {
                // エラーが発生した場合はデフォルト設定を使用
                Console.WriteLine($"設定ファイル読み込みエラー: {ex.Message}");
            }
        }

        /// <summary>
        /// 設定をファイルに保存
        /// </summary>
        public void SaveSettings()
        {
            try
            {
                // 保存する設定オブジェクトを作成
                var settings = new ConnectionSettings
                {
                    WebSocketHost = WebSocketHost,
                    WebSocketPort = WebSocketPort,
                    UserId = UserId
                };

                // JSONにシリアライズ
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true // 整形されたJSONを出力
                };
                string json = JsonSerializer.Serialize(settings, options);

                // ファイルに保存
                File.WriteAllText(SettingsFilePath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"設定ファイル保存エラー: {ex.Message}");
            }
        }

        /// <summary>
        /// 接続設定クラス
        /// </summary>
        private class ConnectionSettings
        {
            public string WebSocketHost { get; set; } = "127.0.0.1";
            public int WebSocketPort { get; set; } = 55600;
            public string UserId { get; set; } = "user01";
        }
    }
}
