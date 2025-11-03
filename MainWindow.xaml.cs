using CocoroDock.Communication;
using CocoroDock.Controls;
using CocoroDock.Services;
using CocoroDock.Utilities;
using CocoroDock.Windows;
using CocoroAI.Services;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using System.IO;

namespace CocoroDock
{

    /// <summary>
    /// MainWindow.xaml の相互作用ロジック
    /// </summary>
    public partial class MainWindow : Window
    {
        private ICommunicationService? _communicationService;
        private readonly IAppSettings _appSettings;
        private ScreenshotService? _screenshotService;
        private bool _isScreenshotPaused = false;
        private RealtimeVoiceRecognitionService? _voiceRecognitionService;
        private MobileWebSocketServer? _mobileWebSocketServer;
        private ScheduledCommandService? _scheduledCommandService;
        private AdminWindow? _adminWindow;
        private LogViewerWindow? _logViewerWindow;


        public MainWindow()
        {
            InitializeComponent();

            // ウィンドウのロード時にメッセージテキストボックスにフォーカスを設定するイベントを追加
            this.Loaded += MainWindow_Loaded;

            // 設定サービスの取得
            _appSettings = AppSettings.Instance;

            // 初期化と接続
            InitializeApp();
        }

        /// <summary>
        /// チャット履歴をクリア
        /// </summary>
        public void ClearChatHistory()
        {
            ChatControlInstance.ClearChat();
            _communicationService?.StartNewConversation();
        }

        /// <summary>
        /// ウィンドウのロード完了時のイベントハンドラ
        /// </summary>
        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // ChatControlのMessageTextBoxにフォーカス設定
            ChatControlInstance.FocusMessageTextBox();
        }

        /// <summary>
        /// アプリケーション初期化
        /// </summary>
        private void InitializeApp()
        {
            try
            {
                // 外部プロセスの起動
                InitializeExternalProcesses();

                // 通信サービスを初期化
                InitializeCommunicationService();

                // MobileWebSocketServerを初期化
                InitializeMobileWebSocketServer();

                // スクリーンショットサービスを初期化
                InitializeScreenshotService();

                // スケジュールコマンドサービスを初期化
                InitializeScheduledCommandService();

                // 音声認識サービスを初期化
                // 起動時はウェイクワードの有無に応じてVoiceRecognitionStateMachine内で状態が決定される
                InitializeVoiceRecognitionService(startActive: false);

                // UIコントロールのイベントハンドラを登録
                RegisterEventHandlers();

                // ボタンの初期状態を設定
                InitializeButtonStates();

                // 初期ステータス表示
                if (_communicationService != null)
                {
                    UpdateCocoroCoreMStatusDisplay(_communicationService.CurrentStatus);
                }

                // APIサーバーの起動を開始
                _ = StartApiServerAsync();
            }
            catch (Exception ex)
            {
                UIHelper.ShowError("初期化エラー", ex.Message);
            }
        }

        /// <summary>
        /// ボタンの初期状態を設定
        /// </summary>
        private void InitializeButtonStates()
        {
            // デスクトップウォッチの状態を反映
            var screenshotSettings = _appSettings.ScreenshotSettings;
            if (screenshotSettings != null)
            {
                _isScreenshotPaused = !screenshotSettings.enabled;
                if (ScreenshotButtonImage != null)
                {
                    ScreenshotButtonImage.Source = new Uri(_isScreenshotPaused ?
                        "pack://application:,,,/Resource/icon/ScreenShotOFF.svg" :
                        "pack://application:,,,/Resource/icon/ScreenShotON.svg",
                        UriKind.Absolute);
                }
                if (PauseScreenshotButton != null)
                {
                    PauseScreenshotButton.ToolTip = _isScreenshotPaused ? "デスクトップウォッチを有効にする" : "デスクトップウォッチを無効にする";
                    PauseScreenshotButton.Opacity = _isScreenshotPaused ? 0.6 : 1.0;
                }
            }

            // 現在のキャラクターの設定を反映
            var currentCharacter = GetStoredCharacterSetting();
            if (currentCharacter != null)
            {
                // STTの状態を反映
                if (MicButtonImage != null)
                {
                    MicButtonImage.Source = new Uri(currentCharacter.isUseSTT ?
                        "pack://application:,,,/Resource/icon/MicON.svg" :
                        "pack://application:,,,/Resource/icon/MicOFF.svg",
                        UriKind.Absolute);
                }
                if (MicButton != null)
                {
                    MicButton.ToolTip = currentCharacter.isUseSTT ? "STTを無効にする" : "STTを有効にする";
                    MicButton.Opacity = currentCharacter.isUseSTT ? 1.0 : 0.6;
                }

                // TTSの状態を反映
                if (MuteButtonImage != null)
                {
                    MuteButtonImage.Source = new Uri(currentCharacter.isUseTTS ?
                        "pack://application:,,,/Resource/icon/SpeakerON.svg" :
                        "pack://application:,,,/Resource/icon/SpeakerOFF.svg",
                        UriKind.Absolute);
                }
                if (MuteButton != null)
                {
                    MuteButton.ToolTip = currentCharacter.isUseTTS ? "TTSを無効にする" : "TTSを有効にする";
                    MuteButton.Opacity = currentCharacter.isUseTTS ? 1.0 : 0.6;
                }
            }
        }

        /// <summary>
        /// 外部プロセスを初期化
        /// </summary>
        private void InitializeExternalProcesses()
        {
            // CocoroShell.exeを起動（既に起動していれば終了してから再起動）
            LaunchCocoroShell();
            // CocoroCoreM.exeを起動（既に起動していれば終了してから再起動）
            LaunchCocoroCoreM();
        }

        /// <summary>
        /// 通信サービスを初期化
        /// </summary>
        private void InitializeCommunicationService()
        {
            // 通信サービスを初期化 (REST APIサーバーを使用)
            _communicationService = new CommunicationService(_appSettings);            // 通信サービスのイベントハンドラを設定
            _communicationService.ChatMessageReceived += OnChatMessageReceived;
            _communicationService.NotificationMessageReceived += OnNotificationMessageReceived;
            _communicationService.ControlCommandReceived += OnControlCommandReceived;
            _communicationService.ErrorOccurred += OnErrorOccurred;
            _communicationService.StatusChanged += OnCocoroCoreMStatusChanged;
        }

