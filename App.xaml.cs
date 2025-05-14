using System;
using System.Windows;
using System.Windows.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using CocoroDock.Services;
using System.Drawing; // 追加
using MessageBox = System.Windows.MessageBox;
using Application = System.Windows.Application;
// System.Windows.FormsのNotifyIconを使用
using NotifyIcon = System.Windows.Forms.NotifyIcon;
using ContextMenuStrip = System.Windows.Forms.ContextMenuStrip;
using ToolStripMenuItem = System.Windows.Forms.ToolStripMenuItem;

namespace CocoroDock
{
    /// <summary>
    /// App.xaml の相互作用ロジック
    /// </summary>
    public partial class App : Application
    {
        private NotifyIcon? _notifyIcon;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // 二重起動チェック
            string processName = Process.GetCurrentProcess().ProcessName;
            Process[] processes = Process.GetProcessesByName(processName);
            // 現在のプロセスを含めて2つ以上あれば、既に起動している
            if (processes.Length > 1)
            {
                // 既にアプリケーションが起動している場合
                MessageBox.Show("二重起動です", "二重起動警告", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            // 未処理の例外ハンドラを登録
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            Application.Current.DispatcherUnhandledException += Application_DispatcherUnhandledException;
            TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;

            // メインウィンドウを作成・表示
            MainWindow mainWindow = new MainWindow();
            mainWindow.Show();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            // システムトレイアイコンの解放
            _notifyIcon?.Dispose();

            // アプリケーション終了時の処理
            try
            {
                // 設定を保存
                AppSettings.Instance.SaveSettings();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"設定保存エラー: {ex.Message}");
            }

            base.OnExit(e);
        }

        /// <summary>
        /// 通常スレッドでの未処理例外ハンドラ
        /// </summary>
        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            ShowFatalError(e.ExceptionObject as Exception, "未処理の例外が発生しました");
        }

        /// <summary>
        /// UIスレッドでの未処理例外ハンドラ
        /// </summary>
        private void Application_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            // エラーをユーザーに表示
            MessageBox.Show($"エラーが発生しました: {e.Exception.Message}",
                "エラー", MessageBoxButton.OK, MessageBoxImage.Error);

            // 例外を処理済みとしてマーク（アプリケーションを継続）
            e.Handled = true;
        }

        /// <summary>
        /// 未監視のタスク例外ハンドラ
        /// </summary>
        private void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            e.SetObserved(); // 例外を監視済みとしてマーク
        }

        /// <summary>
        /// 致命的エラーを表示し、アプリケーションを終了
        /// </summary>
        private void ShowFatalError(Exception? ex, string message)
        {
            try
            {
                string errorMessage = ex != null ? ex.Message : "不明なエラー";
                MessageBox.Show($"致命的なエラー: {errorMessage}\n\nアプリケーションを終了します。",
                    "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                Environment.Exit(1);
            }
        }
    }
}
