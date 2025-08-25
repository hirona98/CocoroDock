using System.Windows;

namespace CocoroDock.Windows
{
    public partial class MemoryDeleteProgressDialog : Window
    {
        public string CharacterName { get; set; } = string.Empty;

        public MemoryDeleteProgressDialog()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            // 詳細情報を表示
            DetailsText.Text = $"キャラクター: {CharacterName}";
        }
    }
}