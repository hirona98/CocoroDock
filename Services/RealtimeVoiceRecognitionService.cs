using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;

namespace CocoroDock.Services
{
    public class RealtimeVoiceRecognitionService : IDisposable
    {
        private WaveInEvent? _waveIn;
        private readonly List<byte> _audioBuffer = new();
        private readonly VoiceRecognitionStateMachine _stateMachine;
        private readonly AmiVoiceSyncClient _amiVoiceClient;

        // 音声検出パラメータ
        private readonly float _voiceThreshold;
        private readonly int _silenceTimeoutMs;
        private const int MIN_VOICE_DURATION_MS = 200;

        private DateTime _lastVoiceTime = DateTime.Now;
        private bool _isRecordingVoice = false;
        private Timer? _silenceTimer;
        private bool _isDisposed = false;

        // イベント
        public event Action<string>? OnRecognizedText;
        public event Action<VoiceRecognitionState>? OnStateChanged;
        public event Action<float>? OnVoiceLevel;

        public VoiceRecognitionState CurrentState => _stateMachine.CurrentState;
        public bool IsListening { get; private set; }

        public RealtimeVoiceRecognitionService(
            string apiKey,
            string wakeWords,
            float voiceThreshold = 0.02f,
            int silenceTimeoutMs = 300,
            int activeTimeoutMs = 60000)
        {
            _voiceThreshold = voiceThreshold;
            _silenceTimeoutMs = silenceTimeoutMs;

            _amiVoiceClient = new AmiVoiceSyncClient(apiKey);
            _stateMachine = new VoiceRecognitionStateMachine(wakeWords, activeTimeoutMs);

            // イベントの転送
            _stateMachine.OnRecognizedText += (text) => OnRecognizedText?.Invoke(text);
            _stateMachine.OnStateChanged += (state) => OnStateChanged?.Invoke(state);
        }

        public void StartListening()
        {
            if (IsListening || _isDisposed)
                return;

            try
            {
                _waveIn = new WaveInEvent
                {
                    WaveFormat = new WaveFormat(16000, 16, 1), // 16kHz, 16bit, モノラル
                    BufferMilliseconds = 50,
                    NumberOfBuffers = 2
                };

                _waveIn.DataAvailable += OnAudioDataAvailable;
                _waveIn.RecordingStopped += OnRecordingStopped;
                _waveIn.StartRecording();

                // 無音タイマー初期化
                _silenceTimer = new Timer(OnSilenceDetected, null, Timeout.Infinite, Timeout.Infinite);

                IsListening = true;
                System.Diagnostics.Debug.WriteLine("[VoiceService] Started listening");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[VoiceService] Failed to start listening: {ex.Message}");
                StopListening();
            }
        }

        public void StopListening()
        {
            if (!IsListening)
                return;

            IsListening = false;

            try
            {
                _waveIn?.StopRecording();
                _waveIn?.Dispose();
                _waveIn = null;

                _silenceTimer?.Dispose();
                _silenceTimer = null;

                _isRecordingVoice = false;
                _audioBuffer.Clear();

                System.Diagnostics.Debug.WriteLine("[VoiceService] Stopped listening");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[VoiceService] Error stopping listening: {ex.Message}");
            }
        }

