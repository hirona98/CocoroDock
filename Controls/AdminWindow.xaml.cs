using CocoroDock.Communication;
using CocoroDock.Services;
using CocoroDock.Utilities;
using CocoroDock.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;

namespace CocoroDock.Controls
{

    /// <summary>
    /// AdminWindow.xaml の相互作用ロジック
    /// </summary>
    public partial class AdminWindow : Window
    {
        // Display 設定は DisplaySettingsControl に委譲
        private Dictionary<string, object> _originalDisplaySettings = new Dictionary<string, object>();
        private List<CharacterSettings> _originalCharacterList = new List<CharacterSettings>();

        // 現在選択されているキャラクターのインデックス
        private int _currentCharacterIndex = 0;

        // 通信サービス
        private ICommunicationService? _communicationService;

        // CocoroCoreM再起動が必要な設定の前回値を保存
        private ConfigSettings _previousCocoroCoreMSettings;
        private Dictionary<int, string> _previousSystemPrompts = new Dictionary<int, string>();

        public bool IsClosed { get; private set; } = false;

        public AdminWindow() : this(null)
        {
        }

        public AdminWindow(ICommunicationService? communicationService)
        {
            InitializeComponent();

            _communicationService = communicationService;

            // Display タブ初期化
            DisplaySettingsControl.SetCommunicationService(_communicationService);
            DisplaySettingsControl.InitializeFromAppSettings();

            // キャラクター設定の初期化
            InitializeCharacterSettings();

            // MCPタブの初期化
            McpSettingsControl.Initialize();

            // システム設定コントロールを初期化
            _ = SystemSettingsControl.InitializeAsync();

            // システム設定変更イベントを登録
            SystemSettingsControl.SettingsChanged += (sender, args) =>
            {
                // 設定変更を即座に保存してメインウィンドウに反映
                SaveSystemSettings();
            };

            // 元の設定のバックアップを作成
            BackupSettings();

            // CocoroCoreM再起動チェック用に現在の設定のディープコピーを保存
            _previousCocoroCoreMSettings = AppSettings.Instance.GetConfigSettings().DeepCopy();

            // systemPrompt内容を保存
            SaveSystemPromptContents();
        }

        /// <summary>
        /// ウィンドウがロードされた後に呼び出されるイベントハンドラ
        /// </summary>
        protected override void OnSourceInitialized(System.EventArgs e)
        {
            base.OnSourceInitialized(e);
            // Owner設定後にメインサービスを初期化
            InitializeMainServices();
        }

        #region 初期化メソッド

