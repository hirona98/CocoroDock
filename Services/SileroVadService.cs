using System;
using System.Collections.Concurrent;
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
    /// <summary>
    /// セッション管理型SileroVAD - ONNXモデルは共有、状態は個別管理
    /// </summary>
    public class SileroVadService : IDisposable
    {
        // 共有リソース（スレッドセーフ）
        private static InferenceSession? _sharedModel;
        private static readonly object _modelLock = new object();
        private static readonly ConcurrentQueue<VadSession> _sessionPool = new ConcurrentQueue<VadSession>();

        // インスタンス固有のセッション
        private VadSession? _currentSession;

        public SileroVadService(float threshold = 0.5f, int minSilenceDurationMs = 100)
        {
            EnsureModelLoaded();
            _currentSession = GetSession(threshold, minSilenceDurationMs);
        }

        /// <summary>
        /// ONNXモデルの初期化（共有リソース）
        /// </summary>
        private static void EnsureModelLoaded()
        {
            if (_sharedModel != null) return;

            lock (_modelLock)
            {
                if (_sharedModel != null) return;

                try
                {
                    var assembly = Assembly.GetExecutingAssembly();
                    var resourceName = "CocoroDock.Resource.silero_vad.onnx";

                    using var stream = assembly.GetManifestResourceStream(resourceName);
                    if (stream == null)
                        throw new FileNotFoundException($"Embedded resource '{resourceName}' not found.");

                    var modelData = new byte[stream.Length];
                    stream.Read(modelData, 0, modelData.Length);

                    var options = new SessionOptions
                    {
                        GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
                        ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
                        InterOpNumThreads = 1,
                        IntraOpNumThreads = 1
                    };

                    _sharedModel = new InferenceSession(modelData, options);
                    System.Diagnostics.Debug.WriteLine("[SileroVAD] Shared model loaded");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[SileroVAD] Model load error: {ex.Message}");
                    throw;
                }
            }
        }

        /// <summary>
        /// セッションをプールから取得または新規作成
        /// </summary>
        private static VadSession GetSession(float threshold, int minSilenceDurationMs)
        {
            if (_sessionPool.TryDequeue(out var session))
            {
                session.Configure(threshold, minSilenceDurationMs);
                session.Reset();
                System.Diagnostics.Debug.WriteLine("[SileroVAD] Session reused from pool");
                return session;
            }

            System.Diagnostics.Debug.WriteLine("[SileroVAD] New session created");
            return new VadSession(threshold, minSilenceDurationMs);
        }

        /// <summary>
        /// セッションをプールに返却
        /// </summary>
        private static void ReturnSession(VadSession session)
        {
            if (_sessionPool.Count < 5) // プールサイズ制限
            {
                _sessionPool.Enqueue(session);
                System.Diagnostics.Debug.WriteLine("[SileroVAD] Session returned to pool");
            }
        }

        public bool ProcessAudio(byte[] buffer, int bytesRecorded, out bool isSpeechStart, out bool isSpeechEnd)
        {
            isSpeechStart = false;
            isSpeechEnd = false;

            if (_currentSession == null)
                return false;

            try
            {
                var samples = ConvertBytesToFloat(buffer, bytesRecorded);
                _currentSession.AddSamples(samples);

                while (_currentSession.HasWindow())
                {
                    var windowData = _currentSession.GetWindow();
                    var speechProb = RunInference(windowData, _currentSession);
                    var result = _currentSession.ProcessResult(speechProb);

                    if (result.IsSpeechStart) isSpeechStart = true;
                    if (result.IsSpeechEnd) isSpeechEnd = true;
                }

                return _currentSession.IsTriggered;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SileroVAD] ProcessAudio error: {ex.Message}");
                return false;
            }
        }

        private static float RunInference(float[] audioData, VadSession session)
        {
            if (_sharedModel == null)
                return 0f;

            try
            {
                lock (_modelLock)
                {
                    var inputTensor = new DenseTensor<float>(audioData, new[] { 1, audioData.Length });
                    var stateTensor = new DenseTensor<float>(session.GetState(), new[] { 2, 1, 128 });
                    var srTensor = new DenseTensor<long>(new[] { 16000L }, new[] { 1 });

                    var inputs = new List<NamedOnnxValue>
                    {
                        NamedOnnxValue.CreateFromTensor("input", inputTensor),
                        NamedOnnxValue.CreateFromTensor("state", stateTensor),
                        NamedOnnxValue.CreateFromTensor("sr", srTensor)
                    };

                    using var results = _sharedModel.Run(inputs);
                    var output = results.First(r => r.Name == "output").AsEnumerable<float>().First();

                    // 新しいstateをセッションに更新
                    var newState = results.First(r => r.Name == "stateN").AsEnumerable<float>().ToArray();
                    session.UpdateState(newState);

                    return output;
                }
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

        public void UpdateSettings(float threshold, int minSilenceDurationMs)
        {
            _currentSession?.Configure(threshold, minSilenceDurationMs);
            System.Diagnostics.Debug.WriteLine($"[SileroVAD] Settings updated - threshold: {threshold:F3}, silence: {minSilenceDurationMs}ms");
        }

        public void Reset()
        {
            _currentSession?.Reset();
            System.Diagnostics.Debug.WriteLine("[SileroVAD] Session reset");
        }

        public void Dispose()
        {
            if (_currentSession != null)
            {
                ReturnSession(_currentSession);
                _currentSession = null;
            }
        }

        /// <summary>
        /// 共有リソースの解放（アプリケーション終了時）
        /// </summary>
        public static void DisposeSharedResources()
        {
            lock (_modelLock)
            {
                _sharedModel?.Dispose();
                _sharedModel = null;

                while (_sessionPool.TryDequeue(out var session))
                {
                    // セッションは軽量なのでGCに任せる
                }

                System.Diagnostics.Debug.WriteLine("[SileroVAD] Shared resources disposed");
            }
        }

    }

    /// <summary>
    /// VAD処理の個別セッション状態
    /// </summary>
    public class VadSession
    {
        private float _threshold;
        private int _minSilenceDurationMs;
        private int _sampleRate = 16000;
        private int _windowSizeSamples = 512;

        // セッション固有状態
        private float[] _state = new float[2 * 1 * 128];
        private int _samplesSinceLastSpeech;
        private bool _triggered;
        private readonly List<float> _sampleBuffer = new List<float>();

        public bool IsTriggered => _triggered;

        public VadSession(float threshold, int minSilenceDurationMs)
        {
            Configure(threshold, minSilenceDurationMs);
            Reset();
        }

        public void Configure(float threshold, int minSilenceDurationMs)
        {
            _threshold = threshold;
            _minSilenceDurationMs = minSilenceDurationMs;
        }

        public void Reset()
        {
            _state = new float[2 * 1 * 128]; // Silero VAD state
            _samplesSinceLastSpeech = 0;
            _triggered = false;
            _sampleBuffer.Clear();
        }

        public float[] GetState()
        {
            return _state;
        }

        public void UpdateState(float[] newState)
        {
            Array.Copy(newState, _state, Math.Min(newState.Length, _state.Length));
        }

        public void AddSamples(float[] samples)
        {
            _sampleBuffer.AddRange(samples);
        }

        public bool HasWindow()
        {
            return _sampleBuffer.Count >= _windowSizeSamples;
        }

        public float[] GetWindow()
        {
            var windowData = new float[_windowSizeSamples];
            _sampleBuffer.CopyTo(0, windowData, 0, _windowSizeSamples);
            _sampleBuffer.RemoveRange(0, _windowSizeSamples);
            return windowData;
        }

        public VadResult ProcessResult(float speechProb)
        {
            var isSpeech = speechProb >= _threshold;
            var result = new VadResult();

            if (isSpeech && !_triggered)
            {
                _triggered = true;
                result.IsSpeechStart = true;
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
                        result.IsSpeechEnd = true;
                        _samplesSinceLastSpeech = 0;
                        System.Diagnostics.Debug.WriteLine("[SileroVAD] Speech end");
                    }
                }
                else
                {
                    _samplesSinceLastSpeech = 0;
                }
            }

            return result;
        }
    }

    /// <summary>
    /// VAD処理結果
    /// </summary>
    public class VadResult
    {
        public bool IsSpeechStart { get; set; }
        public bool IsSpeechEnd { get; set; }
    }
}