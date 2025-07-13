using System;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.IO;
using System.Linq;
using System.Collections.Specialized;
using System.Diagnostics;
using CocoroDock.Windows;

namespace CocoroDock.Controls
{
    /// <summary>
    /// チャットコントロール（バブルデザイン）
    /// </summary>
    public partial class ChatControl : UserControl
    {
        public event EventHandler<string>? MessageSent;

        // 添付画像データ（Base64形式のdata URL）
        private string? _attachedImageDataUrl;
        private BitmapSource? _attachedImageSource;

        public ChatControl()
        {
            InitializeComponent();

            // ペーストイベントハンドラを追加
            DataObject.AddPastingHandler(MessageTextBox, OnPaste);
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
            if (string.IsNullOrEmpty(message) && _attachedImageSource == null)
                return;

            // メッセージ送信イベント発火（UIへの追加はMainWindowで行う）
            MessageSent?.Invoke(this, message);

            // テキストボックスをクリア
            MessageTextBox.Clear();
        }

        /// <summary>
        /// ユーザーメッセージをUIに追加
        /// </summary>
        /// <param name="message">メッセージ</param>
        /// <param name="imageSource">画像（オプション）</param>
        public void AddUserMessage(string message, BitmapSource? imageSource = null)
        {
            var messageContainer = new StackPanel();

            var bubble = new Border
            {
                Style = (Style)Resources["UserBubbleStyle"]
            };

            var messageContent = new StackPanel();

            // 画像がある場合は先に表示
            if (imageSource != null)
            {
                var imageBorder = new Border
                {
                    BorderBrush = new SolidColorBrush(Colors.White),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(0),  // 角を丸くしない
                    Margin = new Thickness(0, 0, 0, 5),
                    Cursor = System.Windows.Input.Cursors.Hand
                };

                var image = new Image
                {
                    Source = imageSource,
                    MaxHeight = 150,
                    MaxWidth = 200,
                    Stretch = Stretch.Uniform
                };

                imageBorder.Child = image;

                // クリックイベントで拡大表示
                imageBorder.MouseLeftButtonUp += (s, e) =>
                {
                    var previewWindow = new Windows.ImagePreviewWindow(imageSource);
                    previewWindow.Show();
                };

                messageContent.Children.Add(imageBorder);
            }

            // テキストメッセージ（空でない場合のみ）
            if (!string.IsNullOrEmpty(message))
            {
                var messageText = new TextBox
                {
                    Style = (Style)Resources["UserMessageTextStyle"],
                    Text = message
                };
                messageContent.Children.Add(messageText);
            }

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

            // 表情タグを削除してからテキストを設定
            var cleanMessage = RemoveFaceTags(message);

            var messageText = new TextBox
            {
                Style = (Style)Resources["AiMessageTextStyle"],
                Text = cleanMessage
            };

            messageContent.Children.Add(messageText);
            bubble.Child = messageContent;
            messageContainer.Children.Add(bubble);

            ChatMessagesPanel.Children.Add(messageContainer);

            // 自動スクロール
            ChatScrollViewer.ScrollToEnd();
        }