        /// <summary>
        /// スクリーンショットサービスを初期化
        /// </summary>
        private void InitializeScreenshotService()
        {
            // スクリーンショット設定を確認
            var screenshotSettings = _appSettings.ScreenshotSettings;
            if (screenshotSettings != null && screenshotSettings.enabled)
            {
                // スクリーンショットサービスを初期化
                _screenshotService = new ScreenshotService(
                    screenshotSettings.intervalMinutes,
                    async (screenshotData) => await OnScreenshotCaptured(screenshotData),
                    async (message) => await OnScreenshotSkipped(message)
                );

                _screenshotService.CaptureActiveWindowOnly = screenshotSettings.captureActiveWindowOnly;
                _screenshotService.IdleTimeoutMinutes = screenshotSettings.idleTimeoutMinutes;
                _screenshotService.SetExcludePatterns(screenshotSettings.excludePatterns);


                // サービスを開始
                _screenshotService.Start();

                Debug.WriteLine($"スクリーンショットサービスを開始しました（間隔: {screenshotSettings.intervalMinutes}分）");
            }
        }

        /// <summary>
        /// スケジュールコマンドサービスを初期化
        /// </summary>
        private void InitializeScheduledCommandService()
        {
            var settings = _appSettings.ScheduledCommandSettings;
            if (settings != null && settings.Enabled && !string.IsNullOrWhiteSpace(settings.Command))
            {
                _scheduledCommandService = new ScheduledCommandService(settings.IntervalMinutes);
                _scheduledCommandService.SetCommand(settings.Command);
                _scheduledCommandService.Start();
                Debug.WriteLine($"スケジュールコマンドサービスを開始しました（間隔: {settings.IntervalMinutes}分）");
            }
        }

        /// <summary>
        /// スクリーンショットが撮影された時の処理
        /// </summary>
        private async Task OnScreenshotCaptured(ScreenshotData screenshotData)
        {
            try
            {
                // 現在のキャラクター設定を取得してLLMの使用状況を確認
                var currentCharacter = GetStoredCharacterSetting();
                bool isLLMEnabled = currentCharacter?.isUseLLM ?? false;

                // LLMが無効の場合は画像表示のみ行い、送信はしない
                UIHelper.RunOnUIThread(() =>
                {
                    ChatControlInstance.AddDesktopMonitoringImage(screenshotData.ImageBase64);
                });

                if (!isLLMEnabled)
                {
                    Debug.WriteLine("デスクトップモニタリング: LLMが無効のため送信をスキップ");
                    return;
                }

                // CommunicationServiceを使用してデスクトップウォッチを送信
                if (_communicationService != null && _communicationService.IsServerRunning)
                {
                    // デスクトップウォッチ専用の送信処理を使用
                    await _communicationService.SendDesktopWatchToCoreAsync(screenshotData);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"デスクトップモニタリング処理エラー: {ex.Message}");
            }
        }

