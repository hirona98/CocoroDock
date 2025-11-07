using System;
using System.Linq;
using NAudio.Dsp;

namespace CocoroDock.Services
{
    /// <summary>
    /// Mel-frequency bank (Fbank) 特徴量抽出
    /// WeSpeaker ONNXモデル用
    /// </summary>
    public class FbankExtractor
    {
        private const int SAMPLE_RATE = 16000;
        private const int NUM_MEL_BINS = 80;
        private const float FRAME_LENGTH_MS = 25.0f;
        private const float FRAME_SHIFT_MS = 10.0f;
        private const int FFT_SIZE = 512;

        private readonly int _frameLength;
        private readonly int _frameShift;
        private readonly float[][] _melFilterbank;

        public FbankExtractor()
        {
            _frameLength = (int)(SAMPLE_RATE * FRAME_LENGTH_MS / 1000.0f); // 400 samples
            _frameShift = (int)(SAMPLE_RATE * FRAME_SHIFT_MS / 1000.0f);   // 160 samples

            // メルフィルタバンク行列を事前計算
            _melFilterbank = CreateMelFilterbank(SAMPLE_RATE, FFT_SIZE, NUM_MEL_BINS);
        }

        /// <summary>
        /// 音声データからFbank特徴量を抽出
        /// </summary>
        /// <param name="samples">音声サンプル（-1.0 ~ 1.0の範囲）</param>
        /// <returns>Fbank特徴量 [num_frames, 80]</returns>
        public float[,] ExtractFeatures(float[] samples)
        {
            if (samples == null || samples.Length == 0)
                throw new ArgumentException("音声サンプルが空です", nameof(samples));

            // フレーム数を計算
            int numFrames = (samples.Length - _frameLength) / _frameShift + 1;
            if (numFrames <= 0)
                throw new ArgumentException("音声が短すぎます", nameof(samples));

            var features = new float[numFrames, NUM_MEL_BINS];

            // 各フレームを処理
            for (int frameIdx = 0; frameIdx < numFrames; frameIdx++)
            {
                int startIdx = frameIdx * _frameShift;
                var frame = ExtractFrame(samples, startIdx, _frameLength);

                // ハミング窓を適用
                ApplyHammingWindow(frame);

                // FFT
                var spectrum = ComputePowerSpectrum(frame, FFT_SIZE);

                // メルフィルタバンク適用
                var melEnergies = ApplyMelFilterbank(spectrum, _melFilterbank);

                // 対数変換
                for (int i = 0; i < NUM_MEL_BINS; i++)
                {
                    features[frameIdx, i] = (float)Math.Log(Math.Max(melEnergies[i], 1e-10f));
                }
            }

            // CMN（Cepstral Mean Normalization）
            ApplyCMN(features);

            return features;
        }

        /// <summary>
        /// フレームを抽出
        /// </summary>
        private float[] ExtractFrame(float[] samples, int start, int length)
        {
            var frame = new float[length];
            int end = Math.Min(start + length, samples.Length);
            int copyLength = end - start;

            Array.Copy(samples, start, frame, 0, copyLength);

            // 足りない部分はゼロパディング
            if (copyLength < length)
            {
                Array.Clear(frame, copyLength, length - copyLength);
            }

            return frame;
        }

        /// <summary>
        /// ハミング窓を適用
        /// </summary>
        private void ApplyHammingWindow(float[] frame)
        {
            int N = frame.Length;
            for (int i = 0; i < N; i++)
            {
                float window = 0.54f - 0.46f * (float)Math.Cos(2.0 * Math.PI * i / (N - 1));
                frame[i] *= window;
            }
        }

