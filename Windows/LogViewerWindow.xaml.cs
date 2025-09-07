using CocoroDock.Communication;
using CocoroDock.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Data;

namespace CocoroDock.Windows
{
    /// <summary>
    /// ログビューアーウィンドウ
    /// </summary>
    public partial class LogViewerWindow : Window
    {
        private ObservableCollection<LogMessage> _allLogs = new ObservableCollection<LogMessage>();
        private ICollectionView? _filteredLogs;
        private string _levelFilter = "";
        private string _componentFilter = "";
        private LogFileWatcherService? _logWatcher;
        private const int MaxDisplayedLogs = 1000;
        public bool IsClosed { get; private set; } = false;

        // スクロール位置保持用
        private ScrollViewer? _scrollViewer;
        private double _lastVerticalOffset = 0;

        public LogViewerWindow()
        {
            InitializeComponent();
            InitializeLogView();

            // 初期UI状態を設定
            Cursor = System.Windows.Input.Cursors.Arrow;
            LogDataGrid.IsEnabled = true;

            // 非同期でログ監視開始
            StartLogWatching();

            // DataGridがロードされた後にScrollViewerの参照を取得
            LogDataGrid.Loaded += (s, e) => InitializeScrollViewer();
        }

        /// <summary>
        /// ログビューの初期化
        /// </summary>
        private void InitializeLogView()
        {
            _filteredLogs = CollectionViewSource.GetDefaultView(_allLogs);
            _filteredLogs.Filter = LogFilter;

            LogDataGrid.ItemsSource = _filteredLogs;

            UpdateLogCount();
        }

        /// <summary>
        /// ScrollViewerの参照を初期化
        /// </summary>
        private void InitializeScrollViewer()
        {
            if (_scrollViewer == null)
            {
                _scrollViewer = GetScrollViewer(LogDataGrid);
            }
        }

        /// <summary>
        /// DataGridからScrollViewerを取得
        /// </summary>
        /// <param name="dataGrid">対象のDataGrid</param>
        /// <returns>ScrollViewer</returns>
        private ScrollViewer? GetScrollViewer(DataGrid dataGrid)
        {
            if (dataGrid == null) return null;

            // VisualTreeからScrollViewerを探す
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(dataGrid); i++)
            {
                var child = VisualTreeHelper.GetChild(dataGrid, i);
                var scrollViewer = FindScrollViewer(child);
                if (scrollViewer != null)
                {
                    return scrollViewer;
                }
            }
            return null;
        }

        /// <summary>
        /// VisualTree内でScrollViewerを再帰的に検索
        /// </summary>
        /// <param name="visual">検索対象</param>
        /// <returns>ScrollViewer</returns>
        private ScrollViewer? FindScrollViewer(DependencyObject visual)
        {
            if (visual is ScrollViewer scrollViewer)
            {
                return scrollViewer;
            }

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(visual); i++)
            {
                var child = VisualTreeHelper.GetChild(visual, i);
                var result = FindScrollViewer(child);
                if (result != null)
                {
                    return result;
                }
            }

