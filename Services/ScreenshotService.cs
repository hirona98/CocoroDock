using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Tesseract;
using System.Text.RegularExpressions;
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
        private TesseractEngine? _ocrEngine;
        private readonly object _ocrLock = new object();
        private int _idleTimeoutMinutes = 5; // デフォルト5分

        /// <summary>
        /// フィルタリングされたときに発生するイベント
        /// </summary>
        public event EventHandler<string>? Filtered;

        public bool IsRunning { get; private set; }
        public bool CaptureActiveWindowOnly { get; set; }
        public bool EnableRegexFiltering { get; set; }
        public string? RegexPattern { get; set; }
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
            EnableRegexFiltering = true;

            // OCRエンジンの初期化
            InitializeOcrEngine();
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

                // 正規表現フィルタリングが有効な場合
                if (EnableRegexFiltering && !string.IsNullOrEmpty(RegexPattern))
                {
                    var filterResult = await ShouldFilterByOcr(screenshot.ImageBase64);
                    if (filterResult.ShouldFilter)
                    {
                        Debug.WriteLine($"正規表現フィルタリングにより画像送信をスキップしました: {screenshot.WindowTitle}");

                        // フィルタリングイベントを発火
                        var message = $"デスクトップ画像をフィルタリング: 「{filterResult.MatchedText}」を検出";
                        Filtered?.Invoke(this, message);

                        // フィルタリングされたことをマーク
                        screenshot.IsFiltered = true;
                        screenshot.FilterReason = "正規表現マッチのためスキップ";
                    }
                }

                // フィルタリングされた場合でも画像を表示するため、常にコールバックを実行
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
        /// OCRエンジンを初期化
        /// </summary>
        private void InitializeOcrEngine()
        {
            // 初期化を非同期で実行
            Task.Run(async () =>
            {
                try
                {
                    // 言語データを確認・ダウンロード
                    await TessdataDownloader.EnsureLanguageDataAsync();

                    var tessdataPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tessdata");
                    if (Directory.Exists(tessdataPath))
                    {
                        // 日本語と英語の両方を使用
                        _ocrEngine = new TesseractEngine(tessdataPath, "jpn+eng", EngineMode.Default);

                        // Tesseractの内部ログを抑制（オプション）
                        // これらの警告は通常のOCR処理では問題ありません
                        _ocrEngine.SetVariable("debug_file", "/dev/null");

                        Debug.WriteLine("OCRエンジンを初期化しました");
                    }
                    else
                    {
                        Debug.WriteLine($"tessdataディレクトリが見つかりません: {tessdataPath}");
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"OCRエンジンの初期化エラー: {ex.Message}");
                    _ocrEngine = null;
                }
            });
        }

        /// <summary>
        /// OCRフィルタリング結果
        /// </summary>
        private class FilterResult
        {
            public bool ShouldFilter { get; set; }
            public string MatchedText { get; set; } = string.Empty;
        }

        /// <summary>
        /// OCRで文字認識して正規表現でフィルタリングすべきか判定
        /// </summary>
        private async Task<FilterResult> ShouldFilterByOcr(string imageBase64)
        {
            if (_ocrEngine == null || string.IsNullOrEmpty(RegexPattern))
                return new FilterResult { ShouldFilter = false };

            return await Task.Run(() =>
            {
                try
                {
                    // Base64から画像データに変換
                    var imageBytes = Convert.FromBase64String(imageBase64);

                    lock (_ocrLock)
                    {
                        using (var ms = new MemoryStream(imageBytes))
                        using (var bitmap = new Bitmap(ms))
                        using (var pix = Tesseract.PixConverter.ToPix(bitmap))
                        {
                            // Tesseractのログレベルを一時的に変更して警告を抑制
                            using (var page = _ocrEngine.Process(pix, PageSegMode.Auto))
                            {
                                var text = page.GetText();

                                bool DebugMode = false;
                                if (DebugMode)
                                {
                                    Debug.WriteLine($"OCR認識結果: {text.Length}文字");

                                    // デバッグ用：認識されたテキストを表示（最大2000文字）
                                    if (!string.IsNullOrWhiteSpace(text))
                                    {
                                        const int maxDebugLength = 5000;
                                        var debugText = text.Length > maxDebugLength
                                            ? text.Substring(0, maxDebugLength) + $"... (全{text.Length}文字)"
                                            : text;
                                        Debug.WriteLine("=== OCR認識テキスト（デバッグ） ===");
                                        Debug.WriteLine(debugText);
                                        Debug.WriteLine("=================================");
                                    }
                                }
                                {
                                    // 正規表現マッチング前にすべての空白を削除
                                    var textForMatching = Regex.Replace(text, @"\s+", "");

                                    if (DebugMode && !string.IsNullOrWhiteSpace(textForMatching))
                                    {
                                        Debug.WriteLine($"空白削除後の文字数: {textForMatching.Length}文字");

                                        // 空白削除後のテキストも表示（最大2000文字）
                                        const int maxDebugLength = 2000;
                                        var debugTextNoSpace = textForMatching.Length > maxDebugLength
                                            ? textForMatching.Substring(0, maxDebugLength) + $"... (全{textForMatching.Length}文字)"
                                            : textForMatching;
                                        Debug.WriteLine("=== 空白削除後のテキスト（デバッグ） ===");
                                        Debug.WriteLine(debugTextNoSpace);
                                        Debug.WriteLine("=====================================");
                                    }

                                    // 正規表現でマッチング
                                    var regex = new Regex(RegexPattern, RegexOptions.IgnoreCase | RegexOptions.Multiline);
                                    var match = regex.Match(textForMatching);

                                    if (DebugMode)
                                    {
                                        Debug.WriteLine($"正規表現パターン: {RegexPattern}");
                                    }

                                    if (match.Success)
                                    {
                                        if (DebugMode)
                                        {
                                            Debug.WriteLine($"正規表現にマッチしました！");
                                            Debug.WriteLine($"マッチ位置（空白削除後）: {match.Index}");
                                            Debug.WriteLine($"マッチした文字列（空白削除後）: {match.Value}");
                                        }

                                        // マッチしたテキストを短く切り詰める（最大30文字）
                                        var matchedText = match.Value;
                                        if (matchedText.Length > 30)
                                        {
                                            matchedText = matchedText.Substring(0, 27) + "...";
                                        }

                                        return new FilterResult
                                        {
                                            ShouldFilter = true,
                                            MatchedText = matchedText
                                        };
                                    }
                                    else
                                    {
                                        if (DebugMode)
                                        {
                                            Debug.WriteLine("正規表現にマッチしませんでした");
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"OCR処理エラー: {ex.Message}");
                }

                return new FilterResult { ShouldFilter = false };
            });
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

            lock (_ocrLock)
            {
                _ocrEngine?.Dispose();
                _ocrEngine = null;
            }

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
        public bool IsFiltered { get; set; } = false;
        public string FilterReason { get; set; } = string.Empty;
    }
}