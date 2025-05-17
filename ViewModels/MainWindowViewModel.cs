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
        private readonly ProcessManagementService _processManager;
        private readonly AppSettings _appSettings;
        private bool _isConnected;
        private string _statusMessage = "";

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
            get => _processManager.IsAiRunning;
        }

        public ICommand OpenAdminWindowCommand { get; }

        public ICommand ToggleAiProcessCommand { get; }

        public event EventHandler<string>? ChatMessageReceived;

        public event EventHandler<SystemMessagePayload>? SystemMessageReceived;

        public event EventHandler<string>? ErrorOccurred;

        public MainWindowViewModel()
        {
            _appSettings = AppSettings.Instance;
            _communicationService = new CommunicationService(_appSettings.CocoroDockPort);
            _processManager = ProcessManagementService.Instance;

            OpenAdminWindowCommand = new RelayCommand(_ => OpenAdminWindow());
            ToggleAiProcessCommand = new RelayCommand(_ => _processManager.ToggleAiProcess());

            _communicationService.ChatMessageReceived += OnChatMessageReceived;
            _communicationService.SystemMessageReceived += OnSystemMessageReceived;
            _communicationService.StatusUpdateReceived += OnStatusUpdateReceived;
            _communicationService.ErrorOccurred += OnErrorOccurred;
            _communicationService.Connected += OnConnected;
            _communicationService.Disconnected += OnDisconnected;

            _processManager.StatusChanged += OnProcessStatusChanged;
            _processManager.AiProcessStarted += (_, _) => OnPropertyChanged(nameof(IsAiRunning));
            _processManager.AiProcessStopped += (_, _) => OnPropertyChanged(nameof(IsAiRunning));

            InitializeAsync();
        }

        private async void InitializeAsync()
        {
            try
            {
                await _communicationService.StartServerAsync();
                _processManager.Initialize();
            }
            catch (Exception ex)
            {
                StatusMessage = $"初期化エラー: {ex.Message}";
                ErrorHandlingService.Instance.LogError(
                    ErrorHandlingService.ErrorLevel.Error,
                    "初期化エラー",
                    ex);
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
                ErrorHandlingService.Instance.LogError(
                    ErrorHandlingService.ErrorLevel.Error,
                    "管理画面オープンエラー",
                    ex);
            }
        }

        private void OnProcessStatusChanged(object? sender, string status)
        {
            StatusMessage = status;
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
                ErrorHandlingService.Instance.LogError(
                    ErrorHandlingService.ErrorLevel.Error,
                    "メッセージ送信エラー",
                    ex);
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
            StatusMessage = status.Status;
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
            _processManager.Dispose();
            _communicationService.StopServerAsync().Wait();
            _communicationService.Dispose();
        }
    }
}
