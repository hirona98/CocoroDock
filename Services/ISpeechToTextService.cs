using System;
using System.Threading.Tasks;

namespace CocoroDock.Services
{
    /// <summary>
    /// 音声認識サービスの抽象化インターフェース
    /// </summary>
    public interface ISpeechToTextService : IDisposable
    {
        /// <summary>
        /// 音声データを認識してテキストに変換
        /// </summary>
        /// <param name="audioData">音声データ（WAVフォーマット）</param>
        /// <returns>認識されたテキスト</returns>
        Task<string> RecognizeAsync(byte[] audioData);

        /// <summary>
        /// サービス名（ログ・デバッグ用）
        /// </summary>
        string ServiceName { get; }

        /// <summary>
        /// サービスが利用可能かどうか
        /// </summary>
        bool IsAvailable { get; }
    }

    /// <summary>
    /// AmiVoiceの実装
    /// </summary>
    public class AmiVoiceSpeechToTextService : ISpeechToTextService
    {
        private readonly AmiVoiceSyncClient _client;
        private bool _disposed;

        public string ServiceName => "AmiVoice";
        public bool IsAvailable => !_disposed && _client != null;

        public AmiVoiceSpeechToTextService(string apiKey)
        {
            _client = new AmiVoiceSyncClient(apiKey);
        }

        public async Task<string> RecognizeAsync(byte[] audioData)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(AmiVoiceSpeechToTextService));

            try
            {
                var result = await _client.RecognizeAsync(audioData);
                return result ?? string.Empty;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[{ServiceName}] Recognition error: {ex.Message}");
                return string.Empty;
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                _client?.Dispose();
                System.Diagnostics.Debug.WriteLine($"[{ServiceName}] Disposed");
            }
        }
    }

    /// <summary>
    /// 将来のWhisperやその他STTサービス用のベーステンプレート
    /// </summary>
    public class WhisperSpeechToTextService : ISpeechToTextService
    {
        private bool _disposed;

        public string ServiceName => "Whisper";
        public bool IsAvailable => !_disposed;

        public WhisperSpeechToTextService(string apiKey = "")
        {
            // Whisper初期化処理
        }

        public async Task<string> RecognizeAsync(byte[] audioData)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(WhisperSpeechToTextService));

            // TODO: Whisper実装
            await Task.Delay(1);
            return string.Empty;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                System.Diagnostics.Debug.WriteLine($"[{ServiceName}] Disposed");
            }
        }
    }
}