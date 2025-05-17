using CocoroDock.Communication;
using CocoroDock.Controls;
using CocoroDock.ViewModels;
using System;
using System.Windows;

namespace CocoroDock
{
    /// <summary>
    /// MainWindow.xaml の相互作用ロジック
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly MainWindowViewModel _viewModel;

        public MainWindow()
        {
            InitializeComponent();

            // ViewModelを初期化
            _viewModel = new MainWindowViewModel();
            DataContext = _viewModel;

            // ウィンドウのロード時にメッセージテキストボックスにフォーカスを設定するイベントを追加
            this.Loaded += MainWindow_Loaded;

            // ViewModelのイベントハンドラを設定
            _viewModel.ChatMessageReceived += OnChatMessageReceived;
            _viewModel.SystemMessageReceived += OnSystemMessageReceived;
            _viewModel.ErrorOccurred += OnErrorOccurred;

            // チャットコントロールのイベント登録
            ChatControlInstance.MessageSent += OnChatMessageSent;
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
        /// チャットメッセージ送信時のハンドラ
        /// </summary>
        private async void OnChatMessageSent(object? sender, string message)
        {
            try
            {
                await _viewModel.SendChatMessageAsync(message);
            }
            catch (Exception ex)
            {
                ChatControlInstance.AddSystemErrorMessage($"エラー: {ex.Message}");
            }
        }

        /// <summary>
        /// チャットメッセージ受信時のハンドラ
        /// </summary>
        private void OnChatMessageReceived(object? sender, string message)
        {
            Application.Current.Dispatcher.Invoke(() => 
            {
                ChatControlInstance.AddAiMessage(message);
            });
        }

        /// <summary>
        /// システムメッセージ受信時のハンドラ
        /// </summary>
        private void OnSystemMessageReceived(object? sender, SystemMessagePayload systemMessage)
        {
            // levelがerrorの場合のみ処理する（Infoは無視）
            if (systemMessage.Level == "Error")
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    // エラーメッセージをチャットウィンドウに表示
                    ChatControlInstance.AddSystemErrorMessage(systemMessage.Message);
                });
            }
        }

        /// <summary>
        /// エラー発生時のハンドラ
        /// </summary>
        private void OnErrorOccurred(object? sender, string error)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                MessageBox.Show($"エラー: {error}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            });
        }

        /// <summary>
        /// アプリケーション終了時の処理
        /// </summary>
        protected override void OnClosed(EventArgs e)
        {
            // ViewModelのリソースを解放
            (_viewModel as IDisposable)?.Dispose();
            
            base.OnClosed(e);
        }
    }
}