        /// <summary>
        /// スクリーンショットがスキップされた時の処理
        /// </summary>
        private Task OnScreenshotSkipped(string message)
        {
            try
            {
                // UIスレッドでチャットコントロールに通知メッセージを追加
                UIHelper.RunOnUIThread(() =>
                {
                    if (ChatControlInstance != null)
                    {
                        ChatControlInstance.AddSystemErrorMessage($"ℹ️ {message}");
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"スクリーンショットスキップ通知エラー: {ex.Message}");
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// スクリーンショットサービスの設定を更新
        /// </summary>
        private void UpdateScreenshotService()
        {
            var screenshotSettings = _appSettings.ScreenshotSettings;

            // 現在のサービスが存在し、設定が無効になった場合は停止
            if (_screenshotService != null && (screenshotSettings == null || !screenshotSettings.enabled))
            {
                _screenshotService.Stop();
                _screenshotService.Dispose();
                _screenshotService = null;
                Debug.WriteLine("スクリーンショットサービスを停止しました");
            }
            // 設定が有効でサービスが存在しない場合は開始
            else if (screenshotSettings != null && screenshotSettings.enabled && _screenshotService == null)
            {
                InitializeScreenshotService();
            }
            // サービスが存在し、設定が変更された場合は更新または再起動
            else if (_screenshotService != null && screenshotSettings != null && screenshotSettings.enabled)
            {
                // 設定の変更を検出
                bool needsRestart = false;

                // 間隔が変更された場合は再起動が必要
                if (_screenshotService.IntervalMinutes != screenshotSettings.intervalMinutes)
                {
                    needsRestart = true;
                }

                // その他の設定は動的に更新
                _screenshotService.CaptureActiveWindowOnly = screenshotSettings.captureActiveWindowOnly;
                _screenshotService.IdleTimeoutMinutes = screenshotSettings.idleTimeoutMinutes;
                _screenshotService.SetExcludePatterns(screenshotSettings.excludePatterns);

                if (needsRestart)
                {
                    _screenshotService.Stop();
                    _screenshotService.Dispose();
                    InitializeScreenshotService();
                    Debug.WriteLine("スクリーンショットサービスを再起動しました");
                }
                else
                {
                    Debug.WriteLine("スクリーンショットサービスの設定を更新しました");
                }
            }
        }

        /// <summary>
        /// スケジュールコマンドサービスの設定を更新
        /// </summary>
        private void UpdateScheduledCommandService()
        {
            var settings = _appSettings.ScheduledCommandSettings;

            // 現在のサービスが存在し、設定が無効になった場合は停止
            if (_scheduledCommandService != null && (settings == null || !settings.Enabled || string.IsNullOrWhiteSpace(settings.Command)))
            {
                _scheduledCommandService.Stop();
                _scheduledCommandService.Dispose();
                _scheduledCommandService = null;
                Debug.WriteLine("スケジュールコマンドサービスを停止しました");
            }
            // 設定が有効でサービスが存在しない場合は開始
            else if (settings != null && settings.Enabled && !string.IsNullOrWhiteSpace(settings.Command) && _scheduledCommandService == null)
            {
                InitializeScheduledCommandService();
            }
            // サービスが存在し、設定が変更された場合は再起動
            else if (_scheduledCommandService != null && settings != null && settings.Enabled && !string.IsNullOrWhiteSpace(settings.Command))
            {
                // 間隔またはコマンドが変更された場合は再起動
                if (_scheduledCommandService.IntervalMinutes != settings.IntervalMinutes)
                {
                    _scheduledCommandService.Stop();
                    _scheduledCommandService.Dispose();
                    InitializeScheduledCommandService();
                    Debug.WriteLine("スケジュールコマンドサービスを再起動しました");
                }
                else
                {
                    // コマンドのみ変更された場合は再起動
                    _scheduledCommandService.Restart(settings.IntervalMinutes, settings.Command);
                    Debug.WriteLine("スケジュールコマンドサービスのコマンドを更新しました");
                }
            }
        }

        /// <summary>
        /// APIサーバーを起動（非同期タスク）
        /// </summary>
        private async Task StartApiServerAsync()
        {
            try
            {
                if (_communicationService != null && !_communicationService.IsServerRunning)
                {
                    // APIサーバーを起動
                    await _communicationService.StartServerAsync();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"APIサーバー起動エラー: {ex.Message}");
            }
        }

        /// <summary>
        /// UIコントロールのイベントハンドラを登録
        /// </summary>
        private void RegisterEventHandlers()
        {
            // チャットコントロールのイベント登録
            ChatControlInstance.MessageSent += OnChatMessageSent;

            // 設定保存イベントの登録
            AppSettings.SettingsSaved += OnSettingsSaved;
        }





        /// <summary>
        /// CocoroCoreMステータスに基づいて表示を更新
        /// </summary>
        /// <param name="status">CocoroCoreMのステータス</param>
        private void UpdateCocoroCoreMStatusDisplay(CocoroCoreMStatus status)
        {
            // 現在のキャラクター設定を取得してLLMの使用状況を確認
            var currentCharacter = GetStoredCharacterSetting();
            bool isLLMEnabled = currentCharacter?.isUseLLM ?? false;

            string statusText = status switch
            {
                CocoroCoreMStatus.WaitingForStartup => isLLMEnabled ? "CocoroCoreM起動待ち" : "LLM無効",
                CocoroCoreMStatus.Normal => isLLMEnabled ? "正常動作中" : "LLM無効",
                CocoroCoreMStatus.ProcessingMessage => "LLMメッセージ処理中",
                CocoroCoreMStatus.ProcessingImage => "LLM画像処理中",
                _ => "不明な状態"
            };

            ConnectionStatusText.Text = $"状態: {statusText}";

            // 送信ボタンの有効/無効を制御（LLMが無効の場合は無効にする）
            bool isSendEnabled = isLLMEnabled && status != CocoroCoreMStatus.WaitingForStartup;
            ChatControlInstance.UpdateSendButtonEnabled(isSendEnabled);
        }

        /// <summary>
        /// 設定を適用
        /// </summary>
        private void ApplySettings()
        {
            UIHelper.RunOnUIThread(() =>
            {
                // スクリーンショットサービスの設定を更新
                UpdateScreenshotService();

                // スケジュールコマンドサービスの設定を更新
                UpdateScheduledCommandService();
            });
        }

        #region チャットコントロールイベントハンドラ

        /// <summary>
        /// チャットメッセージ送信時のハンドラ
        /// </summary>
        private void OnChatMessageSent(object? sender, string message)
        {
            // APIサーバーが起動している場合のみ送信
            if (_communicationService == null || !_communicationService.IsServerRunning)
            {
                ChatControlInstance.AddSystemErrorMessage("サーバーが起動していません");
                return;
            }

            // UIスレッドで画像データを取得・処理（スレッドセーフな形式に変換）
            var imageSources = ChatControlInstance.GetAttachedImageSources();
            var imageDataUrls = ChatControlInstance.GetAndClearAttachedImages();

            // ユーザーメッセージとしてチャットウィンドウに表示（送信前に表示）
            ChatControlInstance.AddUserMessage(message, imageSources);

            // 非同期でCocoroCoreにメッセージを送信（UIをブロックしない）
            _ = Task.Run(async () =>
            {
                try
                {
                    // CocoroCoreにメッセージを送信（API使用、画像付きの場合は画像データも送信）
                    await _communicationService.SendChatToCoreUnifiedAsync(message, null, imageDataUrls);
                }
                catch (TimeoutException)
                {
                    // UIスレッドでエラーメッセージを表示
                    UIHelper.RunOnUIThread(() =>
                    {
                        ChatControlInstance.AddSystemErrorMessage("AI応答がタイムアウトしました。もう一度お試しください。");
                    });
                }
                catch (HttpRequestException ex)
                {
                    // UIスレッドでエラーメッセージを表示
                    UIHelper.RunOnUIThread(() =>
                    {
                        ChatControlInstance.AddSystemErrorMessage("AI応答サーバーに接続できません。");
                    });
                    Debug.WriteLine($"HttpRequestException: {ex.Message}");
                }
                catch (Exception ex)
                {
                    // UIスレッドでエラーメッセージを表示
                    UIHelper.RunOnUIThread(() =>
                    {
                        ChatControlInstance.AddSystemErrorMessage($"エラーが発生しました: {ex.Message}");
                    });
                    Debug.WriteLine($"Exception: {ex}");
                }
            });
        }

        /// <summary>
        /// 設定保存時のイベントハンドラ
        /// </summary>
        private void OnSettingsSaved(object? sender, EventArgs e)
        {
            // 現在の設定に基づいて音声認識サービスを制御
            var currentCharacter = GetStoredCharacterSetting();
            bool shouldBeActive = currentCharacter?.isUseSTT ?? false;

            // 既存のサービスを停止
            if (_voiceRecognitionService != null)
            {
                _voiceRecognitionService.StopListening();
                _voiceRecognitionService.Dispose();
                _voiceRecognitionService = null;
            }

            // 設定に応じてサービスを開始
            if (shouldBeActive)
            {
                InitializeVoiceRecognitionService(startActive: true);
                Debug.WriteLine("[MainWindow] 音声認識サービスを開始しました");
            }
            else
            {
                // 音声レベル表示をリセット
                UIHelper.RunOnUIThread(() =>
                {
                    ChatControlInstance.UpdateVoiceLevel(0, false);
                });
                Debug.WriteLine("[MainWindow] 音声認識サービスを停止しました");
            }

            // デスクトップウォッチ設定の更新
            UpdateScreenshotService();
        }

        #endregion

        #region 通信サービスイベントハンドラ

        /// <summary>
        /// チャットメッセージ受信時のハンドラ（CocoroDock APIから）
        /// </summary>
        private void OnChatMessageReceived(object? sender, ChatRequest request)
        {
            UIHelper.RunOnUIThread(() =>
            {
                if (request.role == "user")
                {
                    ChatControlInstance.AddUserMessage(request.content);
                }
                else if (request.role == "assistant")
                {
                    // サーバー側処理済みメッセージをそのまま新規追加
                    ChatControlInstance.AddAiMessage(request.content);
                }
            });
        }

        /// <summary>
        /// 通知メッセージ受信時のハンドラ
        /// </summary>
        private void OnNotificationMessageReceived(ChatMessagePayload notification, List<System.Windows.Media.Imaging.BitmapSource>? imageSources)
        {
            UIHelper.RunOnUIThread(() =>
            {
                // 通知メッセージをチャットウィンドウに表示（複数画像付き）
                ChatControlInstance.AddNotificationMessage(notification.from, notification.message, imageSources);
            });
        }


        /// <summary>
        /// 制御コマンド受信時のハンドラ（CocoroDock APIから）
        /// </summary>
        private void OnControlCommandReceived(object? sender, ControlRequest request)
        {
            UIHelper.RunOnUIThread(async () =>
            {
                // パラメータ情報をログ出力
                var paramsInfo = request.@params?.Count > 0 ? $" パラメータ: {request.@params.Count}個" : "";
                Debug.WriteLine($"制御コマンド受信: {request.action}, 理由: {request.reason}{paramsInfo}");

                switch (request.action)
                {
                    case "shutdown":
                        // 非同期でシャットダウン処理を実行
                        await PerformGracefulShutdownAsync();
                        break;

                    case "restart":
                        Debug.WriteLine("restart コマンドは現在未実装です");
                        break;

                    case "reloadConfig":
                        Debug.WriteLine("reloadConfig コマンドは現在未実装です");
                        break;

                    default:
                        Debug.WriteLine($"未知の制御コマンド: {request.action}");
                        break;
                }
            });
        }        /// <summary>
                 /// エラー発生時のハンドラ
                 /// </summary>
        private void OnErrorOccurred(object? sender, string error)
        {
            UIHelper.ShowError("エラー", error);
        }

        /// <summary>
        /// CocoroCoreMステータス変更時のハンドラ
        /// </summary>
        private void OnCocoroCoreMStatusChanged(object? sender, CocoroCoreMStatus status)
        {
            UIHelper.RunOnUIThread(() =>
            {
                UpdateCocoroCoreMStatusDisplay(status);
            });
        }

        #endregion

        /// <summary>
        /// ログビューアーを開く
        /// </summary>
        public void OpenLogViewer()
        {
            // 既にログビューアーが開いている場合はアクティブにする
            if (_logViewerWindow != null && !_logViewerWindow.IsClosed)
            {
                _logViewerWindow.Activate();
                _logViewerWindow.WindowState = WindowState.Normal;
                return;
            }

            // ログビューアーを新規作成
            _logViewerWindow = new LogViewerWindow();
            _logViewerWindow.Owner = this;

            // ウィンドウが閉じられた時の処理
            _logViewerWindow.Closed += (sender, args) => { _logViewerWindow = null; };

            _logViewerWindow.Show();
        }

        /// <summary>
        /// アプリケーション終了時の処理
        /// </summary>
        protected override void OnClosed(EventArgs e)
        {
            try
            {
                // イベントハンドラの購読解除
                AppSettings.SettingsSaved -= OnSettingsSaved;

                // スケジュールコマンドサービスを停止
                if (_scheduledCommandService != null)
                {
                    _scheduledCommandService.Stop();
                    _scheduledCommandService.Dispose();
                    _scheduledCommandService = null;
                }

                // 接続中ならリソース解放
                if (_communicationService != null)
                {
                    _communicationService.Dispose();
                    _communicationService = null;
                }
                // 関連アプリを終了
                TerminateExternalApplications();
            }
            catch (Exception)
            {
                // 切断中のエラーは無視
            }

            base.OnClosed(e);

            // Application.Current.ShutdownだけでOK
            // OnExitが自動的に実行される
            Application.Current.Shutdown();
        }

        /// <summary>
        /// 外部アプリケーション（CocoroCore, CocoroShell）を終了する
        /// </summary>
        private void TerminateExternalApplications()
        {
            try
            {
                // 2つのプロセスを並行して終了させる
                var tasks = new[]
                {
                    Task.Run(() => LaunchCocoroCoreM(ProcessOperation.Terminate)),
                    Task.Run(() => LaunchCocoroShell(ProcessOperation.Terminate))
                };

                // すべてのプロセスが終了するまで待機
                Task.WaitAll(tasks);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"外部アプリケーション終了エラー: {ex.Message}");
                // アプリケーションの終了処理なのでエラーメッセージは表示しない
            }
        }

        /// <summary>
        /// 設定ボタンクリック時のイベントハンドラ
        /// </summary>
        private void AdminButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 既に設定画面が開いている場合はアクティブにする
                if (_adminWindow != null && !_adminWindow.IsClosed)
                {
                    _adminWindow.Activate();
                    _adminWindow.WindowState = WindowState.Normal;
                    return;
                }

                // 設定画面を新規作成
                _adminWindow = new AdminWindow(_communicationService);
                _adminWindow.Owner = this; // メインウィンドウを親に設定

                // ウィンドウが閉じられた時にボタンの状態を更新
                _adminWindow.Closed += AdminWindow_Closed;

                _adminWindow.Show(); // モードレスダイアログとして表示
            }
            catch (Exception ex)
            {
                UIHelper.ShowError("設定取得エラー", ex.Message);
            }
        }

        /// <summary>
        /// 設定画面が閉じられた時のイベントハンドラ
        /// </summary>
        private void AdminWindow_Closed(object? sender, EventArgs e)
        {
            // ボタンの状態を最新の設定に更新
            InitializeButtonStates();

            // 設定変更に応じてサービスを更新
            ApplySettings();

            // AdminWindowの参照をクリア
            _adminWindow = null;
        }

        /// <summary>
        /// 画像送信一時停止ボタンクリック時のイベントハンドラ
        /// </summary>
        private void PauseScreenshotButton_Click(object sender, RoutedEventArgs e)
        {
            // デスクトップウォッチ設定をトグル
            var screenshotSettings = _appSettings.ScreenshotSettings;
            if (screenshotSettings != null)
            {
                screenshotSettings.enabled = !screenshotSettings.enabled;
                _isScreenshotPaused = !screenshotSettings.enabled;

                // 設定を保存
                _appSettings.SaveSettings();

                // ボタンの画像を更新
                if (ScreenshotButtonImage != null)
                {
                    ScreenshotButtonImage.Source = new Uri(_isScreenshotPaused ?
                        "pack://application:,,,/Resource/icon/ScreenShotOFF.svg" :
                        "pack://application:,,,/Resource/icon/ScreenShotON.svg",
                        UriKind.Absolute);
                }

                // ツールチップを更新
                if (PauseScreenshotButton != null)
                {
                    PauseScreenshotButton.ToolTip = _isScreenshotPaused ? "デスクトップウォッチを有効にする" : "デスクトップウォッチを無効にする";

                    // 無効状態の場合は半透明にする
                    PauseScreenshotButton.Opacity = _isScreenshotPaused ? 0.6 : 1.0;
                }

                // スクリーンショットサービスの状態を更新
                UpdateScreenshotService();
            }
        }

        /// <summary>
        /// マイクボタンクリック時のイベントハンドラ
        /// </summary>
        private void MicButton_Click(object sender, RoutedEventArgs e)
        {
            // 現在のキャラクターのSTT設定をトグル
            var currentCharacter = GetStoredCharacterSetting();
            if (currentCharacter != null)
            {
                currentCharacter.isUseSTT = !currentCharacter.isUseSTT;

                // ボタンの画像を更新
                if (MicButtonImage != null)
                {
                    MicButtonImage.Source = new Uri(currentCharacter.isUseSTT ?
                        "pack://application:,,,/Resource/icon/MicON.svg" :
                        "pack://application:,,,/Resource/icon/MicOFF.svg",
                        UriKind.Absolute);
                }

                // ツールチップを更新
                if (MicButton != null)
                {
                    MicButton.ToolTip = currentCharacter.isUseSTT ? "STTを無効にする" : "STTを有効にする";
                    MicButton.Opacity = currentCharacter.isUseSTT ? 1.0 : 0.6;
                }

                // 設定を保存（OnSettingsSavedで音声認識サービスが制御される）
                _appSettings.SaveSettings();
            }
        }

        /// <summary>
        /// 保存済みの現在のキャラクター設定を取得（AppSettingsから直接読み取り）
        /// </summary>
        private CharacterSettings? GetStoredCharacterSetting()
        {
            var config = _appSettings.GetConfigSettings();
            if (config.characterList != null &&
                config.currentCharacterIndex >= 0 &&
                config.currentCharacterIndex < config.characterList.Count)
            {
                return config.characterList[config.currentCharacterIndex];
            }
            return null;
        }

        /// <summary>
        /// TTSボタンクリック時のイベントハンドラ
        /// </summary>
        private void TTSButton_Click(object sender, RoutedEventArgs e)
        {
            // 現在のキャラクターのTTS設定をトグル
            var currentCharacter = GetStoredCharacterSetting();
            if (currentCharacter != null)
            {
                currentCharacter.isUseTTS = !currentCharacter.isUseTTS;

                // 設定を保存
                _appSettings.SaveSettings();

                // ボタンの画像を更新
                if (MuteButtonImage != null)
                {
                    MuteButtonImage.Source = new Uri(currentCharacter.isUseTTS ?
                        "pack://application:,,,/Resource/icon/SpeakerON.svg" :
                        "pack://application:,,,/Resource/icon/SpeakerOFF.svg",
                        UriKind.Absolute);
                }

                // ツールチップを更新
                if (MuteButton != null)
                {
                    MuteButton.ToolTip = currentCharacter.isUseTTS ? "TTSを無効にする" : "TTSを有効にする";

                    // 無効状態の場合は半透明にする
                    MuteButton.Opacity = currentCharacter.isUseTTS ? 1.0 : 0.6;
                }

                // CocoroShellにTTS状態を送信
                _ = Task.Run(async () =>
                {
                    try
                    {
                        if (_communicationService != null)
                        {
                            // TTS設定をCocoroShellに送信
                            await _communicationService.SendTTSStateToShellAsync(currentCharacter.isUseTTS);

                            // TTS状態変更完了（ログ出力は既にある）
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"TTS状態の送信エラー: {ex.Message}");
                    }
                });
            }
        }


        /// <summary>
        /// CocoroShell.exeを起動する（既に起動している場合は終了してから再起動）
        /// </summary>
        /// <param name="operation">プロセス操作の種類（デフォルトは再起動）</param>
        private void LaunchCocoroShell(ProcessOperation operation = ProcessOperation.RestartIfRunning)
        {
#if !DEBUG
            if (_appSettings.CharacterList.Count > 0 &&
               _appSettings.CurrentCharacterIndex < _appSettings.CharacterList.Count)
            {
                ProcessHelper.LaunchExternalApplication("CocoroShell.exe", "CocoroShell", operation, true);
            }
            else
            {
                ProcessHelper.LaunchExternalApplication("CocoroShell.exe", "CocoroShell", ProcessOperation.Terminate, true);
            }
#endif
        }

        /// <summary>
        /// CocoroCoreM.exeを起動する（既に起動している場合は終了してから再起動）
        /// </summary>
        /// <param name="operation">プロセス操作の種類（デフォルトは再起動）</param>
        private void LaunchCocoroCoreM(ProcessOperation operation = ProcessOperation.RestartIfRunning)
        {
            if (_appSettings.CharacterList.Count > 0 &&
               _appSettings.CurrentCharacterIndex < _appSettings.CharacterList.Count &&
               _appSettings.CharacterList[_appSettings.CurrentCharacterIndex].isUseLLM)
            {
                // 起動監視を開始
                if (operation != ProcessOperation.Terminate)
                {
#if !DEBUG
                    // プロセス起動
                    ProcessHelper.LaunchExternalApplication("CocoroCoreM.exe", "CocoroCoreM", operation, false);
#endif
                    // 非同期でAPI通信による起動完了を監視（無限ループ）
                    _ = Task.Run(async () =>
                    {
                        await WaitForCocoroCoreMStartupAsync();
                    });
                }
                else
                {
                    ProcessHelper.LaunchExternalApplication("CocoroCoreM.exe", "CocoroCoreM", operation, false);
                }
            }
            else
            {
                // LLMを使用しない場合はCocoroCoreMを終了
                ProcessHelper.LaunchExternalApplication("CocoroCoreM.exe", "CocoroCoreM", ProcessOperation.Terminate, false);
            }
        }

        /// <summary>
        /// CocoroCoreM.exeを起動する（既に起動している場合は終了してから再起動）（非同期版）
        /// </summary>
        /// <param name="operation">プロセス操作の種類（デフォルトは再起動）</param>
        private async Task LaunchCocoroCoreMAsync(ProcessOperation operation = ProcessOperation.RestartIfRunning)
        {
            if (_appSettings.CharacterList.Count > 0 &&
               _appSettings.CurrentCharacterIndex < _appSettings.CharacterList.Count &&
               _appSettings.CharacterList[_appSettings.CurrentCharacterIndex].isUseLLM)
            {
                // 起動監視を開始
                if (operation != ProcessOperation.Terminate)
                {
#if !DEBUG
                    // プロセス起動（非同期）
                    await ProcessHelper.LaunchExternalApplicationAsync("CocoroCoreM.exe", "CocoroCoreM", operation, false);
#endif
                    // 非同期でAPI通信による起動完了を監視（無限ループ）
                    _ = Task.Run(async () =>
                    {
                        await WaitForCocoroCoreMStartupAsync();
                    });
                }
                else
                {
                    await ProcessHelper.LaunchExternalApplicationAsync("CocoroCoreM.exe", "CocoroCoreM", operation, false);
                }
            }
            else
            {
                // LLMを使用しない場合はCocoroCoreMを終了
                await ProcessHelper.LaunchExternalApplicationAsync("CocoroCoreM.exe", "CocoroCoreM", ProcessOperation.Terminate, false);
            }
        }


        /// <summary>
        /// 音声認識サービスを初期化
        /// </summary>
        /// <param name="startActive">ACTIVE状態から開始するかどうか（MicButton切り替え時はtrue）</param>
        private void InitializeVoiceRecognitionService(bool startActive = false)
        {
            try
            {
                // 現在のキャラクター設定を取得
                var currentCharacter = GetStoredCharacterSetting();
                if (currentCharacter == null)
                {
                    Debug.WriteLine("[MainWindow] 現在のキャラクター設定が見つかりません");
                    return;
                }

                // 音声認識が有効でAPIキーが設定されている場合のみ初期化
                if (!currentCharacter.isUseSTT || string.IsNullOrEmpty(currentCharacter.sttApiKey))
                {
                    Debug.WriteLine("[MainWindow] 音声認識機能が無効、またはAPIキーが未設定");
                    // 音量バーを0にリセット（UIスレッドで確実に実行）
                    UIHelper.RunOnUIThread(() =>
                    {
                        ChatControlInstance.UpdateVoiceLevel(0, false);
                    });
                    return;
                }

                // if (string.IsNullOrEmpty(currentCharacter.sttWakeWord))
                // {
                //     Debug.WriteLine("[MainWindow] ウェイクアップワードが未設定");
                //     // 音量バーを0にリセット（UIスレッドで確実に実行）
                //     UIHelper.RunOnUIThread(() =>
                //     {
                //         ChatControlInstance.UpdateVoiceLevel(0, false);
                //     });
                //     return;
                // }

                // 音声処理パラメータ
                // 無音区間判定用の閾値（dB値）
                float inputThresholdDb = _appSettings.MicrophoneSettings?.inputThreshold ?? -45.0f;
                // 音声検出用の閾値（振幅比率に変換）
                float voiceThreshold = (float)(Math.Pow(10, inputThresholdDb / 20.0));
                const int silenceTimeoutMs = 500; // 高速化のため短縮
                const int activeTimeoutMs = 60000;

                // 話者識別サービス初期化（常に有効）
                var dbPath = System.IO.Path.Combine(AppSettings.Instance.UserDataDirectory, "speaker_recognition.db");
                var speakerService = new SpeakerRecognitionService(
                    dbPath,
                    threshold: AppSettings.Instance.MicrophoneSettings.speakerRecognitionThreshold
                );

                _voiceRecognitionService = new RealtimeVoiceRecognitionService(
                    new AmiVoiceSpeechToTextService(currentCharacter.sttApiKey),
                    currentCharacter.sttWakeWord,
                    speakerService, // 話者識別サービスを追加
                    voiceThreshold,
                    silenceTimeoutMs,
                    activeTimeoutMs,
                    startActive
                );

                // イベント購読
                _voiceRecognitionService.OnRecognizedText += OnVoiceRecognized;
                _voiceRecognitionService.OnStateChanged += OnVoiceStateChanged;
                _voiceRecognitionService.OnVoiceLevel += OnVoiceLevelChanged;
                _voiceRecognitionService.OnSpeakerIdentified += OnSpeakerIdentified; // 話者識別イベント

                // 音声認識開始
                _voiceRecognitionService.StartListening();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MainWindow] 音声認識サービス初期化エラー: {ex.Message}");
            }
        }

