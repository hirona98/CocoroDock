using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Globalization;
using System.Threading;
using CocoroDock.Communication;
using System.Text;

namespace CocoroDock.Services
{
    /// <summary>
    /// ログファイル監視サービス
    /// ファイルシステム監視を使用してログファイルの変更をリアルタイムで検出し、
    /// tailコマンドのような動作を提供する
    /// </summary>
    public class LogFileWatcherService : IDisposable
    {
        private FileSystemWatcher? _fileWatcher;
        private string _logFilePath = string.Empty;
        private long _lastReadPosition;
        private bool _disposed = false;
        private bool _isReading = false;
        private Timer? _debounceTimer;
        private readonly object _lockObject = new object();
        private readonly HashSet<string> _processedLogIds = new HashSet<string>();
        private const int MaxLogEntries = 1000;
        private const int DebounceDelayMs = 100;

        // Python標準ロガーの形式: 2025-08-15 12:00:07,337 - __main__ - INFO - CocoroCoreMを初期化しています...
        private readonly Regex _logPattern = new Regex(
            @"^(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2},\d{3}) - (.+?) - (\w+) - (.+)$",
            RegexOptions.Compiled | RegexOptions.Multiline);

        /// <summary>
        /// ログメッセージが受信された時に発生するイベント（リアルタイム更新用）
        /// </summary>
        public event Action<LogMessage>? LogMessageReceived;

        /// <summary>
        /// エラーが発生した時に発生するイベント
        /// </summary>
        public event Action<string>? ErrorOccurred;

        /// <summary>
        /// 初期読み込み開始時に発生するイベント
        /// </summary>
        public event Action? LoadingStarted;

        /// <summary>
        /// 初期読み込み完了時に発生するイベント（読み込んだログリストを渡す）
        /// </summary>
        public event Action<List<LogMessage>>? LoadingCompleted;

        /// <summary>
        /// 読み込み進行状況が更新された時に発生するイベント
        /// </summary>
        public event Action<int, int>? ProgressUpdated; // (current, total)

        /// <summary>
        /// ログファイルの監視を開始する（非同期）
        /// </summary>
        /// <param name="logFilePath">監視するログファイルのパス</param>
        public async Task StartWatchingAsync(string logFilePath)
        {
            if (string.IsNullOrEmpty(logFilePath))
            {
                ErrorOccurred?.Invoke("ログファイルパスが指定されていません");
                return;
            }

            _logFilePath = logFilePath;

            try
            {
                // ファイル監視を先に開始（リアルタイム監視のため）
                SetupFileWatcher();

                // 既存ログの読み込みは非同期で実行
                LoadingStarted?.Invoke();
                var existingLogs = await LoadExistingLogs();
                LoadingCompleted?.Invoke(existingLogs);
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke($"ログファイル監視の開始に失敗: {ex.Message}");
            }
        }

        /// <summary>
        /// 既存のログファイルの最終1000件を別スレッドで読み込み、結果をリストで返す
        /// </summary>
        private async Task<List<LogMessage>> LoadExistingLogs()
        {
            var resultLogs = new List<LogMessage>();

            if (!File.Exists(_logFilePath))
            {
                ErrorOccurred?.Invoke($"ログファイルが見つかりません: {_logFilePath}");
                return resultLogs;
            }

            try
            {
                // ファイル読み込みを別スレッドで実行
                await Task.Run(() =>
                {
                    var allLines = new List<string>();
                    
                    using var fileStream = new FileStream(_logFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    using var reader = new StreamReader(fileStream, Encoding.UTF8);

                    string? line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        allLines.Add(line);
                    }

                    // 最終1000件を取得
                    var lastLines = allLines.Count > MaxLogEntries 
                        ? allLines.Skip(allLines.Count - MaxLogEntries).ToList()
                        : allLines;

                    // ログメッセージを別スレッドで解析し、リストを構築
                    var totalLines = lastLines.Count;
                    
                    for (int i = 0; i < lastLines.Count; i++)
                    {
                        var logLine = lastLines[i];
                        var logMessage = ParseLogLine(logLine);
                        if (logMessage != null)
                        {
                            var logId = GenerateLogId(logMessage);
                            if (_processedLogIds.Add(logId))
                            {
                                resultLogs.Add(logMessage);
                            }
                        }

                        // 100行ごとに進行状況を報告
                        if (i % 100 == 0 || i == totalLines - 1)
                        {
                            ProgressUpdated?.Invoke(i + 1, totalLines);
                        }
                    }

                    _lastReadPosition = fileStream.Position;
                });
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke($"既存ログの読み込みに失敗: {ex.Message}");
            }

            return resultLogs;
        }

        /// <summary>
        /// ファイル監視を設定する
        /// </summary>
        private void SetupFileWatcher()
        {
            try
            {
                var directory = Path.GetDirectoryName(_logFilePath);
                var fileName = Path.GetFileName(_logFilePath);

                if (string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(fileName))
                {
                    ErrorOccurred?.Invoke("ログファイルパスが無効です");
                    return;
                }

                _fileWatcher = new FileSystemWatcher(directory, fileName)
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size
                };

                _fileWatcher.Changed += OnFileChanged;
                _fileWatcher.Error += OnWatcherError;
                _fileWatcher.EnableRaisingEvents = true;
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke($"ファイル監視の設定に失敗: {ex.Message}");
            }
        }

