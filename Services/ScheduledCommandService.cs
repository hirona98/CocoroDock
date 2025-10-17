using System;
using System.Diagnostics;
using System.Text;
using System.Threading;

namespace CocoroDock.Services
{
    /// <summary>
    /// 定期コマンド実行サービス
    /// </summary>
    public class ScheduledCommandService : IDisposable
    {
        static ScheduledCommandService()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        }

        private System.Threading.Timer? _commandTimer;
        private readonly int _intervalMilliseconds;
        private string _command = string.Empty;
        private bool _isDisposed;

        public bool IsRunning { get; private set; }
        public int IntervalMinutes => _intervalMilliseconds / 60000;

        public ScheduledCommandService(int intervalMinutes = 60)
        {
            _intervalMilliseconds = intervalMinutes * 60 * 1000;
        }

        /// <summary>
        /// 実行するコマンドを設定
        /// </summary>
        public void SetCommand(string command)
        {
            _command = command ?? string.Empty;
        }

        /// <summary>
        /// 定期実行を開始
        /// </summary>
        public void Start()
        {
            if (IsRunning) return;
            if (string.IsNullOrWhiteSpace(_command))
            {
                Debug.WriteLine("コマンドが設定されていないため、定期実行を開始できません");
                return;
            }

            IsRunning = true;
            _commandTimer = new System.Threading.Timer(ExecuteCommandCallback, null, _intervalMilliseconds, _intervalMilliseconds);
            Debug.WriteLine($"定期コマンド実行を開始しました: {_command} (間隔: {IntervalMinutes}分)");
        }

        /// <summary>
        /// 定期実行を停止
        /// </summary>
        public void Stop()
        {
            if (!IsRunning) return;

            IsRunning = false;
            _commandTimer?.Dispose();
            _commandTimer = null;
            Debug.WriteLine("定期コマンド実行を停止しました");
        }

        /// <summary>
        /// 定期実行を再起動
        /// </summary>
        public void Restart(int intervalMinutes, string command)
        {
            Stop();
            SetCommand(command);
            if (_intervalMilliseconds != intervalMinutes * 60 * 1000)
            {
                // 間隔が変更された場合は新しいインスタンスが必要
                Debug.WriteLine($"実行間隔が変更されました: {IntervalMinutes}分 → {intervalMinutes}分");
            }
            Start();
        }

        /// <summary>
        /// タイマーコールバック: コマンドを実行
        /// </summary>
        private void ExecuteCommandCallback(object? state)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(_command))
                {
                    Debug.WriteLine("コマンドが空のため実行をスキップしました");
                    return;
                }

                Debug.WriteLine($"コマンド実行開始: {_command}");

                var processStartInfo = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c {_command}",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = new UTF8Encoding(false),
                    StandardErrorEncoding = new UTF8Encoding(false)
                };

                using (var process = Process.Start(processStartInfo))
                {
                    if (process == null)
                    {
                        Debug.WriteLine("ERROR: プロセスの起動に失敗しました");
                        Stop();
                        return;
                    }

                    process.WaitForExit();

                    var output = process.StandardOutput.ReadToEnd();
                    var error = process.StandardError.ReadToEnd();

                    if (process.ExitCode != 0)
                    {
                        Debug.WriteLine($"ERROR: コマンド実行が失敗しました (ExitCode: {process.ExitCode})");
                        if (!string.IsNullOrWhiteSpace(error))
                        {
                            Debug.WriteLine($"エラー出力: {error}");
                        }
                        Stop();
                        return;
                    }

                    Debug.WriteLine($"コマンド実行成功 (ExitCode: {process.ExitCode})");
                    if (!string.IsNullOrWhiteSpace(output))
                    {
                        Debug.WriteLine($"出力: {output}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ERROR: コマンド実行中に例外が発生しました: {ex.Message}");
                Stop();
            }
        }

        public void Dispose()
        {
            if (_isDisposed) return;

            Stop();
            _isDisposed = true;
        }
    }
}
