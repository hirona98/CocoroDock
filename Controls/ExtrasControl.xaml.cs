using CocoroDock.Models;
using CocoroDock.Services;
using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace CocoroDock.Controls
{
    /// <summary>
    /// ExtrasControl.xaml の相互作用ロジック
    /// </summary>
    public partial class ExtrasControl : UserControl
    {
        /// <summary>
        /// 設定が変更されたときに発生するイベント
        /// </summary>
        public event EventHandler? SettingsChanged;

        /// <summary>
        /// 読み込み完了フラグ
        /// </summary>
        private bool _isInitialized = false;

        public ExtrasControl()
        {
            InitializeComponent();
        }

        /// <summary>
        /// 初期化処理
        /// </summary>
        public Task InitializeAsync()
        {
            try
            {
                var appSettings = AppSettings.Instance;

                // 定期コマンド実行設定
                EnableScheduledCommandCheckBox.IsChecked = appSettings.ScheduledCommandSettings.Enabled;
                CommandTextBox.Text = appSettings.ScheduledCommandSettings.Command;
                IntervalMinutesTextBox.Text = appSettings.ScheduledCommandSettings.IntervalMinutes.ToString();

                // イベントハンドラーを設定
                SetupEventHandlers();

                _isInitialized = true;

                return Task.CompletedTask;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"おまけ設定の初期化エラー: {ex.Message}", "エラー",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return Task.CompletedTask;
            }
        }

        /// <summary>
        /// イベントハンドラーを設定
        /// </summary>
        private void SetupEventHandlers()
        {
            // 定期コマンド実行設定
            EnableScheduledCommandCheckBox.Checked += OnSettingsChanged;
            EnableScheduledCommandCheckBox.Unchecked += OnSettingsChanged;
            CommandTextBox.TextChanged += OnSettingsChanged;
            IntervalMinutesTextBox.TextChanged += OnSettingsChanged;
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
        /// 定期コマンド実行設定を取得
        /// </summary>
        public ScheduledCommandSettings GetScheduledCommandSettings()
        {
            if (!int.TryParse(IntervalMinutesTextBox.Text, out int interval))
            {
                throw new InvalidOperationException("実行間隔には正の整数を入力してください。");
            }

            if (interval <= 0)
            {
                throw new InvalidOperationException("実行間隔には1以上の値を入力してください。");
            }

            bool isEnabled = EnableScheduledCommandCheckBox.IsChecked ?? false;
            string command = CommandTextBox.Text ?? string.Empty;

            if (isEnabled && string.IsNullOrWhiteSpace(command))
            {
                throw new InvalidOperationException("定期コマンド実行を有効にする場合はコマンドを入力してください。");
            }

            return new ScheduledCommandSettings
            {
                Enabled = isEnabled,
                Command = command,
                IntervalMinutes = interval
            };
        }

        /// <summary>
        /// 定期コマンド実行設定を設定
        /// </summary>
        public void SetScheduledCommandSettings(ScheduledCommandSettings settings)
        {
            EnableScheduledCommandCheckBox.IsChecked = settings.Enabled;
            CommandTextBox.Text = settings.Command;
            IntervalMinutesTextBox.Text = settings.IntervalMinutes.ToString();
        }
    }
}
