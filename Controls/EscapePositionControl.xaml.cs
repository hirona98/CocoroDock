using CocoroDock.Communication;
using CocoroDock.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace CocoroDock.Controls
{
    /// <summary>
    /// 逃げ先座標設定用のViewModelクラス
    /// </summary>
    public class EscapePositionViewModel : INotifyPropertyChanged
    {
        private float _x;
        private float _y;
        private bool _enabled;

        public float X
        {
            get => _x;
            set
            {
                _x = value;
                OnPropertyChanged(nameof(X));
            }
        }

        public float Y
        {
            get => _y;
            set
            {
                _y = value;
                OnPropertyChanged(nameof(Y));
            }
        }

        public bool Enabled
        {
            get => _enabled;
            set
            {
                _enabled = value;
                OnPropertyChanged(nameof(Enabled));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public EscapePosition ToEscapePosition()
        {
            return new EscapePosition { x = X, y = Y, enabled = Enabled };
        }

        public static EscapePositionViewModel FromEscapePosition(EscapePosition position)
        {
            return new EscapePositionViewModel { X = position.x, Y = position.y, Enabled = position.enabled };
        }
    }

    /// <summary>
    /// EscapePositionControl.xaml の相互作用ロジック
    /// </summary>
    public partial class EscapePositionControl : UserControl
    {
        /// <summary>
        /// 逃げ先座標設定用コレクション
        /// </summary>
        public ObservableCollection<EscapePositionViewModel> EscapePositionsCollection { get; set; } = new ObservableCollection<EscapePositionViewModel>();

        /// <summary>
        /// 通信サービス
        /// </summary>
        private readonly ICommunicationService _communicationService;

        /// <summary>
        /// 設定が変更されたときに発生するイベント
        /// </summary>
        public event EventHandler? SettingsChanged;

        public EscapePositionControl()
        {
            InitializeComponent();
            _communicationService = new CommunicationService(AppSettings.Instance);
            InitializeEscapePositions();
        }

        /// <summary>
        /// 逃げ先座標設定の初期化
        /// </summary>
        private void InitializeEscapePositions()
        {
            // ItemsControlのItemsSourceを設定
            EscapePositionsItemsControl.ItemsSource = EscapePositionsCollection;

            // 設定から逃げ先座標を読み込み
            LoadEscapePositionsFromSettings();
        }

        /// <summary>
        /// 設定から逃げ先座標を読み込み
        /// </summary>
        public void LoadEscapePositionsFromSettings()
        {
            EscapePositionsCollection.Clear();

            var appSettings = AppSettings.Instance;
            foreach (var position in appSettings.EscapePositions)
            {
                EscapePositionsCollection.Add(EscapePositionViewModel.FromEscapePosition(position));
            }
        }

        /// <summary>
        /// 現在の逃げ先座標を取得
        /// </summary>
        public List<EscapePosition> GetEscapePositions()
        {
            var escapePositions = new List<EscapePosition>();
            foreach (var position in EscapePositionsCollection)
            {
                if (position.Enabled || position.X != 0 || position.Y != 0)
                {
                    escapePositions.Add(position.ToEscapePosition());
                }
            }
            return escapePositions;
        }

        /// <summary>
        /// 逃げ先座標を設定
        /// </summary>
        public void SetEscapePositions(List<EscapePosition> positions)
        {
            EscapePositionsCollection.Clear();
            foreach (var position in positions)
            {
                EscapePositionsCollection.Add(EscapePositionViewModel.FromEscapePosition(position));
            }
        }

        /// <summary>
        /// 現在位置を追加ボタンクリック
        /// </summary>
        private async void AddEscapePositionButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 最大10箇所まで追加可能
                if (EscapePositionsCollection.Count < 10)
                {
                    float x = 100, y = 100; // デフォルト値

                    try
                    {
                        // CocoroShellから現在位置を取得
                        var response = await _communicationService.GetShellPositionAsync();
                        if (response?.position != null)
                        {
                            x = response.position.x;
                            y = response.position.y;
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"位置取得エラー: {ex.Message}");
                        // エラーの場合はデフォルト値を使用
                    }

                    var newPosition = new EscapePositionViewModel
                    {
                        X = x,
                        Y = y,
                        Enabled = true
                    };
                    EscapePositionsCollection.Add(newPosition);

                    // 設定変更イベントを発生
                    SettingsChanged?.Invoke(this, EventArgs.Empty);
                }
                else
                {
                    MessageBox.Show("逃げ先座標は最大10箇所まで設定できます。", "上限到達",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"座標追加エラー: {ex.Message}", "エラー",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 個別削除ボタンクリック
        /// </summary>
        private void RemoveEscapePosition_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is Button button && button.Tag is EscapePositionViewModel position)
                {
                    EscapePositionsCollection.Remove(position);
                    
                    // 設定変更イベントを発生
                    SettingsChanged?.Invoke(this, EventArgs.Empty);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"座標削除エラー: {ex.Message}", "エラー",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 全削除ボタンクリック
        /// </summary>
        private void ClearEscapePositionsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 確認ダイアログなしで直接削除
                EscapePositionsCollection.Clear();
                
                // 設定変更イベントを発生
                SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"全削除エラー: {ex.Message}", "エラー",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}