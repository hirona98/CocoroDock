using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;

namespace CocoroDock.Utilities
{
    /// <summary>
    /// UI操作に関するユーティリティメソッドを提供するヘルパークラス
    /// </summary>
    public static class UIHelper
    {
        /// <summary>
        /// UIスレッドでアクションを実行します
        /// </summary>
        /// <param name="action">実行するアクション</param>
        public static void RunOnUIThread(Action action)
        {
            if (action == null)
                return;

            if (Application.Current?.Dispatcher != null)
            {
                if (Application.Current.Dispatcher.CheckAccess())
                {
                    action();
                }
                else
                {
                    try
                    {
                        Application.Current.Dispatcher.InvokeAsync(action);
                    }
                    catch (TaskCanceledException)
                    {
                        // キャンセルされた場合は無視
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"UI更新エラー: {ex.Message}");
                    }
                }
            }
        }

        /// <summary>
        /// エラーをメッセージボックスで表示
        /// </summary>
        /// <param name="title">エラータイトル</param>
        /// <param name="message">エラーメッセージ</param>
        public static void ShowError(string title, string message)
        {
            RunOnUIThread(() =>
            {
                MessageBox.Show($"{title}: {message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            });
        }
    }
}