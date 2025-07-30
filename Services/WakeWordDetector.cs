using System;
using System.Linq;

namespace CocoroDock.Services
{
    public class WakeWordDetector
    {
        private readonly string[] _wakeWords;

        public WakeWordDetector(string wakeWordsConfig)
        {
            if (string.IsNullOrWhiteSpace(wakeWordsConfig))
            {
                _wakeWords = new string[0];
                return;
            }

            _wakeWords = wakeWordsConfig
                .Split(',')
                .Select(w => w.Trim().ToLower())
                .Where(w => !string.IsNullOrEmpty(w))
                .ToArray();

            System.Diagnostics.Debug.WriteLine($"[WakeWordDetector] Initialized with words: {string.Join(", ", _wakeWords)}");
        }

        public bool ContainsWakeWord(string recognizedText)
        {
            if (string.IsNullOrEmpty(recognizedText) || _wakeWords.Length == 0)
                return false;

            string lowerText = recognizedText.ToLower();
            
            bool found = _wakeWords.Any(wakeWord => lowerText.Contains(wakeWord));
            
            if (found)
            {
                var detectedWord = _wakeWords.First(wakeWord => lowerText.Contains(wakeWord));
                System.Diagnostics.Debug.WriteLine($"[WakeWordDetector] Detected wake word: '{detectedWord}' in text: '{recognizedText}'");
            }

            return found;
        }

        public bool ContainsWakeWordExact(string recognizedText)
        {
            if (string.IsNullOrEmpty(recognizedText) || _wakeWords.Length == 0)
                return false;

            // より厳密な単語境界での検出（必要に応じて使用）
            var words = recognizedText
                .ToLower()
                .Split(new char[] { ' ', '、', '。', '！', '？', '\t', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            bool found = _wakeWords.Any(wakeWord => words.Contains(wakeWord));

            if (found)
            {
                var detectedWord = _wakeWords.First(wakeWord => words.Contains(wakeWord));
                System.Diagnostics.Debug.WriteLine($"[WakeWordDetector] Detected exact wake word: '{detectedWord}' in text: '{recognizedText}'");
            }

            return found;
        }

        public string[] GetWakeWords()
        {
            return (string[])_wakeWords.Clone();
        }

        public bool HasWakeWords => _wakeWords.Length > 0;
    }
}