        /// <summary>
        /// メインサービスの初期化
        /// </summary>
        private void InitializeMainServices()
        {
            // 通信サービスの取得（メインウィンドウから）
            if (Owner is MainWindow mainWindow &&
                typeof(MainWindow).GetField("_communicationService", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(mainWindow) is CommunicationService service)
            {
                _communicationService = service;
            }
            LoadLicenseText();
        }

        /// <summary>
        /// キャラクター設定の初期化
        /// </summary>
        private void InitializeCharacterSettings()
        {
            // CharacterManagementControlの初期化
            if (_communicationService != null)
            {
                CharacterManagementControl.SetCommunicationService(_communicationService);
            }
            CharacterManagementControl.Initialize();

            // キャラクター変更イベントを登録
            CharacterManagementControl.CharacterChanged += (sender, args) =>
            {
                // 現在のキャラクターインデックスを更新
                _currentCharacterIndex = CharacterManagementControl.GetCurrentCharacterIndex();

                // アニメーション設定を更新
                AnimationSettingsControl.Initialize();

                // Memory設定を更新
                InitializeMemorySettings();
            };

            // 現在のキャラクターインデックスを取得
            _currentCharacterIndex = CharacterManagementControl.GetCurrentCharacterIndex();

            // アニメーション設定コントロールを初期化
            if (_communicationService != null)
            {
                AnimationSettingsControl.SetCommunicationService(_communicationService);
            }
            AnimationSettingsControl.Initialize();

            // アニメーション設定変更イベントを登録
            AnimationSettingsControl.SettingsChanged += (sender, args) =>
            {
                // 設定変更の記録（必要に応じて処理を追加）
            };

            // MemorySettingsControlの初期化
            InitializeMemorySettings();
        }

        /// <summary>
        /// MemorySettingsControlを初期化する
        /// </summary>
        private void InitializeMemorySettings()
        {
            if (AppSettings.Instance.CharacterList != null &&
                _currentCharacterIndex >= 0 &&
                _currentCharacterIndex < AppSettings.Instance.CharacterList.Count)
            {
                var currentCharacter = AppSettings.Instance.CharacterList[_currentCharacterIndex];
                // 無理やり全キャラクター共通設定にしています
                MemorySettingsControl.LoadCharacterSettings(currentCharacter);
            }
        }

        // EscapePositionControl は DisplaySettingsControl 内で取り扱う

        /// <summary>
        /// 現在の設定をバックアップする
        /// </summary>
        private void BackupSettings()
        {
            // 表示設定のバックアップ
            DisplaySettingsControl.SaveToSnapshot();
            _originalDisplaySettings = DisplaySettingsControl.GetSnapshot();

            // キャラクターリストのバックアップ（Deep Copy）
            _originalCharacterList.Clear();
            foreach (var character in AppSettings.Instance.CharacterList)
            {
                _originalCharacterList.Add(DeepCopyCharacterSettings(character));
            }
        }

        #endregion

        #region 表示設定メソッド


        // System やその他設定の収集はこのまま AdminWindow 側で実施
        private Dictionary<string, object> CollectSystemAndMcpSettings()
        {
            var dict = new Dictionary<string, object>();
            dict["IsEnableWebService"] = SystemSettingsControl.GetIsEnableWebService();
            dict["IsEnableReminder"] = SystemSettingsControl.GetIsEnableReminder();
            dict["IsEnableNotificationApi"] = SystemSettingsControl.GetIsEnableNotificationApi();

            var screenshotSettings = SystemSettingsControl.GetScreenshotSettings();
            dict["ScreenshotEnabled"] = screenshotSettings.enabled;
            dict["ScreenshotInterval"] = screenshotSettings.intervalMinutes;
            dict["IdleTimeout"] = screenshotSettings.idleTimeoutMinutes;
            dict["CaptureActiveWindowOnly"] = screenshotSettings.captureActiveWindowOnly;
            dict["ExcludePatterns"] = screenshotSettings.excludePatterns;

            var microphoneSettings = SystemSettingsControl.GetMicrophoneSettings();
            dict["MicInputThreshold"] = microphoneSettings.inputThreshold;

            var CocoroCoreMSettings = SystemSettingsControl.GetCocoroCoreMSettings();
            dict["EnableProMode"] = CocoroCoreMSettings.enableProMode;
            dict["EnableInternetRetrieval"] = CocoroCoreMSettings.enableInternetRetrieval;
            dict["GoogleApiKey"] = CocoroCoreMSettings.googleApiKey;
            dict["GoogleSearchEngineId"] = CocoroCoreMSettings.googleSearchEngineId;
            dict["InternetMaxResults"] = CocoroCoreMSettings.internetMaxResults;

            // MCP 有効/無効
            dict["IsEnableMcp"] = McpSettingsControl.GetMcpEnabled();
            return dict;
        }

        /// <summary>
        /// システム設定を即座に保存してメインウィンドウに反映
        /// </summary>
        private void SaveSystemSettings()
        {
            try
            {
                // System/MCP の設定を収集
                var systemSnapshot = CollectSystemAndMcpSettings();

                // AppSettings に反映（System/MCP）
                ApplySystemSnapshotToAppSettings(systemSnapshot);

                // 設定をファイルに保存
                AppSettings.Instance.SaveAppSettings();

                System.Diagnostics.Debug.WriteLine("[AdminWindow] システム設定を即座に保存しました");
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AdminWindow] システム設定の保存中にエラーが発生しました: {ex.Message}");
            }
        }

        #endregion

