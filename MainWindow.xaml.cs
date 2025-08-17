using CocoroDock.Communication;
using CocoroDock.Controls;
using CocoroDock.Services;
using CocoroDock.Utilities;
using CocoroAI.Services;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

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

                // スクリーンショットサービスを初期化
                InitializeScreenshotService();

                // 音声認識サービスを初期化
                InitializeVoiceRecognitionService();

                // UIコントロールのイベントハンドラを登録
                RegisterEventHandlers();

                // ボタンの初期状態を設定
                InitializeButtonStates();

                // 初期ステータス表示
                if (_communicationService != null)
                {
                    UpdateCocoroCore2StatusDisplay(_communicationService.CurrentStatus);
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
            var currentCharacter = GetCurrentCharacterSettings();
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
            // CocoroCore2.exeを起動（既に起動していれば終了してから再起動）
            LaunchCocoroCore2();
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
            _communicationService.StatusUpdateRequested += OnStatusUpdateRequested;
            _communicationService.StatusChanged += OnCocoroCore2StatusChanged;
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
                    async (screenshotData) => await OnScreenshotCaptured(screenshotData)
                );

                _screenshotService.CaptureActiveWindowOnly = screenshotSettings.captureActiveWindowOnly;
                _screenshotService.IdleTimeoutMinutes = screenshotSettings.idleTimeoutMinutes;


                // サービスを開始
                _screenshotService.Start();

                Debug.WriteLine($"スクリーンショットサービスを開始しました（間隔: {screenshotSettings.intervalMinutes}分）");
            }
        }

        /// <summary>
        /// スクリーンショットが撮影された時の処理
        /// </summary>
        private async Task OnScreenshotCaptured(ScreenshotData screenshotData)
        {
            try
            {
                // 画像を表示
                UIHelper.RunOnUIThread(() =>
                {
                    ChatControlInstance.AddDesktopMonitoringImage(screenshotData.ImageBase64);
                });

                // CommunicationServiceを使用してデスクトップモニタリングを送信
                if (_communicationService != null && _communicationService.IsServerRunning)
                {
                    // デスクトップモニタリング用の送信処理（WebSocket使用）
                    // 画像データをdata URL形式に変換
                    var imageDataUrl = $"data:image/png;base64,{screenshotData.ImageBase64}";
                    await _communicationService.SendChatToCoreUnifiedAsync("", null, imageDataUrl);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"デスクトップモニタリング処理エラー: {ex.Message}");
            }
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
        /// CocoroCore2ステータスに基づいて表示を更新
        /// </summary>
        /// <param name="status">CocoroCore2のステータス</param>
        private void UpdateCocoroCore2StatusDisplay(CocoroCore2Status status)
        {
            string statusText = status switch
            {
                CocoroCore2Status.WaitingForStartup => "CocoroCore2起動待ち",
                CocoroCore2Status.Normal => "正常動作中",
                CocoroCore2Status.ProcessingMessage => "LLMメッセージ処理中",
                CocoroCore2Status.ProcessingImage => "LLM画像処理中",
                _ => "不明な状態"
            };

            ConnectionStatusText.Text = $"状態: {statusText}";

            // 送信ボタンの有効/無効を制御
            bool isSendEnabled = status != CocoroCore2Status.WaitingForStartup;
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
                // 最前面表示の設定を適用
                Topmost = _appSettings.IsTopmost;

                // その他の設定はここに追加（必要に応じて）
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
            // マイク関連設定が変更された可能性があるので、音声認識サービスを再初期化
            if (_voiceRecognitionService != null)
            {
                bool wasListening = _voiceRecognitionService.IsListening;
                var currentState = _voiceRecognitionService.CurrentState;

                // 現在のサービスを停止・破棄
                _voiceRecognitionService.StopListening();
                _voiceRecognitionService.Dispose();
                _voiceRecognitionService = null;

                // 新しい設定で再初期化（必要に応じて開始状態を復元）
                bool startActive = currentState == VoiceRecognitionState.ACTIVE;
                InitializeVoiceRecognitionService(startActive);

                Debug.WriteLine("[MainWindow] 設定変更により音声認識サービスを再初期化しました");
            }
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
        /// ステータス更新要求時のハンドラ
        /// </summary>
        private void OnStatusUpdateRequested(object? sender, StatusUpdateEventArgs e)
        {
            // 新仕様では古いステータスメッセージは使用しない
            // 必要に応じてログ出力のみ
            if (!string.IsNullOrEmpty(e.Message))
            {
                Debug.WriteLine($"[StatusUpdate] {e.Message}");
            }
        }

        /// <summary>
        /// CocoroCore2ステータス変更時のハンドラ
        /// </summary>
        private void OnCocoroCore2StatusChanged(object? sender, CocoroCore2Status status)
        {
            UIHelper.RunOnUIThread(() =>
            {
                UpdateCocoroCore2StatusDisplay(status);
            });
        }

        #endregion

        /// <summary>
        /// アプリケーション終了時の処理
        /// </summary>
        protected override void OnClosed(EventArgs e)
        {
            try
            {
                // イベントハンドラの購読解除
                AppSettings.SettingsSaved -= OnSettingsSaved;

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
                    Task.Run(() => LaunchCocoroCore2(ProcessOperation.Terminate)),
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
                // 設定画面を表示
                var adminWindow = new AdminWindow(_communicationService);
                adminWindow.Owner = this; // メインウィンドウを親に設定

                // ウィンドウが閉じられた時にボタンの状態を更新
                adminWindow.Closed += AdminWindow_Closed;

                adminWindow.Show(); // モードレスダイアログとして表示
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
            var currentCharacter = GetCurrentCharacterSettings();
            if (currentCharacter != null)
            {
                currentCharacter.isUseSTT = !currentCharacter.isUseSTT;

                // 設定を保存
                _appSettings.SaveSettings();

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

                    // 無効状態の場合は半透明にする
                    MicButton.Opacity = currentCharacter.isUseSTT ? 1.0 : 0.6;
                }

                // 音声認識サービスの開始/停止
                if (currentCharacter.isUseSTT)
                {
                    // STTを有効にする場合は音声認識を開始（MicButton切り替えなのでACTIVE状態から開始）
                    InitializeVoiceRecognitionService(startActive: true);
                }
                else
                {
                    // STTを無効にする場合は音声認識を停止
                    if (_voiceRecognitionService != null)
                    {
                        _voiceRecognitionService.Dispose();
                        _voiceRecognitionService = null;
                    }

                    // 音量バーを0にリセット（UIスレッドで確実に実行）
                    UIHelper.RunOnUIThread(() =>
                    {
                        ChatControlInstance.UpdateVoiceLevel(0, false);
                    });
                }
            }
        }

        /// <summary>
        /// 現在のキャラクター設定を取得
        /// </summary>
        private CharacterSettings? GetCurrentCharacterSettings()
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
            var currentCharacter = GetCurrentCharacterSettings();
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
        /// CocoroCore2.exeを起動する（既に起動している場合は終了してから再起動）
        /// </summary>
        /// <param name="operation">プロセス操作の種類（デフォルトは再起動）</param>
        private void LaunchCocoroCore2(ProcessOperation operation = ProcessOperation.RestartIfRunning)
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
                    ProcessHelper.LaunchExternalApplication("CocoroCore2.exe", "CocoroCore2", operation, false);
#endif
                    // 非同期でAPI通信による起動完了を監視（無限ループ）
                    _ = Task.Run(async () =>
                    {
                        await WaitForCocoroCore2StartupAsync();
                    });
                }
                else
                {
                    ProcessHelper.LaunchExternalApplication("CocoroCore2.exe", "CocoroCore2", operation, false);
                }
            }
            else
            {
                // LLMを使用しない場合はCocoroCore2を終了
                ProcessHelper.LaunchExternalApplication("CocoroCore2.exe", "CocoroCore2", ProcessOperation.Terminate, false);
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
                var currentCharacter = GetCurrentCharacterSettings();
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

                if (string.IsNullOrEmpty(currentCharacter.sttWakeWord))
                {
                    Debug.WriteLine("[MainWindow] ウェイクアップワードが未設定");
                    // 音量バーを0にリセット（UIスレッドで確実に実行）
                    UIHelper.RunOnUIThread(() =>
                    {
                        ChatControlInstance.UpdateVoiceLevel(0, false);
                    });
                    return;
                }

                // 音声処理パラメータ
                // 無音区間判定用の閾値（dB値）
                float inputThresholdDb = _appSettings.MicrophoneSettings?.inputThreshold ?? -45.0f;
                // 音声検出用の閾値（振幅比率に変換）
                float voiceThreshold = (float)(Math.Pow(10, inputThresholdDb / 20.0));
                const int silenceTimeoutMs = 500; // 高速化のため短縮
                const int activeTimeoutMs = 60000;

                _voiceRecognitionService = new RealtimeVoiceRecognitionService(
                    currentCharacter.sttApiKey,
                    currentCharacter.sttWakeWord,
                    voiceThreshold,
                    silenceTimeoutMs,
                    activeTimeoutMs,
                    startActive
                );

                // イベント購読
                _voiceRecognitionService.OnRecognizedText += OnVoiceRecognized;
                _voiceRecognitionService.OnStateChanged += OnVoiceStateChanged;
                _voiceRecognitionService.OnVoiceLevel += OnVoiceLevelChanged;

                // 音声認識開始
                _voiceRecognitionService.StartListening();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MainWindow] 音声認識サービス初期化エラー: {ex.Message}");
            }
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

                // CocoroCore2に送信
                SendMessageToCocoroCore(text, null);

                Debug.WriteLine($"[MainWindow] 音声認識結果: {text}");
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
        /// CocoroCore2にメッセージを送信
        /// </summary>
        private async void SendMessageToCocoroCore(string message, string? imageData)
        {
            try
            {
                if (_communicationService != null)
                {
                    var currentCharacter = GetCurrentCharacterSettings();
                    if (currentCharacter != null && currentCharacter.isUseLLM)
                    {
                        await _communicationService.SendChatToCoreUnifiedAsync(message, currentCharacter.modelName, imageData);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MainWindow] CocoroCore2送信エラー: {ex.Message}");
            }
        }

        /// <summary>
        /// CocoroCore2のAPI起動完了を監視（無限ループ）
        /// </summary>
        private async Task WaitForCocoroCore2StartupAsync()
        {
            var delay = TimeSpan.FromSeconds(1); // 1秒間隔でチェック

            while (true)
            {
                try
                {
                    if (_communicationService != null)
                    {
                        // StatusPollingServiceのステータスで起動状態を確認
                        if (_communicationService.CurrentStatus == CocoroCore2Status.Normal ||
                            _communicationService.CurrentStatus == CocoroCore2Status.ProcessingMessage ||
                            _communicationService.CurrentStatus == CocoroCore2Status.ProcessingImage)
                        {
                            // 起動成功時はログ出力のみ
                            Debug.WriteLine("[MainWindow] CocoroCore2起動完了");
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

                // シャットダウンオーバーレイを表示
                ShutdownOverlay.Visibility = Visibility.Visible;

                // CocoroCore2のプロセスIDを事前に取得
                int? cocoroCore2ProcessId = GetProcessIdByPort(_appSettings.CocoroCorePort);
                Debug.WriteLine($"CocoroCore2プロセスID: {cocoroCore2ProcessId?.ToString() ?? "見つかりません"}");

                // CocoroShellとCocoroCore2にシャットダウン要求を送信
                Debug.WriteLine("CocoroShellとCocoroCore2に終了要求を送信中...");

                // CocoroShellとCocoroCore2に並行してシャットダウン要求を送信
                var shutdownTasks = new[]
                {
                    Task.Run(() => ProcessHelper.ExitProcess("CocoroShell", ProcessOperation.Terminate)),
                    Task.Run(() => ProcessHelper.ExitProcess("CocoroCore2", ProcessOperation.Terminate))
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

                // CocoroCore2プロセスの確実な終了を待機
                if (cocoroCore2ProcessId.HasValue)
                {
                    Debug.WriteLine("CocoroCore2プロセスの終了を監視中...");
                    var maxWaitTime = TimeSpan.FromSeconds(120);
                    var startTime = DateTime.Now;

                    while (IsProcessRunning(cocoroCore2ProcessId.Value))
                    {
                        if (DateTime.Now - startTime > maxWaitTime)
                        {
                            Debug.WriteLine("CocoroCore2の終了待機がタイムアウトしました。");
                            break;
                        }

                        await Task.Delay(500); // 0.5秒間隔でチェック
                    }

                    Debug.WriteLine("CocoroCore2プロセスの終了を確認しました。");
                }
                else
                {
                    Debug.WriteLine("CocoroCore2プロセスが見つからなかったため、通常の監視を実行します。");

                    // プロセスIDが取得できない場合は疎通確認で監視
                    var maxWaitTime = TimeSpan.FromSeconds(120);
                    var startTime = DateTime.Now;

                    while (_communicationService != null && _communicationService.CurrentStatus != CocoroCore2Status.WaitingForStartup)
                    {
                        if (DateTime.Now - startTime > maxWaitTime)
                        {
                            Debug.WriteLine("CocoroCore2の終了待機がタイムアウトしました。");
                            break;
                        }

                        await Task.Delay(100);
                    }

                    Debug.WriteLine("CocoroCore2の動作停止を確認しました。");
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