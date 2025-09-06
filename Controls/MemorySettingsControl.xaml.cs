using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Navigation;
using CocoroDock.Communication;

namespace CocoroDock.Controls
{
    public partial class MemorySettingsControl : UserControl
    {
        private CharacterSettings? _currentCharacter;

        public MemorySettingsControl()
        {
            InitializeComponent();
        }

        public void LoadCharacterSettings(CharacterSettings character)
        {
            _currentCharacter = character;
            if (character != null)
            {
                EmbeddedModelTextBox.Text = character.embeddedModel;
                EmbeddedDimensionTextBox.Text = character.embeddedDimension;
                EmbeddedApiKeyPasswordBox.Text = character.embeddedApiKey;
            }
        }

        public void SaveToCharacterSettings(CharacterSettings character)
        {
            if (character != null)
            {
                character.embeddedModel = EmbeddedModelTextBox.Text;
                character.embeddedDimension = EmbeddedDimensionTextBox.Text;
                character.embeddedApiKey = EmbeddedApiKeyPasswordBox.Text;
            }
        }

        private void EmbeddedApiKeyPasteOverrideButton_Click(object sender, RoutedEventArgs e)
        {
            PasteFromClipboardIntoTextBox(EmbeddedApiKeyPasswordBox);
        }

        private void PasteFromClipboardIntoTextBox(TextBox textBox)
        {
            if (Clipboard.ContainsText())
            {
                textBox.Text = Clipboard.GetText();
            }
        }

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
                e.Handled = true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error opening link: {ex.Message}");
            }
        }
    }
}