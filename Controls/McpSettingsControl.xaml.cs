using CocoroDock.Services;
using CocoroDock.ViewModels;
using System;
using System.Windows;
using System.Windows.Controls;

namespace CocoroDock.Controls
{
    /// <summary>
    /// McpSettingsControl.xaml の相互作用ロジック
    /// </summary>
    public partial class McpSettingsControl : UserControl
    {
        /// <summary>
        /// 設定が変更されたときに発生するイベント
        /// </summary>
        public event EventHandler? SettingsChanged;

        /// <summary>
        /// MCPタブ用ViewModel
        /// </summary>
        private McpTabViewModel? _mcpTabViewModel;

        public McpSettingsControl()
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
                // MCPタブViewModelの初期化
                _mcpTabViewModel = new McpTabViewModel(AppSettings.Instance);
                this.DataContext = _mcpTabViewModel;

                // ViewModelが初期化されたので、バインディングが自動的に動作する
                // 直接UIコントロールを設定する必要はない
            }
            catch (Exception ex)
            {
                MessageBox.Show($"MCP設定の初期化エラー: {ex.Message}", "エラー",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }


        /// <summary>
        /// 現在のMCP有効状態を取得
        /// </summary>
        public bool GetMcpEnabled()
        {
            return _mcpTabViewModel?.IsMcpEnabled ?? false;
        }

        /// <summary>
        /// MCP有効状態を設定
        /// </summary>
        public void SetMcpEnabled(bool enabled)
        {
            if (_mcpTabViewModel != null)
            {
                _mcpTabViewModel.IsMcpEnabled = enabled;
            }
        }

        /// <summary>
        /// 設定を保存して再読み込みボタンクリック
        /// </summary>
        private void SaveMcpConfigButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_mcpTabViewModel != null && _mcpTabViewModel.SaveConfigCommand.CanExecute(null))
                {
                    _mcpTabViewModel.SaveConfigCommand.Execute(null);

                    // 設定変更イベントを発生
                    SettingsChanged?.Invoke(this, EventArgs.Empty);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"MCP設定保存エラー: {ex.Message}", "エラー",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// ViewModelを取得
        /// </summary>
        public McpTabViewModel? GetViewModel()
        {
            return _mcpTabViewModel;
        }
    }
}