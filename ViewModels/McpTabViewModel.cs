using CocoroDock.Communication;
using CocoroDock.Services;
using CocoroDock.Utilities;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Threading;

namespace CocoroDock.ViewModels
{
    /// <summary>
    /// MCPタブのViewModel
    /// </summary>
    public class McpTabViewModel : INotifyPropertyChanged, IDisposable
    {
        private readonly IAppSettings _appSettings;
        private readonly CocoroCoreClient _cocoroCoreClient;
        private readonly DispatcherTimer _statusUpdateTimer;
        private readonly string _mcpConfigPath;

        private bool _isMcpEnabled;
        private bool _originalMcpEnabled;
        private string _mcpConfigJson = string.Empty;
        private McpStatus? _mcpStatus;
        private string _statusMessage = string.Empty;
        private string _diagnosticDetails = string.Empty;
        private bool _isLoading;

        public McpTabViewModel(IAppSettings appSettings)
        {
            _appSettings = appSettings;
            _cocoroCoreClient = new CocoroCoreClient(_appSettings.CocoroCorePort);

            // MCPファイルのパス設定（設定ファイルと同じディレクトリのUserDataフォルダ）
            var execDir = AppContext.BaseDirectory;
            var userDataDir = Path.Combine(execDir, "UserData2");
            _mcpConfigPath = Path.Combine(userDataDir, "cocoroAiMcp.json");

            // タイマーの初期化（データがない時のみ再取得用）
            _statusUpdateTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _statusUpdateTimer.Tick += async (s, e) => await RetryUpdateIfDataMissing();

            // コマンドの初期化
            SaveConfigCommand = new RelayCommand(async () => await SaveMcpConfigAsync(), () => !IsLoading && !_statusUpdateTimer.IsEnabled);

            // 初期化
            LoadMcpConfig();
            IsMcpEnabled = _appSettings.IsEnableMcp;
            _originalMcpEnabled = _appSettings.IsEnableMcp;

            // 初期表示を設定
            DiagnosticDetails = "設定確認中...";
            StatusMessage = "接続状態を確認中...";

            // 設定ダイアログ開始時にデータ取得
            _ = InitialMcpStatusUpdateAsync();
        }

        #region Properties

        public bool IsMcpEnabled
        {
            get => _isMcpEnabled;
            set
            {
                if (_isMcpEnabled != value)
                {
                    _isMcpEnabled = value;
                    OnPropertyChanged();
                }
            }
        }

        public string McpConfigJson
        {
            get => _mcpConfigJson;
            set
            {
                if (_mcpConfigJson != value)
                {
                    _mcpConfigJson = value;
                    OnPropertyChanged();
                }
            }
        }

        public McpStatus? McpStatus
        {
            get => _mcpStatus;
            set
            {
                if (_mcpStatus != value)
                {
                    _mcpStatus = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(McpServers));
                }
            }
        }

        public ObservableCollection<McpServerViewModel> McpServers
        {
            get
            {
                var servers = new ObservableCollection<McpServerViewModel>();
                if (_mcpStatus?.servers != null)
                {
                    foreach (var server in _mcpStatus.servers)
                    {
                        servers.Add(new McpServerViewModel
                        {
                            Name = server.Key,
                            IsConnected = server.Value.connected,
                            ToolCount = server.Value.tool_count,
                            ConnectionType = server.Value.connection_type
                        });
                    }
                }
                return servers;
            }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set
            {
                if (_statusMessage != value)
                {
                    _statusMessage = value;
                    OnPropertyChanged();
                }
            }
        }

