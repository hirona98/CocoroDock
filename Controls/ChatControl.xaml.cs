using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace CocoroDock.Controls
{
    /// <summary>
    /// チャットコントロール（バブルデザイン）
    /// </summary>
    public partial class ChatControl : UserControl
    {
        public event EventHandler<string>? MessageSent;

        public ChatControl()
        {
            InitializeComponent();
        }

        /// <summary>
        /// メッセージ入力テキストボックスにフォーカスを設定
        /// </summary>
        public void FocusMessageTextBox()
        {
            MessageTextBox.Focus();
        }

        /// <summary>
        /// ユーザーメッセージを送信
        /// </summary>
        private void SendMessage()
        {
            string message = MessageTextBox.Text.Trim();
            if (string.IsNullOrEmpty(message))
                return;

            // UIにユーザーメッセージを追加
            AddUserMessage(message);

            // メッセージ送信イベント発火
            MessageSent?.Invoke(this, message);

            // テキストボックスをクリア
            MessageTextBox.Clear();
        }

        /// <summary>
        /// ユーザーメッセージをUIに追加
        /// </summary>
        /// <param name="message">メッセージ</param>
        public void AddUserMessage(string message)
        {
            var messageContainer = new StackPanel();

            var bubble = new Border
            {
                Style = (Style)Resources["UserBubbleStyle"]
            };

            var messageContent = new StackPanel();

            var messageText = new TextBlock
            {
                Style = (Style)Resources["UserMessageTextStyle"],
                Text = message
            };

            messageContent.Children.Add(messageText);
            bubble.Child = messageContent;
            messageContainer.Children.Add(bubble);

            ChatMessagesPanel.Children.Add(messageContainer);

            // 自動スクロール
            ChatScrollViewer.ScrollToEnd();
        }

        /// <summary>
        /// AIレスポンスをUIに追加
        /// </summary>
        /// <param name="message">レスポンスメッセージ</param>
        public void AddAiMessage(string message)
        {
            var messageContainer = new StackPanel();

            var bubble = new Border
            {
                Style = (Style)Resources["AiBubbleStyle"]
            };

            var messageContent = new StackPanel();

            var messageText = new TextBlock
            {
                Style = (Style)Resources["AiMessageTextStyle"],
                Text = message
            };

            messageContent.Children.Add(messageText);
            bubble.Child = messageContent;
            messageContainer.Children.Add(bubble);

            ChatMessagesPanel.Children.Add(messageContainer);

            // 自動スクロール
            ChatScrollViewer.ScrollToEnd();
        }

        /// <summary>
        /// システムエラーメッセージをUIに追加（中央のグレー枠に表示）
        /// </summary>
        /// <param name="message">エラーメッセージ</param>
        public void AddSystemErrorMessage(string message)
        {
            var messageContainer = new StackPanel();

            var bubble = new Border
            {
                Style = (Style)Resources["SystemMessageBubbleStyle"]
            };

            var messageContent = new StackPanel();

            var messageText = new TextBlock
            {
                Style = (Style)Resources["SystemMessageTextStyle"],
                Text = message
            };

            messageContent.Children.Add(messageText);
            bubble.Child = messageContent;
            messageContainer.Children.Add(bubble);

            ChatMessagesPanel.Children.Add(messageContainer);

            // 自動スクロール
            ChatScrollViewer.ScrollToEnd();
        }

        /// <summary>
        /// 通知メッセージをUIに追加（中央のグレー枠に白文字で表示）
        /// </summary>
        /// <param name="from">通知元のアプリ名</param>
        /// <param name="message">通知メッセージ</param>
        public void AddNotificationMessage(string from, string message)
        {
            var messageContainer = new StackPanel();

            var bubble = new Border
            {
                Style = (Style)Resources["SystemMessageBubbleStyle"]
            };

            var messageContent = new StackPanel();

            var messageText = new TextBlock
            {
                Style = (Style)Resources["SystemMessageTextStyle"],
                Text = $"[{from}] {message}"
            };

            messageContent.Children.Add(messageText);
            bubble.Child = messageContent;
            messageContainer.Children.Add(bubble);

            ChatMessagesPanel.Children.Add(messageContainer);

            // 自動スクロール
            ChatScrollViewer.ScrollToEnd();
        }

        /// <summary>
        /// チャット履歴をクリア
        /// </summary>
        public void ClearChat()
        {
            ChatMessagesPanel.Children.Clear();
        }

        /// <summary>
        /// 送信ボタンクリックハンドラ
        /// </summary>
        private void SendButton_Click(object sender, RoutedEventArgs e)
        {
            SendMessage();
        }

        /// <summary>
        /// テキストボックスのキー入力ハンドラ（Enterキーで送信）
        /// </summary>
        private void MessageTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // Enterキーが押された場合
            if (e.Key == Key.Enter)
            {
                // Shiftキーが押されていない場合は送信処理
                if ((Keyboard.Modifiers & ModifierKeys.Shift) != ModifierKeys.Shift)
                {
                    // Enterキーのデフォルト動作（改行）を防止
                    e.Handled = true;

                    // 送信処理を実行
                    SendMessage();
                }
                // Shift+Enterの場合はデフォルト動作（改行）をそのまま許可
            }
        }

        /// <summary>
        /// テキストボックスのキー入力ハンドラ（Enterキーで送信、Shift+Enterで改行）
        /// </summary>
        private void MessageTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                // Shift+Enterの場合は改行を挿入
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
                {
                    // デフォルト動作（改行挿入）を許可
                    return;
                }
                // Enterのみの場合はメッセージ送信
                else
                {
                    e.Handled = true;
                    SendMessage();
                }
            }
        }

        /// <summary>
        /// テキストボックスのテキスト変更ハンドラ（内容に応じて高さを自動調整）
        /// </summary>
        private void MessageTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            // センダーをTextBoxとして安全に変換
            if (sender is TextBox textBox)
            {
                // 内容に基づいて高さを自動調整
                AdjustTextBoxHeight(textBox);
            }
        }

        /// <summary>
        /// テキストボックスの高さを内容に合わせて自動調整
        /// </summary>
        private void AdjustTextBoxHeight(TextBox textBox)
        {
            // 現在の行数を計算
            int lineCount = textBox.LineCount;

            // 行数に基づいて高さを調整（1行の場合は初期高さを維持）
            if (lineCount <= 1)
            {
                textBox.Height = textBox.MinHeight;
            }
            else
            {
                // 1行あたりの高さを概算（フォントサイズ + パディング）
                double lineHeight = textBox.FontSize + 8;

                // 行数に基づいて高さを計算（最大5行まで）
                int maxLines = 5;
                int actualLines = Math.Min(lineCount, maxLines);

                // 新しい高さを設定（基本の高さ + 追加行分の高さ）
                double newHeight = textBox.MinHeight + (actualLines - 1) * lineHeight;

                // 最大高さを超えないように制限
                textBox.Height = Math.Min(newHeight, textBox.MaxHeight);
            }
        }
    }
}