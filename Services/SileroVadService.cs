using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using NAudio.Wave;

namespace CocoroDock.Services
{
    public class SileroVadService : IDisposable
    {
        private InferenceSession? _session;
        private readonly int _sampleRate;
        private readonly int _windowSizeSamples;
        private readonly float _threshold;
        private readonly int _minSilenceDurationMs;
        private readonly int _speechPadMs;

        private float[]? _state;
        private int _samplesSinceLastSpeech;
        private bool _triggered;

        private readonly object _lock = new object();

        public SileroVadService(
            int sampleRate = 16000,
            float threshold = 0.5f,
            int minSilenceDurationMs = 100,
            int speechPadMs = 30)
        {
            _sampleRate = sampleRate;
            _windowSizeSamples = 512; // Silero VADは512サンプル（32ms @ 16kHz）のウィンドウを使用
            _threshold = threshold;
            _minSilenceDurationMs = minSilenceDurationMs;
            _speechPadMs = speechPadMs;

            InitializeModel();
            ResetStates();
        }

        private void InitializeModel()
        {
            try
            {
                // 埋め込みリソースからモデルを読み込み
                var assembly = Assembly.GetExecutingAssembly();
                var resourceName = "CocoroDock.Resource.silero_vad.onnx";

                using var stream = assembly.GetManifestResourceStream(resourceName);
                if (stream == null)
                {
                    throw new FileNotFoundException($"Embedded resource '{resourceName}' not found.");
                }

                var modelData = new byte[stream.Length];
                stream.Read(modelData, 0, modelData.Length);

                var options = new SessionOptions
                {
                    GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
                    ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
                    InterOpNumThreads = 1,
                    IntraOpNumThreads = 1
                };

                _session = new InferenceSession(modelData, options);
                System.Diagnostics.Debug.WriteLine("[SileroVAD] Model loaded from embedded resource");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SileroVAD] Failed to load model: {ex.Message}");
                throw;
            }
        }

        private void ResetStates()
        {
            // state tensor: shape (2, batch_size=1, 128)
            _state = new float[2 * 1 * 128]; // すべて0で初期化
            _samplesSinceLastSpeech = 0;
            _triggered = false;
        }

        private readonly List<float> _sampleBuffer = new List<float>();

        public bool ProcessAudio(byte[] buffer, int bytesRecorded, out bool isSpeechStart, out bool isSpeechEnd)
        {
            isSpeechStart = false;
            isSpeechEnd = false;

            try
            {
                lock (_lock)
                {
                    // バイト配列をfloat配列に変換
                    var samples = ConvertBytesToFloat(buffer, bytesRecorded);

                    // サンプルをバッファに追加
                    _sampleBuffer.AddRange(samples);

                    // 512サンプルごとに処理
                    while (_sampleBuffer.Count >= _windowSizeSamples)
                    {
                        var windowData = new float[_windowSizeSamples];
                        _sampleBuffer.CopyTo(0, windowData, 0, _windowSizeSamples);
                        _sampleBuffer.RemoveRange(0, _windowSizeSamples);

                        var speechProb = RunInference(windowData);
                        var isSpeech = speechProb >= _threshold;

                        if (isSpeech && !_triggered)
                        {
                            _triggered = true;
                            isSpeechStart = true;
                            System.Diagnostics.Debug.WriteLine($"[SileroVAD] Speech start (prob: {speechProb:F3})");
                        }

                        if (_triggered)
                        {
                            if (!isSpeech)
                            {
                                _samplesSinceLastSpeech += _windowSizeSamples;

                                if (_samplesSinceLastSpeech > _minSilenceDurationMs * _sampleRate / 1000)
                                {
                                    _triggered = false;
                                    isSpeechEnd = true;
                                    _samplesSinceLastSpeech = 0;
                                    System.Diagnostics.Debug.WriteLine($"[SileroVAD] Speech end");
                                }
                            }
                            else
                            {
                                _samplesSinceLastSpeech = 0;
                            }
                        }
                    }

                    return _triggered;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SileroVAD] ProcessAudio error: {ex.Message}");
                return false;
            }
        }

        private float RunInference(float[] audioData)
        {
            if (_session == null || _state == null)
                return 0f;

            try
            {
                // Silero VAD v5の正しい入力形式
                var inputTensor = new DenseTensor<float>(audioData, new[] { 1, audioData.Length });
                var stateTensor = new DenseTensor<float>(_state, new[] { 2, 1, 128 });
                var srTensor = new DenseTensor<long>(new[] { (long)_sampleRate }, new[] { 1 });

                var inputs = new List<NamedOnnxValue>
                {
                    NamedOnnxValue.CreateFromTensor("input", inputTensor),
                    NamedOnnxValue.CreateFromTensor("state", stateTensor),
                    NamedOnnxValue.CreateFromTensor("sr", srTensor)
                };

                using var results = _session.Run(inputs);
                var output = results.First(r => r.Name == "output").AsEnumerable<float>().First();

                // 新しいstateを更新
                var newState = results.First(r => r.Name == "stateN").AsEnumerable<float>().ToArray();
                Array.Copy(newState, _state, Math.Min(newState.Length, _state.Length));

                return output;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SileroVAD] Inference error: {ex.Message}");
                return 0f;
            }
        }

        private float[] ConvertBytesToFloat(byte[] buffer, int bytesRecorded)
        {
            var sampleCount = bytesRecorded / 2;
            var samples = new float[sampleCount];

            for (int i = 0; i < sampleCount; i++)
            {
                var sample = BitConverter.ToInt16(buffer, i * 2);
                samples[i] = sample / 32768f;
            }

            return samples;
        }


        public void Reset()
        {
            lock (_lock)
            {
                ResetStates();
                _sampleBuffer.Clear();
            }
        }

        public void Dispose()
        {
            _session?.Dispose();
        }
    }
}