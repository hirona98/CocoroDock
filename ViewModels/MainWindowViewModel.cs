using CocoroDock.Communication;
using CocoroDock.Services;
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace CocoroDock.ViewModels
{
    public class MainWindowViewModel : ViewModelBase
    {
        private readonly CommunicationService _communicationService;
        private readonly AppSettings _appSettings;
        private Process? _aiProcess;
        private bool _isConnected;
        private string _statusMessage = "";
        private bool _isAiRunning;

        public bool IsConnected
        {
            get => _isConnected;
            private set => SetProperty(ref _isConnected, value);
        }

        public string StatusMessage
        {
            get => _statusMessage;
            private set => SetProperty(ref _statusMessage, value);
        }

        public bool IsAiRunning
        {
            get => _isAiRunning;
            private set => SetProperty(ref _isAiRunning, value);
        }

        public ICommand OpenAdminWindowCommand { get; }

        public ICommand ToggleAiProcessCommand { get; }

        public event EventHandler<string>? ChatMessageReceived;

        public event EventHandler<SystemMessagePayload>? SystemMessageReceived;

        public event EventHandler<string>? ErrorOccurred;

        public MainWindowViewModel()
        {
            _appSettings = AppSettings.Instance;
            _communicationService = new CommunicationService(_appSettings.WebSocketPort);

            OpenAdminWindowCommand = new RelayCommand(_ => OpenAdminWindow());
            ToggleAiProcessCommand = new RelayCommand(_ => ToggleAiProcess());

            _communicationService.ChatMessageReceived += OnChatMessageReceived;
            _communicationService.SystemMessageReceived += OnSystemMessageReceived;
            _communicationService.StatusUpdateReceived += OnStatusUpdateReceived;
            _communicationService.ErrorOccurred += OnErrorOccurred;
            _communicationService.Connected += OnConnected;
            _communicationService.Disconnected += OnDisconnected;

            InitializeAsync();
        }

        private async void InitializeAsync()
        {
            try
            {
                await _communicationService.StartServerAsync();

                if (_appSettings.AutoStartAi)
                {
                    await Task.Delay(500); // 少し待機してからプロセス起動
                    StartAiProcess();
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"初期化エラー: {ex.Message}";
                Debug.WriteLine($"初期化エラー: {ex.Message}");
            }
        }

        private void OpenAdminWindow()
        {
            try
            {
                var adminWindow = new Controls.AdminWindow();
                adminWindow.Owner = Application.Current.MainWindow;
                adminWindow.ShowDialog();
            }
            catch (Exception ex)
            {
                StatusMessage = $"管理画面オープンエラー: {ex.Message}";
                Debug.WriteLine($"管理画面オープンエラー: {ex.Message}");
            }
        }

        private void ToggleAiProcess()
        {
            if (IsAiRunning)
            {
                StopAiProcess();
            }
            else
            {
                StartAiProcess();
            }
        }

        private void StartAiProcess()
        {
            try
            {
                if (IsAiRunning || _aiProcess != null)
                {
                    return;
                }

                StatusMessage = "AIプロセスを起動中...";

                string aiPath = _appSettings.AiExecutablePath;
                if (string.IsNullOrEmpty(aiPath))
                {
                    StatusMessage = "AIの実行ファイルパスが設定されていません";
                    return;
                }

                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = aiPath,
                    UseShellExecute = true,
                    CreateNoWindow = false
                };

                _aiProcess = Process.Start(startInfo);
                if (_aiProcess != null)
                {
                    IsAiRunning = true;
                    _aiProcess.EnableRaisingEvents = true;
                    _aiProcess.Exited += AiProcess_Exited;
                    StatusMessage = "AIプロセスを起動しました";
                }
                else
                {
                    StatusMessage = "AIプロセスの起動に失敗しました";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"AIプロセス起動エラー: {ex.Message}";
                Debug.WriteLine($"AIプロセス起動エラー: {ex.Message}");
                IsAiRunning = false;
                _aiProcess = null;
            }
        }

        private void StopAiProcess()
        {
            try
            {
                if (!IsAiRunning || _aiProcess == null)
                {
                    return;
                }

                StatusMessage = "AIプロセスを停止中...";

                if (!_aiProcess.HasExited)
                {
                    _communicationService.SendControlCommandAsync("shutdown", "User requested shutdown").Wait();
                    
                    Task.Delay(2000).Wait();
                }

                if (!_aiProcess.HasExited)
                {
                    _aiProcess.Kill();
                }

                _aiProcess = null;
                IsAiRunning = false;
                StatusMessage = "AIプロセスを停止しました";
            }
            catch (Exception ex)
            {
                StatusMessage = $"AIプロセス停止エラー: {ex.Message}";
                Debug.WriteLine($"AIプロセス停止エラー: {ex.Message}");
            }
        }

        private void AiProcess_Exited(object? sender, EventArgs e)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                IsAiRunning = false;
                _aiProcess = null;
                StatusMessage = "AIプロセスが終了しました";
            });
        }

        public async Task SendChatMessageAsync(string message)
        {
            try
            {
                await _communicationService.SendChatMessageAsync(message);
            }
            catch (Exception ex)
            {
                StatusMessage = $"メッセージ送信エラー: {ex.Message}";
                Debug.WriteLine($"メッセージ送信エラー: {ex.Message}");
            }
        }

        private void OnChatMessageReceived(object? sender, string message)
        {
            ChatMessageReceived?.Invoke(this, message);
        }

        private void OnSystemMessageReceived(object? sender, SystemMessagePayload message)
        {
            SystemMessageReceived?.Invoke(this, message);
        }

        private void OnStatusUpdateReceived(object? sender, StatusMessagePayload status)
        {
            StatusMessage = status.message;
        }

        private void OnErrorOccurred(object? sender, string error)
        {
            StatusMessage = $"エラー: {error}";
            ErrorOccurred?.Invoke(this, error);
            
            ErrorHandlingService.Instance.LogError(
                ErrorHandlingService.ErrorLevel.Error, 
                error);
        }

        private void OnConnected(object? sender, EventArgs e)
        {
            IsConnected = true;
            StatusMessage = "AIと接続しました";
        }

        private void OnDisconnected(object? sender, EventArgs e)
        {
            IsConnected = false;
            StatusMessage = "AIとの接続が切断されました";
        }

        public void Dispose()
        {
            StopAiProcess();

            _communicationService.StopServerAsync().Wait();
            _communicationService.Dispose();
        }
    }
}
