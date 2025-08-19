using CocoroDock.Communication;
using CocoroDock.Services;
using CocoroDock.Utilities;
using CocoroDock.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
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

        // 現在選択されているキャラクターのインデックス
        private int _currentCharacterIndex = 0;

        // 通信サービス
        private ICommunicationService? _communicationService;

        // CocoroCore2再起動が必要な設定の前回値を保存
        private ConfigSettings _previousCocoroCore2Settings;

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
            SystemSettingsControl.Initialize();

            // システム設定変更イベントを登録
            SystemSettingsControl.SettingsChanged += (sender, args) =>
            {
                // 設定変更の記録（必要に応じて処理を追加）
            };

            // 元の設定のバックアップを作成
            BackupSettings();

            // CocoroCore2再起動チェック用に現在の設定を保存
            _previousCocoroCore2Settings = AppSettings.Instance.GetConfigSettings();
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
        }

        #endregion

        #region 表示設定メソッド


        // System やその他設定の収集はこのまま AdminWindow 側で実施
        private Dictionary<string, object> CollectSystemAndMcpSettings()
        {
            var dict = new Dictionary<string, object>();
            dict["IsEnableNotificationApi"] = SystemSettingsControl.GetIsEnableNotificationApi();

            var screenshotSettings = SystemSettingsControl.GetScreenshotSettings();
            dict["ScreenshotEnabled"] = screenshotSettings.enabled;
            dict["ScreenshotInterval"] = screenshotSettings.intervalMinutes;
            dict["IdleTimeout"] = screenshotSettings.idleTimeoutMinutes;
            dict["CaptureActiveWindowOnly"] = screenshotSettings.captureActiveWindowOnly;

            var microphoneSettings = SystemSettingsControl.GetMicrophoneSettings();
            dict["MicInputThreshold"] = microphoneSettings.inputThreshold;

            var cocoroCore2Settings = SystemSettingsControl.GetCocoroCore2Settings();
            dict["EnableProMode"] = cocoroCore2Settings.enableProMode;
            dict["EnableInternetRetrieval"] = cocoroCore2Settings.enableInternetRetrieval;
            dict["GoogleApiKey"] = cocoroCore2Settings.googleApiKey;
            dict["GoogleSearchEngineId"] = cocoroCore2Settings.googleSearchEngineId;
            dict["InternetMaxResults"] = cocoroCore2Settings.internetMaxResults;

            // MCP 有効/無効
            dict["IsEnableMcp"] = McpSettingsControl.GetMcpEnabled();
            return dict;
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
                // CharacterManagementControlの設定確定処理を実行
                CharacterManagementControl.ConfirmSettings();

                // UI上の現在の設定を取得してCocoroCore2再起動が必要かチェック
                var currentSettings = GetCurrentUISettings();
                bool needsCocoroCore2Restart = HasCocoroCore2RestartRequiredChanges(_previousCocoroCore2Settings, currentSettings);

                // すべてのタブの設定を保存
                SaveAllSettings();

                // CocoroShellを再起動
                RestartCocoroShell();

                // CocoroCore2の設定変更があった場合は通知
                if (needsCocoroCore2Restart)
                {
                    ShowCocoroCore2RestartNotificationDialog();
                }

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
                // CharacterManagementControlの設定確定処理を実行
                CharacterManagementControl.ConfirmSettings();

                // UI上の現在の設定を取得してCocoroCore2再起動が必要かチェック
                var currentSettings = GetCurrentUISettings();
                bool needsCocoroCore2Restart = HasCocoroCore2RestartRequiredChanges(_previousCocoroCore2Settings, currentSettings);

                // すべてのタブの設定を保存
                SaveAllSettings();

                // CocoroShellを再起動
                RestartCocoroShell();

                // CocoroCore2の設定変更があった場合は通知
                if (needsCocoroCore2Restart)
                {
                    ShowCocoroCore2RestartNotificationDialog();
                }

                // 設定のバックアップを更新（適用後の状態を新しいベースラインとする）
                BackupSettings();
                _previousCocoroCore2Settings = AppSettings.Instance.GetConfigSettings();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"設定の保存中にエラーが発生しました: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
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
        }

        #endregion

        #region 設定保存メソッド

        /// <summary>
        /// ウィンドウが閉じられる前に呼び出されるイベントハンドラ
        /// </summary>
        protected override void OnClosed(EventArgs e)
        {
            // MCPタブのViewModelを破棄
            McpSettingsControl.GetViewModel()?.Dispose();
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
        /// 再起動が必要な場合にダイアログで通知
        /// </summary>
        /// <param name="needsCocoroCore2Restart">CocoroCore2の再起動が必要か</param>
        private void ShowCocoroCore2RestartNotificationDialog()
        {
            MessageBox.Show("VRMモデル以外の設定は次回起動時に有効になります",
                          "設定変更完了",
                          MessageBoxButton.OK,
                          MessageBoxImage.Information);
        }

        /// <summary>
        /// 表示設定を保存する
        /// </summary>
        // Display タブ以外の設定を AppSettings に適用
        private void ApplySystemSnapshotToAppSettings(Dictionary<string, object> snapshot)
        {
            var appSettings = AppSettings.Instance;
            appSettings.IsEnableNotificationApi = (bool)snapshot["IsEnableNotificationApi"];
            appSettings.IsEnableMcp = (bool)snapshot["IsEnableMcp"];

            appSettings.ScreenshotSettings.enabled = (bool)snapshot["ScreenshotEnabled"];
            appSettings.ScreenshotSettings.intervalMinutes = (int)snapshot["ScreenshotInterval"];
            appSettings.ScreenshotSettings.idleTimeoutMinutes = (int)snapshot["IdleTimeout"];
            appSettings.ScreenshotSettings.captureActiveWindowOnly = (bool)snapshot["CaptureActiveWindowOnly"];

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

            var currentCharacterSetting = CharacterManagementControl.GetCurrentCharacterSetting();
            if (currentCharacterSetting != null)
            {
                var currentIndex = CharacterManagementControl.GetCurrentCharacterIndex();
                if (currentIndex >= 0 && currentIndex < appSettings.CharacterList.Count)
                {
                    appSettings.CharacterList[currentIndex] = currentCharacterSetting;
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
        /// CocoroCore2を再起動する
        /// </summary>
        private async Task RestartCocoroCore2Async()
        {
            try
            {
                // MainWindowのインスタンスを取得
                var mainWindow = Application.Current.MainWindow as MainWindow;
                if (mainWindow != null)
                {
                    // MainWindowのLaunchCocoroCore2メソッドを呼び出してCocoroCore2を再起動
                    var launchMethod = mainWindow.GetType().GetMethod("LaunchCocoroCore2",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                    if (launchMethod != null)
                    {
                        // ProcessOperation.RestartIfRunning を指定してCocoroCore2を再起動
                        launchMethod.Invoke(mainWindow, new object[] { ProcessOperation.RestartIfRunning });
                        Debug.WriteLine("CocoroCore2を再起動要求をしました");

                        // 再起動完了を待機
                        await WaitForCocoroCore2RestartAsync();
                        Debug.WriteLine("CocoroCore2の再起動が完了しました");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"CocoroCore2再起動中にエラーが発生しました: {ex.Message}");
                throw new Exception($"CocoroCore2の再起動に失敗しました: {ex.Message}");
            }
        }

        /// <summary>
        /// CocoroCore2の再起動完了を待機
        /// </summary>
        private async Task WaitForCocoroCore2RestartAsync()
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
                            if (currentStatus == CocoroCore2Status.WaitingForStartup)
                            {
                                hasBeenDisconnected = true;
                                Debug.WriteLine("CocoroCore2停止を確認（起動待ち）");
                            }
                        }
                        // 停止を確認済みの場合、再起動完了を待機
                        else
                        {
                            if (currentStatus == CocoroCore2Status.Normal ||
                                currentStatus == CocoroCore2Status.ProcessingMessage ||
                                currentStatus == CocoroCore2Status.ProcessingImage)
                            {
                                Debug.WriteLine("CocoroCore2再起動完了");
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

            throw new TimeoutException("CocoroCore2の再起動がタイムアウトしました");
        }

        /// <summary>
        /// UI上の現在の設定を取得する
        /// </summary>
        /// <returns>現在のUI設定から構築したConfigSettings</returns>
        private ConfigSettings GetCurrentUISettings()
        {
            var config = new ConfigSettings();

            // System設定の取得
            config.isEnableNotificationApi = SystemSettingsControl.GetIsEnableNotificationApi();
            config.isEnableMcp = McpSettingsControl.GetMcpEnabled();

            var cocoroCore2Settings = SystemSettingsControl.GetCocoroCore2Settings();
            config.enable_pro_mode = cocoroCore2Settings.enableProMode;
            config.enable_internet_retrieval = cocoroCore2Settings.enableInternetRetrieval;
            config.googleApiKey = cocoroCore2Settings.googleApiKey;
            config.googleSearchEngineId = cocoroCore2Settings.googleSearchEngineId;
            config.internetMaxResults = cocoroCore2Settings.internetMaxResults;

            // Character設定の取得
            config.currentCharacterIndex = CharacterManagementControl.GetCurrentCharacterIndex();
            var currentCharacterSetting = CharacterManagementControl.GetCurrentCharacterSetting();
            if (currentCharacterSetting != null)
            {
                // 既存のCharacterListをコピーしてから現在のキャラクターを更新
                var appSettings = AppSettings.Instance;
                config.characterList = new List<CharacterSettings>(appSettings.CharacterList);

                if (config.currentCharacterIndex >= 0 && config.currentCharacterIndex < config.characterList.Count)
                {
                    config.characterList[config.currentCharacterIndex] = currentCharacterSetting;
                }
            }
            else
            {
                config.characterList = new List<CharacterSettings>(AppSettings.Instance.CharacterList);
            }

            return config;
        }

        /// <summary>
        /// CocoroCore2再起動が必要な設定項目が変更されたかどうかをチェック
        /// </summary>
        /// <param name="previousSettings">以前の設定</param>
        /// <param name="currentSettings">現在の設定</param>
        /// <returns>CocoroCore2再起動が必要な変更があった場合true</returns>
        private bool HasCocoroCore2RestartRequiredChanges(ConfigSettings previousSettings, ConfigSettings currentSettings)
        {
            // 基本設定項目の比較
            if (currentSettings.isEnableNotificationApi != previousSettings.isEnableNotificationApi ||
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

            for (int i = 0; i < currentSettings.characterList.Count; i++)
            {
                var current = currentSettings.characterList[i];
                var previous = previousSettings.characterList[i];

                if (current.isUseLLM != previous.isUseLLM ||
                    current.apiKey != previous.apiKey ||
                    current.llmModel != previous.llmModel ||
                    current.visionApiKey != previous.visionApiKey ||
                    current.visionModel != previous.visionModel ||
                    current.localLLMBaseUrl != previous.localLLMBaseUrl ||
                    current.isEnableMemory != previous.isEnableMemory ||
                    current.memoryId != previous.memoryId ||
                    current.embeddedApiKey != previous.embeddedApiKey ||
                    current.embeddedModel != previous.embeddedModel)
                {
                    return true;
                }
            }

            return false;
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
