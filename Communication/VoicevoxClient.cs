using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CocoroDock.Models;
using CocoroDock.Utils;

namespace CocoroDock.Communication
{
    /// <summary>
    /// VOICEVOX API クライアント
    /// </summary>
    public class VoicevoxClient : ISpeechSynthesizerClient
    {
        private readonly HttpClient _httpClient;
        private readonly string _endpointUrl;
        private readonly string _audioDirectory;
        private Timer? _cleanupTimer;

        public string ProviderName => "VOICEVOX";

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
        /// <param name="characterSettings">キャラクター設定</param>
        /// <returns>音声ファイルのURL（失敗時はnull）</returns>
        public async Task<string?> SynthesizeAsync(string text, CharacterSettings characterSettings)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(text))
                {
                    Debug.WriteLine("[VoicevoxClient] テキストが空のため音声合成をスキップ");
                    return null;
                }

                // [face:～] パターンを除去
                var filteredText = TextFilterHelper.RemoveFacePatterns(text);

                if (string.IsNullOrWhiteSpace(filteredText))
                {
                    Debug.WriteLine("[VoicevoxClient] フィルター後のテキストが空のため音声合成をスキップ");
                    return null;
                }

                var config = characterSettings.voicevoxConfig ?? new VoicevoxConfig();
                var speakerId = config.speakerId;

                // 1. audio_query API 呼び出し
                var audioQuery = await GetAudioQueryAsync(filteredText, speakerId);
                if (audioQuery == null)
                {
                    return null;
                }

                // 2. パラメータを適用してaudio_queryを編集
                var modifiedAudioQuery = ApplyVoicevoxParameters(audioQuery, config);
                if (modifiedAudioQuery == null)
                {
                    return null;
                }

                // 3. synthesis API 呼び出し
                var audioData = await SynthesizeAudioAsync(modifiedAudioQuery, speakerId);
                if (audioData == null)
                {
                    return null;
                }

                // 3. WAVファイル保存
                var fileName = $"response_{DateTime.Now:yyyyMMddHHmmssffff}.wav";
                var filePath = Path.Combine(_audioDirectory, fileName);
                await File.WriteAllBytesAsync(filePath, audioData);

                var audioUrl = $"/audio/{fileName}";
                return audioUrl;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[VoicevoxClient] 音声合成エラー: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// VOICEVOX パラメータをaudio_query JSONに適用
        /// </summary>
        private string? ApplyVoicevoxParameters(string audioQuery, VoicevoxConfig config)
        {
            try
            {
                using var jsonDoc = JsonDocument.Parse(audioQuery);
                using var stream = new MemoryStream();
                using var writer = new Utf8JsonWriter(stream);

                writer.WriteStartObject();

                // 元のJSONからすべてのプロパティをコピーし、パラメータを更新
                foreach (var property in jsonDoc.RootElement.EnumerateObject())
                {
                    switch (property.Name)
                    {
                        case "speedScale":
                            writer.WriteNumber("speedScale", config.speedScale);
                            break;
                        case "pitchScale":
                            writer.WriteNumber("pitchScale", config.pitchScale);
                            break;
                        case "intonationScale":
                            writer.WriteNumber("intonationScale", config.intonationScale);
                            break;
                        case "volumeScale":
                            writer.WriteNumber("volumeScale", config.volumeScale);
                            break;
                        case "prePhonemeLength":
                            writer.WriteNumber("prePhonemeLength", config.prePhonemeLength);
                            break;
                        case "postPhonemeLength":
                            writer.WriteNumber("postPhonemeLength", config.postPhonemeLength);
                            break;
                        case "outputSamplingRate":
                            writer.WriteNumber("outputSamplingRate", config.outputSamplingRate);
                            break;
                        case "outputStereo":
                            writer.WriteBoolean("outputStereo", config.outputStereo);
                            break;
                        default:
                            // その他のプロパティはそのままコピー
                            writer.WritePropertyName(property.Name);
                            property.Value.WriteTo(writer);
                            break;
                    }
                }

                writer.WriteEndObject();
                writer.Flush();

                var modifiedJson = Encoding.UTF8.GetString(stream.ToArray());
                return modifiedJson;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[VoicevoxClient] パラメータ適用エラー: {ex.Message}");
                return audioQuery; // エラー時は元のクエリを返す
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