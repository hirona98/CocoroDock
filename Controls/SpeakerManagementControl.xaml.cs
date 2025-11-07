using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using CocoroDock.Services;
using NAudio.Wave;

namespace CocoroDock.Controls
{
    /// <summary>
    /// SpeakerManagementControl.xaml の相互作用ロジック
    /// </summary>
    public partial class SpeakerManagementControl : UserControl
    {
        private SpeakerRecognitionService? _speakerService;
        private WaveInEvent? _recordingDevice;
        private List<byte> _recordingBuffer = new();
        private float _currentThreshold = 0.6f;

        public SpeakerManagementControl()
        {
            InitializeComponent();
            ThresholdSlider.Value = _currentThreshold;
            UpdateThresholdText();
        }

        /// <summary>
        /// SpeakerRecognitionServiceを初期化
        /// </summary>
        public void Initialize(SpeakerRecognitionService speakerService, float threshold)
        {
            _speakerService = speakerService ?? throw new ArgumentNullException(nameof(speakerService));
            _currentThreshold = threshold;
            ThresholdSlider.Value = threshold;
            UpdateThresholdText();
            RefreshSpeakerList();
        }

        /// <summary>
        /// 話者リストを更新
        /// </summary>
        public void RefreshSpeakerList()
        {
            if (_speakerService == null)
                return;

            try
            {
                var speakers = _speakerService.GetRegisteredSpeakers();
                SpeakersListBox.ItemsSource = speakers.Select(s => new
                {
                    speakerId = s.speakerId,
                    speakerName = s.speakerName
                }).ToList();

                System.Diagnostics.Debug.WriteLine($"[SpeakerManagement] Speaker list refreshed: {speakers.Count} speakers");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"話者リストの取得に失敗しました: {ex.Message}", "エラー",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 録音して話者を登録
        /// </summary>
        private async void RecordAndRegisterSpeaker_Click(object sender, RoutedEventArgs e)
        {
            if (_speakerService == null)
            {
                MessageBox.Show("話者識別サービスが初期化されていません", "エラー",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var speakerName = NewSpeakerNameBox.Text.Trim();
            if (string.IsNullOrEmpty(speakerName))
            {
                MessageBox.Show("話者名を入力してください", "エラー",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                // 録音開始
                RecordingStatusText.Text = "録音中... (5秒間)";
                RecordingStatusText.Visibility = Visibility.Visible;

                var audioSample = await RecordAudioAsync(5000); // 5秒

                RecordingStatusText.Text = "処理中...";

                // 話者登録
                var speakerId = Guid.NewGuid().ToString();
                _speakerService.RegisterSpeaker(speakerId, speakerName, audioSample);

                MessageBox.Show($"話者「{speakerName}」を登録しました", "成功",
                    MessageBoxButton.OK, MessageBoxImage.Information);

                NewSpeakerNameBox.Clear();
                RefreshSpeakerList();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"登録エラー: {ex.Message}", "エラー",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                System.Diagnostics.Debug.WriteLine($"[SpeakerManagement] Registration error: {ex.Message}");
            }
            finally
            {
                RecordingStatusText.Visibility = Visibility.Collapsed;
            }
        }

        /// <summary>
        /// 話者を削除
        /// </summary>
        private void DeleteSpeaker_Click(object sender, RoutedEventArgs e)
        {
            if (_speakerService == null)
                return;

            if (sender is not Button button || button.Tag is not string speakerId)
                return;

            try
            {
                var result = MessageBox.Show("この話者を削除しますか?", "確認",
                    MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    _speakerService.DeleteSpeaker(speakerId);
                    RefreshSpeakerList();
                    MessageBox.Show("話者を削除しました", "完了",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"削除エラー: {ex.Message}", "エラー",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 閾値スライダーの値変更
        /// </summary>
        private void ThresholdSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            _currentThreshold = (float)e.NewValue;
            UpdateThresholdText();
        }

        /// <summary>
        /// 閾値のテキスト表示を更新
        /// </summary>
        private void UpdateThresholdText()
        {
            if (ThresholdValueText == null)
                return;

            string description = _currentThreshold switch
            {
                < 0.6f => "寛容（偽陽性のリスクあり）",
                >= 0.6f and < 0.7f => "バランス（推奨）",
                >= 0.7f => "厳格（偽陰性のリスクあり）",
                _ => ""
            };

            ThresholdValueText.Text = $"現在値: {_currentThreshold:F2} - {description}";
        }

        /// <summary>
        /// 現在の閾値を取得
        /// </summary>
        public float GetCurrentThreshold()
        {
            return _currentThreshold;
        }

        /// <summary>
        /// 音声を録音
        /// </summary>
        private Task<byte[]> RecordAudioAsync(int durationMs)
        {
            var tcs = new TaskCompletionSource<byte[]>();

            _recordingBuffer.Clear();
            _recordingDevice = new WaveInEvent
            {
                WaveFormat = new WaveFormat(16000, 16, 1), // 16kHz, 16bit, モノラル
                BufferMilliseconds = 50
            };

            _recordingDevice.DataAvailable += (s, e) =>
            {
                _recordingBuffer.AddRange(e.Buffer.Take(e.BytesRecorded));
            };

            _recordingDevice.StartRecording();

            Task.Delay(durationMs).ContinueWith(_ =>
            {
                _recordingDevice?.StopRecording();
                _recordingDevice?.Dispose();

                // WAVヘッダー追加
                var wavData = AddWavHeader(_recordingBuffer.ToArray());
                tcs.SetResult(wavData);
            });

            return tcs.Task;
        }

        /// <summary>
        /// WAVヘッダーを追加
        /// </summary>
        private byte[] AddWavHeader(byte[] audioData)
        {
            using var memoryStream = new MemoryStream();
            using var writer = new BinaryWriter(memoryStream);

            // RIFF header
            writer.Write(new char[] { 'R', 'I', 'F', 'F' });
            writer.Write(36 + audioData.Length); // ChunkSize
            writer.Write(new char[] { 'W', 'A', 'V', 'E' });

            // fmt sub-chunk
            writer.Write(new char[] { 'f', 'm', 't', ' ' });
            writer.Write(16); // Subchunk1Size (PCM)
            writer.Write((short)1); // AudioFormat (PCM)
            writer.Write((short)1); // NumChannels (Mono)
            writer.Write(16000); // SampleRate
            writer.Write(16000 * 1 * 16 / 8); // ByteRate
            writer.Write((short)(1 * 16 / 8)); // BlockAlign
            writer.Write((short)16); // BitsPerSample

            // data sub-chunk
            writer.Write(new char[] { 'd', 'a', 't', 'a' });
            writer.Write(audioData.Length); // Subchunk2Size
            writer.Write(audioData);

            return memoryStream.ToArray();
        }
    }
}
