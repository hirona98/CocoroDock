using CocoroDock.Communication;
using CocoroDock.Services;
using CocoroDock.Windows;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace CocoroDock.Controls
{
    /// <summary>
    /// 表示用記憶情報
    /// </summary>
    public class DisplayMemoryInfo
    {
        public string MemoryId { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
    }

    /// <summary>
    /// SystemSettingsControl.xaml の相互作用ロジック
    /// </summary>
    public partial class SystemSettingsControl : UserControl
    {
        /// <summary>
        /// 設定が変更されたときに発生するイベント
        /// </summary>
        public event EventHandler? SettingsChanged;

        /// <summary>
        /// 読み込み完了フラグ
        /// </summary>
        private bool _isInitialized = false;

        /// <summary>
        /// 現在の記憶統計情報
        /// </summary>
        private MemoryStatsResponse? _currentMemoryStats;

        public SystemSettingsControl()
        {
            InitializeComponent();
        }

        /// <summary>
        /// 初期化処理
        /// </summary>
        public void Initialize()
        {
            try
            {
                var appSettings = AppSettings.Instance;

                // 通知API設定
                IsEnableNotificationApiCheckBox.IsChecked = appSettings.IsEnableNotificationApi;
                ApiDetailsTextBox.Text = GetApiDetails();

                // デスクトップウォッチ設定
                ScreenshotEnabledCheckBox.IsChecked = appSettings.ScreenshotSettings.enabled;
                CaptureActiveWindowOnlyCheckBox.IsChecked = appSettings.ScreenshotSettings.captureActiveWindowOnly;
                ScreenshotIntervalTextBox.Text = appSettings.ScreenshotSettings.intervalMinutes.ToString();
                IdleTimeoutTextBox.Text = appSettings.ScreenshotSettings.idleTimeoutMinutes.ToString();

                // マイク設定
                MicThresholdSlider.Value = appSettings.MicrophoneSettings.inputThreshold;

                // CocoroCore2設定
                EnableProModeCheckBox.IsChecked = appSettings.EnableProMode;
                EnableInternetRetrievalCheckBox.IsChecked = appSettings.EnableInternetRetrieval;
                GoogleApiKeyTextBox.Text = appSettings.GoogleApiKey;
                GoogleSearchEngineIdTextBox.Text = appSettings.GoogleSearchEngineId;
                InternetMaxResultsTextBox.Text = appSettings.InternetMaxResults.ToString();

                // 記憶管理の初期化
                LoadMemoryManagementSettings();

                // イベントハンドラーを設定
                SetupEventHandlers();

                _isInitialized = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"システム設定の初期化エラー: {ex.Message}", "エラー",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// イベントハンドラーを設定
        /// </summary>
        private void SetupEventHandlers()
        {
            // 通知API設定
            IsEnableNotificationApiCheckBox.Checked += OnSettingsChanged;
            IsEnableNotificationApiCheckBox.Unchecked += OnSettingsChanged;

            // デスクトップウォッチ設定
            ScreenshotEnabledCheckBox.Checked += OnSettingsChanged;
            ScreenshotEnabledCheckBox.Unchecked += OnSettingsChanged;
            CaptureActiveWindowOnlyCheckBox.Checked += OnSettingsChanged;
            CaptureActiveWindowOnlyCheckBox.Unchecked += OnSettingsChanged;
            ScreenshotIntervalTextBox.TextChanged += OnSettingsChanged;
            IdleTimeoutTextBox.TextChanged += OnSettingsChanged;

            // マイク設定
            MicThresholdSlider.ValueChanged += OnSettingsChanged;

            // CocoroCore2設定
            EnableProModeCheckBox.Checked += OnSettingsChanged;
            EnableProModeCheckBox.Unchecked += OnSettingsChanged;
            EnableInternetRetrievalCheckBox.Checked += OnSettingsChanged;
            EnableInternetRetrievalCheckBox.Unchecked += OnSettingsChanged;
            GoogleApiKeyTextBox.TextChanged += OnSettingsChanged;
            GoogleSearchEngineIdTextBox.TextChanged += OnSettingsChanged;
            InternetMaxResultsTextBox.TextChanged += OnSettingsChanged;
        }

        /// <summary>
        /// 設定変更イベントハンドラー
        /// </summary>
        private void OnSettingsChanged(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized)
                return;

            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// API詳細テキストを取得（エンドポイント/ボディ/レスポンス/使用例を含む）
        /// </summary>
        private string GetApiDetails()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("エンドポイント:");
            sb.AppendLine("POST http://127.0.0.1:55604/api/v1/notification");
            sb.AppendLine();
            sb.AppendLine("リクエストボディ (JSON):");
            sb.AppendLine("{");
            sb.AppendLine("  \"from\": \"アプリ名\",");
            sb.AppendLine("  \"message\": \"通知メッセージ\",");
            sb.AppendLine("  \"images\": [  // オプション（最大5枚）");
            sb.AppendLine("    \"data:image/jpeg;base64,/9j/4AAQ...\",  // 1枚目");
            sb.AppendLine("    \"data:image/png;base64,iVBORw0KGgo...\"  // 2枚目");
            sb.AppendLine("  ]");
            sb.AppendLine("}");
            sb.AppendLine();
            sb.AppendLine("レスポンス:");
            sb.AppendLine("HTTP/1.1 204 No Content");
            sb.AppendLine();
            sb.AppendLine("使用例 (cURL):");
            sb.AppendLine("# 1枚の画像を送る場合");
            sb.AppendLine("curl -X POST http://127.0.0.1:55604/api/v1/notification \\");
            sb.AppendLine("  -H \"Content-Type: application/json\" \\");
            sb.AppendLine("  -d '{\"from\":\"MyApp\",\"message\":\"処理完了\",\"images\":[\"data:image/jpeg;base64,...\"]}'");
            sb.AppendLine();
            sb.AppendLine("# 複数枚の画像を送る場合");
            sb.AppendLine("curl -X POST http://127.0.0.1:55604/api/v1/notification \\");
            sb.AppendLine("  -H \"Content-Type: application/json\" \\");
            sb.AppendLine("  -d '{\"from\":\"MyApp\",\"message\":\"結果\",\"images\":[\"data:image/jpeg;base64,...\",\"data:image/png;base64,...\"]}'");
            sb.AppendLine();
            sb.AppendLine("使用例 (PowerShell):");
            sb.AppendLine("# 複数枚の画像を送る場合");
            sb.AppendLine("Invoke-RestMethod -Method Post `");
            sb.AppendLine("  -Uri \"http://127.0.0.1:55604/api/v1/notification\" `");
            sb.AppendLine("  -ContentType \"application/json; charset=utf-8\" `");
            sb.AppendLine("  -Body '{\"from\":\"MyApp\",\"message\":\"結果\",\"images\":[\"data:image/jpeg;base64,...\",\"data:image/png;base64,...\"]}'");
            return sb.ToString();
        }

        /// <summary>
        /// 通知API有効状態を取得
        /// </summary>
        public bool GetIsEnableNotificationApi()
        {
            return IsEnableNotificationApiCheckBox.IsChecked ?? false;
        }

        /// <summary>
        /// 通知API有効状態を設定
        /// </summary>
        public void SetIsEnableNotificationApi(bool enabled)
        {
            IsEnableNotificationApiCheckBox.IsChecked = enabled;
        }

        /// <summary>
        /// スクリーンショット設定を取得
        /// </summary>
        public ScreenshotSettings GetScreenshotSettings()
        {
            var settings = new ScreenshotSettings
            {
                enabled = ScreenshotEnabledCheckBox.IsChecked ?? false,
                captureActiveWindowOnly = CaptureActiveWindowOnlyCheckBox.IsChecked ?? false
            };

            if (int.TryParse(ScreenshotIntervalTextBox.Text, out int interval))
            {
                settings.intervalMinutes = interval;
            }

            if (int.TryParse(IdleTimeoutTextBox.Text, out int timeout))
            {
                settings.idleTimeoutMinutes = timeout;
            }

            return settings;
        }

        /// <summary>
        /// スクリーンショット設定を設定
        /// </summary>
        public void SetScreenshotSettings(ScreenshotSettings settings)
        {
            ScreenshotEnabledCheckBox.IsChecked = settings.enabled;
            CaptureActiveWindowOnlyCheckBox.IsChecked = settings.captureActiveWindowOnly;
            ScreenshotIntervalTextBox.Text = settings.intervalMinutes.ToString();
            IdleTimeoutTextBox.Text = settings.idleTimeoutMinutes.ToString();
        }

        /// <summary>
        /// マイク設定を取得
        /// </summary>
        public MicrophoneSettings GetMicrophoneSettings()
        {
            return new MicrophoneSettings
            {
                inputThreshold = (int)MicThresholdSlider.Value
            };
        }

        /// <summary>
        /// マイク設定を設定
        /// </summary>
        public void SetMicrophoneSettings(MicrophoneSettings settings)
        {
            MicThresholdSlider.Value = settings.inputThreshold;
        }

        /// <summary>
        /// CocoroCore2設定を取得
        /// </summary>
        public (bool enableProMode, bool enableInternetRetrieval, string googleApiKey, string googleSearchEngineId, int internetMaxResults) GetCocoroCore2Settings()
        {
            bool enableProMode = EnableProModeCheckBox.IsChecked ?? true;
            bool enableInternetRetrieval = EnableInternetRetrievalCheckBox.IsChecked ?? true;
            string googleApiKey = GoogleApiKeyTextBox.Text;
            string googleSearchEngineId = GoogleSearchEngineIdTextBox.Text;
            int internetMaxResults = 5;

            if (int.TryParse(InternetMaxResultsTextBox.Text, out int maxResults))
            {
                if (maxResults >= 1 && maxResults <= 10)
                {
                    internetMaxResults = maxResults;
                }
            }

            return (enableProMode, enableInternetRetrieval, googleApiKey, googleSearchEngineId, internetMaxResults);
        }

        /// <summary>
        /// CocoroCore2設定を設定
        /// </summary>
        public void SetCocoroCore2Settings(bool enableProMode, bool enableInternetRetrieval, string googleApiKey, string googleSearchEngineId, int internetMaxResults)
        {
            EnableProModeCheckBox.IsChecked = enableProMode;
            EnableInternetRetrievalCheckBox.IsChecked = enableInternetRetrieval;
            GoogleApiKeyTextBox.Text = googleApiKey;
            GoogleSearchEngineIdTextBox.Text = googleSearchEngineId;
            InternetMaxResultsTextBox.Text = internetMaxResults.ToString();
        }

        // ========================================
        // 記憶管理機能
        // ========================================

        /// <summary>
        /// 記憶管理設定の読み込み
        /// </summary>
        private async void LoadMemoryManagementSettings()
        {
            try
            {
                await LoadRegisteredMemories();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"記憶管理設定の読み込みエラー: {ex.Message}");
                SelectedCharacterText.Text = "記憶管理の初期化に失敗しました";
                DeleteMemoryButton.IsEnabled = false;
            }
        }

        /// <summary>
        /// CocoroCore2に登録されているメモリー一覧を取得
        /// </summary>
        private async Task LoadRegisteredMemories()
        {
            try
            {
                var appSettings = AppSettings.Instance;
                using var coreClient = new CocoroCoreClient(appSettings.CocoroCorePort);

                // CocoroCore2からメモリー一覧を取得
                var memoriesResponse = await coreClient.GetMemoryListAsync();

                if (memoriesResponse.data?.Any() == true)
                {
                    // 表示用のメモリー情報リストを作成
                    var displayMemories = memoriesResponse.data.Select(u => new DisplayMemoryInfo
                    {
                        MemoryId = u.memory_id,
                        DisplayName = !string.IsNullOrEmpty(u.memory_name) ? u.memory_name : u.memory_id,
                        Role = u.role
                    }).ToList();

                    MemoryComboBox.ItemsSource = displayMemories;

                    // 最初のメモリーを選択
                    if (displayMemories.Any())
                    {
                        MemoryComboBox.SelectedIndex = 0;
                    }
                }
                else
                {
                    SelectedCharacterText.Text = "CocoroCore2にメモリーが登録されていません";
                    DeleteMemoryButton.IsEnabled = false;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"メモリー一覧取得エラー: {ex.Message}");
                SelectedCharacterText.Text = "メモリー一覧の取得に失敗しました";
                DeleteMemoryButton.IsEnabled = false;
            }
        }

        /// <summary>
        /// メモリー選択変更時
        /// </summary>
        private async void MemoryComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var selectedMemory = MemoryComboBox.SelectedItem as DisplayMemoryInfo;
            if (selectedMemory == null)
            {
                DeleteMemoryButton.IsEnabled = false;
                return;
            }

            await LoadMemoryStats(selectedMemory);
        }

        /// <summary>
        /// 更新ボタンクリック時
        /// </summary>
        private async void RefreshMemoryStatsButton_Click(object sender, RoutedEventArgs e)
        {
            // 現在選択中のメモリーIDを保存
            var currentSelectedMemoryId = (MemoryComboBox.SelectedItem as DisplayMemoryInfo)?.MemoryId;

            // メモリー一覧を再取得
            await LoadRegisteredMemories();

            // 前回選択していたメモリーを再選択（存在する場合）
            if (!string.IsNullOrEmpty(currentSelectedMemoryId))
            {
                var itemsSource = MemoryComboBox.ItemsSource as List<DisplayMemoryInfo>;
                if (itemsSource != null)
                {
                    var memoryToSelect = itemsSource.FirstOrDefault(u => u.MemoryId == currentSelectedMemoryId);
                    if (memoryToSelect != null)
                    {
                        MemoryComboBox.SelectedItem = memoryToSelect;
                    }
                    else
                    {
                        // 削除されたメモリーの場合は最初のメモリーを選択
                        if (itemsSource.Any())
                        {
                            MemoryComboBox.SelectedIndex = 0;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 記憶統計情報の読み込み
        /// </summary>
        private async Task LoadMemoryStats(DisplayMemoryInfo memory)
        {
            try
            {
                RefreshMemoryStatsButton.IsEnabled = false;
                SelectedCharacterText.Text = $"{memory.DisplayName} を読み込み中...";

                var appSettings = AppSettings.Instance;
                using var coreClient = new CocoroCoreClient(appSettings.CocoroCorePort);
                _currentMemoryStats = await coreClient.GetMemoryStatsAsync(memory.MemoryId);

                // UI更新
                SelectedCharacterText.Text = memory.DisplayName;
                TotalMemoriesText.Text = $"{_currentMemoryStats.total_memories:N0}件";
                MemoryDetailsText.Text = $"テキスト: {_currentMemoryStats.text_memories:N0}件 / " +
                                        $"アクティベーション: {_currentMemoryStats.activation_memories:N0}件 / " +
                                        $"パラメトリック: {_currentMemoryStats.parametric_memories:N0}件";

                if (_currentMemoryStats.last_updated.HasValue)
                {
                    LastUpdatedText.Text = _currentMemoryStats.last_updated.Value.ToString("yyyy/MM/dd HH:mm:ss");
                }
                else
                {
                    LastUpdatedText.Text = "不明";
                }

                // 削除ボタンの有効化（記憶が1件以上ある場合）
                DeleteMemoryButton.IsEnabled = _currentMemoryStats.total_memories > 0;
            }
            catch (Exception ex)
            {
                SelectedCharacterText.Text = memory.DisplayName;
                TotalMemoriesText.Text = "エラー";
                MemoryDetailsText.Text = ex.Message;
                LastUpdatedText.Text = "-";
                DeleteMemoryButton.IsEnabled = false;

                Debug.WriteLine($"記憶統計情報の読み込みエラー: {ex.Message}");
            }
            finally
            {
                RefreshMemoryStatsButton.IsEnabled = true;
            }
        }

        /// <summary>
        /// 記憶削除ボタンクリック時
        /// </summary>
        private async void DeleteMemoryButton_Click(object sender, RoutedEventArgs e)
        {
            var selectedMemory = MemoryComboBox.SelectedItem as DisplayMemoryInfo;
            if (selectedMemory == null || _currentMemoryStats == null) return;

            // 確認ダイアログ
            var result = MessageBox.Show(
                Window.GetWindow(this),
                $"「{selectedMemory.DisplayName}」の記憶（{_currentMemoryStats.total_memories:N0}件）を\n" +
                "すべて削除します。\n\n" +
                "内訳:\n" +
                $"  ・テキスト記憶: {_currentMemoryStats.text_memories:N0}件\n" +
                $"  ・アクティベーション記憶: {_currentMemoryStats.activation_memories:N0}件\n" +
                $"  ・パラメトリック記憶: {_currentMemoryStats.parametric_memories:N0}件\n\n" +
                "この操作は取り消すことができません。\n" +
                "本当に続行しますか？",
                "警告: 記憶の初期化",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning,
                MessageBoxResult.Cancel
            );

            if (result != MessageBoxResult.OK) return;

            // 二重確認
            var confirmResult = MessageBox.Show(
                Window.GetWindow(this),
                $"最終確認\n\n「{selectedMemory.DisplayName}」の記憶をすべて削除します。\n" +
                "よろしいですか？",
                "最終確認",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question,
                MessageBoxResult.No
            );

            if (confirmResult != MessageBoxResult.Yes) return;

            await ExecuteMemoryDeletion(selectedMemory);
        }

        /// <summary>
        /// 記憶削除の実行
        /// </summary>
        private async Task ExecuteMemoryDeletion(DisplayMemoryInfo memory)
        {
            var progressDialog = new MemoryDeleteProgressDialog
            {
                Owner = Window.GetWindow(this),
                CharacterName = memory.DisplayName,
                TotalMemories = _currentMemoryStats?.total_memories ?? 0
            };

            try
            {
                // プログレスダイアログを表示（非モーダル）
                progressDialog.Show();

                var appSettings = AppSettings.Instance;
                using var coreClient = new CocoroCoreClient(appSettings.CocoroCorePort);

                // 削除実行
                var response = await coreClient.DeleteUserMemoriesAsync(memory.MemoryId);

                progressDialog.Close();

                // 完了通知
                MessageBox.Show(
                    Window.GetWindow(this),
                    $"記憶の削除が完了しました。\n\n" +
                    $"削除された記憶: {response.deleted_count:N0}件\n" +
                    $"  ・テキスト記憶: {response.details.text_memories:N0}件\n" +
                    $"  ・アクティベーション記憶: {response.details.activation_memories:N0}件\n" +
                    $"  ・パラメトリック記憶: {response.details.parametric_memories:N0}件",
                    "完了",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information
                );

                // 統計情報を再読み込み
                await LoadMemoryStats(memory);
            }
            catch (Exception ex)
            {
                progressDialog.Close();

                string errorMessage = ex switch
                {
                    TimeoutException => "処理がタイムアウトしました。\nCocoroCore2の応答に時間がかかっています。",
                    HttpRequestException => "通信エラーが発生しました。\nCocoroCore2が起動していることを確認してください。",
                    InvalidOperationException => ex.Message,
                    _ => $"予期しないエラーが発生しました。\n\n詳細: {ex.Message}"
                };

                MessageBox.Show(
                    Window.GetWindow(this),
                    $"記憶の削除に失敗しました。\n\n{errorMessage}",
                    "エラー",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );

                Debug.WriteLine($"Memory deletion failed: {ex}");
            }
        }

        /// <summary>
        /// 詳細表示ボタンクリック時（未実装）
        /// </summary>
        private void ShowMemoryDetailsButton_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show(
                Window.GetWindow(this),
                "記憶の詳細表示機能は今後実装予定です。",
                "未実装",
                MessageBoxButton.OK,
                MessageBoxImage.Information
            );
        }
    }
}