using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CocoroDock.Utils;

namespace CocoroDock.Communication
{
    /// <summary>
    /// AivisCloud音声合成クライアント
    /// </summary>
    public class AivisCloudClient : ISpeechSynthesizerClient
    {
        private readonly HttpClient _httpClient;
        private readonly string _audioDirectory;
        private readonly AivisCloudConfig _config;
        private Timer? _cleanupTimer;

        public string ProviderName => "AivisCloud";

        public AivisCloudClient(AivisCloudConfig? config, string audioDirectory = "wwwroot/audio")
        {
            _config = config ?? new AivisCloudConfig();
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
            _audioDirectory = audioDirectory;

            // 音声ディレクトリを作成
            Directory.CreateDirectory(_audioDirectory);

            // 1時間ごとに古い音声ファイルを削除するタイマー
            _cleanupTimer = new Timer(CleanupOldAudioFiles, null, TimeSpan.FromMinutes(60), TimeSpan.FromMinutes(60));

            Debug.WriteLine($"[AivisCloudClient] 初期化完了: endpoint={_config.endpointUrl}, audioDir={_audioDirectory}");
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
                    Debug.WriteLine("[AivisCloudClient] テキストが空のため音声合成をスキップ");
                    return null;
                }

                // [face:～] パターンを除去
                var filteredText = TextFilterHelper.RemoveFacePatterns(text);

                if (string.IsNullOrWhiteSpace(filteredText))
                {
                    Debug.WriteLine("[AivisCloudClient] フィルター後のテキストが空のため音声合成をスキップ");
                    return null;
                }

                var config = characterSettings.aivisCloudConfig ?? _config;

                // パラメータのデバッグログ
                Debug.WriteLine($"[AivisCloudClient] パラメータ適用: modelUuid={config.modelUuid}, speakerUuid={config.speakerUuid}");
                Debug.WriteLine($"[AivisCloudClient] 音声パラメータ: speakingRate={config.speakingRate}, pitch={config.pitch}, volume={config.volume}");

                // APIキーの確認
                if (string.IsNullOrEmpty(config.apiKey))
                {
                    Debug.WriteLine("[AivisCloudClient] APIキーが設定されていません");
                    return null;
                }

                // AivisCloud API呼び出し
                var audioData = await CallAivisCloudApiAsync(filteredText, config);
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
                Debug.WriteLine($"[AivisCloudClient] 音声合成エラー: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// AivisCloud API呼び出し
        /// </summary>
        private async Task<byte[]?> CallAivisCloudApiAsync(string text, AivisCloudConfig config)
        {
            try
            {
                var url = string.IsNullOrEmpty(config.endpointUrl)
                    ? "https://api.aivis-project.com/v1/tts/synthesize"
                    : config.endpointUrl;

                var headers = new Dictionary<string, string>()
                {
                    { "Authorization", $"Bearer {config.apiKey}"}
                };

                var payload = new Dictionary<string, object>()
                {
                    {"text", text},
                    {"model_uuid", config.modelUuid},
                    {"style_id", config.styleId},
                    {"use_ssml", config.useSSML},
                    {"language", config.language},
                    {"speaking_rate", config.speakingRate},
                    {"emotional_intensity", config.emotionalIntensity},
                    {"tempo_dynamics", config.tempoDynamics},
                    {"pitch", config.pitch},
                    {"volume", config.volume},
                    {"output_format", config.outputFormat},
                    {"output_sampling_rate", config.outputSamplingRate},
                    {"output_audio_channels", config.outputAudioChannels}
                };

                if (!string.IsNullOrEmpty(config.speakerUuid))
                {
                    payload["speaker_uuid"] = config.speakerUuid;
                }
                if (!string.IsNullOrEmpty(config.styleName))
                {
                    payload["style_name"] = config.styleName;
                }
                if (config.outputBitrate > 0)
                {
                    payload["output_bitrate"] = config.outputBitrate;
                }

                Debug.WriteLine($"[AivisCloudClient] API呼び出し: {url}");

                var jsonContent = JsonSerializer.Serialize(payload);
                var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                // ヘッダー設定
                foreach (var header in headers)
                {
                    _httpClient.DefaultRequestHeaders.Remove(header.Key);
                    _httpClient.DefaultRequestHeaders.Add(header.Key, header.Value);
                }

                var response = await _httpClient.PostAsync(url, content);

                if (!response.IsSuccessStatusCode)
                {
                    Debug.WriteLine($"[AivisCloudClient] API エラー: {response.StatusCode}");
                    var errorContent = await response.Content.ReadAsStringAsync();
                    Debug.WriteLine($"[AivisCloudClient] エラー詳細: {errorContent}");
                    return null;
                }

                var audioData = await response.Content.ReadAsByteArrayAsync();
                return audioData;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AivisCloudClient] API呼び出し例外: {ex.Message}");
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
                Debug.WriteLine($"[AivisCloudClient] ファイルストリーム取得エラー: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// AivisCloud接続テスト
        /// </summary>
        public async Task<bool> TestConnectionAsync()
        {
            try
            {
                if (string.IsNullOrEmpty(_config.apiKey))
                    return false;

                var url = string.IsNullOrEmpty(_config.endpointUrl)
                    ? "https://api.aivis-project.com/v1/tts/synthesize"
                    : _config.endpointUrl;

                var testPayload = new Dictionary<string, object>()
                {
                    {"text", "test"},
                    {"model_uuid", _config.modelUuid},
                    {"style_id", _config.styleId}
                };

                var headers = new Dictionary<string, string>()
                {
                    { "Authorization", $"Bearer {_config.apiKey}"}
                };

                var jsonContent = JsonSerializer.Serialize(testPayload);
                var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                // ヘッダー設定
                foreach (var header in headers)
                {
                    _httpClient.DefaultRequestHeaders.Remove(header.Key);
                    _httpClient.DefaultRequestHeaders.Add(header.Key, header.Value);
                }

                var response = await _httpClient.PostAsync(url, content);
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
                            Debug.WriteLine($"[AivisCloudClient] ファイル削除エラー {file}: {ex.Message}");
                        }
                    }
                }

                if (deletedCount > 0)
                {
                    Debug.WriteLine($"[AivisCloudClient] 古い音声ファイルを{deletedCount}個削除しました");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AivisCloudClient] ファイルクリーンアップエラー: {ex.Message}");
            }
        }

        public void Dispose()
        {
            _cleanupTimer?.Dispose();
            _httpClient?.Dispose();
        }
    }
}