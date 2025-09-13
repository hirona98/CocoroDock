using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CocoroDock.Models;

namespace CocoroDock.Communication
{
    /// <summary>
    /// VOICEVOX API クライアント
    /// </summary>
    public class VoicevoxClient : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly string _endpointUrl;
        private readonly string _audioDirectory;
        private Timer? _cleanupTimer;

        public VoicevoxClient(string endpointUrl = "http://127.0.0.1:50021", string audioDirectory = "wwwroot/audio")
        {
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
            _endpointUrl = endpointUrl;
            _audioDirectory = audioDirectory;

            // 音声ディレクトリを作成
            Directory.CreateDirectory(_audioDirectory);

            // 1時間ごとに古い音声ファイルを削除するタイマー
            _cleanupTimer = new Timer(CleanupOldAudioFiles, null, TimeSpan.FromMinutes(60), TimeSpan.FromMinutes(60));

            Debug.WriteLine($"[VoicevoxClient] 初期化完了: endpoint={_endpointUrl}, audioDir={_audioDirectory}");
        }

        /// <summary>
        /// 音声合成を実行し、音声ファイルのURLを返す
        /// </summary>
        /// <param name="text">合成するテキスト</param>
        /// <param name="speakerId">話者ID</param>
        /// <returns>音声ファイルのURL（失敗時はnull）</returns>
        public async Task<string?> SynthesizeAsync(string text, int speakerId = 3)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(text))
                {
                    Debug.WriteLine("[VoicevoxClient] テキストが空のため音声合成をスキップ");
                    return null;
                }

                Debug.WriteLine($"[VoicevoxClient] 音声合成開始: text={text.Substring(0, Math.Min(50, text.Length))}..., speaker={speakerId}");

                // 1. audio_query API 呼び出し
                var audioQuery = await GetAudioQueryAsync(text, speakerId);
                if (audioQuery == null)
                {
                    return null;
                }

                // 2. synthesis API 呼び出し
                var audioData = await SynthesizeAudioAsync(audioQuery, speakerId);
                if (audioData == null)
                {
                    return null;
                }

                // 3. WAVファイル保存
                var fileName = $"response_{DateTime.Now:yyyyMMddHHmmssffff}.wav";
                var filePath = Path.Combine(_audioDirectory, fileName);
                await File.WriteAllBytesAsync(filePath, audioData);

                var audioUrl = $"/audio/{fileName}";
                Debug.WriteLine($"[VoicevoxClient] 音声合成完了: {audioUrl}");
                return audioUrl;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[VoicevoxClient] 音声合成エラー: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// audio_query APIを呼び出す
        /// </summary>
        private async Task<string?> GetAudioQueryAsync(string text, int speakerId)
        {
            try
            {
                var encodedText = Uri.EscapeDataString(text);
                var url = $"{_endpointUrl}/audio_query?text={encodedText}&speaker={speakerId}";

                var response = await _httpClient.PostAsync(url, new StringContent(""));

                if (!response.IsSuccessStatusCode)
                {
                    Debug.WriteLine($"[VoicevoxClient] audio_query API エラー: {response.StatusCode}");
                    return null;
                }

                var audioQuery = await response.Content.ReadAsStringAsync();
                Debug.WriteLine($"[VoicevoxClient] audio_query API 成功");
                return audioQuery;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[VoicevoxClient] audio_query API 例外: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// synthesis APIを呼び出す
        /// </summary>
        private async Task<byte[]?> SynthesizeAudioAsync(string audioQuery, int speakerId)
        {
            try
            {
                var url = $"{_endpointUrl}/synthesis?speaker={speakerId}";
                var content = new StringContent(audioQuery, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync(url, content);

                if (!response.IsSuccessStatusCode)
                {
                    Debug.WriteLine($"[VoicevoxClient] synthesis API エラー: {response.StatusCode}");
                    return null;
                }

                var audioData = await response.Content.ReadAsByteArrayAsync();
                Debug.WriteLine($"[VoicevoxClient] synthesis API 成功: {audioData.Length} bytes");
                return audioData;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[VoicevoxClient] synthesis API 例外: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 古い音声ファイルを削除する
        /// </summary>
        private void CleanupOldAudioFiles(object? state)
        {
            try
            {
                if (!Directory.Exists(_audioDirectory))
                    return;

                var cutoffTime = DateTime.Now.AddHours(-1);
                var files = Directory.GetFiles(_audioDirectory, "*.wav");
                var deletedCount = 0;

                foreach (var file in files)
                {
                    var fileInfo = new FileInfo(file);
                    if (fileInfo.CreationTime < cutoffTime)
                    {
                        try
                        {
                            File.Delete(file);
                            deletedCount++;
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[VoicevoxClient] ファイル削除エラー {file}: {ex.Message}");
                        }
                    }
                }

                if (deletedCount > 0)
                {
                    Debug.WriteLine($"[VoicevoxClient] 古い音声ファイルを{deletedCount}個削除しました");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[VoicevoxClient] ファイルクリーンアップエラー: {ex.Message}");
            }
        }

        /// <summary>
        /// 音声ファイルの静的配信用ストリームを取得
        /// </summary>
        public FileStream? GetAudioFileStream(string fileName)
        {
            try
            {
                // ファイル名のサニタイズ（パストラバーサル攻撃防止）
                fileName = Path.GetFileName(fileName);
                if (string.IsNullOrEmpty(fileName) || !fileName.EndsWith(".wav"))
                {
                    return null;
                }

                var filePath = Path.Combine(_audioDirectory, fileName);
                if (File.Exists(filePath))
                {
                    return new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                }

                return null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[VoicevoxClient] ファイルストリーム取得エラー: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// VOICEVOX接続テスト
        /// </summary>
        public async Task<bool> TestConnectionAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync($"{_endpointUrl}/version");
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        public void Dispose()
        {
            _cleanupTimer?.Dispose();
            _httpClient?.Dispose();
        }
    }
}