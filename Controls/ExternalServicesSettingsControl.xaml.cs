using CocoroDock.Services;
using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace CocoroDock.Controls
{
    /// <summary>
    /// ExternalServicesSettingsControl.xaml の相互作用ロジック
    /// </summary>
    public partial class ExternalServicesSettingsControl : UserControl
    {
        /// <summary>
        /// 設定が変更されたときに発生するイベント
        /// </summary>
        public event EventHandler? SettingsChanged;

        /// <summary>
        /// 読み込み完了フラグ
        /// </summary>
        private bool _isInitialized = false;

        public ExternalServicesSettingsControl()
        {
            InitializeComponent();
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

                // イベントハンドラーを設定
                SetupEventHandlers();

                _isInitialized = true;

                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"外部サービス設定の初期化エラー: {ex.Message}", "エラー",
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
    }
}