        /// <summary>
        /// パワースペクトルを計算
        /// </summary>
        private float[] ComputePowerSpectrum(float[] frame, int fftSize)
        {
            // NAudioのFFTを使用
            var complex = new Complex[fftSize];
            for (int i = 0; i < frame.Length && i < fftSize; i++)
            {
                complex[i].X = frame[i];
                complex[i].Y = 0;
            }

            FastFourierTransform.FFT(true, (int)Math.Log(fftSize, 2), complex);

            // パワースペクトル計算（半分のみ使用）
            int spectrumSize = fftSize / 2 + 1;
            var powerSpectrum = new float[spectrumSize];
            for (int i = 0; i < spectrumSize; i++)
            {
                powerSpectrum[i] = complex[i].X * complex[i].X + complex[i].Y * complex[i].Y;
            }

            return powerSpectrum;
        }

        /// <summary>
        /// メルフィルタバンクを適用
        /// </summary>
        private float[] ApplyMelFilterbank(float[] powerSpectrum, float[][] filterbank)
        {
            var melEnergies = new float[NUM_MEL_BINS];

            for (int i = 0; i < NUM_MEL_BINS; i++)
            {
                float energy = 0;
                for (int j = 0; j < powerSpectrum.Length; j++)
                {
                    energy += powerSpectrum[j] * filterbank[i][j];
                }
                melEnergies[i] = energy;
            }

            return melEnergies;
        }

        /// <summary>
        /// CMN（Cepstral Mean Normalization）を適用
        /// </summary>
        private void ApplyCMN(float[,] features)
        {
            int numFrames = features.GetLength(0);
            int numBins = features.GetLength(1);

            // 各次元の平均を計算
            var means = new float[numBins];
            for (int bin = 0; bin < numBins; bin++)
            {
                float sum = 0;
                for (int frame = 0; frame < numFrames; frame++)
                {
                    sum += features[frame, bin];
                }
                means[bin] = sum / numFrames;
            }

            // 平均を引く
            for (int frame = 0; frame < numFrames; frame++)
            {
                for (int bin = 0; bin < numBins; bin++)
                {
                    features[frame, bin] -= means[bin];
                }
            }
        }

        /// <summary>
        /// メルフィルタバンク行列を作成
        /// </summary>
        private float[][] CreateMelFilterbank(int sampleRate, int fftSize, int numMelBins)
        {
            int spectrumSize = fftSize / 2 + 1;
            var filterbank = new float[numMelBins][];
            for (int i = 0; i < numMelBins; i++)
            {
                filterbank[i] = new float[spectrumSize];
            }

            // メル尺度の変換
            float melMin = HzToMel(0);
            float melMax = HzToMel(sampleRate / 2.0f);

            // メルフィルタバンクの中心周波数
            var melPoints = new float[numMelBins + 2];
            for (int i = 0; i < melPoints.Length; i++)
            {
                float mel = melMin + (melMax - melMin) * i / (numMelBins + 1);
                melPoints[i] = MelToHz(mel);
            }

            // 周波数ビンのインデックスに変換
            var binPoints = new int[numMelBins + 2];
            for (int i = 0; i < binPoints.Length; i++)
            {
                binPoints[i] = (int)Math.Floor((fftSize + 1) * melPoints[i] / sampleRate);
            }

            // 三角フィルタを作成
            for (int i = 0; i < numMelBins; i++)
            {
                int leftBin = binPoints[i];
                int centerBin = binPoints[i + 1];
                int rightBin = binPoints[i + 2];

                // 左側の傾斜
                for (int j = leftBin; j < centerBin && j < spectrumSize; j++)
                {
                    filterbank[i][j] = (float)(j - leftBin) / (centerBin - leftBin);
                }

                // 右側の傾斜
                for (int j = centerBin; j < rightBin && j < spectrumSize; j++)
                {
                    filterbank[i][j] = (float)(rightBin - j) / (rightBin - centerBin);
                }
            }

            return filterbank;
        }

        /// <summary>
        /// HzをMelに変換
        /// </summary>
        private float HzToMel(float hz)
        {
            return 2595.0f * (float)Math.Log10(1.0f + hz / 700.0f);
        }

        /// <summary>
        /// MelをHzに変換
        /// </summary>
        private float MelToHz(float mel)
        {
            return 700.0f * ((float)Math.Pow(10.0f, mel / 2595.0f) - 1.0f);
        }
    }
}
