using CocoroDock.Communication;
using CocoroDock.Controls;
using CocoroDock.Services;
using CocoroDock.Utilities;
using System;
using System.Diagnostics;
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

                // UIコントロールのイベントハンドラを登録
                RegisterEventHandlers();

                // APIサーバーの起動を開始
                _ = StartApiServerAsync();
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
            // 通信サービスを初期化 (REST APIサーバーを使用)
            _communicationService = new CommunicationService(_appSettings);            // 通信サービスのイベントハンドラを設定
            _communicationService.ChatMessageReceived += OnChatMessageReceived;
            _communicationService.NotificationMessageReceived += OnNotificationMessageReceived;
            _communicationService.ControlCommandReceived += OnControlCommandReceived;
            _communicationService.ErrorOccurred += OnErrorOccurred;
            _communicationService.StatusUpdateRequested += OnStatusUpdateRequested;
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

                    // 設定をリロード
                    await RequestConfigAsync();
                }
            }
            catch (Exception ex)
            {
                UpdateConnectionStatus(false, "サーバー起動エラー");
                Debug.WriteLine($"APIサーバー起動エラー: {ex.Message}");
            }
        }

        /// <summary>
        /// 設定をリロードして適用
        /// </summary>
        private async Task RequestConfigAsync()
        {
            try
            {
                // 設定読み込み状態をリセット
                _appSettings.IsLoaded = false;

                // 設定ファイルを読み込む
                _appSettings.LoadAppSettings();

                // 設定をUIに反映
                ApplySettings();

                // CocoroShellに設定変更を通知
                if (_communicationService != null && _communicationService.IsServerRunning)
                {
                    await _communicationService.SendControlToShellAsync("reloadConfig");
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
                    ConnectionStatusText.Text = "状態: 正常動作中";
                }
                else
                {
                    string statusText = customMessage ?? "停止中";
                    ConnectionStatusText.Text = $"状態: {statusText}";
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
                // APIサーバーが起動している場合のみ送信
                if (_communicationService != null && _communicationService.IsServerRunning)
                {
                    // ユーザーメッセージとしてチャットウィンドウに表示（送信前に表示）
                    ChatControlInstance.AddUserMessage(message);

                    // CocoroCoreにメッセージを送信
                    await _communicationService.SendChatToCoreAsync(message);
                }
                else
                {
                    ChatControlInstance.AddSystemErrorMessage("サーバーが起動していません");
                }
            }
            catch (TimeoutException)
            {
                // タイムアウトエラー専用のメッセージ
                ChatControlInstance.AddSystemErrorMessage("AI応答がタイムアウトしました。もう一度お試しください。");
            }
            catch (HttpRequestException ex)
            {
                // 接続エラー専用のメッセージ
                ChatControlInstance.AddSystemErrorMessage("AI応答サーバーに接続できません。");
                Debug.WriteLine($"HttpRequestException: {ex.Message}");
                // アプリケーションは終了しない
            }
            catch (Exception ex)
            {
                // その他のエラー
                ChatControlInstance.AddSystemErrorMessage($"エラーが発生しました: {ex.Message}");
                Debug.WriteLine($"Exception: {ex}");
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
                    ChatControlInstance.AddAiMessage(request.content);
                }
            });
        }

        /// <summary>
        /// 通知メッセージ受信時のハンドラ
        /// </summary>
        private void OnNotificationMessageReceived(object? sender, ChatMessagePayload notification)
        {
            UIHelper.RunOnUIThread(() =>
            {
                // 通知メッセージをチャットウィンドウに表示
                ChatControlInstance.AddNotificationMessage(notification.userId, notification.message);
            });
        }


        /// <summary>
        /// 制御コマンド受信時のハンドラ（CocoroDock APIから）
        /// </summary>
        private void OnControlCommandReceived(object? sender, ControlRequest request)
        {
            UIHelper.RunOnUIThread(async () =>
            {
                switch (request.command)
                {
                    case "shutdown":
                        // シャットダウン理由をログに記録
                        Debug.WriteLine($"シャットダウン要求を受信しました: {request.reason}");
                        Application.Current.Shutdown();
                        break;

                    case "restart":
                        // 再起動処理
                        Debug.WriteLine($"再起動要求を受信しました: {request.reason}");
                        // TODO: 再起動処理の実装
                        break;

                    case "reloadConfig":
                        // 設定の再読み込み
                        Debug.WriteLine("設定再読み込み要求を受信しました");
                        await RequestConfigAsync();
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
                // 接続中ならリソース解放
                if (_communicationService != null)
                {
                    _communicationService.StopServerAsync().Wait(TimeSpan.FromSeconds(5));
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
                var adminWindow = new AdminWindow(_communicationService);
                adminWindow.Owner = this; // メインウィンドウを親に設定
                adminWindow.ShowDialog(); // モーダルダイアログとして表示
            }
            catch (Exception ex)
            {
                UIHelper.ShowError("設定取得エラー", ex.Message);
            }
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
                ProcessHelper.LaunchExternalApplication("CocoroCore", "CocoroCore.exe","CocoroCore", operation);
            }
            else
            {
                ProcessHelper.LaunchExternalApplication("CocoroCore", "CocoroCore.exe", "CocoroCore", ProcessOperation.Terminate);
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
                ProcessHelper.LaunchExternalApplication("CocoroMemory", "CocoroMemory.exe", "CocoroMemory", operation);
            }
            else
            {
                // 記憶機能が無効な場合は終了
                ProcessHelper.LaunchExternalApplication("CocoroMemory", "CocoroMemory.exe", "CocoroMemory", ProcessOperation.Terminate);
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