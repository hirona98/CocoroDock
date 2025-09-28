using CocoroDock.Communication;
using CocoroDock.Models;
using CocoroDock.Services;
using CocoroDock.Windows;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace CocoroDock.Controls
{
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
        /// リマインダーサービス
        /// </summary>
        private IReminderService _reminderService;

        public SystemSettingsControl()
        {
            InitializeComponent();
            _reminderService = new ReminderService(AppSettings.Instance);
        }

        /// <summary>
        /// 初期化処理
        /// </summary>
        public async Task InitializeAsync()
        {
            try
            {
                var appSettings = AppSettings.Instance;

                // Webサービス設定
                IsEnableWebServiceCheckBox.IsChecked = appSettings.IsEnableWebService;

                // 通知API設定
                IsEnableNotificationApiCheckBox.IsChecked = appSettings.IsEnableNotificationApi;
                ApiDetailsTextBox.Text = GetApiDetails();

                // デスクトップウォッチ設定
                ScreenshotEnabledCheckBox.IsChecked = appSettings.ScreenshotSettings.enabled;
                CaptureActiveWindowOnlyCheckBox.IsChecked = appSettings.ScreenshotSettings.captureActiveWindowOnly;
                ScreenshotIntervalTextBox.Text = appSettings.ScreenshotSettings.intervalMinutes.ToString();
                IdleTimeoutTextBox.Text = appSettings.ScreenshotSettings.idleTimeoutMinutes.ToString();
                ExcludePatternsTextBox.Text = string.Join(Environment.NewLine, appSettings.ScreenshotSettings.excludePatterns);

                // マイク設定
                MicThresholdSlider.Value = appSettings.MicrophoneSettings.inputThreshold;

                // CocoroCoreM設定
                EnableInternetRetrievalCheckBox.IsChecked = appSettings.EnableInternetRetrieval;
                GoogleApiKeyTextBox.Text = appSettings.GoogleApiKey;
                GoogleSearchEngineIdTextBox.Text = appSettings.GoogleSearchEngineId;
                InternetMaxResultsTextBox.Text = appSettings.InternetMaxResults.ToString();

                // リマインダーUI初期化（スペース区切り形式）
                ReminderDateTimeTextBox.Text = DateTime.Now.AddHours(1).ToString("yyyy-MM-dd HH:mm");

                // リマインダーを読み込み
                await LoadRemindersAsync();

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
            // Webサービス設定
            IsEnableWebServiceCheckBox.Checked += OnSettingsChanged;
            IsEnableWebServiceCheckBox.Unchecked += OnSettingsChanged;

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
            ExcludePatternsTextBox.TextChanged += OnSettingsChanged;

            // マイク設定
            MicThresholdSlider.ValueChanged += OnSettingsChanged;

            // CocoroCoreM設定
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
        /// Webサービス有効状態を取得
        /// </summary>
        public bool GetIsEnableWebService()
        {
            return IsEnableWebServiceCheckBox.IsChecked ?? false;
        }

        /// <summary>
        /// Webサービス有効状態を設定
        /// </summary>
        public void SetIsEnableWebService(bool enabled)
        {
            IsEnableWebServiceCheckBox.IsChecked = enabled;
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

            // 除外パターンを取得（空行を除外）
            var patterns = ExcludePatternsTextBox.Text
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Trim())
                .Where(p => !string.IsNullOrEmpty(p))
                .ToList();
            settings.excludePatterns = patterns;

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
            ExcludePatternsTextBox.Text = string.Join(Environment.NewLine, settings.excludePatterns);
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
        /// CocoroCoreM設定を取得
        /// </summary>
        public (bool enableProMode, bool enableInternetRetrieval, string googleApiKey, string googleSearchEngineId, int internetMaxResults) GetCocoroCoreMSettings()
        {
            bool enableProMode = true; // 設定ファイルのみで制御
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
        /// CocoroCoreM設定を設定
        /// </summary>
        public void SetCocoroCoreMSettings(bool enableProMode, bool enableInternetRetrieval, string googleApiKey, string googleSearchEngineId, int internetMaxResults)
        {
            // enableProModeはコメントアウト（設定ファイルでのみ制御）
            EnableInternetRetrievalCheckBox.IsChecked = enableInternetRetrieval;
            GoogleApiKeyTextBox.Text = googleApiKey;
            GoogleSearchEngineIdTextBox.Text = googleSearchEngineId;
            InternetMaxResultsTextBox.Text = internetMaxResults.ToString();
        }

        #region リマインダー関連メソッド

        /// <summary>
        /// リマインダーリストを読み込み
        /// </summary>
        private async Task LoadRemindersAsync()
        {
            try
            {
                var reminders = await _reminderService.GetAllRemindersAsync();

                // UIスレッドで実行
                Dispatcher.Invoke(() =>
                {
                    RemindersItemsControl.ItemsSource = reminders;
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"リマインダー読み込みエラー: {ex.Message}");
            }
        }

        /// <summary>
        /// 現在時刻設定ボタンクリック
        /// </summary>
        private void SetCurrentTimeButton_Click(object sender, RoutedEventArgs e)
        {
            ReminderDateTimeTextBox.Text = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
        }

        /// <summary>
        /// リマインダー追加ボタンクリック
        /// </summary>
        private async void AddReminderButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dateTimeText = ReminderDateTimeTextBox.Text.Trim();
                var messageText = ReminderMessageTextBox.Text.Trim();

                if (string.IsNullOrEmpty(dateTimeText))
                {
                    MessageBox.Show("予定日時を入力してください。", "入力エラー",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (string.IsNullOrEmpty(messageText))
                {
                    MessageBox.Show("メッセージを入力してください。", "入力エラー",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // スペース区切り形式の解析（CocoroCoreM/SQLiteが対応）
                DateTime scheduledAt;
                if (!DateTime.TryParseExact(dateTimeText, new[] { "yyyy-MM-dd HH:mm", "yyyy-M-d H:mm", "yyyy-MM-dd HH:mm:ss", "yyyy-M-d H:mm:ss" },
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None,
                    out scheduledAt))
                {
                    MessageBox.Show("日時の形式が正しくありません。\nYYYY-MM-DD HH:MM の形式で入力してください。",
                        "入力エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (scheduledAt <= DateTime.Now)
                {
                    MessageBox.Show("過去の時刻は設定できません。", "入力エラー",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var reminder = new Reminder
                {
                    RemindDatetime = scheduledAt.ToString("yyyy-MM-dd HH:mm:ss"),
                    Requirement = messageText
                };

                await _reminderService.CreateReminderAsync(reminder);
                await LoadRemindersAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"リマインダー追加エラー: {ex.Message}", "エラー",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// リマインダー削除ボタンクリック
        /// </summary>
        private async void DeleteReminderButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is Button button && button.Tag is int reminderId)
                {
                    await _reminderService.DeleteReminderAsync(reminderId);
                    await LoadRemindersAsync();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"リマインダー削除エラー: {ex.Message}", "エラー",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// リマインダーリスト更新ボタンクリック
        /// </summary>
        private async void RefreshRemindersButton_Click(object sender, RoutedEventArgs e)
        {
            await LoadRemindersAsync();
        }

        /// <summary>
        /// リマインダー入力テキストをパース
        /// </summary>
        /// <param name="input">入力文字列</param>
        /// <returns>予定時刻とメッセージのタプル</returns>
        private (DateTime?, string) ParseReminderInput(string input)
        {
            try
            {
                // 「○時間後に〜」「○分後に〜」形式
                var relativeMatch = Regex.Match(input, @"^(\d+)(時間|分)後に(.+)$");
                if (relativeMatch.Success)
                {
                    var amount = int.Parse(relativeMatch.Groups[1].Value);
                    var unit = relativeMatch.Groups[2].Value;
                    var message = relativeMatch.Groups[3].Value;

                    var scheduledAt = unit == "時間"
                        ? DateTime.Now.AddHours(amount)
                        : DateTime.Now.AddMinutes(amount);

                    return (scheduledAt, message);
                }

                // 「HH:mmに〜」形式
                var timeMatch = Regex.Match(input, @"^(\d{1,2}):(\d{2})に(.+)$");
                if (timeMatch.Success)
                {
                    var hour = int.Parse(timeMatch.Groups[1].Value);
                    var minute = int.Parse(timeMatch.Groups[2].Value);
                    var message = timeMatch.Groups[3].Value;

                    if (hour >= 0 && hour <= 23 && minute >= 0 && minute <= 59)
                    {
                        var now = DateTime.Now;
                        var scheduledAt = new DateTime(now.Year, now.Month, now.Day, hour, minute, 0);

                        // 指定時刻が現在時刻より前の場合は翌日に設定
                        if (scheduledAt <= now)
                        {
                            scheduledAt = scheduledAt.AddDays(1);
                        }

                        return (scheduledAt, message);
                    }
                }

                // 「yyyy-MM-dd HH:mmに〜」形式
                var fullDateMatch = Regex.Match(input, @"^(\d{4}-\d{2}-\d{2} \d{1,2}:\d{2})に(.+)$");
                if (fullDateMatch.Success)
                {
                    var dateTimeStr = fullDateMatch.Groups[1].Value;
                    var message = fullDateMatch.Groups[2].Value;

                    if (DateTime.TryParse(dateTimeStr, out var scheduledAt))
                    {
                        return (scheduledAt, message);
                    }
                }

                return (null, input);
            }
            catch
            {
                return (null, input);
            }
        }

        #endregion
    }
}