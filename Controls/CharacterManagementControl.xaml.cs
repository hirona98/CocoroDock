using CocoroDock.Communication;
using CocoroDock.Services;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace CocoroDock.Controls
{
    /// <summary>
    /// CharacterManagementControl.xaml の相互作用ロジック
    /// </summary>
    public partial class CharacterManagementControl : UserControl
    {
        /// <summary>
        /// 設定が変更されたときに発生するイベント
        /// </summary>
        public event EventHandler? SettingsChanged;

        /// <summary>
        /// キャラクターが変更されたときに発生するイベント
        /// </summary>
        public event EventHandler? CharacterChanged;

        /// <summary>
        /// 現在選択中のキャラクターインデックス
        /// </summary>
        private int _currentCharacterIndex = -1;

        /// <summary>
        /// 読み込み完了フラグ
        /// </summary>
        private bool _isInitialized = false;

        /// <summary>
        /// 通信サービス
        /// </summary>
        private ICommunicationService? _communicationService;

        public CharacterManagementControl()
        {
            InitializeComponent();
            _communicationService = new CommunicationService(AppSettings.Instance);
        }

        /// <summary>
        /// 初期化処理
        /// </summary>
        public void Initialize()
        {
            LoadCharacterList();

            // 選択されたキャラクターの設定をUIに反映
            if (CharacterSelectComboBox.SelectedIndex >= 0)
            {
                _currentCharacterIndex = CharacterSelectComboBox.SelectedIndex;
                UpdateCharacterUI();
            }

            _isInitialized = true;
        }

        /// <summary>
        /// 通信サービスを設定
        /// </summary>
        public void SetCommunicationService(ICommunicationService communicationService)
        {
            _communicationService = communicationService;
        }

        /// <summary>
        /// キャラクターリストを読み込み
        /// </summary>
        private void LoadCharacterList()
        {
            var appSettings = AppSettings.Instance;
            CharacterSelectComboBox.Items.Clear();

            foreach (var character in appSettings.CharacterList)
            {
                CharacterSelectComboBox.Items.Add(character.modelName);
            }

            if (CharacterSelectComboBox.Items.Count > 0 &&
                appSettings.CurrentCharacterIndex >= 0 &&
                appSettings.CurrentCharacterIndex < CharacterSelectComboBox.Items.Count)
            {
                CharacterSelectComboBox.SelectedIndex = appSettings.CurrentCharacterIndex;
            }
        }

        /// <summary>
        /// 現在のキャラクター設定を取得
        /// </summary>
        public CharacterSettings? GetCurrentCharacterSetting()
        {
            if (_currentCharacterIndex < 0 || _currentCharacterIndex >= AppSettings.Instance.CharacterList.Count)
                return null;

            var character = AppSettings.Instance.CharacterList[_currentCharacterIndex];

            // UIから最新の値を取得して設定
            character.modelName = CharacterNameTextBox.Text;
            character.vrmFilePath = VRMFilePathTextBox.Text;
            character.isConvertMToon = ConvertMToonCheckBox.IsChecked ?? false;
            character.isEnableShadowOff = EnableShadowOffCheckBox.IsChecked ?? false;
            character.shadowOffMesh = ShadowOffMeshTextBox.Text;
            character.isUseLLM = IsUseLLMCheckBox.IsChecked ?? false;
            character.apiKey = ApiKeyPasswordBox.Password;
            character.llmModel = LlmModelTextBox.Text;
            character.systemPrompt = SystemPromptTextBox.Text;
            character.isEnableMemory = IsEnableMemoryCheckBox.IsChecked ?? false;
            character.userId = UserIdTextBox.Text;
            character.embeddedApiKey = EmbeddedApiKeyPasswordBox.Password;
            character.embeddedModel = EmbeddedModelTextBox.Text;
            character.isUseSTT = IsUseSTTCheckBox.IsChecked ?? false;
            character.sttEngine = STTEngineComboBox.SelectedItem is ComboBoxItem selectedSttEngine ? selectedSttEngine.Tag?.ToString() ?? "amivoice" : "amivoice";
            character.sttWakeWord = STTWakeWordTextBox.Text;
            character.sttApiKey = STTApiKeyPasswordBox.Password;
            character.isUseTTS = IsUseTTSCheckBox.IsChecked ?? false;
            character.ttsEndpointURL = TTSEndpointURLTextBox.Text;
            character.ttsSperkerID = TTSSperkerIDTextBox.Text;

            return character;
        }

        /// <summary>
        /// 現在のキャラクターインデックスを取得
        /// </summary>
        public int GetCurrentCharacterIndex()
        {
            return _currentCharacterIndex;
        }

        /// <summary>
        /// キャラクター選択変更イベント
        /// </summary>
        private void CharacterSelectComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isInitialized || CharacterSelectComboBox.SelectedIndex < 0)
                return;

            _currentCharacterIndex = CharacterSelectComboBox.SelectedIndex;
            UpdateCharacterUI();

            // キャラクター変更イベントを発生
            CharacterChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// キャラクターUIを更新
        /// </summary>
        private void UpdateCharacterUI()
        {
            if (_currentCharacterIndex < 0 || _currentCharacterIndex >= AppSettings.Instance.CharacterList.Count)
                return;

            var character = AppSettings.Instance.CharacterList[_currentCharacterIndex];

            // 基本設定
            CharacterNameTextBox.Text = character.modelName;
            VRMFilePathTextBox.Text = character.vrmFilePath;
            ConvertMToonCheckBox.IsChecked = character.isConvertMToon;
            EnableShadowOffCheckBox.IsChecked = character.isEnableShadowOff;
            ShadowOffMeshTextBox.Text = character.shadowOffMesh;
            ShadowOffMeshTextBox.IsEnabled = character.isEnableShadowOff;

            // LLM設定
            IsUseLLMCheckBox.IsChecked = character.isUseLLM;
            ApiKeyPasswordBox.Password = character.apiKey;
            LlmModelTextBox.Text = character.llmModel;
            SystemPromptTextBox.Text = character.systemPrompt;

            // 記憶機能
            IsEnableMemoryCheckBox.IsChecked = character.isEnableMemory;
            UserIdTextBox.Text = character.userId;
            EmbeddedApiKeyPasswordBox.Password = character.embeddedApiKey;
            EmbeddedModelTextBox.Text = character.embeddedModel;

            // STT設定
            IsUseSTTCheckBox.IsChecked = character.isUseSTT;

            // STTエンジンComboBox設定
            foreach (ComboBoxItem item in STTEngineComboBox.Items)
            {
                if (item.Tag?.ToString() == character.sttEngine)
                {
                    STTEngineComboBox.SelectedItem = item;
                    break;
                }
            }

            STTWakeWordTextBox.Text = character.sttWakeWord;
            STTApiKeyPasswordBox.Password = character.sttApiKey;

            // TTS設定
            IsUseTTSCheckBox.IsChecked = character.isUseTTS;
            TTSEndpointURLTextBox.Text = character.ttsEndpointURL;
            TTSSperkerIDTextBox.Text = character.ttsSperkerID;

            // 読み取り専用の場合は削除ボタンを無効化
            DeleteCharacterButton.IsEnabled = !character.isReadOnly;
        }

        /// <summary>
        /// キャラクター追加ボタンクリック
        /// </summary>
        private void AddCharacterButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 新規キャラクターの名前を生成
                var newName = "新規キャラクター";

                // 同名のキャラクターが既に存在する場合は番号を付ける
                int characterNumber = 1;
                while (AppSettings.Instance.CharacterList.Any(c => c.modelName == newName))
                {
                    newName = $"新規キャラクター{characterNumber}";
                    characterNumber++;
                }

                var newCharacter = new CharacterSettings
                {
                    modelName = newName,
                    isReadOnly = false
                };

                AppSettings.Instance.CharacterList.Add(newCharacter);
                CharacterSelectComboBox.Items.Add(newCharacter.modelName);
                CharacterSelectComboBox.SelectedIndex = CharacterSelectComboBox.Items.Count - 1;

                // 設定変更イベントを発生
                SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"キャラクター追加エラー: {ex.Message}", "エラー",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// キャラクター削除ボタンクリック
        /// </summary>
        private void DeleteCharacterButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_currentCharacterIndex < 0 || _currentCharacterIndex >= AppSettings.Instance.CharacterList.Count)
                    return;

                var character = AppSettings.Instance.CharacterList[_currentCharacterIndex];
                if (character.isReadOnly)
                {
                    return;
                }

                AppSettings.Instance.CharacterList.RemoveAt(_currentCharacterIndex);
                CharacterSelectComboBox.Items.RemoveAt(_currentCharacterIndex);

                if (CharacterSelectComboBox.Items.Count > 0)
                {
                    CharacterSelectComboBox.SelectedIndex = 0;
                }

                // 設定変更イベントを発生
                SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"キャラクター削除エラー: {ex.Message}", "エラー",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// キャラクター複製ボタンクリック
        /// </summary>
        private void DuplicateCharacterButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_currentCharacterIndex < 0 || _currentCharacterIndex >= AppSettings.Instance.CharacterList.Count)
                    return;

                var sourceCharacter = AppSettings.Instance.CharacterList[_currentCharacterIndex];

                // 複製するキャラクターの名前を生成
                var newName = sourceCharacter.modelName + "_copy";

                // 同名のキャラクターが既に存在する場合は番号を付ける
                int copyNumber = 1;
                while (AppSettings.Instance.CharacterList.Any(c => c.modelName == newName))
                {
                    newName = $"{sourceCharacter.modelName}_copy{copyNumber}";
                    copyNumber++;
                }

                // キャラクター設定をコピー
                var newCharacter = new CharacterSettings
                {
                    modelName = newName,
                    vrmFilePath = sourceCharacter.vrmFilePath,
                    isUseLLM = sourceCharacter.isUseLLM,
                    apiKey = sourceCharacter.apiKey,
                    llmModel = sourceCharacter.llmModel,
                    systemPrompt = sourceCharacter.systemPrompt,
                    isUseTTS = sourceCharacter.isUseTTS,
                    ttsEndpointURL = sourceCharacter.ttsEndpointURL,
                    ttsSperkerID = sourceCharacter.ttsSperkerID,
                    isEnableMemory = sourceCharacter.isEnableMemory,
                    userId = sourceCharacter.userId,
                    embeddedApiKey = sourceCharacter.embeddedApiKey,
                    embeddedModel = sourceCharacter.embeddedModel,
                    isUseSTT = sourceCharacter.isUseSTT,
                    sttModel = sourceCharacter.sttModel,
                    sttEngine = sourceCharacter.sttEngine,
                    sttWakeWord = sourceCharacter.sttWakeWord,
                    sttApiKey = sourceCharacter.sttApiKey,
                    isConvertMToon = sourceCharacter.isConvertMToon,
                    isEnableShadowOff = sourceCharacter.isEnableShadowOff,
                    shadowOffMesh = sourceCharacter.shadowOffMesh,
                    isReadOnly = false
                };

                // リストに追加
                AppSettings.Instance.CharacterList.Add(newCharacter);
                CharacterSelectComboBox.Items.Add(newCharacter.modelName);

                // 新しく追加したキャラクターを選択
                CharacterSelectComboBox.SelectedIndex = CharacterSelectComboBox.Items.Count - 1;

                // 設定変更イベントを発生
                SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"キャラクター複製エラー: {ex.Message}", "エラー",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// VRMファイル選択ボタンクリック
        /// </summary>
        private void BrowseVrmFileButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dialog = new OpenFileDialog
                {
                    Filter = "VRM Files (*.vrm)|*.vrm|All Files (*.*)|*.*",
                    Title = "VRMファイルを選択してください"
                };

                if (dialog.ShowDialog() == true)
                {
                    VRMFilePathTextBox.Text = dialog.FileName;

                    // ファイル名から自動的にキャラクター名を更新（ユーザーが変更可能）
                    if (string.IsNullOrWhiteSpace(CharacterNameTextBox.Text))
                    {
                        CharacterNameTextBox.Text = Path.GetFileNameWithoutExtension(dialog.FileName);
                    }

                    // 設定変更イベントを発生
                    SettingsChanged?.Invoke(this, EventArgs.Empty);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"ファイル選択エラー: {ex.Message}", "エラー",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 影オフチェックボックスのチェック状態変更
        /// </summary>
        private void EnableShadowOffCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            if (ShadowOffMeshTextBox != null)
            {
                ShadowOffMeshTextBox.IsEnabled = true;
            }
        }

        private void EnableShadowOffCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            if (ShadowOffMeshTextBox != null)
            {
                ShadowOffMeshTextBox.IsEnabled = false;
            }
        }

        /// <summary>
        /// ハイパーリンククリック処理
        /// </summary>
        private void Hyperlink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
                e.Handled = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"リンクを開けませんでした: {ex.Message}", "エラー",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}