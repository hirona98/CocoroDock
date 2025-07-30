using System;
using System.Threading;

namespace CocoroDock.Services
{
    public enum VoiceRecognitionState
    {
        SLEEPING,    // ウェイクアップワード待ち
        ACTIVE,      // 会話モード（CocoroAIに送信）
        PROCESSING   // 音声認識処理中
    }

    public class VoiceRecognitionStateMachine
    {
        private VoiceRecognitionState _currentState = VoiceRecognitionState.SLEEPING;  // ウェイクアップワード待ち
        private Timer? _timeoutTimer;
        private readonly int _activeTimeoutMs;
        private readonly WakeWordDetector _wakeWordDetector;
        private readonly object _lockObject = new object();

        public event Action<string>? OnRecognizedText;
        public event Action<VoiceRecognitionState>? OnStateChanged;

        public VoiceRecognitionState CurrentState
        {
            get
            {
                lock (_lockObject)
                {
                    return _currentState;
                }
            }
        }

        public VoiceRecognitionStateMachine(string wakeWords, int activeTimeoutMs = 60000)
        {
            _activeTimeoutMs = activeTimeoutMs;
            _wakeWordDetector = new WakeWordDetector(wakeWords);
        }

        public void ProcessRecognitionResult(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;

            lock (_lockObject)
            {
                System.Diagnostics.Debug.WriteLine($"[VoiceRecognition] State: {_currentState}, Text: {text}");
                switch (_currentState)
                {
                    case VoiceRecognitionState.SLEEPING:
                        if (_wakeWordDetector.ContainsWakeWord(text))
                        {
                            TransitionTo(VoiceRecognitionState.ACTIVE);
                            OnRecognizedText?.Invoke(text); // ウェイクアップワード含む発話も送信
                        }
                        break;

                    case VoiceRecognitionState.ACTIVE:
                        OnRecognizedText?.Invoke(text); // 全て送信（ウェイクワードも含む）
                        ResetTimeoutTimer(); // タイマーリセット
                        break;

                    case VoiceRecognitionState.PROCESSING:
                        // PROCESSING状態では何もしない（一時的な状態）
                        break;
                }
            }
        }

        public void TransitionTo(VoiceRecognitionState newState)
        {
            lock (_lockObject)
            {
                if (_currentState == newState)
                    return;

                var oldState = _currentState;
                _currentState = newState;
                switch (newState)
                {
                    case VoiceRecognitionState.ACTIVE:
                        StartTimeoutTimer();
                        break;

                    case VoiceRecognitionState.SLEEPING:
                        StopTimeoutTimer();
                        break;

                    case VoiceRecognitionState.PROCESSING:
                        // 処理中はタイマーを一時停止
                        break;
                }

                OnStateChanged?.Invoke(newState);
            }
        }

        private void StartTimeoutTimer()
        {
            StopTimeoutTimer();
            _timeoutTimer = new Timer(OnTimeout, null, _activeTimeoutMs, Timeout.Infinite);
        }

        private void ResetTimeoutTimer()
        {
            if (_currentState == VoiceRecognitionState.ACTIVE)
            {
                StartTimeoutTimer();
            }
        }

        private void StopTimeoutTimer()
        {
            _timeoutTimer?.Dispose();
            _timeoutTimer = null;
        }

        private void OnTimeout(object? state)
        {
            TransitionTo(VoiceRecognitionState.SLEEPING);
        }

        public void Dispose()
        {
            StopTimeoutTimer();
        }
    }
}