        /// <summary>
        /// MobileWebSocketServerを初期化
        /// </summary>
        private void InitializeMobileWebSocketServer()
        {
            try
            {
                if (!_appSettings.IsEnableWebService)
                {
                    Debug.WriteLine("[MainWindow] Web機能が無効のため、MobileWebSocketServerを起動しません");
                    return;
                }

                _mobileWebSocketServer = new MobileWebSocketServer(_appSettings.CocoroWebPort, _appSettings);

                // モバイルチャットのイベントハンドラを設定
                _mobileWebSocketServer.MobileMessageReceived += OnMobileMessageReceived;
                _mobileWebSocketServer.MobileImageMessageReceived += OnMobileImageMessageReceived;
                _mobileWebSocketServer.MobileAiResponseReceived += OnMobileAiResponseReceived;

                // 非同期で起動
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _mobileWebSocketServer.StartAsync();
                        Debug.WriteLine($"[MainWindow] MobileWebSocketServer起動完了: ポート{_appSettings.CocoroWebPort}");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[MainWindow] MobileWebSocketServer起動エラー: {ex.Message}");

                        // UIスレッドでエラー表示
                        Dispatcher.Invoke(() =>
                        {
                            UIHelper.ShowError("Web機能初期化エラー",
                                $"MobileWebSocketServerの起動に失敗しました:\n{ex.Message}\n\nWeb機能は無効になります。");
                        });

                        _mobileWebSocketServer?.Dispose();
                        _mobileWebSocketServer = null;
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MainWindow] MobileWebSocketServer初期化エラー: {ex.Message}");
                UIHelper.ShowError("Web機能初期化エラー", $"MobileWebSocketServerの初期化に失敗しました: {ex.Message}");
            }
        }

        /// <summary>
        /// モバイルメッセージ受信イベント
        /// </summary>
        private void OnMobileMessageReceived(object? sender, string message)
        {
            UIHelper.RunOnUIThread(() =>
            {
                // チャットにモバイルメッセージを表示
                ChatControlInstance.AddUserMessage(message);

                // ステータスバーにLLM処理中を表示
                UpdateCocoroCoreMStatusDisplay(CocoroCoreMStatus.ProcessingMessage);
            });
        }

        /// <summary>
        /// モバイルからの画像付きメッセージ受信イベントハンドラ
        /// </summary>
        private void OnMobileImageMessageReceived(object? sender, (string message, string imageBase64) data)
        {
            UIHelper.RunOnUIThread(() =>
            {
                // Base64画像データをBitmapImageに変換
                try
                {
                    var imageBytes = Convert.FromBase64String(data.imageBase64);
                    using (var stream = new MemoryStream(imageBytes))
                    {
                        var bitmapImage = new BitmapImage();
                        bitmapImage.BeginInit();
                        bitmapImage.StreamSource = stream;
                        bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                        bitmapImage.EndInit();
                        bitmapImage.Freeze();

                        // 画像付きメッセージに📱プレフィックスを付けて表示
                        string displayMessage = $"📱 {data.message}";
                        var imageSources = new List<BitmapSource> { bitmapImage };
                        ChatControlInstance.AddUserMessage(displayMessage, imageSources);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[MainWindow] 画像変換エラー: {ex.Message}");
                    // エラーの場合はテキストのみ表示
                    string displayMessage = $"📱 {data.message}";
                    ChatControlInstance.AddUserMessage(displayMessage, null);
                }

                Debug.WriteLine($"[MainWindow] 画像付きユーザーメッセージ表示: '📱 {data.message}', 画像サイズ: {data.imageBase64.Length} bytes");
            });
        }

        /// <summary>
        /// モバイルからのAI応答受信イベントハンドラ（画像付き対応）
        /// </summary>
        private void OnMobileAiResponseReceived(object? sender, (string text, string? imageBase64) data)
        {
            UIHelper.RunOnUIThread(() =>
            {
                // AI応答には画像を含めない（ユーザーが送信した画像をAI応答として表示するのは不適切）
                // AIが画像を生成した場合のみ画像付きで表示するべきだが、現在の実装では対応していない
                ChatControlInstance.AddAiMessage(data.text);

                if (!string.IsNullOrEmpty(data.imageBase64))
                {
                    Debug.WriteLine($"[MainWindow] AI応答で画像データを受信したが表示しない: '{data.text}', 画像サイズ: {data.imageBase64.Length} bytes");
                }
            });
        }

        /// <summary>
        /// 音声認識結果を処理
        /// </summary>
        private void OnVoiceRecognized(string text)
        {
            if (string.IsNullOrEmpty(text))
                return;

            UIHelper.RunOnUIThread(() =>
            {
                // チャットに音声認識結果を表示
                ChatControlInstance.AddVoiceMessage(text);

                // CocoroCoreMに送信
                SendMessageToCocoroCore(text, null);
            });
        }

        /// <summary>
        /// 話者識別結果を処理
        /// </summary>
        private void OnSpeakerIdentified(string speakerId, string speakerName, float confidence)
        {
            UIHelper.RunOnUIThread(() =>
            {
                // ステータス表示更新（必要に応じて）
                System.Diagnostics.Debug.WriteLine($"[MainWindow] Speaker identified: {speakerName} ({confidence:P0})");
            });
        }

        /// <summary>
        /// 音声認識状態変更を処理
        /// </summary>
        private void OnVoiceStateChanged(VoiceRecognitionState state)
        {
            UIHelper.RunOnUIThread(() =>
            {
                // 音声認識状態変更はログのみ
                string statusMessage = state switch
                {
                    VoiceRecognitionState.SLEEPING => "ウェイクアップワード待機中",
                    VoiceRecognitionState.ACTIVE => "会話モード開始",
                    VoiceRecognitionState.PROCESSING => "音声認識処理中",
                    _ => ""
                };

                if (!string.IsNullOrEmpty(statusMessage))
                {
                    Debug.WriteLine($"[VoiceRecognition] {statusMessage}");
                }
            });
        }

        /// <summary>
        /// 音声レベル変更を処理
        /// </summary>
        private void OnVoiceLevelChanged(float level, bool isAboveThreshold)
        {
            UIHelper.RunOnUIThread(() =>
            {
                ChatControlInstance.UpdateVoiceLevel(level, isAboveThreshold);
            });
        }

        /// <summary>
        /// CocoroCoreMにメッセージを送信
        /// </summary>
        private async void SendMessageToCocoroCore(string message, string? imageData)
        {
            try
            {
                if (_communicationService != null)
                {
                    var currentCharacter = GetStoredCharacterSetting();
                    if (currentCharacter != null && currentCharacter.isUseLLM)
                    {
                        await _communicationService.SendChatToCoreUnifiedAsync(message, currentCharacter.modelName, imageData);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MainWindow] CocoroCoreM送信エラー: {ex.Message}");
            }
        }

        /// <summary>
        /// CocoroCoreMのAPI起動完了を監視（無限ループ）
        /// </summary>
        private async Task WaitForCocoroCoreMStartupAsync()
        {
            var delay = TimeSpan.FromSeconds(1); // 1秒間隔でチェック

            while (true)
            {
                try
                {
                    if (_communicationService != null)
                    {
                        // StatusPollingServiceのステータスで起動状態を確認
                        if (_communicationService.CurrentStatus == CocoroCoreMStatus.Normal ||
                            _communicationService.CurrentStatus == CocoroCoreMStatus.ProcessingMessage ||
                            _communicationService.CurrentStatus == CocoroCoreMStatus.ProcessingImage)
                        {
                            // 起動成功時はログ出力のみ
                            Debug.WriteLine("[MainWindow] CocoroCoreM起動完了");
                            return; // 起動完了で監視終了
                        }
                    }
                }
                catch
                {
                    // API未応答時は継続してチェック
                }
                await Task.Delay(delay);
            }
        }

        /// <summary>
        /// ウィンドウのクローズイベントをキャンセルし、代わりに最小化する
        /// </summary>
        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            // 本当に終了するか確認（ALT+F4やタイトルバーのXボタン押下時）
            if (System.Windows.Application.Current.ShutdownMode != ShutdownMode.OnExplicitShutdown)
            {
                // 終了ではなく最小化して非表示にする
                e.Cancel = true;
                WindowState = WindowState.Minimized;
                this.Hide();
            }
            else
            {
                // アプリケーション終了時のクリーンアップ
                if (_screenshotService != null)
                {
                    _screenshotService.Dispose();
                }

                if (_voiceRecognitionService != null)
                {
                    _voiceRecognitionService.Dispose();
                }

                if (_scheduledCommandService != null)
                {
                    _scheduledCommandService.Dispose();
                }

                base.OnClosing(e);
            }
        }

        /// <summary>
        /// 指定されたポート番号を使用しているプロセスIDを取得します
        /// </summary>
        /// <param name="port">ポート番号</param>
        /// <returns>プロセスID（見つからない場合はnull）</returns>
        private static int? GetProcessIdByPort(int port)
        {
            try
            {
                var processInfo = new ProcessStartInfo("netstat", "-ano")
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };

                using var process = Process.Start(processInfo);
                if (process == null) return null;

                using var reader = process.StandardOutput;
                string? line;

                while ((line = reader.ReadLine()) != null)
                {
                    // ポート番号を含む行でLISTENING状態のものを探す
                    if (line.Contains($":{port} ") && line.Contains("LISTENING"))
                    {
                        // 行の最後の数字（PID）を抽出
                        var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length > 0 && int.TryParse(parts[^1], out int pid))
                        {
                            return pid;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"プロセスID取得中にエラーが発生しました: {ex.Message}");
            }
            return null;
        }

        /// <summary>
        /// 指定されたプロセスIDのプロセスが実行中かどうかを確認します
        /// </summary>
        /// <param name="processId">プロセスID</param>
        /// <returns>実行中の場合true、終了している場合false</returns>
        private static bool IsProcessRunning(int processId)
        {
            try
            {
                Process.GetProcessById(processId);
                return true;
            }
            catch (ArgumentException)
            {
                // プロセスが見つからない（終了している）場合
                return false;
            }
        }

        /// <summary>
        /// 正常なシャットダウン処理を実行
        /// </summary>
        public async Task PerformGracefulShutdownAsync()
        {
            try
            {
                // ウィンドウを最前面に表示
                this.Show();
                if (WindowState == WindowState.Minimized)
                {
                    WindowState = WindowState.Normal;
                }
                this.Topmost = true;
                this.Activate();

                // LLMが有効かどうかを確認してシャットダウンメッセージを設定
                var currentCharacter = GetStoredCharacterSetting();
                bool isLLMEnabled = currentCharacter?.isUseLLM ?? false;

                if (!isLLMEnabled)
                {
                    // LLMが無効の場合は「記憶を整理しています」を非表示に
                    if (ShutdownOverlay.FindName("MemoryCleanupText") is System.Windows.Controls.TextBlock memoryText)
                    {
                        memoryText.Visibility = Visibility.Collapsed;
                    }
                }

                // シャットダウンオーバーレイを表示
                ShutdownOverlay.Visibility = Visibility.Visible;

                // MobileWebSocketServerを停止
                if (_mobileWebSocketServer != null)
                {
                    Debug.WriteLine("MobileWebSocketServerを停止中...");
                    try
                    {
                        // イベント購読解除
                        _mobileWebSocketServer.MobileMessageReceived -= OnMobileMessageReceived;

                        await _mobileWebSocketServer.StopAsync();
                        _mobileWebSocketServer.Dispose();
                        _mobileWebSocketServer = null;
                        Debug.WriteLine("MobileWebSocketServer停止完了");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"MobileWebSocketServer停止エラー: {ex.Message}");
                    }
                }

                // CocoreCoreMのプロセスIDを事前に取得
                int? CocoroCoreMProcessId = GetProcessIdByPort(_appSettings.CocoroCorePort);
                Debug.WriteLine($"CocoroCoreM プロセスID: {CocoroCoreMProcessId?.ToString() ?? "見つかりません"}");

                // CocoroShellとCoreMに並行してシャットダウン要求を送信
                Debug.WriteLine("CocoroShellとCocoreCoreMに終了要求を送信中...");

                // CocoroShellとCocoreCoreMに並行してシャットダウン要求を送信
                var shutdownTasks = new[]
                {
                    Task.Run(() => ProcessHelper.ExitProcess("CocoroShell", ProcessOperation.Terminate)),
                    Task.Run(() => ProcessHelper.ExitProcess("CocoroCoreM", ProcessOperation.Terminate))
                };

                // すべてのシャットダウン要求の完了を待つ（最大5秒）
                try
                {
                    await Task.WhenAll(shutdownTasks).WaitAsync(TimeSpan.FromSeconds(5));
                }
                catch (TimeoutException)
                {
                    Debug.WriteLine("一部のシャットダウン要求がタイムアウトしました。");
                }

                // CocoreCoreM プロセスの確実な終了を待機
                if (CocoroCoreMProcessId.HasValue)
                {
                    Debug.WriteLine("CocoreCoreM プロセスの終了を監視中...");
                    var maxWaitTime = TimeSpan.FromSeconds(120);
                    var startTime = DateTime.Now;

                    while (IsProcessRunning(CocoroCoreMProcessId.Value))
                    {
                        if (DateTime.Now - startTime > maxWaitTime)
                        {
                            Debug.WriteLine("CocoreCoreMの終了待機がタイムアウトしました。");
                            break;
                        }

                        await Task.Delay(500); // 0.5秒間隔でチェック
                    }

                    Debug.WriteLine("CocoreCoreM プロセスの終了を確認しました。");
                }
                else
                {
                    Debug.WriteLine("CocoreCoreM プロセスが見つからなかったため、通常の監視を実行します。");

                    // プロセスIDが取得できない場合は疎通確認で監視
                    var maxWaitTime = TimeSpan.FromSeconds(120);
                    var startTime = DateTime.Now;

                    while (_communicationService != null && _communicationService.CurrentStatus != CocoroCoreMStatus.WaitingForStartup)
                    {
                        if (DateTime.Now - startTime > maxWaitTime)
                        {
                            Debug.WriteLine("CocoreCoreMの終了待機がタイムアウトしました。");
                            break;
                        }

                        await Task.Delay(100);
                    }

                    Debug.WriteLine("CocoreCoreMの動作停止を確認しました。");
                }

                // オーバーレイを非表示
                ShutdownOverlay.Visibility = Visibility.Collapsed;

                // アプリケーションを終了
                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"シャットダウン処理中にエラーが発生しました: {ex.Message}");

                // エラーが発生してもオーバーレイを非表示
                ShutdownOverlay.Visibility = Visibility.Collapsed;

                Application.Current.Shutdown();
            }
        }
    }
}