        public string DiagnosticDetails
        {
            get => _diagnosticDetails;
            set
            {
                if (_diagnosticDetails != value)
                {
                    _diagnosticDetails = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsLoading
        {
            get => _isLoading;
            set
            {
                if (_isLoading != value)
                {
                    _isLoading = value;
                    OnPropertyChanged();
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        #endregion

        #region Commands

        public ICommand SaveConfigCommand { get; }

        /// <summary>
        /// MCP設定が変更されているかどうかを確認
        /// </summary>
        public bool HasMcpSettingsChanged => _isMcpEnabled != _originalMcpEnabled;

        #endregion

        #region Methods

        private void LoadMcpConfig()
        {
            try
            {
                if (File.Exists(_mcpConfigPath))
                {
                    McpConfigJson = File.ReadAllText(_mcpConfigPath);
                }
                else
                {
                    // サンプルファイルから読み込み（Settingと同じUserDataディレクトリ）
                    var userDataDir = Path.GetDirectoryName(_mcpConfigPath) ?? "";
                    var samplePath = Path.Combine(userDataDir, "Sample_CocoroAiMcp.json");
                    if (File.Exists(samplePath))
                    {
                        McpConfigJson = File.ReadAllText(samplePath);
                    }
                    else
                    {
                        // サンプルファイルがない場合は空の値
                        McpConfigJson = "";
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MCPファイル読み込みエラー: {ex.Message}");
                StatusMessage = $"設定ファイルの読み込み失敗: {ex.Message}";
            }
        }

        private async Task SaveMcpConfigAsync()
        {
            IsLoading = true;
            try
            {
                // JSONの妥当性チェック
                try
                {
                    var jsonObj = System.Text.Json.JsonSerializer.Deserialize<object>(McpConfigJson);
                }
                catch (Exception ex)
                {
                    StatusMessage = $"JSON形式が無効です: {ex.Message}";
                    return;
                }

                // MCPファイルに保存
                await File.WriteAllTextAsync(_mcpConfigPath, McpConfigJson);

                // MCP有効無効の設定のみを保存（他の設定は変更しない）
                var currentSettings = _appSettings.GetConfigSettings();
                currentSettings.isEnableMcp = IsMcpEnabled;
                _appSettings.UpdateSettings(currentSettings);
                _appSettings.SaveAppSettings(); // ファイルに保存

                StatusMessage = "設定保存";

                // 元の値を更新（設定保存後の基準値として）
                _originalMcpEnabled = IsMcpEnabled;

                // CocoroCore再起動の通知
                await RestartCocoroCoreAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MCP設定保存エラー: {ex.Message}");
                StatusMessage = $"設定保存失敗: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task InitialMcpStatusUpdateAsync()
        {
            try
            {
                await UpdateMcpStatusAsync();

                // MCPステータスが正常に取得できた場合はタイマーを停止
                if (McpStatus != null)
                {
                    _statusUpdateTimer.Stop();
                    CommandManager.InvalidateRequerySuggested();
                    IsLoading = false;
                }
                else
                {
                    // データが不足している場合はタイマーを開始して再取得
                    _statusUpdateTimer.Start();
                    CommandManager.InvalidateRequerySuggested();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"初期MCPステータス更新エラー: {ex.Message}");
                // 初回接続エラー時にもメッセージを表示
                StatusMessage = "CocoroCoreの起動を待っています";
                DiagnosticDetails = "";
                // 失敗した場合もタイマーを開始して再試行
                _statusUpdateTimer.Start();
                CommandManager.InvalidateRequerySuggested();
            }
        }

        private async Task RetryUpdateIfDataMissing()
        {
            // 常にポーリングを実行（接続できるまで継続）
            try
            {
                await UpdateMcpStatusAsync();

                // MCPステータスが正常に取得できた場合はタイマーを停止
                if (McpStatus != null)
                {
                    _statusUpdateTimer.Stop();
                    CommandManager.InvalidateRequerySuggested();
                    IsLoading = false;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MCP再取得エラー: {ex.Message}");
                // 接続エラー時にもメッセージを表示
                StatusMessage = "CocoroCoreの起動を待っています";
                DiagnosticDetails = "";
            }
        }

        private async Task UpdateMcpStatusAsync()
        {
            try
            {
                // ヘルスチェックでMCPステータスを取得
                var health = await _cocoroCoreClient.GetDetailedHealthAsync();
                McpStatus = health.mcp_status;

                if (McpStatus?.error != null)
                {
                    StatusMessage = $"MCPエラー: {McpStatus.error}";
                    DiagnosticDetails = "";
                }
                else
                {
                    StatusMessage = $"接続済み: {McpStatus?.connected_servers ?? 0}/{McpStatus?.total_servers ?? 0} サーバー, {McpStatus?.total_tools ?? 0}個のツール";

                    // 専用APIからツール登録ログを取得
                    try
                    {
                        var logResponse = await _cocoroCoreClient.GetMcpToolRegistrationLogAsync();
                        if (logResponse.logs != null && logResponse.logs.Count > 0)
                        {
                            DiagnosticDetails = string.Join("\n", logResponse.logs);
                            // ログが正常に取得できた場合はポーリングを停止
                            _statusUpdateTimer.Stop();
                            CommandManager.InvalidateRequerySuggested();
                        }
                        else
                        {
                            DiagnosticDetails = "ツール登録ログがありません";
                            // ログがない場合もポーリングを停止（正常な状態として扱う）
                            _statusUpdateTimer.Stop();
                            CommandManager.InvalidateRequerySuggested();
                        }
                    }
                    catch (Exception logEx)
                    {
                        Debug.WriteLine($"ツール登録ログ取得エラー: {logEx.Message}");
                        DiagnosticDetails = "ツール登録ログの取得に失敗しました";
                        // エラーの場合はポーリングを継続
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MCPステータス更新エラー: {ex.Message}");
                StatusMessage = "CocoroCoreの起動を待っています";
                McpStatus = null;
                DiagnosticDetails = "";
            }
        }


        private async Task RestartCocoroCoreAsync()
        {
            try
            {
                StatusMessage = "CocoroCoreを再起動しています...";
                DiagnosticDetails = "設定確認中...";

                // ProcessHelperを使用してCocoroCoreを再起動
                await Task.Run(() =>
                {
                    ProcessHelper.LaunchExternalApplication("CocoroCore.exe", "CocoroCore", ProcessOperation.RestartIfRunning);
                });

                // 起動を待つ
                await Task.Delay(5000);

                // ポーリングを再開
                _statusUpdateTimer.Start();
                CommandManager.InvalidateRequerySuggested();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"CocoroCore再起動エラー: {ex.Message}");
                StatusMessage = "再起動失敗";
                IsLoading = false;
            }
        }


        /// <summary>
        /// MCP設定を適用し、必要に応じてCocoroCoreを再起動
        /// </summary>
        public async Task ApplyMcpSettingsAsync()
        {
            if (HasMcpSettingsChanged)
            {
                // 設定を保存
                _appSettings.IsEnableMcp = _isMcpEnabled;
                _appSettings.SaveAppSettings();

                // 元の値を更新
                _originalMcpEnabled = _isMcpEnabled;

                // CocoroCoreを再起動
                await RestartCocoroCoreAsync();
            }
        }

        #endregion

        #region INotifyPropertyChanged

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #endregion

        #region IDisposable

        private bool _disposed = false;

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // タイマーを停止して破棄
                    if (_statusUpdateTimer != null)
                    {
                        _statusUpdateTimer.Stop();
                        _statusUpdateTimer.Tick -= async (s, e) => await RetryUpdateIfDataMissing();
                    }
                    Debug.WriteLine("MCPタブのタイマーを停止しました");
                }
                _disposed = true;
            }
        }

        #endregion
    }

    /// <summary>
    /// MCPサーバー表示用ViewModel
    /// </summary>
    public class McpServerViewModel
    {
        public string Name { get; set; } = string.Empty;
        public bool IsConnected { get; set; }
        public int ToolCount { get; set; }
        public string ConnectionType { get; set; } = string.Empty;

        public string Status => IsConnected ? $"接続済み ({ToolCount} ツール)" : "未接続";
    }

    /// <summary>
    /// シンプルなRelayCommand実装
    /// </summary>
    public class RelayCommand : ICommand
    {
        private readonly Func<Task> _executeAsync;
        private readonly Func<bool>? _canExecute;

        public RelayCommand(Func<Task> executeAsync, Func<bool>? canExecute = null)
        {
            _executeAsync = executeAsync;
            _canExecute = canExecute;
        }

        public event EventHandler? CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }

        public bool CanExecute(object? parameter)
        {
            return _canExecute?.Invoke() ?? true;
        }

        public async void Execute(object? parameter)
        {
            await _executeAsync();
        }
    }
}