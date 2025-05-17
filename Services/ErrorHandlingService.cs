using System;
using System.Diagnostics;
using System.Windows;

namespace CocoroDock.Services
{
    public class ErrorHandlingService
    {
        private static ErrorHandlingService? _instance;
        private static readonly object _lock = new object();

        public static ErrorHandlingService Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        _instance ??= new ErrorHandlingService();
                    }
                }
                return _instance;
            }
        }

        public enum ErrorLevel
        {
            Debug,      // デバッグ情報（開発時のみ）
            Info,       // 情報メッセージ
            Warning,    // 警告メッセージ
            Error,      // エラーメッセージ
            Fatal       // 致命的エラー
        }

        public void LogError(ErrorLevel level, string message, Exception? ex = null)
        {
            string errorMessage = ex != null
                ? $"{message}: {ex.Message}"
                : message;

            switch (level)
            {
                case ErrorLevel.Debug:
                    Debug.WriteLine($"[DEBUG] {errorMessage}");
                    break;
                case ErrorLevel.Info:
                    Debug.WriteLine($"[INFO] {errorMessage}");
                    break;
                case ErrorLevel.Warning:
                    Debug.WriteLine($"[WARNING] {errorMessage}");
                    break;
                case ErrorLevel.Error:
                    Debug.WriteLine($"[ERROR] {errorMessage}");
                    break;
                case ErrorLevel.Fatal:
                    Debug.WriteLine($"[FATAL] {errorMessage}");
                    break;
            }

            if (ex != null && level >= ErrorLevel.Error)
            {
                Debug.WriteLine($"スタックトレース: {ex.StackTrace}");
            }
        }

        public void ShowErrorMessage(string message, string title = "エラー", ErrorLevel level = ErrorLevel.Error)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                MessageBoxImage icon = MessageBoxImage.Information;
                
                switch (level)
                {
                    case ErrorLevel.Info:
                        icon = MessageBoxImage.Information;
                        break;
                    case ErrorLevel.Warning:
                        icon = MessageBoxImage.Warning;
                        break;
                    case ErrorLevel.Error:
                    case ErrorLevel.Fatal:
                        icon = MessageBoxImage.Error;
                        break;
                }

                MessageBox.Show(message, title, MessageBoxButton.OK, icon);
            });
        }

        public void HandleException(Exception ex, string context, bool showUI = true, ErrorLevel level = ErrorLevel.Error)
        {
            string errorMessage = $"{context}: {ex.Message}";
            
            LogError(level, context, ex);
            
            if (showUI)
            {
                ShowErrorMessage(errorMessage, "エラー", level);
            }
        }

        public bool SafeExecute(Action action, string context, bool showUI = true, ErrorLevel level = ErrorLevel.Error)
        {
            try
            {
                action();
                return true;
            }
            catch (Exception ex)
            {
                HandleException(ex, context, showUI, level);
                return false;
            }
        }

        public T SafeExecute<T>(Func<T> func, string context, T defaultValue, bool showUI = true, ErrorLevel level = ErrorLevel.Error)
        {
            try
            {
                return func();
            }
            catch (Exception ex)
            {
                HandleException(ex, context, showUI, level);
                return defaultValue;
            }
        }
    }
}
