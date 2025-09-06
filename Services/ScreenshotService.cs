using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Linq;

namespace CocoroAI.Services
{
    /// <summary>
    /// スクリーンショット取得サービス
    /// </summary>
    public class ScreenshotService : IDisposable
    {
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

        [StructLayout(LayoutKind.Sequential)]
        private struct LASTINPUTINFO
        {
            public uint cbSize;
            public uint dwTime;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;

            public int Width => Right - Left;
            public int Height => Bottom - Top;
        }

        private System.Threading.Timer? _captureTimer;
        private readonly int _intervalMilliseconds;
        private readonly Func<ScreenshotData, Task>? _onCaptured;
        private readonly Func<string, Task>? _onSkipped;
        private bool _isDisposed;
        private int _idleTimeoutMinutes = 5; // デフォルト5分
        private List<Regex>? _compiledExcludePatterns;


        public bool IsRunning { get; private set; }
        public bool CaptureActiveWindowOnly { get; set; }
        public int IntervalMinutes => _intervalMilliseconds / 60000;
        public int IdleTimeoutMinutes
        {
            get => _idleTimeoutMinutes;
            set => _idleTimeoutMinutes = value > 0 ? value : 5;
        }

        public ScreenshotService(int intervalMinutes = 10, Func<ScreenshotData, Task>? onCaptured = null, Func<string, Task>? onSkipped = null)
        {
            _intervalMilliseconds = intervalMinutes * 60 * 1000;
            _onCaptured = onCaptured;
            _onSkipped = onSkipped;
            CaptureActiveWindowOnly = true;
        }

        /// <summary>
        /// 除外パターンを設定
        /// </summary>
        /// <param name="patterns">正規表現パターンのリスト</param>
        public void SetExcludePatterns(IEnumerable<string> patterns)
        {
            if (patterns == null || !patterns.Any())
            {
                _compiledExcludePatterns = null;
                return;
            }

            try
            {
                _compiledExcludePatterns = patterns
                    .Where(p => !string.IsNullOrWhiteSpace(p))
                    .Select(pattern =>
                    {
                        try
                        {
                            return new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
                        }
                        catch (ArgumentException ex)
                        {
                            Debug.WriteLine($"無効な正規表現パターンをスキップ: {pattern} - {ex.Message}");
                            return null;
                        }
                    })
                    .Where(regex => regex != null)
                    .Cast<Regex>()
                    .ToList();

                Debug.WriteLine($"除外パターンを設定: {_compiledExcludePatterns.Count}個");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"除外パターン設定エラー: {ex.Message}");
                _compiledExcludePatterns = null;
            }
        }

