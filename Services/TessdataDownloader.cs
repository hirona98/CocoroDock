using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace CocoroAI.Services
{
    /// <summary>
    /// Tesseract言語データのダウンローダー
    /// </summary>
    public static class TessdataDownloader
    {
        private const string TessdataBaseUrl = "https://github.com/tesseract-ocr/tessdata/raw/main/";
        private static readonly HttpClient HttpClient = new HttpClient() { Timeout = TimeSpan.FromMinutes(5) };

        /// <summary>
        /// 必要な言語データが存在するか確認し、なければダウンロード
        /// </summary>
        public static async Task EnsureLanguageDataAsync()
        {
            var tessdataPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tessdata");
            
            // tessdataディレクトリが存在しない場合は作成
            if (!Directory.Exists(tessdataPath))
            {
                Directory.CreateDirectory(tessdataPath);
                Debug.WriteLine($"tessdataディレクトリを作成しました: {tessdataPath}");
            }

            // 必要な言語ファイル
            var requiredFiles = new[] { "eng.traineddata", "jpn.traineddata" };

            foreach (var file in requiredFiles)
            {
                var filePath = Path.Combine(tessdataPath, file);
                
                if (!File.Exists(filePath))
                {
                    Debug.WriteLine($"{file}が見つかりません。ダウンロードを開始します...");
                    
                    try
                    {
                        await DownloadFileAsync(file, filePath);
                        Debug.WriteLine($"{file}のダウンロードが完了しました");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"{file}のダウンロードに失敗しました: {ex.Message}");
                        // ダウンロードに失敗してもアプリケーションは続行
                    }
                }
                else
                {
                    Debug.WriteLine($"{file}は既に存在します");
                }
            }
        }

        /// <summary>
        /// ファイルをダウンロード
        /// </summary>
        private static async Task DownloadFileAsync(string fileName, string destinationPath)
        {
            var url = TessdataBaseUrl + fileName;
            
            using (var response = await HttpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                
                var totalBytes = response.Content.Headers.ContentLength ?? -1L;
                var canReportProgress = totalBytes != -1;

                using (var contentStream = await response.Content.ReadAsStreamAsync())
                using (var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
                {
                    var buffer = new byte[8192];
                    var totalRead = 0L;
                    var read = 0;

                    while ((read = await contentStream.ReadAsync(buffer, 0, buffer.Length)) != 0)
                    {
                        await fileStream.WriteAsync(buffer, 0, read);
                        totalRead += read;

                        if (canReportProgress)
                        {
                            var progress = (double)totalRead / totalBytes * 100;
                            Debug.WriteLine($"{fileName}: {progress:F1}% ({totalRead}/{totalBytes} bytes)");
                        }
                    }
                }
            }
        }
    }
}