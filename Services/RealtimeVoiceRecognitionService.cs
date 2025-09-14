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
        private readonly SileroVadService _sileroVad;

        // マイクゲイン設定
        private float _microphoneGain = 1f; // デフォルト2倍（ハードコーディング）

        // プリバッファリング（言葉の頭切れ対策）
        private readonly Queue<byte[]> _preBuffer = new();
        private const int PRE_BUFFER_MS = 500; // 0.5秒分のプリバッファ

        private bool _isRecordingVoice = false;
        private bool _isDisposed = false;


        // イベント
        public event Action<string>? OnRecognizedText;
        public event Action<VoiceRecognitionState>? OnStateChanged;
        public event Action<float, bool>? OnVoiceLevel;  // level, isAboveThreshold

        public VoiceRecognitionState CurrentState => _stateMachine.CurrentState;
        public bool IsListening { get; private set; }

        public RealtimeVoiceRecognitionService(
            string apiKey,
            string wakeWords,
            float vadThreshold = 0.5f,
            int silenceTimeoutMs = 300,
            int activeTimeoutMs = 60000,
            bool startActive = false)
        {
            _amiVoiceClient = new AmiVoiceSyncClient(apiKey);
            _stateMachine = new VoiceRecognitionStateMachine(wakeWords, activeTimeoutMs, startActive);

            // Silero VADの初期化
            _sileroVad = new SileroVadService(
                sampleRate: 16000,
                threshold: vadThreshold,
                minSilenceDurationMs: silenceTimeoutMs,
                speechPadMs: 30);

            System.Diagnostics.Debug.WriteLine("[VoiceService] Silero VAD initialized");

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

                _isRecordingVoice = false;
                _audioBuffer.Clear();
                _preBuffer.Clear(); // プリバッファもクリア

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
                // マイクゲインを適用
                var amplifiedBuffer = ApplyMicrophoneGain(e.Buffer, e.BytesRecorded);

                // プリバッファに追加
                _preBuffer.Enqueue(amplifiedBuffer);
                int maxBuffers = PRE_BUFFER_MS / 50;
                while (_preBuffer.Count > maxBuffers)
                {
                    _preBuffer.Dequeue();
                }

                // 表示用レベル計算
                float displayLevel = CalculateDisplayLevel(amplifiedBuffer, e.BytesRecorded);

                // Silero VADで音声検出
                bool isSpeechStart, isSpeechEnd;
                bool isSpeaking = _sileroVad.ProcessAudio(amplifiedBuffer, e.BytesRecorded, out isSpeechStart, out isSpeechEnd);

                OnVoiceLevel?.Invoke(displayLevel, isSpeaking);

                if (isSpeechStart)
                {
                    _isRecordingVoice = true;
                    _audioBuffer.Clear();
                    AddWavHeader();

                    // プリバッファの内容を録音に含める
                    foreach (var preBufferData in _preBuffer)
                    {
                        _audioBuffer.AddRange(preBufferData);
                    }

                    System.Diagnostics.Debug.WriteLine($"[VoiceService] Speech started with {_preBuffer.Count} pre-buffers");
                }
                else if (_isRecordingVoice)
                {
                    _audioBuffer.AddRange(amplifiedBuffer);
                }

                if (isSpeechEnd && _isRecordingVoice)
                {
                    _isRecordingVoice = false;
                    System.Diagnostics.Debug.WriteLine($"[VoiceService] Speech ended, processing {_audioBuffer.Count} bytes");

                    // 非同期で音声認識実行
                    _ = ProcessAudioBuffer();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[VoiceService] Error in audio processing: {ex.Message}");
            }
        }

        private void OnRecordingStopped(object? sender, StoppedEventArgs e)
        {
            if (e.Exception != null)
            {
                System.Diagnostics.Debug.WriteLine($"[VoiceService] Recording stopped with error: {e.Exception.Message}");
            }
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

                // デバッグ用音声ファイル保存（デスクトップに保存）
                // SaveAudioFileForDebug(audioData);

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

        /// <summary>
        /// デバッグ用音声ファイル保存
        /// </summary>
        private static int _audioFileCounter = 0;
        private void SaveAudioFileForDebug(byte[] audioData)
        {
            try
            {
                // デバッグファイル保存ディレクトリ
                string debugDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "CocoroAI_AudioDebug");
                if (!Directory.Exists(debugDir))
                {
                    Directory.CreateDirectory(debugDir);
                }

                // ファイル名（タイムスタンプ + 連番）
                var now = DateTime.Now;
                var fileName = $"voice_{now:yyyyMMdd_HHmmss}_{Interlocked.Increment(ref _audioFileCounter):D3}.wav";
                string filePath = Path.Combine(debugDir, fileName);

                // WAVファイルとして保存
                File.WriteAllBytes(filePath, audioData);

                System.Diagnostics.Debug.WriteLine($"[VoiceService] Debug audio saved: {filePath} ({audioData.Length} bytes)");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[VoiceService] Failed to save debug audio: {ex.Message}");
            }
        }

        /// <summary>
        /// マイクゲインを適用して音声データを増幅
        /// </summary>
        private byte[] ApplyMicrophoneGain(byte[] buffer, int bytesRecorded)
        {
            if (_microphoneGain == 1.0f)
            {
                // ゲインが1.0の場合は何もしない（パフォーマンス向上）
                return buffer.Take(bytesRecorded).ToArray();
            }

            var amplifiedBuffer = new byte[bytesRecorded];

            for (int i = 0; i < bytesRecorded; i += 2)
            {
                if (i + 1 < bytesRecorded)
                {
                    // 16bit サンプルを取得
                    short sample = (short)((buffer[i + 1] << 8) | buffer[i]);

                    // ゲインを適用
                    float amplifiedSample = sample * _microphoneGain;

                    // クリッピング防止（-32768 ~ 32767の範囲に制限）
                    amplifiedSample = Math.Max(-32768, Math.Min(32767, amplifiedSample));

                    // バイト配列に戻す
                    short clippedSample = (short)amplifiedSample;
                    amplifiedBuffer[i] = (byte)(clippedSample & 0xFF);
                    amplifiedBuffer[i + 1] = (byte)((clippedSample >> 8) & 0xFF);
                }
            }

            return amplifiedBuffer;
        }

        /// <summary>
        /// マイクゲインを設定
        /// </summary>
        public void SetMicrophoneGain(float gain)
        {
            _microphoneGain = Math.Max(0.1f, Math.Min(10.0f, gain)); // 0.1倍～10倍の範囲で制限
            System.Diagnostics.Debug.WriteLine($"[VoiceService] Microphone gain set to: {_microphoneGain:F1}x");
        }

        /// <summary>
        /// WebSocket音声データの認識（リアルタイム処理なし）
        /// </summary>
        public async Task<string> RecognizeAudioDataAsync(byte[] audioData)
        {
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(RealtimeVoiceRecognitionService));

            if (audioData == null || audioData.Length == 0)
                return string.Empty;

            try
            {
                // AmiVoice API呼び出し
                var recognizedText = await _amiVoiceClient.RecognizeAsync(audioData);
                return recognizedText ?? string.Empty;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[VoiceService] WebSocket音声認識エラー: {ex.Message}");
                return string.Empty;
            }
        }

        public void Dispose()
        {
            if (_isDisposed)
                return;

            _isDisposed = true;

            StopListening();
            _stateMachine?.Dispose();
            _amiVoiceClient?.Dispose();
            _sileroVad?.Dispose();

            System.Diagnostics.Debug.WriteLine("[VoiceService] Disposed");
        }
    }
}