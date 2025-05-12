using CocoroDock.Communication;
using CocoroDock.Controls;
using CocoroDock.Services;
using System;
using System.Threading; // Timerを使用するために追加
using System.Threading.Tasks;
using System.Windows;

namespace CocoroDock
{
    /// <summary>
    /// MainWindow.xaml の相互作用ロジック
    /// </summary>
    public partial class MainWindow : Window
    {
        private CommunicationService? _communicationService;
        private Timer? _reconnectTimer; // 再接続用タイマー
        private const int ReconnectIntervalMs = 3000; // 再接続間隔（3秒）

        public MainWindow()
        {
            InitializeComponent();

            // ウィンドウのロード時にメッセージテキストボックスにフォーカスを設定するイベントを追加
            this.Loaded += MainWindow_Loaded;

            // ウィンドウの状態変更イベントを監視
            this.StateChanged += MainWindow_StateChanged;

            // ウィンドウのCloseButtonクリック時（×ボタン）のイベントを監視
            this.Closing += MainWindow_Closing;

            // 初期化と接続
            InitializeApp();
        }

        /// <summary>
        /// ウィンドウの閉じるボタンクリック時のイベントハンドラ
        /// </summary>
        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            // 閉じる操作をキャンセルし、代わりにウィンドウを非表示にする
            e.Cancel = true;
            this.Hide();
        }

