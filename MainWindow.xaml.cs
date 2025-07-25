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
        private Timer? _statusMessageTimer;
        private readonly List<StatusMessage> _statusMessages = new List<StatusMessage>();
        private readonly object _statusLock = new object();
        private const int MaxStatusMessages = 5; // 最大表示メッセージ数
        private ScreenshotService? _screenshotService;
        private bool _isScreenshotPaused = false;

        private class StatusMessage
        {
            public string Message { get; set; }
            public Timer Timer { get; set; } = null!;

            public StatusMessage(string message)
            {
                Message = message;
            }
        }

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

                // UIコントロールのイベントハンドラを登録
                RegisterEventHandlers();

                // ボタンの初期状態を設定
                InitializeButtonStates();

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
#if !DEBUG
            // CocoroShell.exeを起動（既に起動していれば終了してから再起動）
            LaunchCocoroShell();
            // CocoroCore.exeを起動（既に起動していれば終了してから再起動）
            LaunchCocoroCore();
            // CocoroMemory.exeを起動（既に起動していれば終了してから再起動）
            LaunchCocoroMemory();
#endif
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
                    // デスクトップモニタリング用の送信処理
                    await _communicationService.SendDesktopMonitoringToCoreAsync(screenshotData.ImageBase64);
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
                // UI更新
                UpdateConnectionStatus(false, "サーバーを起動中...");

                if (_communicationService != null && !_communicationService.IsServerRunning)
                {
                    // APIサーバーを起動
                    await _communicationService.StartServerAsync();

                    // UI更新
                    UpdateConnectionStatus(true);
                }
            }
            catch (Exception ex)
            {
                UpdateConnectionStatus(false, "サーバー起動エラー");
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
        }

        /// <summary>
        /// 接続ステータス表示を更新
        /// </summary>
        private void UpdateConnectionStatus(bool isConnected, string? customMessage = null)
        {
            // UIスレッドで実行
            UIHelper.RunOnUIThread(() =>
            {
                if (!string.IsNullOrEmpty(customMessage))
                {
                    // カスタムメッセージがある場合は履歴に追加
                    AddStatusMessage(customMessage);
                }
                else
                {
                    // 切断状態
                    string statusText = "停止中";
                    lock (_statusLock)
                    {
                        // すべてのタイマーを破棄
                        foreach (var msg in _statusMessages)
                        {
                            msg.Timer?.Dispose();
                        }
                        _statusMessages.Clear();
                        ConnectionStatusText.Text = $"状態: {statusText}";
                    }
                }
            });
        }

        /// <summary>
        /// ステータスメッセージを履歴に追加して表示
        /// </summary>
        private void AddStatusMessage(string message)
        {
            lock (_statusLock)
            {
                // 既存の同じメッセージを探す
                var existingMessage = _statusMessages.FirstOrDefault(m => m.Message == message);

                if (existingMessage != null)
                {
                    // 既存のメッセージがある場合はタイマーをリセット
                    existingMessage.Timer?.Dispose();
                    existingMessage.Timer = CreateMessageTimer(message);
                }
                else
                {
                    // 新しいメッセージを追加
                    var newMessage = new StatusMessage(message);
                    newMessage.Timer = CreateMessageTimer(message);
                    _statusMessages.Add(newMessage);

                    // 最大数を超えたら古いメッセージを削除
                    while (_statusMessages.Count > MaxStatusMessages)
                    {
                        var oldestMessage = _statusMessages[0];
                        oldestMessage.Timer?.Dispose();
                        _statusMessages.RemoveAt(0);
                    }
                }

                // 表示を更新
                UpdateStatusDisplay();
            }
        }

        /// <summary>
        /// メッセージ用のタイマーを作成
        /// </summary>
        private Timer CreateMessageTimer(string message)
        {
            return new Timer(_ =>
            {
                UIHelper.RunOnUIThread(() =>
                {
                    RemoveStatusMessage(message);
                });
            }, null, 3000, Timeout.Infinite); // 3秒後に削除
        }

        /// <summary>
        /// 特定のステータスメッセージを削除
        /// </summary>
        private void RemoveStatusMessage(string message)
        {
            lock (_statusLock)
            {
                var messageToRemove = _statusMessages.FirstOrDefault(m => m.Message == message);
                if (messageToRemove != null)
                {
                    messageToRemove.Timer?.Dispose();
                    _statusMessages.Remove(messageToRemove);
                    UpdateStatusDisplay();
                }
            }
        }

        /// <summary>
        /// ステータス表示を更新
        /// </summary>
        private void UpdateStatusDisplay()
        {
            if (_statusMessages.Count == 0)
            {
                ConnectionStatusText.Text = "状態: 正常動作中";
            }
            else
            {
                // 最新のメッセージを左に、古いメッセージを右に表示
                var messages = _statusMessages.Select(m => m.Message).Reverse().ToArray();
                ConnectionStatusText.Text = $"状態: {string.Join(" | ", messages)}";
            }
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

            // 添付画像を取得（あれば）
            var imageSource = ChatControlInstance.GetAttachedImageSource();
            string? imageDataUrl = ChatControlInstance.GetAndClearAttachedImage();

            // ユーザーメッセージとしてチャットウィンドウに表示（送信前に表示）
            ChatControlInstance.AddUserMessage(message, imageSource);

            // 非同期でCocoroCoreにメッセージを送信（UIをブロックしない）
            _ = Task.Run(async () =>
            {
                try
                {
                    // CocoroCoreにメッセージを送信（画像付きの場合は画像データも送信）
                    await _communicationService.SendChatToCoreAsync(message, null, imageDataUrl);
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
                ChatControlInstance.AddNotificationMessage(notification.userId, notification.message, imageSources);
            });
        }


        /// <summary>
        /// 制御コマンド受信時のハンドラ（CocoroDock APIから）
        /// </summary>
        private void OnControlCommandReceived(object? sender, ControlRequest request)
        {
            UIHelper.RunOnUIThread(() =>
            {
                switch (request.command)
                {
                    case "shutdown":
                        // シャットダウン理由をログに記録
                        Debug.WriteLine($"シャットダウン要求を受信しました: {request.reason}");
                        Application.Current.Shutdown();
                        break;

                    default:
                        Debug.WriteLine($"未知の制御コマンド: {request.command}");
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
            UpdateConnectionStatus(e.IsConnected, e.Message);
        }

        #endregion

        /// <summary>
        /// アプリケーション終了時の処理
        /// </summary>
        protected override void OnClosed(EventArgs e)
        {
            try
            {
                // タイマーのクリーンアップ
                _statusMessageTimer?.Dispose();
                _statusMessageTimer = null;

                // すべてのステータスメッセージタイマーを破棄
                lock (_statusLock)
                {
                    foreach (var msg in _statusMessages)
                    {
                        msg.Timer?.Dispose();
                    }
                    _statusMessages.Clear();
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
        /// 外部アプリケーション（CocoroCore, CocoroShell, CocoroMemory）を終了する
        /// </summary>
        private void TerminateExternalApplications()
        {
            try
            {
                // 3つのプロセスを並行して終了させる
                var tasks = new[]
                {
                    Task.Run(() => LaunchCocoroCore(ProcessOperation.Terminate)),
                    Task.Run(() => LaunchCocoroShell(ProcessOperation.Terminate)),
                    Task.Run(() => LaunchCocoroMemory(ProcessOperation.Terminate))
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

                AddStatusMessage(_isScreenshotPaused ? "デスクトップウォッチを無効にしました" : "デスクトップウォッチを有効にしました");
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

                // CocoroCoreにSTT状態を送信
                _ = Task.Run(async () =>
                {
                    try
                    {
                        if (_communicationService != null)
                        {
                            // STT設定をCocoroCoreに送信
                            await _communicationService.SendSTTStateToCoreAsync(currentCharacter.isUseSTT);

                            UIHelper.RunOnUIThread(() =>
                            {
                                AddStatusMessage(currentCharacter.isUseSTT ? "STTを有効にしました" : "STTを無効にしました");
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"STT状態の送信エラー: {ex.Message}");
                    }
                });
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

                            UIHelper.RunOnUIThread(() =>
                            {
                                AddStatusMessage(currentCharacter.isUseTTS ? "TTSを有効にしました" : "TTSを無効にしました");
                            });
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
                ProcessHelper.LaunchExternalApplication("CocoroShell.exe", "CocoroShell", operation);
            }
            else
            {
                ProcessHelper.LaunchExternalApplication("CocoroShell.exe", "CocoroShell", ProcessOperation.Terminate);
            }
#endif
        }

        /// <summary>
        /// CocoroCore.exeを起動する（既に起動している場合は終了してから再起動）
        /// </summary>
        /// <param name="operation">プロセス操作の種類（デフォルトは再起動）</param>
        private void LaunchCocoroCore(ProcessOperation operation = ProcessOperation.RestartIfRunning)
        {
#if !DEBUG
            if (_appSettings.CharacterList.Count > 0 &&
               _appSettings.CurrentCharacterIndex < _appSettings.CharacterList.Count &&
               _appSettings.CharacterList[_appSettings.CurrentCharacterIndex].isUseLLM)
            {
                ProcessHelper.LaunchExternalApplication("CocoroCore.exe", "CocoroCore", operation);
            }
            else
            {
                ProcessHelper.LaunchExternalApplication("CocoroCore.exe", "CocoroCore", ProcessOperation.Terminate);
            }
#endif
        }

        /// <summary>
        /// CocoroMemory.exeを起動する（既に起動している場合は終了してから再起動）
        /// </summary>
        /// <param name="operation">プロセス操作の種類（デフォルトは再起動）</param>
        private void LaunchCocoroMemory(ProcessOperation operation = ProcessOperation.RestartIfRunning)
        {
#if !DEBUG
            // 現在のキャラクターが有効で、記憶機能が有効な場合のみ起動
            if (_appSettings.CharacterList.Count > 0 &&
               _appSettings.CurrentCharacterIndex < _appSettings.CharacterList.Count &&
               _appSettings.CharacterList[_appSettings.CurrentCharacterIndex].isEnableMemory)
            {
                ProcessHelper.LaunchExternalApplication("CocoroMemory.exe", "CocoroMemory", operation);
            }
            else
            {
                // 記憶機能が無効な場合は終了
                ProcessHelper.LaunchExternalApplication("CocoroMemory.exe", "CocoroMemory", ProcessOperation.Terminate);
            }
#endif
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
                base.OnClosing(e);
            }
        }
    }
}