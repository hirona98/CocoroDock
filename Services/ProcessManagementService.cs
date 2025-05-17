using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;

namespace CocoroDock.Services
{
    public class ProcessManagementService
    {
        private static ProcessManagementService? _instance;
        private static readonly object _lock = new object();

        private Process? _aiProcess;
        private readonly CommunicationService _communicationService;
        private readonly AppSettings _appSettings;

        public bool IsAiRunning { get; private set; }

        public event EventHandler<string>? StatusChanged;
        public event EventHandler? AiProcessStarted;
        public event EventHandler? AiProcessStopped;

        public static ProcessManagementService Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        _instance ??= new ProcessManagementService();
                    }
                }
                return _instance;
            }
        }

        private ProcessManagementService()
        {
            _appSettings = AppSettings.Instance;
            _communicationService = new CommunicationService(_appSettings.WebSocketPort);
            IsAiRunning = false;
        }

        public void Initialize()
        {
            if (_appSettings.AutoStartAi)
            {
                Task.Delay(500).ContinueWith(_ => StartAiProcess());
            }
        }

        public void ToggleAiProcess()
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

        public void StartAiProcess()
        {
            try
            {
                if (IsAiRunning || _aiProcess != null)
                {
                    return;
                }

                OnStatusChanged("AIプロセスを起動中...");

                string aiPath = _appSettings.AiExecutablePath;
                if (string.IsNullOrEmpty(aiPath))
                {
                    OnStatusChanged("AIの実行ファイルパスが設定されていません");
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
                    OnStatusChanged("AIプロセスを起動しました");
                    AiProcessStarted?.Invoke(this, EventArgs.Empty);
                }
                else
                {
                    OnStatusChanged("AIプロセスの起動に失敗しました");
                }
            }
            catch (Exception ex)
            {
                string errorMessage = $"AIプロセス起動エラー: {ex.Message}";
                OnStatusChanged(errorMessage);
                
                ErrorHandlingService.Instance.LogError(
                    ErrorHandlingService.ErrorLevel.Error,
                    "AIプロセス起動エラー",
                    ex);
                    
                IsAiRunning = false;
                _aiProcess = null;
            }
        }

        public void StopAiProcess()
        {
            try
            {
                if (!IsAiRunning || _aiProcess == null)
                {
                    return;
                }

                OnStatusChanged("AIプロセスを停止中...");

                if (!_aiProcess.HasExited)
                {
                    try
                    {
                        _communicationService.SendControlCommandAsync("shutdown", "User requested shutdown").Wait();
                        
                        Task.Delay(2000).Wait();
                    }
                    catch (Exception ex)
                    {
                        ErrorHandlingService.Instance.LogError(
                            ErrorHandlingService.ErrorLevel.Warning,
                            "シャットダウンコマンド送信エラー",
                            ex);
                    }
                }

                if (!_aiProcess.HasExited)
                {
                    try
                    {
                        _aiProcess.Kill();
                    }
                    catch (Exception ex)
                    {
                        ErrorHandlingService.Instance.LogError(
                            ErrorHandlingService.ErrorLevel.Error,
                            "プロセス強制終了エラー",
                            ex);
                    }
                }

                _aiProcess = null;
                IsAiRunning = false;
                OnStatusChanged("AIプロセスを停止しました");
                AiProcessStopped?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                string errorMessage = $"AIプロセス停止エラー: {ex.Message}";
                OnStatusChanged(errorMessage);
                
                ErrorHandlingService.Instance.LogError(
                    ErrorHandlingService.ErrorLevel.Error,
                    "AIプロセス停止エラー",
                    ex);
            }
        }

        private void AiProcess_Exited(object? sender, EventArgs e)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                IsAiRunning = false;
                _aiProcess = null;
                OnStatusChanged("AIプロセスが終了しました");
                AiProcessStopped?.Invoke(this, EventArgs.Empty);
            });
        }

        private void OnStatusChanged(string status)
        {
            StatusChanged?.Invoke(this, status);
        }

        public void Dispose()
        {
            StopAiProcess();
            _communicationService.Dispose();
        }
    }
}