        /// <summary>
        /// 表情タグを削除
        /// </summary>
        /// <param name="message">元のメッセージ</param>
        /// <returns>表情タグを削除したメッセージ</returns>
        private string RemoveFaceTags(string message)
        {
            // [face:XXX] 形式のタグを削除（XXXは任意の文字列）
            return Regex.Replace(message, @"\[face:[^\]]+\]", "").Trim();
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

            var messageText = new TextBox
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

            var messageText = new TextBox
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
        /// デスクトップモニタリング画像を表示
        /// </summary>
        /// <param name="imageBase64">Base64エンコードされた画像データ</param>
        public void AddDesktopMonitoringImage(string imageBase64)
        {
            try
            {
                var messageContainer = new StackPanel();

                var bubble = new Border
                {
                    Style = (Style)Resources["SystemMessageBubbleStyle"]
                };

                var messageContent = new StackPanel();

                // タイトルテキスト
                var titleText = new TextBox
                {
                    Style = (Style)Resources["SystemMessageTextStyle"],
                    Text = "[デスクトップウォッチ画像]",
                    Margin = new Thickness(0, 0, 0, 5)
                };
                messageContent.Children.Add(titleText);

                // 画像を表示
                var imageBytes = Convert.FromBase64String(imageBase64);
                using (var stream = new System.IO.MemoryStream(imageBytes))
                {
                    var bitmapImage = new BitmapImage();
                    bitmapImage.BeginInit();
                    bitmapImage.StreamSource = stream;
                    bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                    bitmapImage.EndInit();
                    bitmapImage.Freeze();

                    var image = new Image
                    {
                        Source = bitmapImage,
                        MaxHeight = 200,
                        MaxWidth = 400,
                        Stretch = Stretch.Uniform,
                        Margin = new Thickness(0, 0, 0, 0),
                        Cursor = Cursors.Hand
                    };

                    // クリックで拡大表示
                    image.MouseLeftButtonUp += (s, e) =>
                    {
                        var previewWindow = new ImagePreviewWindow(bitmapImage);
                        previewWindow.ShowDialog();
                    };

                    messageContent.Children.Add(image);
                }

                bubble.Child = messageContent;
                messageContainer.Children.Add(bubble);

                ChatMessagesPanel.Children.Add(messageContainer);

                // 自動スクロール
                ChatScrollViewer.ScrollToEnd();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"デスクトップモニタリング画像の表示エラー: {ex.Message}");
            }
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
            // Ctrl+Vの場合は画像ペーストを処理
            else if (e.Key == Key.V && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                if (Clipboard.ContainsImage())
                {
                    var image = Clipboard.GetImage();
                    if (image != null)
                    {
                        LoadImageFromBitmapSource(image);
                        e.Handled = true;
                    }
                }
                else if (Clipboard.ContainsFileDropList())
                {
                    var files = Clipboard.GetFileDropList();
                    if (files.Count > 0)
                    {
                        string? filePath = files[0];
                        if (filePath != null)
                        {
                            LoadImageFromFile(filePath);
                            e.Handled = true;
                        }
                    }
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

        /// <summary>
        /// グリッド全体のドラッグエンターイベントハンドラ
        /// </summary>
        private void Grid_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetDataPresent(DataFormats.Bitmap))
            {
                e.Effects = DragDropEffects.Copy;
                e.Handled = true;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
        }

        /// <summary>
        /// グリッド全体のドラッグオーバーイベントハンドラ
        /// </summary>
        private void Grid_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetDataPresent(DataFormats.Bitmap))
            {
                e.Effects = DragDropEffects.Copy;
                e.Handled = true;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
        }

        /// <summary>
        /// グリッド全体のドラッグリーブイベントハンドラ
        /// </summary>
        private void Grid_DragLeave(object sender, DragEventArgs e)
        {
            e.Handled = true;
        }

        /// <summary>
        /// グリッド全体のドロップイベントハンドラ
        /// </summary>
        private void Grid_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files.Length > 0)
                {
                    string filePath = files[0];
                    LoadImageFromFile(filePath);
                }
            }
            e.Handled = true;
        }

        /// <summary>
        /// TextBoxのPreviewDragEnterイベントハンドラ
        /// </summary>
        private void TextBox_PreviewDragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetDataPresent(DataFormats.Bitmap))
            {
                e.Effects = DragDropEffects.Copy;
                e.Handled = true;
            }
        }

        /// <summary>
        /// TextBoxのPreviewDragOverイベントハンドラ
        /// </summary>
        private void TextBox_PreviewDragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetDataPresent(DataFormats.Bitmap))
            {
                e.Effects = DragDropEffects.Copy;
                e.Handled = true;
            }
        }

        /// <summary>
        /// TextBoxのPreviewDropイベントハンドラ
        /// </summary>
        private void TextBox_PreviewDrop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files.Length > 0)
                {
                    string filePath = files[0];
                    LoadImageFromFile(filePath);
                }
                e.Handled = true;
            }
        }

        /// <summary>
        /// ペーストイベントハンドラ
        /// </summary>
        private void OnPaste(object sender, DataObjectPastingEventArgs e)
        {
            if (e.DataObject.GetDataPresent(DataFormats.Bitmap))
            {
                var image = e.DataObject.GetData(DataFormats.Bitmap) as BitmapSource;
                if (image != null)
                {
                    LoadImageFromBitmapSource(image);
                    e.CancelCommand();
                }
            }
            else if (e.DataObject.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.DataObject.GetData(DataFormats.FileDrop);
                if (files.Length > 0)
                {
                    string filePath = files[0];
                    LoadImageFromFile(filePath);
                    e.CancelCommand();
                }
            }
        }

        /// <summary>
        /// ファイルから画像を読み込み
        /// </summary>
        private void LoadImageFromFile(string filePath)
        {
            try
            {
                // サポートされる画像形式を確認
                string[] supportedExtensions = { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp" };
                string extension = Path.GetExtension(filePath).ToLower();

                if (!supportedExtensions.Contains(extension))
                {
                    MessageBox.Show("サポートされていない画像形式です。", "エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var bitmap = new BitmapImage(new Uri(filePath));
                LoadImageFromBitmapSource(bitmap);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"画像の読み込みに失敗しました: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// BitmapSourceから画像を読み込み
        /// </summary>
        private void LoadImageFromBitmapSource(BitmapSource bitmapSource)
        {
            try
            {
                // 画像をBase64エンコード
                _attachedImageDataUrl = ConvertToDataUrl(bitmapSource);
                _attachedImageSource = bitmapSource;

                // プレビューに表示
                PreviewImage.Source = bitmapSource;
                PreviewImage.Visibility = Visibility.Visible;
                ImagePlaceholderText.Visibility = Visibility.Collapsed;
                RemoveImageButton.Visibility = Visibility.Visible;
                ImagePreviewBorder.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"画像の処理に失敗しました: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// BitmapSourceをBase64形式のdata URLに変換
        /// </summary>
        private string ConvertToDataUrl(BitmapSource bitmapSource)
        {
            using (var memoryStream = new MemoryStream())
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmapSource));
                encoder.Save(memoryStream);

                byte[] imageBytes = memoryStream.ToArray();
                string base64String = Convert.ToBase64String(imageBytes);
                return $"data:image/png;base64,{base64String}";
            }
        }

        /// <summary>
        /// 画像削除ボタンクリックハンドラ
        /// </summary>
        private void RemoveImageButton_Click(object sender, RoutedEventArgs e)
        {
            _attachedImageDataUrl = null;
            _attachedImageSource = null;
            PreviewImage.Source = null;
            PreviewImage.Visibility = Visibility.Collapsed;
            ImagePlaceholderText.Visibility = Visibility.Visible;
            RemoveImageButton.Visibility = Visibility.Collapsed;
            ImagePreviewBorder.Visibility = Visibility.Collapsed;
        }

        /// <summary>
        /// 添付画像を取得してクリア
        /// </summary>
        public string? GetAndClearAttachedImage()
        {
            string? imageDataUrl = _attachedImageDataUrl;
            if (_attachedImageDataUrl != null)
            {
                RemoveImageButton_Click(null!, null!);
            }
            return imageDataUrl;
        }

        /// <summary>
        /// 添付画像のBitmapSourceを取得
        /// </summary>
        public BitmapSource? GetAttachedImageSource()
        {
            return _attachedImageSource;
        }
    }
}