        /// <summary>
        /// ウィンドウタイトルがフィルタリング対象かどうかを判定
        /// </summary>
        /// <param name="windowTitle">ウィンドウタイトル</param>
        /// <returns>スキップすべき場合はtrue</returns>
        private bool ShouldSkipCapture(string windowTitle)
        {
            if (_compiledExcludePatterns == null || _compiledExcludePatterns.Count == 0)
                return false;

            if (string.IsNullOrWhiteSpace(windowTitle))
                return false;

            try
            {
                foreach (var regex in _compiledExcludePatterns)
                {
                    if (regex.IsMatch(windowTitle))
                    {
                        Debug.WriteLine($"除外パターンマッチ: '{windowTitle}' - パターン: {regex}");
                        return true;
                    }
                }
                return false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"フィルタリング判定エラー: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// スクリーンショットの定期取得を開始
        /// </summary>
        public void Start()
        {
            if (IsRunning) return;

            IsRunning = true;
            // 初回実行を設定された間隔後に行うように変更（dueTimeを_intervalMillisecondsに設定）
            _captureTimer = new System.Threading.Timer(async _ => await CaptureTimerCallback(), null, _intervalMilliseconds, _intervalMilliseconds);
        }

        /// <summary>
        /// スクリーンショットの定期取得を停止
        /// </summary>
        public void Stop()
        {
            if (!IsRunning) return;

            IsRunning = false;
            _captureTimer?.Dispose();
            _captureTimer = null;
        }

        /// <summary>
        /// アクティブウィンドウのスクリーンショットを取得
        /// </summary>
        public async Task<ScreenshotData> CaptureActiveWindowAsync()
        {
            return await Task.Run(() =>
            {
                var hwnd = GetForegroundWindow();
                if (hwnd == IntPtr.Zero)
                {
                    throw new InvalidOperationException("アクティブウィンドウが見つかりません");
                }

                // ウィンドウタイトルを取得
                var titleLength = GetWindowTextLength(hwnd);
                var titleBuilder = new System.Text.StringBuilder(titleLength + 1);
                GetWindowText(hwnd, titleBuilder, titleBuilder.Capacity);
                var windowTitle = titleBuilder.ToString();

                // フィルタリング判定
                if (ShouldSkipCapture(windowTitle))
                {
                    throw new InvalidOperationException($"フィルタリングによりスキップ: {windowTitle}");
                }

                // ウィンドウの位置とサイズを取得
                if (!GetWindowRect(hwnd, out RECT rect))
                {
                    throw new InvalidOperationException("ウィンドウの情報を取得できません");
                }

                // スクリーンショットを撮影
                using var bitmap = new Bitmap(rect.Width, rect.Height);
                using (var graphics = Graphics.FromImage(bitmap))
                {
                    graphics.CopyFromScreen(
                        rect.Left,
                        rect.Top,
                        0,
                        0,
                        new Size(rect.Width, rect.Height),
                        CopyPixelOperation.SourceCopy
                    );
                }

                // Base64エンコード
                using var ms = new MemoryStream();
                bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                var imageBytes = ms.ToArray();
                var base64String = Convert.ToBase64String(imageBytes);

                return new ScreenshotData
                {
                    ImageBase64 = base64String,
                    WindowTitle = windowTitle,
                    CaptureTime = DateTime.Now,
                    IsActiveWindow = true,
                    Width = rect.Width,
                    Height = rect.Height
                };
            });
        }

        /// <summary>
        /// 全画面のスクリーンショットを取得
        /// </summary>
        public async Task<ScreenshotData> CaptureFullScreenAsync()
        {
            return await Task.Run(() =>
            {
                var bounds = Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1920, 1080);

                using var bitmap = new Bitmap(bounds.Width, bounds.Height);
                using (var graphics = Graphics.FromImage(bitmap))
                {
                    graphics.CopyFromScreen(
                        bounds.X,
                        bounds.Y,
                        0,
                        0,
                        bounds.Size,
                        CopyPixelOperation.SourceCopy
                    );
                }

                // Base64エンコード
                using var ms = new MemoryStream();
                bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                var imageBytes = ms.ToArray();
                var base64String = Convert.ToBase64String(imageBytes);

                return new ScreenshotData
                {
                    ImageBase64 = base64String,
                    WindowTitle = "全画面",
                    CaptureTime = DateTime.Now,
                    IsActiveWindow = false,
                    Width = bounds.Width,
                    Height = bounds.Height
                };
            });
        }

        private async Task CaptureTimerCallback()
        {
            try
            {
                // アイドル時間をチェック
                if (IsUserIdle())
                {
                    Debug.WriteLine($"ユーザーがアイドル状態（{_idleTimeoutMinutes}分以上操作なし）のため、スクリーンショットをスキップします");
                    return;
                }

                var screenshot = CaptureActiveWindowOnly
                    ? await CaptureActiveWindowAsync()
                    : await CaptureFullScreenAsync();

                // コールバックを実行
                if (_onCaptured != null)
                {
                    await _onCaptured(screenshot);
                }
            }
            catch (InvalidOperationException ex) when (ex.Message.StartsWith("フィルタリングによりスキップ"))
            {
                // フィルタリングでスキップされた場合はスキップコールバックを実行
                var windowTitle = ex.Message.Replace("フィルタリングによりスキップ: ", "");
                Debug.WriteLine($"除外パターンマッチによりスキップ: {windowTitle}");

                if (_onSkipped != null)
                {
                    await _onSkipped($"除外パターンにマッチしたため画面キャプチャをスキップしました: {windowTitle}");
                }
            }
            catch (Exception ex)
            {
                // その他のエラー
                System.Diagnostics.Debug.WriteLine($"スクリーンショット取得エラー: {ex.Message}");
            }
        }



        /// <summary>
        /// ユーザーがアイドル状態かどうかを判定
        /// </summary>
        private bool IsUserIdle()
        {
            try
            {
                var lastInputInfo = new LASTINPUTINFO();
                lastInputInfo.cbSize = (uint)Marshal.SizeOf(lastInputInfo);

                if (GetLastInputInfo(ref lastInputInfo))
                {
                    // 最終入力からの経過時間を計算（ミリ秒）
                    var idleTime = Environment.TickCount - lastInputInfo.dwTime;

                    // アイドルタイムアウト時間（ミリ秒）と比較
                    var idleTimeoutMs = _idleTimeoutMinutes * 60 * 1000;

                    return idleTime > idleTimeoutMs;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"アイドル時間の取得エラー: {ex.Message}");
            }

            // エラーが発生した場合はアイドルとみなす
            return true;
        }

        public void Dispose()
        {
            if (_isDisposed) return;

            Stop();


            _isDisposed = true;
        }
    }

    /// <summary>
    /// スクリーンショットデータ
    /// </summary>
    public class ScreenshotData
    {
        public string ImageBase64 { get; set; } = string.Empty;
        public string WindowTitle { get; set; } = string.Empty;
        public DateTime CaptureTime { get; set; }
        public bool IsActiveWindow { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
    }
}