        /// <summary>
        /// アニメーションチェックボックスのチェック時の処理
        /// </summary>
        private void AnimationCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox checkBox && checkBox.Tag is AnimationConfig animation)
            {
                animation.isEnabled = true;
            }
        }

        /// <summary>
        /// アニメーションチェックボックスのアンチェック時の処理
        /// </summary>
        private void AnimationCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox checkBox && checkBox.Tag is AnimationConfig animation)
            {
                animation.isEnabled = false;
            }
        }

        /// <summary>
        /// アニメーション再生ボタンクリック時の処理
        /// </summary>
        private async void PlayAnimationButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is AnimationConfig animation)
            {
                if (_communicationService != null)
                {
                    try
                    {
                        // CocoroShellにアニメーション再生指示を送信
                        await _communicationService.SendAnimationToShellAsync(animation.animationName);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"アニメーション再生エラー: {ex.Message}");
                        UIHelper.ShowError("アニメーション再生エラー", ex.Message);
                    }
                }
                else
                {
                    UIHelper.ShowError("通信エラー", "通信サービスが利用できません。");
                }
            }
        }

        /// <summary>
        /// キャラクター情報をUIに反映（CharacterManagementControlに移行済み）
        /// </summary>
        private void UpdateCharacterUI(int index)
        {
            // CharacterManagementControlに移行済み - このメソッドは使用されません
            return;
        }

        #region 共通ボタンイベントハンドラ
        /// <summary>
        /// OKボタンのクリックイベントハンドラ
        /// </summary>
        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 共通の設定保存処理を実行
                ApplySettingsChanges();

                // ウィンドウを閉じる
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"設定の保存中にエラーが発生しました: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// キャンセルボタンのクリックイベントハンドラ
        /// </summary>
        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            // CharacterManagementControlの削除予定リストをクリア
            CharacterManagementControl.ResetPendingChanges();

            // 変更を破棄して元の設定に戻す
            RestoreOriginalSettings();

            // ウィンドウを閉じる
            Close();
        }

        /// <summary>
        /// 適用ボタンのクリックイベントハンドラ
        /// </summary>
        private void ApplyButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 共通の設定保存処理を実行
                ApplySettingsChanges();

                // 設定のバックアップを更新（適用後の状態を新しいベースラインとする）
                BackupSettings();
                _previousCocoroCoreMSettings = AppSettings.Instance.GetConfigSettings().DeepCopy();

                // systemPrompt内容も更新
                SaveSystemPromptContents();

                // メインウィンドウのボタン状態とサービスを更新
                UpdateMainWindowStates();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"設定の保存中にエラーが発生しました: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 設定変更を適用する共通処理
        /// </summary>
        private void ApplySettingsChanges()
        {
            // CharacterManagementControlの設定確定処理を実行
            CharacterManagementControl.ConfirmSettings();

            // UI上の現在の設定を取得してCocoroCoreM再起動が必要かチェック
            var currentSettings = GetCurrentUISettings();
            bool needsCocoroCoreMRestart = HasCocoroCoreMRestartRequiredChanges(_previousCocoroCoreMSettings, currentSettings);

            // すべてのタブの設定を保存
            SaveAllSettings();

            // CocoroShellを再起動
            RestartCocoroShell();

            // CocoroCoreMの設定変更があった場合は再起動
            if (needsCocoroCoreMRestart)
            {
                _ = RestartCocoroCoreMAsync();
                Debug.WriteLine("CocoroCoreM再起動処理を実行しました");
            }
        }

        /// <summary>
        /// すべてのタブの設定を保存する
        /// </summary>
        private void SaveAllSettings()
        {
            try
            {
                // Display タブのスナップショットを更新
                DisplaySettingsControl.SaveToSnapshot();
                var displaySnapshot = DisplaySettingsControl.GetSnapshot();

                // System/MCP の設定を収集
                var systemSnapshot = CollectSystemAndMcpSettings();

                // AppSettings に反映（Display）
                DisplaySettingsControl.ApplySnapshotToAppSettings(displaySnapshot);

                // AppSettings に反映（System/MCP）
                ApplySystemSnapshotToAppSettings(systemSnapshot);

                // Character/Animation の反映
                UpdateCharacterAndAnimationAppSettings();

                // 設定をファイルに保存
                AppSettings.Instance.SaveAppSettings();

                // デスクトップウォッチの設定変更を反映
                UpdateDesktopWatchSettings();
            }
            catch (System.Exception ex)
            {
                MessageBox.Show($"設定の保存中にエラーが発生しました: {ex.Message}",
                    "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 元の設定に戻す（一設定などがあるためDisplayのみ復元が必要）
        /// </summary>
        private void RestoreOriginalSettings()
        {
            // Display の復元
            DisplaySettingsControl.ApplySnapshotToAppSettings(_originalDisplaySettings);
            DisplaySettingsControl.InitializeFromAppSettings();

            // キャラクターリストの復元
            AppSettings.Instance.CharacterList.Clear();
            foreach (var character in _originalCharacterList)
            {
                AppSettings.Instance.CharacterList.Add(DeepCopyCharacterSettings(character));
            }

            // CharacterManagementControlのUIを更新
            CharacterManagementControl.RefreshCharacterList();
        }

        #endregion

        #region 設定保存メソッド

        /// <summary>
        /// ウィンドウが閉じられる前に呼び出されるイベントハンドラ
        /// </summary>

        /// <summary>
        /// キャラクター設定のディープコピーを作成
        /// </summary>
        private CharacterSettings DeepCopyCharacterSettings(CharacterSettings source)
        {
            return new CharacterSettings
            {
                modelName = source.modelName,
                vrmFilePath = source.vrmFilePath,
                isUseLLM = source.isUseLLM,
                apiKey = source.apiKey,
                llmModel = source.llmModel,
                max_turns_window = source.max_turns_window,
                max_tokens = source.max_tokens,
                max_tokens_vision = source.max_tokens_vision,
                localLLMBaseUrl = source.localLLMBaseUrl,
                visionApiKey = source.visionApiKey,
                visionModel = source.visionModel,
                systemPromptFilePath = source.systemPromptFilePath,
                isUseTTS = source.isUseTTS,
                ttsType = source.ttsType,
                voicevoxConfig = new VoicevoxConfig
                {
                    endpointUrl = source.voicevoxConfig.endpointUrl,
                    speakerId = source.voicevoxConfig.speakerId,
                    speedScale = source.voicevoxConfig.speedScale,
                    pitchScale = source.voicevoxConfig.pitchScale,
                    intonationScale = source.voicevoxConfig.intonationScale,
                    volumeScale = source.voicevoxConfig.volumeScale,
                    prePhonemeLength = source.voicevoxConfig.prePhonemeLength,
                    postPhonemeLength = source.voicevoxConfig.postPhonemeLength,
                    outputSamplingRate = source.voicevoxConfig.outputSamplingRate,
                    outputStereo = source.voicevoxConfig.outputStereo
                },
                styleBertVits2Config = new StyleBertVits2Config
                {
                    endpointUrl = source.styleBertVits2Config.endpointUrl,
                    modelName = source.styleBertVits2Config.modelName,
                    modelId = source.styleBertVits2Config.modelId,
                    speakerName = source.styleBertVits2Config.speakerName,
                    speakerId = source.styleBertVits2Config.speakerId,
                    style = source.styleBertVits2Config.style,
                    styleWeight = source.styleBertVits2Config.styleWeight,
                    language = source.styleBertVits2Config.language,
                    sdpRatio = source.styleBertVits2Config.sdpRatio,
                    noise = source.styleBertVits2Config.noise,
                    noiseW = source.styleBertVits2Config.noiseW,
                    length = source.styleBertVits2Config.length,
                    autoSplit = source.styleBertVits2Config.autoSplit,
                    splitInterval = source.styleBertVits2Config.splitInterval,
                    assistText = source.styleBertVits2Config.assistText,
                    assistTextWeight = source.styleBertVits2Config.assistTextWeight,
                    referenceAudioPath = source.styleBertVits2Config.referenceAudioPath
                },
                aivisCloudConfig = new AivisCloudConfig
                {
                    apiKey = source.aivisCloudConfig.apiKey,
                    endpointUrl = source.aivisCloudConfig.endpointUrl,
                    modelUuid = source.aivisCloudConfig.modelUuid,
                    speakerUuid = source.aivisCloudConfig.speakerUuid,
                    styleId = source.aivisCloudConfig.styleId,
                    styleName = source.aivisCloudConfig.styleName,
                    useSSML = source.aivisCloudConfig.useSSML,
                    language = source.aivisCloudConfig.language,
                    speakingRate = source.aivisCloudConfig.speakingRate,
                    emotionalIntensity = source.aivisCloudConfig.emotionalIntensity,
                    tempoDynamics = source.aivisCloudConfig.tempoDynamics,
                    pitch = source.aivisCloudConfig.pitch,
                    volume = source.aivisCloudConfig.volume,
                    outputFormat = source.aivisCloudConfig.outputFormat,
                    outputBitrate = source.aivisCloudConfig.outputBitrate,
                    outputSamplingRate = source.aivisCloudConfig.outputSamplingRate,
                    outputAudioChannels = source.aivisCloudConfig.outputAudioChannels
                },
                isEnableMemory = source.isEnableMemory,
                memoryId = source.memoryId,
                embeddedApiKey = source.embeddedApiKey,
                embeddedModel = source.embeddedModel,
                embeddedDimension = source.embeddedDimension,
                embeddedBaseUrl = source.embeddedBaseUrl,
                isUseSTT = source.isUseSTT,
                sttEngine = source.sttEngine,
                sttWakeWord = source.sttWakeWord,
                sttApiKey = source.sttApiKey,
                sttLanguage = source.sttLanguage,
                isConvertMToon = source.isConvertMToon,
                isEnableShadowOff = source.isEnableShadowOff,
                shadowOffMesh = source.shadowOffMesh,
                isReadOnly = source.isReadOnly
            };
        }

        protected override void OnClosed(EventArgs e)
        {
            // MCPタブのViewModelを破棄
            McpSettingsControl.GetViewModel()?.Dispose();
            IsClosed = true;
            base.OnClosed(e);
        }

        /// <summary>
        /// ボタンの有効/無効状態を設定する
        /// </summary>
        /// <param name="enabled">有効にするかどうか</param>
        private void SetButtonsEnabled(bool enabled)
        {
            OkButton.IsEnabled = enabled;
            ApplyButton.IsEnabled = enabled;
            CancelButton.IsEnabled = enabled;
        }

        /// <summary>
        /// 表示設定を保存する
        /// </summary>
        // Display タブ以外の設定を AppSettings に適用
        private void ApplySystemSnapshotToAppSettings(Dictionary<string, object> snapshot)
        {
            var appSettings = AppSettings.Instance;
            appSettings.IsEnableWebService = (bool)snapshot["IsEnableWebService"];
            appSettings.IsEnableReminder = (bool)snapshot["IsEnableReminder"];
            appSettings.IsEnableNotificationApi = (bool)snapshot["IsEnableNotificationApi"];
            appSettings.IsEnableMcp = (bool)snapshot["IsEnableMcp"];

            appSettings.ScreenshotSettings.enabled = (bool)snapshot["ScreenshotEnabled"];
            appSettings.ScreenshotSettings.intervalMinutes = (int)snapshot["ScreenshotInterval"];
            appSettings.ScreenshotSettings.idleTimeoutMinutes = (int)snapshot["IdleTimeout"];
            appSettings.ScreenshotSettings.captureActiveWindowOnly = (bool)snapshot["CaptureActiveWindowOnly"];
            appSettings.ScreenshotSettings.excludePatterns = (List<string>)snapshot["ExcludePatterns"];

            appSettings.MicrophoneSettings.inputThreshold = (int)snapshot["MicInputThreshold"];

            appSettings.EnableProMode = (bool)snapshot["EnableProMode"];
            appSettings.EnableInternetRetrieval = (bool)snapshot["EnableInternetRetrieval"];
            appSettings.GoogleApiKey = (string)snapshot["GoogleApiKey"];
            appSettings.GoogleSearchEngineId = (string)snapshot["GoogleSearchEngineId"];
            appSettings.InternetMaxResults = (int)snapshot["InternetMaxResults"];

            // MCPタブのViewModelにも反映
            McpSettingsControl.SetMcpEnabled(appSettings.IsEnableMcp);
        }

        /// <summary>
        /// AppSettingsを更新する
        /// </summary>
        private void UpdateCharacterAndAnimationAppSettings()
        {
            var appSettings = AppSettings.Instance;
            appSettings.CurrentCharacterIndex = CharacterManagementControl.GetCurrentCharacterIndex();

            var currentCharacterSetting = CharacterManagementControl.GetCurrentCharacterSettingFromUI();
            if (currentCharacterSetting != null)
            {
                var currentIndex = CharacterManagementControl.GetCurrentCharacterIndex();
                if (currentIndex >= 0 &&
                    currentIndex < appSettings.CharacterList.Count)
                {
                    // 現在のキャラクターの設定を更新
                    appSettings.CharacterList[currentIndex] = currentCharacterSetting;

                    // Memory設定を現在のキャラクターから取得
                    MemorySettingsControl.SaveToCharacterSettings(currentCharacterSetting);

                    // 全キャラクターに記憶用設定を適用
                    string embeddedApiKey = currentCharacterSetting.embeddedApiKey;
                    string embeddedModel = currentCharacterSetting.embeddedModel;
                    string embeddedDimension = currentCharacterSetting.embeddedDimension;
                    string embeddedBaseUrl = currentCharacterSetting.embeddedBaseUrl;

                    foreach (var character in appSettings.CharacterList)
                    {
                        character.embeddedApiKey = embeddedApiKey;
                        character.embeddedModel = embeddedModel;
                        character.embeddedDimension = embeddedDimension;
                        character.embeddedBaseUrl = embeddedBaseUrl;
                    }
                }
            }

            appSettings.CurrentAnimationSettingIndex = AnimationSettingsControl.GetCurrentAnimationSettingIndex();
            appSettings.AnimationSettings = AnimationSettingsControl.GetAnimationSettings();
        }

        #endregion

        #region VRMファイル選択イベントハンドラ

        /// <summary>
        /// VRMファイル参照ボタンのクリックイベント
        /// </summary>
        private void BrowseVrmFileButton_Click(object sender, RoutedEventArgs e)
        {
            // ファイルダイアログの設定
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "VRMファイルを選択",
                Filter = "VRMファイル (*.vrm)|*.vrm|すべてのファイル (*.*)|*.*",
                CheckFileExists = true,
                Multiselect = false
            };
        }

        #endregion

        private void LoadLicenseText()
        {
            try
            {
                // 埋め込みリソースからライセンステキストを読み込む
                var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                var resourceName = "CocoroDock.Resource.License.txt";

                using (var stream = assembly.GetManifestResourceStream(resourceName))
                {
                    if (stream != null)
                    {
                        using (var reader = new System.IO.StreamReader(stream))
                        {
                            string licenseText = reader.ReadToEnd();
                            LicenseTextBox.Text = licenseText;
                        }
                    }
                    else
                    {
                        // リソースが見つからない場合
                        LicenseTextBox.Text = "ライセンスリソースが見つかりませんでした。";
                    }
                }
            }
            catch (Exception ex)
            {
                // エラーが発生した場合
                LicenseTextBox.Text = $"ライセンスリソースの読み込み中にエラーが発生しました: {ex.Message}";
            }
        }

        /// <summary>
        /// ハイパーリンクをクリックしたときにブラウザで開く
        /// </summary>
        private void Hyperlink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = e.Uri.AbsoluteUri,
                    UseShellExecute = true
                });
                e.Handled = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"URLを開けませんでした: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// デスクトップウォッチ設定の変更を適用
        /// </summary>
        private void UpdateDesktopWatchSettings()
        {
            try
            {
                // MainWindowのインスタンスを取得
                var mainWindow = Application.Current.MainWindow as MainWindow;
                if (mainWindow != null)
                {
                    // MainWindowのUpdateScreenshotServiceメソッドを呼び出す
                    var updateMethod = mainWindow.GetType().GetMethod("UpdateScreenshotService",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                    if (updateMethod != null)
                    {
                        updateMethod.Invoke(mainWindow, null);
                        Debug.WriteLine("デスクトップウォッチ設定を更新しました");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"デスクトップウォッチ設定の更新中にエラーが発生しました: {ex.Message}");
            }
        }

        /// <summary>
        /// メインウィンドウのボタン状態とサービスを更新
        /// </summary>
        private void UpdateMainWindowStates()
        {
            try
            {
                // MainWindowのインスタンスを取得
                var mainWindow = Application.Current.MainWindow as MainWindow;
                if (mainWindow != null)
                {
                    // InitializeButtonStatesメソッドを呼び出してボタン状態を更新
                    var initButtonMethod = mainWindow.GetType().GetMethod("InitializeButtonStates",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                    if (initButtonMethod != null)
                    {
                        initButtonMethod.Invoke(mainWindow, null);
                        Debug.WriteLine("[AdminWindow] メインウィンドウのボタン状態を更新しました");
                    }

                    // ApplySettingsメソッドを呼び出してサービスを更新
                    var applyMethod = mainWindow.GetType().GetMethod("ApplySettings",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                    if (applyMethod != null)
                    {
                        applyMethod.Invoke(mainWindow, null);
                        Debug.WriteLine("[AdminWindow] メインウィンドウのサービスを更新しました");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AdminWindow] メインウィンドウの状態更新中にエラーが発生しました: {ex.Message}");
            }
        }

        /// <summary>
        /// ログ表示ボタンのクリックイベント
        /// </summary>
        private void LogViewerButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 通信サービスからログビューアーを開く
                _communicationService?.OpenLogViewer();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"ログビューアーの起動に失敗しました: {ex.Message}",
                               "エラー",
                               MessageBoxButton.OK,
                               MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// CocoroCoreMを再起動する
        /// </summary>
        private async Task RestartCocoroCoreMAsync()
        {
            try
            {
                // MainWindowのインスタンスを取得
                var mainWindow = Application.Current.MainWindow as MainWindow;
                if (mainWindow != null)
                {
                    // MainWindowのLaunchCocoroCoreMAsyncメソッドを呼び出してCocoroCoreMを再起動
                    var launchMethod = mainWindow.GetType().GetMethod("LaunchCocoroCoreMAsync",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                    if (launchMethod != null)
                    {
                        // ProcessOperation.RestartIfRunning を指定してCocoroCoreMを再起動（非同期）
                        var taskResult =
                            launchMethod.Invoke(mainWindow, new object[] { ProcessOperation.RestartIfRunning });
                        Debug.WriteLine("CocoroCoreMを再起動要求をしました");

                        // 非同期で再起動処理を待機
                        if (taskResult is Task task)
                        {
                            await task;
                        }

                        // 再起動完了を待機
                        await WaitForCocoroCoreMRestartAsync();
                        Debug.WriteLine("CocoroCoreMの再起動が完了しました");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"CocoroCoreM再起動中にエラーが発生しました: {ex.Message}");
                throw new Exception($"CocoroCoreMの再起動に失敗しました: {ex.Message}");
            }
        }

        /// <summary>
        /// CocoroCoreMの再起動完了を待機
        /// </summary>
        private async Task WaitForCocoroCoreMRestartAsync()
        {
            var delay = TimeSpan.FromSeconds(1);
            var maxWaitTime = TimeSpan.FromSeconds(120);
            var startTime = DateTime.Now;

            bool hasBeenDisconnected = false;

            while (DateTime.Now - startTime < maxWaitTime)
            {
                try
                {
                    if (_communicationService != null)
                    {
                        var currentStatus = _communicationService.CurrentStatus;

                        // まず停止（起動待ち）状態になることを確認
                        if (!hasBeenDisconnected)
                        {
                            if (currentStatus == CocoroCoreMStatus.WaitingForStartup)
                            {
                                hasBeenDisconnected = true;
                                Debug.WriteLine("CocoroCoreM停止を確認（起動待ち）");
                            }
                        }
                        // 停止を確認済みの場合、再起動完了を待機
                        else
                        {
                            if (currentStatus == CocoroCoreMStatus.Normal ||
                                currentStatus == CocoroCoreMStatus.ProcessingMessage ||
                                currentStatus == CocoroCoreMStatus.ProcessingImage)
                            {
                                Debug.WriteLine("CocoroCoreM再起動完了");
                                return;
                            }
                        }
                    }
                }
                catch
                {
                    // API未応答時は継続してチェック
                }
                await Task.Delay(delay);
            }

            throw new TimeoutException("CocoroCoreMの再起動がタイムアウトしました");
        }

        /// <summary>
        /// UI上の現在の設定を取得する（ディープコピー）
        /// </summary>
        /// <returns>現在のUI設定から構築したConfigSettings</returns>
        private ConfigSettings GetCurrentUISettings()
        {
            // 現在の設定のディープコピーを作成
            var config = AppSettings.Instance.GetConfigSettings().DeepCopy();

            // System設定の取得
            config.isEnableNotificationApi = SystemSettingsControl.GetIsEnableNotificationApi();
            config.isEnableReminder = SystemSettingsControl.GetIsEnableReminder();
            config.isEnableMcp = McpSettingsControl.GetMcpEnabled();

            var CocoroCoreMSettings = SystemSettingsControl.GetCocoroCoreMSettings();
            config.enable_pro_mode = CocoroCoreMSettings.enableProMode;
            config.enable_internet_retrieval = CocoroCoreMSettings.enableInternetRetrieval;
            config.googleApiKey = CocoroCoreMSettings.googleApiKey;
            config.googleSearchEngineId = CocoroCoreMSettings.googleSearchEngineId;
            config.internetMaxResults = CocoroCoreMSettings.internetMaxResults;

            // Character設定の取得（ディープコピーを使用）
            config.currentCharacterIndex = CharacterManagementControl.GetCurrentCharacterIndex();
            var currentCharacterSetting = CharacterManagementControl.GetCurrentCharacterSettingFromUI();
            if (currentCharacterSetting != null)
            {
                if (config.currentCharacterIndex >= 0 && config.currentCharacterIndex < config.characterList.Count)
                {
                    config.characterList[config.currentCharacterIndex] = currentCharacterSetting;
                }
            }

            return config;
        }

        /// <summary>
        /// CocoroCoreM再起動が必要な設定項目が変更されたかどうかをチェック
        /// </summary>
        /// <param name="previousSettings">以前の設定</param>
        /// <param name="currentSettings">現在の設定</param>
        /// <returns>CocoroCoreM再起動が必要な変更があった場合true</returns>
        private bool HasCocoroCoreMRestartRequiredChanges(ConfigSettings previousSettings, ConfigSettings currentSettings)
        {
            // 基本設定項目の比較
            if (currentSettings.isEnableNotificationApi != previousSettings.isEnableNotificationApi ||
                currentSettings.isEnableReminder != previousSettings.isEnableReminder ||
                currentSettings.isEnableMcp != previousSettings.isEnableMcp ||
                currentSettings.enable_pro_mode != previousSettings.enable_pro_mode ||
                currentSettings.enable_internet_retrieval != previousSettings.enable_internet_retrieval ||
                currentSettings.googleApiKey != previousSettings.googleApiKey ||
                currentSettings.googleSearchEngineId != previousSettings.googleSearchEngineId ||
                currentSettings.internetMaxResults != previousSettings.internetMaxResults ||
                currentSettings.currentCharacterIndex != previousSettings.currentCharacterIndex)
            {
                return true;
            }

            // キャラクターリストの比較
            if (currentSettings.characterList.Count != previousSettings.characterList.Count)
            {
                return true;
            }

            // キャラクターの比較（追加して削除して…とかやるとNGだけど…）
            for (int i = 0; i < currentSettings.characterList.Count; i++)
            {
                var current = currentSettings.characterList[i];
                var previous = previousSettings.characterList[i];

                if (current.isUseLLM != previous.isUseLLM ||
                    current.apiKey != previous.apiKey ||
                    current.llmModel != previous.llmModel ||
                    current.max_turns_window != previous.max_turns_window ||
                    current.max_tokens != previous.max_tokens ||
                    current.max_tokens_vision != previous.max_tokens_vision ||
                    current.visionApiKey != previous.visionApiKey ||
                    current.visionModel != previous.visionModel ||
                    current.localLLMBaseUrl != previous.localLLMBaseUrl ||
                    current.systemPromptFilePath != previous.systemPromptFilePath ||
                    current.isEnableMemory != previous.isEnableMemory ||
                    current.memoryId != previous.memoryId ||
                    current.embeddedApiKey != previous.embeddedApiKey ||
                    current.embeddedModel != previous.embeddedModel ||
                    current.embeddedDimension != previous.embeddedDimension ||
                    current.embeddedBaseUrl != previous.embeddedBaseUrl)
                {
                    return true;
                }

                // systemPromptのテキスト内容変更チェック
                if (CharacterManagementControl != null)
                {
                    var currentPrompts = CharacterManagementControl.GetCurrentSystemPrompts();
                    var currentPrompt = currentPrompts.ContainsKey(i) ? currentPrompts[i] : string.Empty;
                    var previousPrompt = _previousSystemPrompts.ContainsKey(i) ? _previousSystemPrompts[i] : string.Empty;

                    if (currentPrompt != previousPrompt)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private void SaveSystemPromptContents()
        {
            _previousSystemPrompts.Clear();

            if (CharacterManagementControl != null)
            {
                var currentPrompts = CharacterManagementControl.GetCurrentSystemPrompts();
                foreach (var kvp in currentPrompts)
                {
                    _previousSystemPrompts[kvp.Key] = kvp.Value;
                }
            }
        }

        /// <summary>
        /// CocoroShellを再起動する
        /// </summary>
        private void RestartCocoroShell()
        {
            try
            {
                // MainWindowのインスタンスを取得
                var mainWindow = Application.Current.MainWindow as MainWindow;
                if (mainWindow != null)
                {
                    // MainWindowのLaunchCocoroShellメソッドを呼び出してCocoroShellを再起動
                    var launchMethod = mainWindow.GetType().GetMethod("LaunchCocoroShell",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                    if (launchMethod != null)
                    {
                        // ProcessOperation.RestartIfRunning を指定してCocoroShellを再起動
                        launchMethod.Invoke(mainWindow, [ProcessOperation.RestartIfRunning]);
                        Debug.WriteLine("CocoroShellを再起動しました");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"CocoroShell再起動中にエラーが発生しました: {ex.Message}");
                MessageBox.Show($"CocoroShellの再起動に失敗しました: {ex.Message}",
                               "警告",
                               MessageBoxButton.OK,
                               MessageBoxImage.Warning);
            }
        }
    }
}