        /// <summary>
        /// ファイル変更イベントハンドラー（デバウンス処理付き）
        /// </summary>
        private void OnFileChanged(object sender, FileSystemEventArgs e)
        {
            lock (_lockObject)
            {
                // 既存のタイマーをキャンセル
                _debounceTimer?.Dispose();
                
                // 新しいタイマーを設定（デバウンス処理）
                _debounceTimer = new Timer(OnDebounceTimerElapsed, null, DebounceDelayMs, Timeout.Infinite);
            }
        }

        /// <summary>
        /// デバウンスタイマーが経過した時の処理
        /// </summary>
        private void OnDebounceTimerElapsed(object? state)
        {
            Task.Run(() => ReadNewLines());
        }

        /// <summary>
        /// ファイル監視エラーイベントハンドラー
        /// </summary>
        private void OnWatcherError(object sender, ErrorEventArgs e)
        {
            ErrorOccurred?.Invoke($"ファイル監視エラー: {e.GetException().Message}");
        }

        /// <summary>
        /// 新しく追加された行を読み込む（重複防止付き）
        /// </summary>
        private void ReadNewLines()
        {
            if (!File.Exists(_logFilePath) || _disposed)
                return;

            lock (_lockObject)
            {
                // 既に読み込み処理中の場合はスキップ
                if (_isReading)
                    return;
                
                _isReading = true;
            }

            try
            {
                using var fileStream = new FileStream(_logFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                
                // ファイルが縮小された場合（ログローテーションなど）
                if (fileStream.Length < _lastReadPosition)
                {
                    _lastReadPosition = 0;
                    // ログローテーション時は処理済みIDをクリア
                    lock (_lockObject)
                    {
                        _processedLogIds.Clear();
                    }
                }

                fileStream.Seek(_lastReadPosition, SeekOrigin.Begin);

                using var reader = new StreamReader(fileStream, Encoding.UTF8);
                string? line;
                var newLogs = new List<LogMessage>();
                
                while ((line = reader.ReadLine()) != null)
                {
                    var logMessage = ParseLogLine(line);
                    if (logMessage != null)
                    {
                        var logId = GenerateLogId(logMessage);
                        
                        lock (_lockObject)
                        {
                            if (_processedLogIds.Add(logId))
                            {
                                newLogs.Add(logMessage);
                                
                                // 処理済みIDの数が制限を超えた場合、古いものを削除
                                if (_processedLogIds.Count > MaxLogEntries * 2)
                                {
                                    var idsToRemove = _processedLogIds.Take(_processedLogIds.Count - MaxLogEntries).ToList();
                                    foreach (var id in idsToRemove)
                                    {
                                        _processedLogIds.Remove(id);
                                    }
                                }
                            }
                        }
                    }
                }

                // 新しいログを個別に通知（リアルタイム更新）
                foreach (var log in newLogs)
                {
                    LogMessageReceived?.Invoke(log);
                }

                _lastReadPosition = fileStream.Position;
            }
            catch (IOException)
            {
                // ファイルがロックされている場合は少し待ってから再試行
                Task.Delay(200).ContinueWith(_ => 
                {
                    lock (_lockObject)
                    {
                        _isReading = false;
                    }
                    Task.Delay(100).ContinueWith(__ => ReadNewLines());
                });
                return;
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke($"ログファイル読み込みエラー: {ex.Message}");
            }
            finally
            {
                lock (_lockObject)
                {
                    _isReading = false;
                }
            }
        }

        /// <summary>
        /// ログ行をパースしてLogMessageオブジェクトに変換する
        /// </summary>
        /// <param name="line">ログ行</param>
        /// <returns>パースされたLogMessage、パースに失敗した場合はnull</returns>
        private LogMessage? ParseLogLine(string line)
        {
            if (string.IsNullOrEmpty(line))
                return null;

            var match = _logPattern.Match(line);
            if (!match.Success)
                return null;

            try
            {
                var timestampStr = match.Groups[1].Value;
                var component = match.Groups[2].Value;
                var level = match.Groups[3].Value;
                var message = match.Groups[4].Value;

                // タイムスタンプを解析
                if (!DateTime.TryParseExact(timestampStr, 
                    "yyyy-MM-dd HH:mm:ss,fff", 
                    CultureInfo.InvariantCulture, 
                    DateTimeStyles.None, 
                    out DateTime timestamp))
                {
                    return null;
                }

                return new LogMessage
                {
                    timestamp = timestamp,
                    component = component,
                    level = level,
                    message = message
                };
            }
            catch (Exception)
            {
                // パースに失敗した場合はnullを返す
                return null;
            }
        }

        /// <summary>
        /// ログメッセージから一意のIDを生成する
        /// </summary>
        /// <param name="logMessage">ログメッセージ</param>
        /// <returns>一意のログID</returns>
        private string GenerateLogId(LogMessage logMessage)
        {
            // タイムスタンプ + コンポーネント + レベル + メッセージの先頭50文字でIDを生成
            var messagePrefix = logMessage.message.Length > 50 
                ? logMessage.message.Substring(0, 50) 
                : logMessage.message;
            
            return $"{logMessage.timestamp:yyyy-MM-dd HH:mm:ss,fff}|{logMessage.component}|{logMessage.level}|{messagePrefix}";
        }

        /// <summary>
        /// 監視を停止する
        /// </summary>
        public void StopWatching()
        {
            _fileWatcher?.Dispose();
            _fileWatcher = null;
            
            _debounceTimer?.Dispose();
            _debounceTimer = null;
        }

        /// <summary>
        /// リソースを解放する
        /// </summary>
        public void Dispose()
        {
            if (!_disposed)
            {
                StopWatching();
                
                lock (_lockObject)
                {
                    _processedLogIds.Clear();
                }
                
                _disposed = true;
            }
        }
    }
}