using System;
using System.Collections.Generic;
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
using CocoroDock.Services;

namespace CocoroDock.Controls
{
    /// <summary>
    /// チャットコントロール（バブルデザイン）
    /// </summary>
    public partial class ChatControl : UserControl
    {
        public event EventHandler<string>? MessageSent;

        // 添付画像データ（Base64形式のdata URL、最大5枚）
        private List<string> _attachedImageDataUrls = new List<string>();
        private List<BitmapSource> _attachedImageSources = new List<BitmapSource>();
        private const int MaxImageCount = 5;

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
            if (string.IsNullOrEmpty(message) && _attachedImageSources.Count == 0)
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
        /// <param name="imageSources">画像リスト（オプション）</param>
        public void AddUserMessage(string message, List<BitmapSource>? imageSources = null)
        {
            var messageContainer = new StackPanel();

            var bubble = new Border
            {
                Style = (Style)Resources["UserBubbleStyle"]
            };

            var messageContent = new StackPanel();

            // 複数画像がある場合は先に表示
            if (imageSources != null && imageSources.Count > 0)
            {
                // 画像を横並びで表示するためのWrapPanel
                var imagePanel = new WrapPanel
                {
                    Orientation = Orientation.Horizontal,
                    Margin = new Thickness(0, 0, 0, 5)
                };

                foreach (var imageSource in imageSources)
                {
                    var imageBorder = new Border
                    {
                        BorderBrush = new SolidColorBrush(Colors.White),
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(0),  // 角を丸くしない
                        Margin = new Thickness(0, 0, 5, 0),
                        Cursor = System.Windows.Input.Cursors.Hand
                    };

                    var image = new Image
                    {
                        Source = imageSource,
                        MaxHeight = 120,
                        MaxWidth = 160,
                        Stretch = Stretch.Uniform
                    };

                    imageBorder.Child = image;

                    // クリックイベントで拡大表示
                    imageBorder.MouseLeftButtonUp += (s, e) =>
                    {
                        var previewWindow = new Windows.ImagePreviewWindow(imageSource);
                        previewWindow.Show();
                    };

                    imagePanel.Children.Add(imageBorder);
                }

                messageContent.Children.Add(imagePanel);
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
        /// 最後のAIメッセージにテキストを追記
        /// </summary>
        /// <param name="additionalText">追記するテキスト</param>
        public void AppendToLastAiMessage(string additionalText)
        {
            if (ChatMessagesPanel.Children.Count == 0)
                return;

            // 最後のメッセージコンテナを取得
            var lastContainer = ChatMessagesPanel.Children[ChatMessagesPanel.Children.Count - 1] as StackPanel;
            if (lastContainer == null) return;

            // バブルを取得
            var bubble = lastContainer.Children.OfType<Border>().FirstOrDefault(b => b.Style == (Style)Resources["AiBubbleStyle"]);
            if (bubble == null) return;

            // メッセージコンテンツを取得
            var messageContent = bubble.Child as StackPanel;
            if (messageContent == null) return;

            // テキストボックスを取得
            var messageTextBox = messageContent.Children.OfType<TextBox>().FirstOrDefault(tb => tb.Style == (Style)Resources["AiMessageTextStyle"]);
            if (messageTextBox == null) return;

            // 表情タグを削除してからテキストを追記
            var cleanAdditionalText = RemoveFaceTags(additionalText);
            messageTextBox.Text += cleanAdditionalText;

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
        /// <param name="imageSources">画像データリスト（オプション）</param>
        public void AddNotificationMessage(string from, string message, List<BitmapSource>? imageSources = null)
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

            // 複数画像がある場合は追加
            if (imageSources != null && imageSources.Count > 0)
            {
                // 画像を横並びで表示するためのWrapPanel
                var imagePanel = new WrapPanel
                {
                    Orientation = Orientation.Horizontal,
                    Margin = new Thickness(0, 10, 0, 0)
                };

                foreach (var imageSource in imageSources)
                {
                    var imageBorder = new Border
                    {
                        BorderBrush = new SolidColorBrush(Colors.LightGray),
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(3),
                        Margin = new Thickness(0, 0, 5, 0),
                        Cursor = Cursors.Hand
                    };

                    var image = new Image
                    {
                        Source = imageSource,
                        MaxHeight = 120,
                        MaxWidth = 160,
                        Stretch = Stretch.Uniform
                    };

                    imageBorder.Child = image;

                    // クリックイベントで拡大表示
                    imageBorder.MouseLeftButtonUp += (s, e) =>
                    {
                        var previewWindow = new ImagePreviewWindow(imageSource);
                        previewWindow.Show();
                    };

                    imagePanel.Children.Add(imageBorder);
                }

                messageContent.Children.Add(imagePanel);
            }

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
                        previewWindow.Show();
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
                        AddImageFromBitmapSource(image);
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
                            AddImageFromFile(filePath);
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
                    AddImageFromFile(filePath);
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
                foreach (string filePath in files)
                {
                    AddImageFromFile(filePath);
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
                    AddImageFromBitmapSource(image);
                    e.CancelCommand();
                }
            }
            else if (e.DataObject.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.DataObject.GetData(DataFormats.FileDrop);
                foreach (string filePath in files)
                {
                    AddImageFromFile(filePath);
                }
                e.CancelCommand();
            }
        }

        /// <summary>
        /// ファイルから画像を追加
        /// </summary>
        private void AddImageFromFile(string filePath)
        {
            try
            {
                // 画像数の上限チェック
                if (_attachedImageSources.Count >= MaxImageCount)
                {
                    MessageBox.Show($"画像は最大{MaxImageCount}枚まで添付できます。", "制限", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // サポートされる画像形式を確認
                string[] supportedExtensions = { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp" };
                string extension = Path.GetExtension(filePath).ToLower();

                if (!supportedExtensions.Contains(extension))
                {
                    MessageBox.Show("サポートされていない画像形式です。", "エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var bitmap = new BitmapImage(new Uri(filePath));
                AddImageFromBitmapSource(bitmap);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"画像の読み込みに失敗しました: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// BitmapSourceから画像を追加
        /// </summary>
        private void AddImageFromBitmapSource(BitmapSource bitmapSource)
        {
            try
            {
                // 画像数の上限チェック
                if (_attachedImageSources.Count >= MaxImageCount)
                {
                    MessageBox.Show($"画像は最大{MaxImageCount}枚まで添付できます。", "制限", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // 画像をBase64エンコード
                string imageDataUrl = ConvertToDataUrl(bitmapSource);

                // リストに追加
                _attachedImageDataUrls.Add(imageDataUrl);
                _attachedImageSources.Add(bitmapSource);

                // プレビューを更新
                UpdateImagePreview();
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
        /// 画像プレビューを更新
        /// </summary>
        private void UpdateImagePreview()
        {
            // プレビューパネルをクリア
            ImagePreviewPanel.Children.Clear();

            if (_attachedImageSources.Count == 0)
            {
                // 画像がない場合はプレースホルダーを表示
                ImagePreviewBorder.Visibility = Visibility.Collapsed;
                ImagePlaceholderText.Visibility = Visibility.Visible;
                ImagePreviewScrollViewer.Visibility = Visibility.Collapsed;
            }
            else
            {
                // 複数画像をプレビューに表示
                for (int i = 0; i < _attachedImageSources.Count; i++)
                {
                    var imageSource = _attachedImageSources[i];
                    int imageIndex = i; // ラムダ式でキャプチャするためのローカル変数

                    // 各画像のコンテナ
                    var imageContainer = new Border
                    {
                        BorderBrush = new SolidColorBrush(Colors.Gray),
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(3),
                        Margin = new Thickness(2),
                        Background = new SolidColorBrush(Colors.White),
                        Width = 80,
                        Height = 80
                    };

                    // グリッドで画像と削除ボタンを重ねる
                    var grid = new Grid();

                    // 画像要素
                    var image = new Image
                    {
                        Source = imageSource,
                        Stretch = Stretch.Uniform,
                        Margin = new Thickness(2),
                        Cursor = Cursors.Hand
                    };

                    // 画像クリックで拡大表示
                    image.MouseLeftButtonUp += (s, e) =>
                    {
                        var previewWindow = new Windows.ImagePreviewWindow(imageSource);
                        previewWindow.Show();
                    };

                    // 個別削除ボタン
                    var deleteButton = new Button
                    {
                        Content = "×",
                        Width = 16,
                        Height = 16,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        VerticalAlignment = VerticalAlignment.Top,
                        Margin = new Thickness(2),
                        Background = new SolidColorBrush(Color.FromArgb(170, 160, 0, 0)),
                        Foreground = new SolidColorBrush(Colors.White),
                        BorderThickness = new Thickness(0),
                        FontSize = 10,
                        FontWeight = FontWeights.Bold,
                        Cursor = Cursors.Hand
                    };

                    // 個別削除ボタンのクリックイベント
                    deleteButton.Click += (s, e) =>
                    {
                        RemoveImageBySource(imageSource);
                    };

                    grid.Children.Add(image);
                    grid.Children.Add(deleteButton);
                    imageContainer.Child = grid;

                    ImagePreviewPanel.Children.Add(imageContainer);
                }

                // プレビューエリアを表示
                ImagePlaceholderText.Visibility = Visibility.Collapsed;
                ImagePreviewScrollViewer.Visibility = Visibility.Visible;
                ImagePreviewBorder.Visibility = Visibility.Visible;
            }
        }

        /// <summary>
        /// 指定したBitmapSourceの画像を削除
        /// </summary>
        private void RemoveImageBySource(BitmapSource imageSource)
        {
            int index = _attachedImageSources.IndexOf(imageSource);
            if (index >= 0)
            {
                _attachedImageDataUrls.RemoveAt(index);
                _attachedImageSources.RemoveAt(index);
                UpdateImagePreview();
            }
        }

        /// <summary>
        /// 指定したインデックスの画像を削除
        /// </summary>
        private void RemoveImageAt(int index)
        {
            if (index >= 0 && index < _attachedImageSources.Count)
            {
                _attachedImageDataUrls.RemoveAt(index);
                _attachedImageSources.RemoveAt(index);
                UpdateImagePreview();
            }
        }


        /// <summary>
        /// 添付画像データ（複数）を取得してクリア
        /// </summary>
        public List<string> GetAndClearAttachedImages()
        {
            var imageDataUrls = new List<string>(_attachedImageDataUrls);
            if (_attachedImageDataUrls.Count > 0)
            {
                _attachedImageDataUrls.Clear();
                _attachedImageSources.Clear();
                UpdateImagePreview();
            }
            return imageDataUrls;
        }

        /// <summary>
        /// 添付画像の最初の1枚を取得してクリア（既存互換性のため）
        /// </summary>
        public string? GetAndClearAttachedImage()
        {
            string? imageDataUrl = _attachedImageDataUrls.Count > 0 ? _attachedImageDataUrls[0] : null;
            if (_attachedImageDataUrls.Count > 0)
            {
                _attachedImageDataUrls.Clear();
                _attachedImageSources.Clear();
                UpdateImagePreview();
            }
            return imageDataUrl;
        }

        /// <summary>
        /// 添付画像のBitmapSourceリストを取得
        /// </summary>
        public List<BitmapSource> GetAttachedImageSources()
        {
            return new List<BitmapSource>(_attachedImageSources);
        }

        /// <summary>
        /// 添付画像の最初の1枚のBitmapSourceを取得（既存互換性のため）
        /// </summary>
        public BitmapSource? GetAttachedImageSource()
        {
            return _attachedImageSources.Count > 0 ? _attachedImageSources[0] : null;
        }

        /// <summary>
        /// 音声レベルを更新
        /// </summary>
        /// <param name="level">音声レベル (0.0-1.0)</param>
        /// <param name="isAboveThreshold">しきい値を超えているかどうか</param>
        public void UpdateVoiceLevel(float level, bool isAboveThreshold)
        {
            // 常にボーダーは表示（マイクOFF時と同じ見た目）
            VoiceLevelBorder.Visibility = Visibility.Visible;

            if (isAboveThreshold)
            {
                // しきい値を超えた場合はレベルバーを表示
                // 0-1の値を0-55ピクセルにマッピング（下から上に伸びる）
                double height = Math.Max(0, Math.Min(1, level)) * 55;
                VoiceLevelBar.Height = height;
            }
            else
            {
                // しきい値以下の場合はレベルバーを0（背景だけ表示）
                VoiceLevelBar.Height = 0;
            }
        }

        /// <summary>
        /// 送信ボタンの有効/無効を設定
        /// </summary>
        /// <param name="isEnabled">ボタンを有効にするかどうか</param>
        public void UpdateSendButtonEnabled(bool isEnabled)
        {
            SendButton.IsEnabled = isEnabled;
            // テキストボックスとマイク入力も止めたほうが良いけど面倒なので保留
        }

        /// <summary>
        /// 音声認識結果をチャットに追加
        /// </summary>
        /// <param name="text">認識されたテキスト</param>
        public void AddVoiceMessage(string text)
        {
            var messageContainer = new StackPanel();

            var bubble = new Border
            {
                Style = (Style)Resources["UserBubbleStyle"]  // テキスト入力と同じスタイル
            };

            var messageContent = new StackPanel();

            var messageText = new TextBox
            {
                Style = (Style)Resources["UserMessageTextStyle"],
                Text = text  // 🎤アイコンを削除してテキストのみ
            };

            messageContent.Children.Add(messageText);
            bubble.Child = messageContent;
            messageContainer.Children.Add(bubble);

            ChatMessagesPanel.Children.Add(messageContainer);

            // 自動スクロール
            ChatScrollViewer.ScrollToEnd();
        }
    }
}