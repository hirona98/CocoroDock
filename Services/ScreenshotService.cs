using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Diagnostics;

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
        private bool _isDisposed;
        private int _idleTimeoutMinutes = 5; // デフォルト5分


        public bool IsRunning { get; private set; }
        public bool CaptureActiveWindowOnly { get; set; }
        public int IntervalMinutes => _intervalMilliseconds / 60000;
        public int IdleTimeoutMinutes
        {
            get => _idleTimeoutMinutes;
            set => _idleTimeoutMinutes = value > 0 ? value : 5;
        }

        public ScreenshotService(int intervalMinutes = 10, Func<ScreenshotData, Task>? onCaptured = null)
        {
            _intervalMilliseconds = intervalMinutes * 60 * 1000;
            _onCaptured = onCaptured;
            CaptureActiveWindowOnly = true;
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
            catch (Exception ex)
            {
                // ログ出力など（必要に応じて実装）
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