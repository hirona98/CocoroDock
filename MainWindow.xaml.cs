using CocoroDock.Communication;
using CocoroDock.Controls;
using CocoroDock.Services;
using System;
using System.Diagnostics;
using System.IO;
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
        private CommunicationService? _communicationService;
        private Timer? _reconnectTimer; // 再接続用タイマー
        private const int ReconnectIntervalMs = 3000; // 再接続間隔（3秒）

        public MainWindow()
        {
            InitializeComponent();

            // ウィンドウのロード時にメッセージテキストボックスにフォーカスを設定するイベントを追加
            this.Loaded += MainWindow_Loaded;

            // 初期化と接続
            InitializeApp();
        }

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
                // AppSettingsから設定を取得
                var settings = AppSettings.Instance;
#if !DEBUG
                // CocoroShell.exeを起動（既に起動していれば終了してから再起動）
                LaunchCocoroShell();
                // CocoroCore.exeを起動（既に起動していれば終了してから再起動）
                LaunchCocoroCore();
#endif

                // 通信サービスを初期化 (WebSocketServerを使用)
                _communicationService = new CommunicationService(
                    settings.CocoroDockPort);

                // 通信サービスのイベントハンドラを設定
                _communicationService.ChatMessageReceived += OnChatMessageReceived;
                _communicationService.ConfigResponseReceived += OnConfigResponseReceived;
                _communicationService.StatusUpdateReceived += OnStatusUpdateReceived;
                _communicationService.SystemMessageReceived += OnSystemMessageReceived;
                _communicationService.ControlMessageReceived += OnControlMessageReceived;
                _communicationService.ErrorOccurred += OnErrorOccurred;
                _communicationService.Connected += OnConnected;
                _communicationService.Disconnected += OnDisconnected;

                // UIコントロールのイベントハンドラを登録
                RegisterEventHandlers();
                // サーバーの起動を開始
                _ = StartWebSocketServerAsync();
            }
            catch (Exception ex)
            {
                ShowError("初期化エラー", ex.Message);
            }
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

                if (_communicationService != null)
                {
                    if (_communicationService != null && !_communicationService.IsServerRunning)
                    {
                        // サーバーを起動
                        await _communicationService.StartServerAsync();

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

                // 再起動タイマーを開始
                StartServerRestartTimer();
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
                if (_communicationService != null && _communicationService.IsServerRunning)
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
                    try
                    {
                        Application.Current.Dispatcher.InvokeAsync(action);
                    }
                    catch (TaskCanceledException)
                    {
                        // キャンセルされた場合は無視
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"UI更新エラー: {ex.Message}");
                    }
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
        /// 制御メッセージ受信時のハンドラ
        /// </summary>
        private void OnControlMessageReceived(object? sender, ControlMessagePayload controlMessage)
        {
            // 制御コマンドの種類を確認
            if (controlMessage.command == "shutdownCocoroAI")
            {
                RunOnUIThread(() =>
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
            ShowError("エラー", error);
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
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"外部アプリケーション終了エラー: {ex.Message}");
                // アプリケーションの終了処理なのでエラーメッセージは表示しない
            }
        }

        /// <summary>
        /// プロセス操作の種類を定義する列挙型
        /// </summary>
        private enum ProcessOperation
        {
            /// <summary>既存のプロセスを終了して新しいプロセスを起動</summary>
            RestartIfRunning,
            /// <summary>プロセスを強制終了</summary>
            Terminate,
            /// <summary>プロセスの存在チェックのみ</summary>
            CheckOnly
        }

        /// <summary>
        /// 指定した名前のプロセスに対して操作を行う
        /// </summary>
        /// <param name="processName">プロセス名（拡張子なし）</param>
        /// <param name="operation">実行する操作</param>
        /// <returns>プロセスが存在する場合はtrue、存在しない場合はfalse</returns>
        private bool ProcessUtility(string processName, ProcessOperation operation)
        {
            try
            {
                Process[] processes = Process.GetProcessesByName(processName);
                bool exists = processes.Length > 0;

                // 操作に応じたプロセス処理
                if (operation == ProcessOperation.Terminate || operation == ProcessOperation.RestartIfRunning)
                {
                    foreach (Process process in processes)
                    {
                        try
                        {
                            if (!process.HasExited)
                            {
                                process.Kill();
                                process.WaitForExit(3000); // 最大3秒待機
                                Debug.WriteLine($"{processName} プロセスを終了しました。");
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"{processName} プロセス終了エラー: {ex.Message}");
                            // プロセス終了のエラーはログに記録するだけで続行
                        }
                    }
                }

                return exists;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"{processName} プロセス操作エラー: {ex.Message}");
                return false;
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
                ShowError("設定取得エラー", ex.Message);
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
        /// 外部アプリケーションを起動する
        /// </summary>
        /// <param name="appName">アプリケーション名</param>
        /// <param name="exePath">実行ファイルのパス（絶対パスまたは相対パス）</param>
        /// <param name="relativeDir">相対ディレクトリ（nullの場合は直接exePathを使用）</param>
        /// <param name="operation">プロセス操作の種類（終了のみか再起動か）</param>
        private void LaunchExternalApplication(string appName, string exePath, string? relativeDir = null, ProcessOperation operation = ProcessOperation.RestartIfRunning)
        {
            try
            {
                // 実行ファイルパスの構築
                string fullPath;
                if (relativeDir != null)
                {
                    fullPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, relativeDir, exePath);
                }
                else
                {
                    fullPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, exePath);
                }

                // ファイルの存在確認
                if (!File.Exists(fullPath))
                {
                    ShowError("起動エラー", $"{appName}が見つかりません。パス: {fullPath}");
                    return;
                }

                // 同名の実行中プロセスをチェックして終了または再起動
                string processName = Path.GetFileNameWithoutExtension(exePath);
                bool wasRunning = ProcessUtility(processName, operation);

                // 終了のみの場合は起動しない
                if (operation == ProcessOperation.Terminate)
                {
                    return;
                }

                // プロセス起動のためのパラメータを設定
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = fullPath,
                    UseShellExecute = true
                };

                // プロセスを起動
                Process.Start(startInfo);
            }
            catch (Exception ex)
            {
                ShowError($"{appName}起動エラー", ex.Message);
            }
        }

        /// <summary>
        /// CocoroShell.exeを起動する（既に起動している場合は終了してから再起動）
        /// </summary>
        /// <param name="operation">プロセス操作の種類（デフォルトは再起動）</param>
        private void LaunchCocoroShell(ProcessOperation operation = ProcessOperation.RestartIfRunning)
        {
            LaunchExternalApplication("CocoroShell", "CocoroShell.exe", "CocoroShell", operation);
        }

        /// <summary>
        /// CocoroCore.exeを起動する（既に起動している場合は終了してから再起動）
        /// </summary>
        /// <param name="operation">プロセス操作の種類（デフォルトは再起動）</param>
        private void LaunchCocoroCore(ProcessOperation operation = ProcessOperation.RestartIfRunning)
        {
#if !DEBUG
            var settings = AppSettings.Instance;
            if(settings.CharacterList[settings.CurrentCharacterIndex].isUseLLM)
            {
                LaunchExternalApplication("CocoroCore", "CocoroCore.exe", null, operation);
            }else{
                LaunchExternalApplication("CocoroCore", "CocoroCore.exe", null, ProcessOperation.Terminate);
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