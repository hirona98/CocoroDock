using CocoroDock.Communication;
using CocoroDock.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace CocoroDock.ViewModels
{
    public class AdminWindowViewModel : ViewModelBase
    {
        private readonly AppSettings _appSettings;
        private readonly CommunicationService? _communicationService;
        private string _aiExecutablePath = "";
        private int _webSocketPort;
        private bool _autoStartAi;
        private ObservableCollection<CharacterViewModel> _characters = new();
        private CharacterViewModel? _selectedCharacter;
        private bool _isLoading;
        private string _statusMessage = "";

        public string AiExecutablePath
        {
            get => _aiExecutablePath;
            set => SetProperty(ref _aiExecutablePath, value);
        }

        public int WebSocketPort
        {
            get => _webSocketPort;
            set => SetProperty(ref _webSocketPort, value);
        }

        public bool AutoStartAi
        {
            get => _autoStartAi;
            set => SetProperty(ref _autoStartAi, value);
        }

        public ObservableCollection<CharacterViewModel> Characters
        {
            get => _characters;
            set => SetProperty(ref _characters, value);
        }

        public CharacterViewModel? SelectedCharacter
        {
            get => _selectedCharacter;
            set => SetProperty(ref _selectedCharacter, value);
        }

        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        public ICommand SaveSettingsCommand { get; }

        public ICommand BrowseAiFileCommand { get; }

        public ICommand AddCharacterCommand { get; }

        public ICommand RemoveCharacterCommand { get; }

        public AdminWindowViewModel(CommunicationService? communicationService = null)
        {
            _appSettings = AppSettings.Instance;
            _communicationService = communicationService;

            SaveSettingsCommand = new RelayCommand(_ => SaveSettings());
            BrowseAiFileCommand = new RelayCommand(_ => BrowseAiFile());
            AddCharacterCommand = new RelayCommand(_ => AddCharacter());
            RemoveCharacterCommand = new RelayCommand(_ => RemoveSelectedCharacter(), _ => CanRemoveCharacter());

            LoadSettings();
        }

        private void LoadSettings()
        {
            try
            {
                IsLoading = true;

                AiExecutablePath = _appSettings.AiExecutablePath;
                WebSocketPort = _appSettings.CocoroDockPort;
                AutoStartAi = _appSettings.AutoStartAi;

                Characters.Clear();
                if (_appSettings.CharacterList != null)
                {
                    foreach (var character in _appSettings.CharacterList)
                    {
                        Characters.Add(new CharacterViewModel(character));
                    }
                }

                if (Characters.Count > 0)
                {
                    SelectedCharacter = Characters[0];
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"設定読み込みエラー: {ex.Message}";
                Debug.WriteLine($"設定読み込みエラー: {ex.Message}");
            }
            finally
            {
                IsLoading = false;
            }
        }

        private void SaveSettings()
        {
            try
            {
                _appSettings.AiExecutablePath = AiExecutablePath;
                _appSettings.CocoroDockPort = WebSocketPort;
                _appSettings.AutoStartAi = AutoStartAi;

                _appSettings.CharacterList = Characters.Select(c => c.ToCharacterSettings()).ToList();

                _appSettings.SaveSettings();

                if (_communicationService != null)
                {
                    var configSettings = new ConfigSettings
                    {
                        CharacterList = _appSettings.CharacterList
                    };

                    Task.Run(async () =>
                    {
                        try
                        {
                            await _communicationService.UpdateConfigAsync(configSettings);
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"設定更新通知エラー: {ex.Message}");
                        }
                    });
                }

                StatusMessage = "設定を保存しました";
                MessageBox.Show("設定を保存しました", "保存完了", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                StatusMessage = $"設定保存エラー: {ex.Message}";
                Debug.WriteLine($"設定保存エラー: {ex.Message}");
                MessageBox.Show($"設定保存エラー: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BrowseAiFile()
        {
            try
            {
                var dialog = new Microsoft.Win32.OpenFileDialog
                {
                    Filter = "実行ファイル (*.exe)|*.exe|すべてのファイル (*.*)|*.*",
                    Title = "AIの実行ファイルを選択"
                };

                if (dialog.ShowDialog() == true)
                {
                    AiExecutablePath = dialog.FileName;
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"ファイル選択エラー: {ex.Message}";
                Debug.WriteLine($"ファイル選択エラー: {ex.Message}");
            }
        }

        private void AddCharacter()
        {
            var newCharacter = new CharacterViewModel(new CharacterSettings
            {
                Name = "新しいキャラクター",
                SystemPrompt = "",
                LlmModel = "openai/gpt-3.5-turbo",
                Temperature = 0.7,
                MaxTokens = 1000
            });

            Characters.Add(newCharacter);
            SelectedCharacter = newCharacter;
        }

        private void RemoveSelectedCharacter()
        {
            if (SelectedCharacter != null)
            {
                Characters.Remove(SelectedCharacter);
                if (Characters.Count > 0)
                {
                    SelectedCharacter = Characters[0];
                }
                else
                {
                    SelectedCharacter = null;
                }
            }
        }

        private bool CanRemoveCharacter()
        {
            return SelectedCharacter != null && Characters.Count > 1;
        }
    }

    public class CharacterViewModel : ViewModelBase
    {
        private string _name = "";
        private string _systemPrompt = "";
        private string _llmModel = "";
        private double _temperature;
        private int _maxTokens;

        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        public string SystemPrompt
        {
            get => _systemPrompt;
            set => SetProperty(ref _systemPrompt, value);
        }

        public string LlmModel
        {
            get => _llmModel;
            set => SetProperty(ref _llmModel, value);
        }

        public double Temperature
        {
            get => _temperature;
            set => SetProperty(ref _temperature, value);
        }

        public int MaxTokens
        {
            get => _maxTokens;
            set => SetProperty(ref _maxTokens, value);
        }

        public CharacterViewModel(CharacterSettings settings)
        {
            Name = settings.Name;
            SystemPrompt = settings.SystemPrompt;
            LlmModel = settings.LlmModel;
            Temperature = settings.Temperature;
            MaxTokens = settings.MaxTokens;
        }

        public CharacterSettings ToCharacterSettings()
        {
            return new CharacterSettings
            {
                Name = Name,
                SystemPrompt = SystemPrompt,
                LlmModel = LlmModel,
                Temperature = Temperature,
                MaxTokens = MaxTokens
            };
        }
    }
}
