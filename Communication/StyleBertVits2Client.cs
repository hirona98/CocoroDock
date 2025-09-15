using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using CocoroDock.Utils;

namespace CocoroDock.Communication
{
    /// <summary>
    /// Style-Bert-VITS2音声合成クライアント
    /// </summary>
    public class StyleBertVits2Client : ISpeechSynthesizerClient
    {
        private readonly HttpClient _httpClient;
        private readonly string _audioDirectory;
        private readonly StyleBertVits2Config _config;
        private Timer? _cleanupTimer;

        public string ProviderName => "Style-Bert-VITS2";

        public StyleBertVits2Client(StyleBertVits2Config? config, string audioDirectory = "tmp/audio")
        {
            _config = config ?? new StyleBertVits2Config();
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
            _audioDirectory = audioDirectory;

            // 音声ディレクトリを作成
            Directory.CreateDirectory(_audioDirectory);

            // 1時間ごとに古い音声ファイルを削除するタイマー
            _cleanupTimer = new Timer(CleanupOldAudioFiles, null, TimeSpan.FromMinutes(60), TimeSpan.FromMinutes(60));

            Debug.WriteLine($"[StyleBertVits2Client] 初期化完了: endpoint={_config.endpointUrl}, audioDir={_audioDirectory}");
        }

        /// <summary>
        /// 音声合成を実行し、音声ファイルのURLを返す
        /// </summary>
        public async Task<string?> SynthesizeAsync(string text, CharacterSettings characterSettings)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(text))
                {
                    Debug.WriteLine("[StyleBertVits2Client] テキストが空のため音声合成をスキップ");
                    return null;
                }

                // [face:～] パターンを除去
                var filteredText = TextFilterHelper.RemoveFacePatterns(text);

                if (string.IsNullOrWhiteSpace(filteredText))
                {
                    Debug.WriteLine("[StyleBertVits2Client] フィルター後のテキストが空のため音声合成をスキップ");
                    return null;
                }

                var config = characterSettings.styleBertVits2Config ?? _config;

                // パラメータのデバッグログ
                Debug.WriteLine($"[StyleBertVits2Client] パラメータ適用: modelName={config.modelName}, speakerName={config.speakerName}, style={config.style}");
                Debug.WriteLine($"[StyleBertVits2Client] 音声パラメータ: length={config.length}, noise={config.noise}, styleWeight={config.styleWeight}");

                // Style-Bert-VITS2 API呼び出し
                var audioData = await CallStyleBertVits2ApiAsync(filteredText, config);
                if (audioData == null)
                {
                    return null;
                }

                // WAVファイル保存
                var fileName = $"response_{DateTime.Now:yyyyMMddHHmmssffff}.wav";
                var filePath = Path.Combine(_audioDirectory, fileName);
                await File.WriteAllBytesAsync(filePath, audioData);

                var audioUrl = $"/audio/{fileName}";
                return audioUrl;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[StyleBertVits2Client] 音声合成エラー: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Style-Bert-VITS2 API呼び出し
        /// </summary>
        private async Task<byte[]?> CallStyleBertVits2ApiAsync(string text, StyleBertVits2Config config)
        {
            try
            {
                // URLエンコードとクエリパラメータ構築
                var encodedText = HttpUtility.UrlEncode(text, Encoding.UTF8);
                var url = config.endpointUrl + $"/voice?text={encodedText}";

                if (!string.IsNullOrEmpty(config.modelName))
                    url += $"&model_name={HttpUtility.UrlEncode(config.modelName, Encoding.UTF8)}";

                url += $"&model_id={config.modelId}";

                if (!string.IsNullOrEmpty(config.speakerName))
                    url += $"&speaker_name={HttpUtility.UrlEncode(config.speakerName, Encoding.UTF8)}";

                url += $"&sdp_ratio={config.sdpRatio}";
                url += $"&noise={config.noise}";
                url += $"&noisew={config.noiseW}";
                url += $"&length={config.length}";
                url += $"&language={config.language}";
                url += $"&auto_split={config.autoSplit.ToString().ToLower()}";
                url += $"&split_interval={config.splitInterval}";

                if (!string.IsNullOrEmpty(config.assistText))
                {
                    url += $"&assist_text={HttpUtility.UrlEncode(config.assistText, Encoding.UTF8)}";
                    url += $"&assist_text_weight={config.assistTextWeight}";
                }

                url += $"&style={HttpUtility.UrlEncode(config.style, Encoding.UTF8)}";
                url += $"&style_weight={config.styleWeight}";

                if (!string.IsNullOrEmpty(config.referenceAudioPath))
                {
                    url += $"&reference_audio_path={HttpUtility.UrlEncode(config.referenceAudioPath, Encoding.UTF8)}";
                }

                Debug.WriteLine($"[StyleBertVits2Client] API呼び出し: {url}");

                var response = await _httpClient.GetAsync(url);

                if (!response.IsSuccessStatusCode)
                {
                    Debug.WriteLine($"[StyleBertVits2Client] API エラー: {response.StatusCode}");
                    return null;
                }

                var audioData = await response.Content.ReadAsByteArrayAsync();
                return audioData;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[StyleBertVits2Client] API呼び出し例外: {ex.Message}");
                return null;
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
                Debug.WriteLine($"[StyleBertVits2Client] ファイルストリーム取得エラー: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Style-Bert-VITS2接続テスト
        /// </summary>
        public async Task<bool> TestConnectionAsync()
        {
            try
            {
                // エンドポイントの健全性チェック（簡易テキストで確認）
                var response = await _httpClient.GetAsync($"{_config.endpointUrl}/voice?text=test&model_name={_config.modelName}");
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
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
                            Debug.WriteLine($"[StyleBertVits2Client] ファイル削除エラー {file}: {ex.Message}");
                        }
                    }
                }

                if (deletedCount > 0)
                {
                    Debug.WriteLine($"[StyleBertVits2Client] 古い音声ファイルを{deletedCount}個削除しました");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[StyleBertVits2Client] ファイルクリーンアップエラー: {ex.Message}");
            }
        }

        public void Dispose()
        {
            _cleanupTimer?.Dispose();
            _httpClient?.Dispose();
        }
    }
}