            return null;
        }

        /// <summary>
        /// ログメッセージを追加（最大1000件まで）
        /// </summary>
        /// <param name="logMessage">ログメッセージ</param>
        public void AddLogMessage(LogMessage logMessage)
        {
            Dispatcher.BeginInvoke(() =>
            {
                // 自動スクロールがOFFの場合、現在のスクロール位置を保存
                bool shouldPreservePosition = AutoScrollCheckBox.IsChecked != true;
                double savedOffset = 0;

                if (shouldPreservePosition && _scrollViewer != null)
                {
                    savedOffset = _scrollViewer.VerticalOffset;
                    _lastVerticalOffset = savedOffset;
                }

                _allLogs.Add(logMessage);

                // 最大件数を超えた場合、古いログを削除
                bool itemsRemoved = false;
                while (_allLogs.Count > MaxDisplayedLogs)
                {
                    _allLogs.RemoveAt(0);
                    itemsRemoved = true;
                }

                UpdateLogCount();
                UpdateStatus($"最新ログ: {logMessage.timestamp:HH:mm:ss} [{logMessage.level}] {logMessage.component}");

                // スクロール位置の処理
                if (AutoScrollCheckBox.IsChecked == true && LogDataGrid.Items.Count > 0)
                {
                    // 自動スクロールが有効の場合、最新アイテムまでスクロール
                    LogDataGrid.ScrollIntoView(LogDataGrid.Items[LogDataGrid.Items.Count - 1]);
                }
                else if (shouldPreservePosition && _scrollViewer != null)
                {
                    // 自動スクロールが無効の場合、スクロール位置を復元
                    Dispatcher.BeginInvoke(() =>
                    {
                        // アイテムが削除された場合、位置を調整
                        if (itemsRemoved)
                        {
                            // 削除されたアイテム分だけ上にスクロール位置を調整
                            // （削除された数 × 大体のアイテム高さ）を差し引く
                            var adjustedOffset = Math.Max(0, savedOffset - 20); // 20は大体のアイテム高さ
                            _scrollViewer.ScrollToVerticalOffset(adjustedOffset);
                        }
                        else
                        {
                            _scrollViewer.ScrollToVerticalOffset(savedOffset);
                        }
                    }, System.Windows.Threading.DispatcherPriority.Loaded);
                }
            });
        }

        /// <summary>
        /// 初期ログリストを一括で追加（UIスレッドで実行）
        /// </summary>
        /// <param name="logMessages">初期ログメッセージのリスト</param>
        public void LoadInitialLogs(List<LogMessage> logMessages)
        {
            // 既にUIスレッドで呼ばれることを前提とした処理
            _allLogs.Clear();

            foreach (var logMessage in logMessages)
            {
                _allLogs.Add(logMessage);
            }

            UpdateLogCount();

            // 最後のメッセージでステータス更新
            if (logMessages.Count > 0)
            {
                var lastMessage = logMessages.Last();
                UpdateStatus($"初期ログ読み込み完了: {logMessages.Count}件 - 最新: {lastMessage.timestamp:HH:mm:ss}");
            }
            else
            {
                UpdateStatus("ログファイルは空です");
            }

            // 自動スクロール（初期ロード時は常に最新にスクロール）
            if (AutoScrollCheckBox.IsChecked == true && LogDataGrid.Items.Count > 0)
            {
                LogDataGrid.ScrollIntoView(LogDataGrid.Items[LogDataGrid.Items.Count - 1]);
            }
        }

        /// <summary>
        /// ログフィルター
        /// </summary>
        /// <param name="item">フィルター対象のアイテム</param>
        /// <returns>表示するかどうか</returns>
        private bool LogFilter(object item)
        {
            if (item is not LogMessage log) return false;

            // レベルフィルター
            if (!string.IsNullOrEmpty(_levelFilter) && log.level != _levelFilter)
                return false;

            // コンポーネントフィルター
            if (!string.IsNullOrEmpty(_componentFilter) && log.component != _componentFilter)
                return false;

            return true;
        }

        /// <summary>
        /// ログ件数を更新
        /// </summary>
        private void UpdateLogCount()
        {
            // UIが初期化されていない場合は何もしない
            if (LogCountTextBlock == null) return;

            var totalCount = _allLogs.Count;
            var filteredCount = _filteredLogs?.Cast<LogMessage>().Count() ?? 0;

            if (totalCount == filteredCount)
            {
                LogCountTextBlock.Text = $"総件数: {totalCount}";
            }
            else
            {
                LogCountTextBlock.Text = $"表示中: {filteredCount} / 総件数: {totalCount}";
            }
        }

        /// <summary>
        /// ステータスメッセージを更新
        /// </summary>
        /// <param name="message">ステータスメッセージ</param>
        private void UpdateStatus(string message)
        {
            // UIが初期化されていない場合は何もしない
            if (StatusTextBlock == null) return;

            StatusTextBlock.Text = message;
        }

        /// <summary>
        /// レベルフィルター変更イベント
        /// </summary>
        private void LevelFilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // UIが完全に初期化されていない場合は何もしない
            if (LevelFilterComboBox?.SelectedItem is ComboBoxItem selectedItem)
            {
                // フィルター変更時のスクロール位置保持
                double savedOffset = 0;
                if (_scrollViewer != null && AutoScrollCheckBox.IsChecked != true)
                {
                    savedOffset = _scrollViewer.VerticalOffset;
                }

                _levelFilter = selectedItem.Tag?.ToString() ?? "";
                _filteredLogs?.Refresh();
                UpdateLogCount();

                // スクロール位置の復元
                if (_scrollViewer != null && AutoScrollCheckBox.IsChecked != true)
                {
                    Dispatcher.BeginInvoke(() =>
                    {
                        _scrollViewer.ScrollToVerticalOffset(savedOffset);
                    }, System.Windows.Threading.DispatcherPriority.Loaded);
                }
            }
        }

        /// <summary>
        /// コンポーネントフィルター変更イベント
        /// </summary>
        private void ComponentFilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // UIが完全に初期化されていない場合は何もしない
            if (ComponentFilterComboBox?.SelectedItem is ComboBoxItem selectedItem)
            {
                // フィルター変更時のスクロール位置保持
                double savedOffset = 0;
                if (_scrollViewer != null && AutoScrollCheckBox.IsChecked != true)
                {
                    savedOffset = _scrollViewer.VerticalOffset;
                }

                _componentFilter = selectedItem.Tag?.ToString() ?? "";
                _filteredLogs?.Refresh();
                UpdateLogCount();

                // スクロール位置の復元
                if (_scrollViewer != null && AutoScrollCheckBox.IsChecked != true)
                {
                    Dispatcher.BeginInvoke(() =>
                    {
                        _scrollViewer.ScrollToVerticalOffset(savedOffset);
                    }, System.Windows.Threading.DispatcherPriority.Loaded);
                }
            }
        }

        /// <summary>
        /// クリアボタンクリックイベント
        /// </summary>
        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            _allLogs.Clear();
            UpdateLogCount();
            UpdateStatus("ログクリア");
        }

        /// <summary>
        /// ログファイルの監視を開始する（非同期）
        /// </summary>
        private async void StartLogWatching()
        {
            try
            {
                _logWatcher = new LogFileWatcherService();
                _logWatcher.LogMessageReceived += OnLogMessageReceived;
                _logWatcher.ErrorOccurred += OnWatcherError;
                _logWatcher.LoadingStarted += OnLoadingStarted;
                _logWatcher.LoadingCompleted += OnLoadingCompleted;
                _logWatcher.ProgressUpdated += OnProgressUpdated;

                // ログファイルパスを環境に応じて決定
                var logPath = GetLogFilePath();
                if (File.Exists(logPath))
                {
                    UpdateStatus($"ログファイル監視を開始しています...");
                    await _logWatcher.StartWatchingAsync(logPath);
                    UpdateStatus($"ログファイル監視開始: {Path.GetFileName(logPath)}");
                }
                else
                {
                    UpdateStatus($"ログファイルが見つかりません: {logPath}");
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"ログ監視の開始に失敗: {ex.Message}");
                // 例外時もUI状態をリセット
                Cursor = System.Windows.Input.Cursors.Arrow;
                LogDataGrid.IsEnabled = true;
            }
        }

        /// <summary>
        /// 環境に応じてログファイルパスを取得する
        /// </summary>
        /// <returns>ログファイルの絶対パス</returns>
        private string GetLogFilePath()
        {
            // リリースバイナリ実行時のパス
            var releasePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                "CocoroCoreM", "logs", "cocoro_core2.log");
            if (File.Exists(releasePath))
                return releasePath;

            // デバッグ時のパス（CocoroDockからCocoroCoreMへの相対パス）
            var debugPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                "..", "CocoroCoreM", "logs", "cocoro_core2.log");
            var fullDebugPath = Path.GetFullPath(debugPath);
            if (File.Exists(fullDebugPath))
                return fullDebugPath;

            // さらに上のディレクトリから探す場合のパス
            var alternativeDebugPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                "..", "..", "..", "..", "CocoroCoreM", "logs", "cocoro_core2.log");
            return Path.GetFullPath(alternativeDebugPath);
        }

        /// <summary>
        /// ログメッセージ受信イベントハンドラー
        /// </summary>
        /// <param name="logMessage">受信したログメッセージ</param>
        private void OnLogMessageReceived(LogMessage logMessage)
        {
            // UIスレッドで実行されるようにマーシャリング
            Dispatcher.BeginInvoke(new Action(() => AddLogMessage(logMessage)));
        }


        /// <summary>
        /// ファイル監視エラーイベントハンドラー
        /// </summary>
        /// <param name="errorMessage">エラーメッセージ</param>
        private void OnWatcherError(string errorMessage)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                UpdateStatus($"エラー: {errorMessage}");
                // エラー時もUI状態をリセット
                Cursor = System.Windows.Input.Cursors.Arrow;
                LogDataGrid.IsEnabled = true;
            }));
        }

        /// <summary>
        /// 初期読み込み開始イベントハンドラー
        /// </summary>
        private void OnLoadingStarted()
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                UpdateStatus("既存ログを読み込み中...");
                Cursor = System.Windows.Input.Cursors.Wait;
                // LogDataGridを無効化（操作不可にする）
                LogDataGrid.IsEnabled = false;
            }));
        }

        /// <summary>
        /// 初期読み込み完了イベントハンドラー
        /// </summary>
        /// <param name="logMessages">読み込まれたログリスト</param>
        private void OnLoadingCompleted(List<LogMessage> logMessages)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                // 一括でログを追加
                LoadInitialLogs(logMessages);

                // UI状態を通常に戻す
                Cursor = System.Windows.Input.Cursors.Arrow;
                LogDataGrid.IsEnabled = true;
            }));
        }

        /// <summary>
        /// 進行状況更新イベントハンドラー
        /// </summary>
        /// <param name="current">現在の処理数</param>
        /// <param name="total">総処理数</param>
        private void OnProgressUpdated(int current, int total)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                var percentage = (int)((double)current / total * 100);
                UpdateStatus($"既存ログを読み込み中... ({current}/{total} - {percentage}%)");
            }));
        }

        /// <summary>
        /// ウィンドウが閉じられた時の処理
        /// </summary>
        /// <param name="e">イベント引数</param>
        protected override void OnClosed(EventArgs e)
        {
            // UI状態をリセット
            Cursor = System.Windows.Input.Cursors.Arrow;
            LogDataGrid.IsEnabled = true;

            // リソースの解放
            _logWatcher?.Dispose();
            IsClosed = true;
            base.OnClosed(e);
        }

    }
}