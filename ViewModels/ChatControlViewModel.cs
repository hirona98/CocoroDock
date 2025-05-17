using System;
using System.Windows.Input;

namespace CocoroDock.ViewModels
{
    public class ChatControlViewModel : ViewModelBase
    {
        private string _messageText = "";

        public string MessageText
        {
            get => _messageText;
            set => SetProperty(ref _messageText, value);
        }

        public ICommand SendMessageCommand { get; }

        public ICommand ClearChatCommand { get; }

        public event EventHandler<string>? MessageSent;

        public event EventHandler? ChatCleared;

        public ChatControlViewModel()
        {
            SendMessageCommand = new RelayCommand(_ => SendMessage(), _ => CanSendMessage());
            ClearChatCommand = new RelayCommand(_ => ClearChat());
        }

        private bool CanSendMessage()
        {
            return !string.IsNullOrWhiteSpace(MessageText);
        }

        private void SendMessage()
        {
            string message = MessageText.Trim();
            if (string.IsNullOrEmpty(message))
                return;

            MessageSent?.Invoke(this, message);

            MessageText = "";
        }

        private void ClearChat()
        {
            ChatCleared?.Invoke(this, EventArgs.Empty);
        }

        public void ReceiveAiMessage(string message)
        {
        }

        public void HandleSystemErrorMessage(string errorMessage)
        {
        }
    }
}
