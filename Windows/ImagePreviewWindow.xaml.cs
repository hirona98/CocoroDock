using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CocoroDock.Windows
{
    /// <summary>
    /// 画像プレビューウィンドウ
    /// </summary>
    public partial class ImagePreviewWindow : Window
    {
        private double _currentZoom = 1.0;
        private const double ZOOM_STEP = 0.1;
        private const double MIN_ZOOM = 0.1;
        private const double MAX_ZOOM = 5.0;
        private bool _isFitToWindow = false;

        public ImagePreviewWindow(BitmapSource imageSource)
        {
            InitializeComponent();
            PreviewImage.Source = imageSource;
            UpdateZoomText();
        }

        /// <summary>
        /// ズームイン
        /// </summary>
        private void ZoomInButton_Click(object sender, RoutedEventArgs e)
        {
            _isFitToWindow = false;
            _currentZoom = Math.Min(_currentZoom + ZOOM_STEP, MAX_ZOOM);
            ApplyZoom();
        }

        /// <summary>
        /// ズームアウト
        /// </summary>
        private void ZoomOutButton_Click(object sender, RoutedEventArgs e)
        {
            _isFitToWindow = false;
            _currentZoom = Math.Max(_currentZoom - ZOOM_STEP, MIN_ZOOM);
            ApplyZoom();
        }

        /// <summary>
        /// ウィンドウに合わせる
        /// </summary>
        private void FitToWindowButton_Click(object sender, RoutedEventArgs e)
        {
            _isFitToWindow = true;
            PreviewImage.Stretch = Stretch.Uniform;
            _currentZoom = 1.0;
            PreviewImage.LayoutTransform = new ScaleTransform(1.0, 1.0);
            UpdateZoomText();
        }

        /// <summary>
        /// 閉じる
        /// </summary>
        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        /// <summary>
        /// ズームを適用
        /// </summary>
        private void ApplyZoom()
        {
            PreviewImage.Stretch = Stretch.None;
            PreviewImage.LayoutTransform = new ScaleTransform(_currentZoom, _currentZoom);
            UpdateZoomText();
        }

        /// <summary>
        /// ズーム表示を更新
        /// </summary>
        private void UpdateZoomText()
        {
            if (_isFitToWindow)
            {
                ZoomPercentageText.Text = "-";
            }
            else
            {
                ZoomPercentageText.Text = $"{(int)(_currentZoom * 100)}%";
            }
        }
    }
}