        /// <summary>
        /// ウィンドウの状態変更時のイベントハンドラ
        /// </summary>
        private void MainWindow_StateChanged(object? sender, EventArgs e)
        {
            // ウィンドウが最小化されたら非表示にする
            if (this.WindowState == WindowState.Minimized)
            {
                this.Hide();
            }
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
                // AppSettingsから設定を取得
                var settings = AppSettings.Instance;

                // 通信サービスを初期化 (WebSocketServerを使用)
                _communicationService = new CommunicationService(
                    settings.CocoroDockPort);

                // 通信サービスのイベントハンドラを設定
                _communicationService.ChatMessageReceived += OnChatMessageReceived;
                _communicationService.ConfigResponseReceived += OnConfigResponseReceived;
                _communicationService.StatusUpdateReceived += OnStatusUpdateReceived;
                _communicationService.SystemMessageReceived += OnSystemMessageReceived;
                _communicationService.ErrorOccurred += OnErrorOccurred;
                _communicationService.Connected += OnConnected;
                _communicationService.Disconnected += OnDisconnected;

                // UIコントロールのイベントハンドラを登録
                RegisterEventHandlers();

                // サーバーの起動を開始
                _ = ConnectToServiceAsync();
            }
            catch (Exception ex)
            {
                ShowError("初期化エラー", ex.Message);
            }
        }

        /// <summary>
        /// サービスに接続（非同期タスク）
        /// </summary>
        private async Task ConnectToServiceAsync()
        {
            try
            {
                // UI更新
                UpdateConnectionStatus(false, "サーバーを起動中...");

                if (_communicationService != null)
                {
                    if (_communicationService != null && !_communicationService.IsConnected)
                    {
                        // サーバーを起動
                        await _communicationService.ConnectAsync();

                        // UI更新
                        UpdateConnectionStatus(true);

                        // 設定を要求
                        await RequestConfigAsync();
                    }
                }
            }
            catch (Exception)
            {
                UpdateConnectionStatus(false, "サーバー起動エラー");

                // 再接続タイマーを開始
                StartReconnectTimer();
            }
        }

        /// <summary>
        /// サーバーから設定情報を要求
        /// </summary>
        private async Task RequestConfigAsync()
        {
            try
            {
                // 設定読み込み状態をリセット
                AppSettings.Instance.IsLoaded = false;

                // サーバー側で管理している設定ファイルを読み込む
                AppSettings.Instance.LoadAppSettings();

                // 設定をUIに反映
                ApplySettings();

                // クライアントにも設定を通知
                if (_communicationService != null && _communicationService.IsConnected)
                {
                    await _communicationService.UpdateConfigAsync(AppSettings.Instance.GetConfigSettings());
                }
            }
            catch (Exception ex)
            {
                ShowError("設定取得エラー", ex.Message);
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
            RunOnUIThread(() =>
            {
                if (isConnected)
                {
                    ConnectionStatusText.Text = "接続状態: 動作中";
                }
                else
                {
                    string statusText = customMessage ?? "停止中";
                    ConnectionStatusText.Text = $"接続状態: {statusText}";
                }
            });
        }

        /// <summary>
        /// エラーをメッセージボックスで表示
        /// </summary>
        private void ShowError(string title, string message)
        {
            RunOnUIThread(() =>
            {
                MessageBox.Show($"{title}: {message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            });
        }

        /// <summary>
        /// UIスレッドでアクションを実行
        /// </summary>
        private void RunOnUIThread(Action action)
        {
            if (Application.Current?.Dispatcher != null)
            {
                if (Application.Current.Dispatcher.CheckAccess())
                {
                    action();
                }
                else
                {
                    Application.Current.Dispatcher.Invoke(action);
                }
            }
        }

        /// <summary>
        /// 設定を適用
        /// </summary>
        private void ApplySettings()
        {
            var settings = AppSettings.Instance;
            RunOnUIThread(() =>
            {
                // 最前面表示の設定を適用
                Topmost = settings.IsTopmost;

                // その他の設定はここに追加（必要に応じて）
            });
        }

        #region チャットコントロールイベントハンドラ

        /// <summary>
        /// チャットメッセージ送信時のハンドラ
        /// </summary>
        private async void OnChatMessageSent(object? sender, string message)
        {
            try
            {
                // WebSocketが接続されている場合のみ送信
                if (_communicationService != null && _communicationService.IsConnected)
                {
                    await _communicationService.SendChatMessageAsync(message);
                }
                else
                {
                    ChatControlInstance.AddSystemErrorMessage("接続が確立されていません");
                }
            }
            catch (Exception ex)
            {
                ChatControlInstance.AddSystemErrorMessage($"エラー: {ex.Message}");
            }
        }

        #endregion

        #region 通信サービスイベントハンドラ

        /// <summary>
        /// チャットメッセージ受信時のハンドラ
        /// </summary>
        private void OnChatMessageReceived(object? sender, string message)
        {
            RunOnUIThread(() => ChatControlInstance.AddAiMessage(message));
        }

        /// <summary>
        /// 設定レスポンス受信時のハンドラ
        /// </summary>
        private void OnConfigResponseReceived(object? sender, ConfigResponsePayload response)
        {
            RunOnUIThread(() =>
            {
                // 応答ステータスをチェック
                if (response.status != "ok")
                {
                    // エラーの場合はメッセージを表示
                    MessageBox.Show($"設定変更エラー: {response.message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // 設定情報が含まれている場合は適用する
                if (response.settings != null)
                {
                    // アプリケーション設定を更新
                    AppSettings.Instance.UpdateSettings(response.settings);

                    // 設定を画面に反映
                    ApplySettings();
                }
            });
        }

        /// <summary>
        /// 状態更新受信時のハンドラ
        /// </summary>
        private void OnStatusUpdateReceived(object? sender, StatusMessagePayload status)
        {
            // 必要なステータス処理を実装する場合はここに追加
        }

        /// <summary>
        /// システムメッセージ受信時のハンドラ
        /// </summary>
        private void OnSystemMessageReceived(object? sender, SystemMessagePayload systemMessage)
        {
            // levelがerrorの場合のみ処理する（Infoは無視）
            if (systemMessage.level == "Error")
            {
                RunOnUIThread(() =>
                {
                    // エラーメッセージをチャットウィンドウに表示（中央グレー枠）
                    ChatControlInstance.AddSystemErrorMessage(systemMessage.message);
                });
            }
            // その他のレベル（Info等）は無視
        }

        /// <summary>
        /// エラー発生時のハンドラ
        /// </summary>
        private void OnErrorOccurred(object? sender, string error)
        {
            ShowError("エラー", error);
        }

        /// <summary>
        /// 接続成功時のハンドラ
        /// </summary>
        private void OnConnected(object? sender, EventArgs e)
        {
            UpdateConnectionStatus(true);
            StopReconnectTimer(); // 再接続タイマーを停止
        }

        /// <summary>
        /// 切断時のハンドラ
        /// </summary>
        private void OnDisconnected(object? sender, EventArgs e)
        {
            UpdateConnectionStatus(false);
            StartReconnectTimer(); // 再接続タイマーを開始
        }

        #endregion

        /// <summary>
        /// アプリケーション終了時の処理
        /// </summary>
        protected override void OnClosed(EventArgs e)
        {
            try
            {
                // 接続中ならリソース解放
                if (_communicationService != null)
                {
                    _communicationService.Dispose();
                    _communicationService = null;
                }
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
        /// 管理ボタンクリック時のイベントハンドラ
        /// </summary>
        private void AdminButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 管理画面を表示
                var adminWindow = new AdminWindow();
                adminWindow.Owner = this; // メインウィンドウを親に設定
                adminWindow.ShowDialog(); // モーダルダイアログとして表示
            }
            catch (Exception ex)
            {
                ShowError("設定取得エラー", ex.Message);
            }
        }

        /// <summary>
        /// 再接続タイマーを開始
        /// </summary>
        private void StartReconnectTimer()
        {
            if (_reconnectTimer == null)
            {
                _reconnectTimer = new Timer(async _ =>
                {
                    // 接続処理中でない場合のみ再接続を試みる
                    if (_communicationService != null && !_communicationService.IsConnected)
                    {
                        await ConnectToServiceAsync();
                    }
                }, null, ReconnectIntervalMs, ReconnectIntervalMs);
            }
        }

        /// <summary>
        /// 再接続タイマーを停止
        /// </summary>
        private void StopReconnectTimer()
        {
            _reconnectTimer?.Change(Timeout.Infinite, Timeout.Infinite);
            _reconnectTimer = null;
        }
    }
}