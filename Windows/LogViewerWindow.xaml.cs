using CocoroDock.Communication;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
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

        public LogViewerWindow()
        {
            InitializeComponent();
            InitializeLogView();
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
        /// ログメッセージを追加
        /// </summary>
        /// <param name="logMessage">ログメッセージ</param>
        public void AddLogMessage(LogMessage logMessage)
        {
            Dispatcher.Invoke(() =>
            {
                _allLogs.Add(logMessage);
                UpdateLogCount();
                UpdateStatus($"最新ログ: {logMessage.timestamp:HH:mm:ss} [{logMessage.level}] {logMessage.component}");

                // 自動スクロール
                if (AutoScrollCheckBox.IsChecked == true && LogDataGrid.Items.Count > 0)
                {
                    LogDataGrid.ScrollIntoView(LogDataGrid.Items[LogDataGrid.Items.Count - 1]);
                }
            });
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
                _levelFilter = selectedItem.Tag?.ToString() ?? "";
                _filteredLogs?.Refresh();
                UpdateLogCount();
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
                _componentFilter = selectedItem.Tag?.ToString() ?? "";
                _filteredLogs?.Refresh();
                UpdateLogCount();
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


    }
}