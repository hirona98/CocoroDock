using CocoroDock.Communication;
using CocoroDock.Controls;
using CocoroDock.Services;
using CocoroDock.Utilities;
using System;
using System.Diagnostics;
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
        private Timer? _reconnectTimer; // 再接続用タイマー
        private const int ReconnectIntervalMs = 3000; // 再接続間隔（3秒）
        private readonly IAppSettings _appSettings;

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

                // UIコントロールのイベントハンドラを登録
                RegisterEventHandlers();

                // サーバーの起動を開始
                _ = StartWebSocketServerAsync();
            }
            catch (Exception ex)
            {
                UIHelper.ShowError("初期化エラー", ex.Message);
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
            // 通信サービスを初期化 (WebSocketServerを使用)
            _communicationService = new CommunicationService(_appSettings.CocoroDockPort);

            // 通信サービスのイベントハンドラを設定
            _communicationService.ChatMessageReceived += OnChatMessageReceived;
            _communicationService.ConfigResponseReceived += OnConfigResponseReceived;
            _communicationService.StatusUpdateReceived += OnStatusUpdateReceived;
            _communicationService.SystemMessageReceived += OnSystemMessageReceived;
            _communicationService.ControlMessageReceived += OnControlMessageReceived;
            _communicationService.ErrorOccurred += OnErrorOccurred;
            _communicationService.Connected += OnConnected;
            _communicationService.Disconnected += OnDisconnected;
        }

        /// <summary>
        /// WebSocketサーバーを起動（非同期タスク）
        /// </summary>
        private async Task StartWebSocketServerAsync()
        {
            try
            {
                // UI更新
                UpdateConnectionStatus(false, "サーバーを起動中...");

                if (_communicationService != null && !_communicationService.IsServerRunning)
                {
                    // サーバーを起動
                    await _communicationService.StartServerAsync();

                    // UI更新
                    UpdateConnectionStatus(true);

                    // 設定を通知
                    await RequestConfigAsync();
                }
            }
            catch (Exception)
            {
                UpdateConnectionStatus(false, "サーバー起動エラー");

                // 再起動タイマーを開始
                StartServerRestartTimer();
            }
        }

        /// <summary>
        /// クライアントに設定情報を通知
        /// </summary>
        private async Task RequestConfigAsync()
        {
            try
            {
                // 設定読み込み状態をリセット
                _appSettings.IsLoaded = false;

                // サーバー側で管理している設定ファイルを読み込む
                _appSettings.LoadAppSettings();

                // 設定をUIに反映
                ApplySettings();

                // クライアントにも設定を通知
                if (_communicationService != null && _communicationService.IsServerRunning)
                {
                    await _communicationService.UpdateConfigAsync(_appSettings.GetConfigSettings());
                }
            }
            catch (Exception ex)
            {
                UIHelper.ShowError("設定取得エラー", ex.Message);
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
        /// 設定を適用
        /// </summary>
        private void ApplySettings()
        {
            UIHelper.RunOnUIThread(() =>
            {
                // 最前面表示の設定を適用
                Topmost = _appSettings.IsTopmost;

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
                // WebSocketサーバーが起動している場合のみ送信
                if (_communicationService != null && _communicationService.IsServerRunning)
                {
                    await _communicationService.SendChatMessageAsync(message);
                }
                else
                {
                    ChatControlInstance.AddSystemErrorMessage("サーバーが起動していません");
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
            UIHelper.RunOnUIThread(() => ChatControlInstance.AddAiMessage(message));
        }

        /// <summary>
        /// 設定レスポンス受信時のハンドラ
        /// </summary>
        private void OnConfigResponseReceived(object? sender, ConfigResponsePayload response)
        {
            UIHelper.RunOnUIThread(() =>
            {
                ProcessConfigResponse(response);
            });
        }

        /// <summary>
        /// 設定レスポンスを処理
        /// </summary>
        private void ProcessConfigResponse(ConfigResponsePayload response)
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
                _appSettings.UpdateSettings(response.settings);

                // 設定を画面に反映
                ApplySettings();
            }
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
                UIHelper.RunOnUIThread(() =>
                {
                    // エラーメッセージをチャットウィンドウに表示（中央グレー枠）
                    ChatControlInstance.AddSystemErrorMessage(systemMessage.message);
                });
            }
        }

        /// <summary>
        /// 制御メッセージ受信時のハンドラ
        /// </summary>
        private void OnControlMessageReceived(object? sender, ControlMessagePayload controlMessage)
        {
            // 制御コマンドの種類を確認
            if (controlMessage.command == "shutdownCocoroAI")
            {
                UIHelper.RunOnUIThread(() =>
                {
                    // シャットダウン理由をログに記録
                    Debug.WriteLine($"シャットダウン要求を受信しました: {controlMessage.reason}");

                    // アプリケーションを正常に終了
                    Application.Current.Shutdown();
                });
            }
        }

        /// <summary>
        /// エラー発生時のハンドラ
        /// </summary>
        private void OnErrorOccurred(object? sender, string error)
        {
            UIHelper.ShowError("エラー", error);
        }

        /// <summary>
        /// サーバー起動成功時のハンドラ
        /// </summary>
        private void OnConnected(object? sender, EventArgs e)
        {
            UpdateConnectionStatus(true);
            StopServerRestartTimer(); // サーバー再起動タイマーを停止
        }

        /// <summary>
        /// サーバー停止時のハンドラ
        /// </summary>
        private void OnDisconnected(object? sender, EventArgs e)
        {
            UpdateConnectionStatus(false);
            StartServerRestartTimer(); // サーバー再起動タイマーを開始
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
#if !DEBUG
                // CocoroCore と CocoroShell を終了
                TerminateExternalApplications();
#endif
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
        /// 外部アプリケーション（CocoroCore と CocoroShell）を終了する
        /// </summary>
        private void TerminateExternalApplications()
        {
            try
            {
                // CocoroCore プロセスを終了
                LaunchCocoroCore(ProcessOperation.Terminate);

                // CocoroShell プロセスを終了
                LaunchCocoroShell(ProcessOperation.Terminate);

                // CocoroMemory プロセスを終了
                LaunchCocoroMemory(ProcessOperation.Terminate);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"外部アプリケーション終了エラー: {ex.Message}");
                // アプリケーションの終了処理なのでエラーメッセージは表示しない
            }
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
                UIHelper.ShowError("設定取得エラー", ex.Message);
            }
        }

        /// <summary>
        /// サーバー再起動タイマーを開始
        /// </summary>
        private void StartServerRestartTimer()
        {
            if (_reconnectTimer == null)
            {
                _reconnectTimer = new Timer(async _ =>
                {
                    // サーバーが停止している場合のみ再起動を試みる
                    if (_communicationService != null && !_communicationService.IsServerRunning)
                    {
                        await StartWebSocketServerAsync();
                    }
                }, null, ReconnectIntervalMs, ReconnectIntervalMs);
            }
        }

        /// <summary>
        /// サーバー再起動タイマーを停止
        /// </summary>
        private void StopServerRestartTimer()
        {
            _reconnectTimer?.Change(Timeout.Infinite, Timeout.Infinite);
            _reconnectTimer = null;
        }

        /// <summary>
        /// CocoroShell.exeを起動する（既に起動している場合は終了してから再起動）
        /// </summary>
        /// <param name="operation">プロセス操作の種類（デフォルトは再起動）</param>
        private void LaunchCocoroShell(ProcessOperation operation = ProcessOperation.RestartIfRunning)
        {
            ProcessHelper.LaunchExternalApplication("CocoroShell", "CocoroShell.exe", "CocoroShell", operation);
        }

        /// <summary>
        /// CocoroCore.exeを起動する（既に起動している場合は終了してから再起動）
        /// </summary>
        /// <param name="operation">プロセス操作の種類（デフォルトは再起動）</param>
        private void LaunchCocoroCore(ProcessOperation operation = ProcessOperation.RestartIfRunning)
        {
#if !DEBUG
            if(_appSettings.CharacterList.Count > 0 && 
               _appSettings.CurrentCharacterIndex < _appSettings.CharacterList.Count && 
               _appSettings.CharacterList[_appSettings.CurrentCharacterIndex].isUseLLM)
            {
                ProcessHelper.LaunchExternalApplication("CocoroCore", "CocoroCore.exe", null, operation);
            }
            else
            {
                ProcessHelper.LaunchExternalApplication("CocoroCore", "CocoroCore.exe", null, ProcessOperation.Terminate);
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
            if(_appSettings.CharacterList.Count > 0 && 
               _appSettings.CurrentCharacterIndex < _appSettings.CharacterList.Count && 
               _appSettings.CharacterList[_appSettings.CurrentCharacterIndex].isEnableMemory)
            {
                ProcessHelper.LaunchExternalApplication("CocoroMemory", "CocoroMemory.exe", null, operation);
            }
            else
            {
                // 記憶機能が無効な場合は終了
                ProcessHelper.LaunchExternalApplication("CocoroMemory", "CocoroMemory.exe", null, ProcessOperation.Terminate);
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
                base.OnClosing(e);
            }
        }
    }
}