        private void OnAudioDataAvailable(object? sender, WaveInEventArgs e)
        {
            if (_isDisposed || !IsListening)
                return;

            try
            {
                // 音量バー表示用レベル計算（固定閾値）
                float displayLevel = CalculateDisplayLevel(e.Buffer, e.BytesRecorded);
                OnVoiceLevel?.Invoke(displayLevel);

                // 無音区間判定用レベル計算
                float voiceLevel = CalculateVoiceLevel(e.Buffer, e.BytesRecorded);

                if (voiceLevel > _voiceThreshold)
                {
                    // 音声検出
                    if (!_isRecordingVoice)
                    {
                        _isRecordingVoice = true;
                        _audioBuffer.Clear();
                        AddWavHeader();
                        System.Diagnostics.Debug.WriteLine("[VoiceService] Started recording voice");
                    }

                    _audioBuffer.AddRange(e.Buffer.Take(e.BytesRecorded));
                    _lastVoiceTime = DateTime.Now;

                    // 無音タイマーリセット
                    _silenceTimer?.Change(_silenceTimeoutMs, Timeout.Infinite);
                }
                else if (_isRecordingVoice)
                {
                    // 音声中の無音部分も録音に含める
                    _audioBuffer.AddRange(e.Buffer.Take(e.BytesRecorded));
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[VoiceService] Error in audio data processing: {ex.Message}");
            }
        }

        private void OnRecordingStopped(object? sender, StoppedEventArgs e)
        {
            if (e.Exception != null)
            {
                System.Diagnostics.Debug.WriteLine($"[VoiceService] Recording stopped with error: {e.Exception.Message}");
            }
        }

        private async void OnSilenceDetected(object? state)
        {
            if (!_isRecordingVoice || _isDisposed)
                return;

            var duration = DateTime.Now - _lastVoiceTime;
            if (duration.TotalMilliseconds < MIN_VOICE_DURATION_MS)
            {
                System.Diagnostics.Debug.WriteLine($"[VoiceService] Voice too short: {duration.TotalMilliseconds}ms");
                return;
            }

            _isRecordingVoice = false;
            System.Diagnostics.Debug.WriteLine($"[VoiceService] Silence detected, processing {_audioBuffer.Count} bytes");

            // 音声認識実行
            await ProcessAudioBuffer();
        }

        private async Task ProcessAudioBuffer()
        {
            if (_audioBuffer.Count == 0 || _isDisposed)
                return;

            var audioData = _audioBuffer.ToArray();
            _audioBuffer.Clear();

            try
            {
                // PROCESSING状態に遷移（UI表示用）
                var originalState = _stateMachine.CurrentState;
                _stateMachine.TransitionTo(VoiceRecognitionState.PROCESSING);

                // WAVヘッダーを更新してから送信
                UpdateWavHeader(audioData);

                // AmiVoice API呼び出し（並列処理でブロックしない）
                var recognitionTask = _amiVoiceClient.RecognizeAsync(audioData);
                string recognizedText = await recognitionTask.ConfigureAwait(false);

                // 元の状態に戻してから結果を処理
                _stateMachine.TransitionTo(originalState);

                if (!string.IsNullOrEmpty(recognizedText))
                {
                    _stateMachine.ProcessRecognitionResult(recognizedText);
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("[VoiceService] No text recognized");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[VoiceService] Error processing audio: {ex.Message}");
                // エラー時は SLEEPING 状態に戻る
                if (_stateMachine.CurrentState == VoiceRecognitionState.PROCESSING)
                {
                    _stateMachine.TransitionTo(VoiceRecognitionState.SLEEPING);
                }
            }
        }

        /// <summary>
        /// 音量バー表示用レベル計算（固定閾値）
        /// </summary>
        private float CalculateDisplayLevel(byte[] buffer, int bytesRecorded)
        {
            if (bytesRecorded == 0)
                return 0;

            // 最大振幅を検出（RMS値より視覚的にわかりやすい）
            float maxAmplitude = 0;
            for (int i = 0; i < bytesRecorded; i += 2)
            {
                if (i + 1 < bytesRecorded)
                {
                    short sample = (short)((buffer[i + 1] << 8) | buffer[i]);
                    float amplitude = Math.Abs(sample) / 32768f;
                    maxAmplitude = Math.Max(maxAmplitude, amplitude);
                }
            }

            // 音量バー表示用の固定計算（-50dB〜0dBを0〜1にマッピング）
            if (maxAmplitude > 0)
            {
                float db = 20 * (float)Math.Log10(maxAmplitude);
                // -50dB以下は0、0dB以上は1にクリップ
                return Math.Max(0, Math.Min(1, (db + 50) / 50));
            }
            return 0;
        }

        /// <summary>
        /// 無音区間判定用レベル計算（inputThreshold設定を使用）
        /// </summary>
        private float CalculateVoiceLevel(byte[] buffer, int bytesRecorded)
        {
            if (bytesRecorded == 0)
                return 0;

            // 平均振幅を計算（無音区間判定用）
            float sum = 0;
            for (int i = 0; i < bytesRecorded; i += 2)
            {
                if (i + 1 < bytesRecorded)
                {
                    short sample = (short)((buffer[i + 1] << 8) | buffer[i]);
                    sum += Math.Abs(sample) / 32768f;
                }
            }
            return sum / (bytesRecorded / 2);
        }

        private void AddWavHeader()
        {
            // WAVファイルヘッダーを追加
            const int sampleRate = 16000;
            const short channels = 1;
            const short bitsPerSample = 16;

            var header = new List<byte>();

            // RIFF header
            header.AddRange(System.Text.Encoding.ASCII.GetBytes("RIFF"));
            header.AddRange(BitConverter.GetBytes(0)); // ファイルサイズ（後で更新）
            header.AddRange(System.Text.Encoding.ASCII.GetBytes("WAVE"));

            // fmt chunk
            header.AddRange(System.Text.Encoding.ASCII.GetBytes("fmt "));
            header.AddRange(BitConverter.GetBytes(16)); // fmt chunk size
            header.AddRange(BitConverter.GetBytes((short)1)); // PCM format
            header.AddRange(BitConverter.GetBytes(channels));
            header.AddRange(BitConverter.GetBytes(sampleRate));
            header.AddRange(BitConverter.GetBytes(sampleRate * channels * bitsPerSample / 8)); // byte rate
            header.AddRange(BitConverter.GetBytes((short)(channels * bitsPerSample / 8))); // block align
            header.AddRange(BitConverter.GetBytes(bitsPerSample));

            // data chunk header
            header.AddRange(System.Text.Encoding.ASCII.GetBytes("data"));
            header.AddRange(BitConverter.GetBytes(0)); // data size（後で更新）

            _audioBuffer.AddRange(header);
        }

        private void UpdateWavHeader(byte[] audioData)
        {
            if (audioData.Length < 44)
                return;

            int dataSize = audioData.Length - 44;
            int fileSize = audioData.Length - 8;

            // ファイルサイズ更新
            var fileSizeBytes = BitConverter.GetBytes(fileSize);
            Array.Copy(fileSizeBytes, 0, audioData, 4, 4);

            // データサイズ更新
            var dataSizeBytes = BitConverter.GetBytes(dataSize);
            Array.Copy(dataSizeBytes, 0, audioData, 40, 4);
        }

        public void Dispose()
        {
            if (_isDisposed)
                return;

            _isDisposed = true;

            StopListening();
            _stateMachine?.Dispose();
            _amiVoiceClient?.Dispose();

            System.Diagnostics.Debug.WriteLine("[VoiceService] Disposed");
        }
    }
}