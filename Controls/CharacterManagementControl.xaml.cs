using CocoroDock.Communication;
using CocoroDock.Services;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

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
        /// 一時的なsystemPromptテキスト（OK押下まで保留）
        /// </summary>
        private string _tempSystemPromptText = string.Empty;

        /// <summary>
        /// 全キャラクターのシステムプロンプト内容を保持（キャラクターインデックス -> プロンプト内容）
        /// </summary>
        private Dictionary<int, string> _allCharacterSystemPrompts = new Dictionary<int, string>();

        /// <summary>
        /// キャンセル時復元用のオリジナルシステムプロンプト内容（キャラクターインデックス -> プロンプト内容）
        /// </summary>
        private Dictionary<int, string> _originalCharacterSystemPrompts = new Dictionary<int, string>();

        /// <summary>
        /// OK押下時に削除予定のシステムプロンプトファイルパス一覧
        /// </summary>
        private List<string> _filesToDelete = new List<string>();

        /// <summary>
        /// 通信サービス
        /// </summary>
        private ICommunicationService? _communicationService;

        /// <summary>
        /// キャラクター名変更のデバウンス用タイマー
        /// </summary>
        private DispatcherTimer? _characterNameChangeTimer;

        /// <summary>
        /// デバウンス遅延時間（ミリ秒）
        /// </summary>
        private const int CHARACTER_NAME_DEBOUNCE_DELAY_MS = 200;

        public CharacterManagementControl()
        {
            InitializeComponent();

            // キャラクター名変更用のデバウンスタイマーを初期化
            _characterNameChangeTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(CHARACTER_NAME_DEBOUNCE_DELAY_MS)
            };
            _characterNameChangeTimer.Tick += CharacterNameChangeTimer_Tick;

            // Base URLのプレースホルダー制御イベントを設定
            BaseUrlTextBox.TextChanged += BaseUrlTextBox_TextChanged;
            BaseUrlTextBox.GotFocus += BaseUrlTextBox_GotFocus;

            // SystemPromptTextBoxのテキスト変更イベントを設定
            SystemPromptTextBox.TextChanged += SystemPromptTextBox_TextChanged;
            BaseUrlTextBox.LostFocus += BaseUrlTextBox_LostFocus;
        }

        /// <summary>
        /// APIキー欄の「上書き貼付け」ボタンクリック
        /// </summary>
        private void ApiKeyPasteOverrideButton_Click(object sender, RoutedEventArgs e)
        {
            PasteFromClipboardIntoTextBox(ApiKeyPasswordBox);
        }

        private void VisionApiKeyPasteOverrideButton_Click(object sender, RoutedEventArgs e)
        {
            PasteFromClipboardIntoTextBox(VisionApiKeyPasswordBox);
        }


        private void AivisCloudApiKeyPasteOverrideButton_Click(object sender, RoutedEventArgs e)
        {
            PasteFromClipboardIntoTextBox(AivisCloudApiKeyPasswordBox);
        }

        private void STTApiKeyPasteOverrideButton_Click(object sender, RoutedEventArgs e)
        {
            PasteFromClipboardIntoTextBox(STTApiKeyPasswordBox);
        }

        private void PasteFromClipboardIntoTextBox(TextBox target)
        {
            try
            {
                if (Clipboard.ContainsText())
                {
                    var text = Clipboard.GetText();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        target.Text = text.Trim();
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Clipboard paste failed: {ex}");
            }
        }

        /// <summary>
        /// 初期化処理
        /// </summary>
        public void Initialize()
        {
            LoadCharacterList();

            // 全キャラクターのシステムプロンプト内容を初期化時に読み込む
            LoadAllCharacterSystemPrompts();

            // 選択されたキャラクターの設定をUIに反映
            if (CharacterSelectComboBox.SelectedIndex >= 0)
            {
                _currentCharacterIndex = CharacterSelectComboBox.SelectedIndex;
                UpdateCharacterUI();
            }

            _isInitialized = true;
        }

        /// <summary>
        /// 全キャラクターのシステムプロンプト内容を読み込み
        /// </summary>
        private void LoadAllCharacterSystemPrompts()
        {
            _allCharacterSystemPrompts.Clear();
            _originalCharacterSystemPrompts.Clear();

            var appSettings = AppSettings.Instance;
            for (int i = 0; i < appSettings.CharacterList.Count; i++)
            {
                var character = appSettings.CharacterList[i];
                string promptContent = !string.IsNullOrEmpty(character.systemPromptFilePath)
                    ? appSettings.LoadSystemPrompt(character.systemPromptFilePath)
                    : string.Empty;

                _allCharacterSystemPrompts[i] = promptContent;
                _originalCharacterSystemPrompts[i] = promptContent; // バックアップ作成

                Debug.WriteLine($"初期化: キャラクター {i} ({character.modelName}) のプロンプト読み込み完了");
            }

            Debug.WriteLine($"全 {appSettings.CharacterList.Count} キャラクターのシステムプロンプト初期化完了");
        }

        /// <summary>
        /// キャラクターリストの変更後に辞書のインデックスを再構築
        /// </summary>
        private void RebuildCharacterSystemPromptsDictionaries()
        {
            var tempAllPrompts = new Dictionary<int, string>(_allCharacterSystemPrompts);
            var tempOriginalPrompts = new Dictionary<int, string>(_originalCharacterSystemPrompts);

            _allCharacterSystemPrompts.Clear();
            _originalCharacterSystemPrompts.Clear();

            // 現在のCharacterListの順序に合わせて辞書を再構築
            for (int i = 0; i < AppSettings.Instance.CharacterList.Count; i++)
            {
                // 削除されたキャラクターのインデックスを考慮して、適切な内容を設定
                if (i < tempAllPrompts.Count && tempAllPrompts.ContainsKey(i))
                {
                    _allCharacterSystemPrompts[i] = tempAllPrompts[i];
                    _originalCharacterSystemPrompts[i] = tempOriginalPrompts.ContainsKey(i) ? tempOriginalPrompts[i] : tempAllPrompts[i];
                }
                else
                {
                    // 新規追加されたキャラクターまたは削除により詰められたキャラクター
                    var character = AppSettings.Instance.CharacterList[i];
                    string promptContent = !string.IsNullOrEmpty(character.systemPromptFilePath)
                        ? AppSettings.Instance.LoadSystemPrompt(character.systemPromptFilePath)
                        : string.Empty;

                    _allCharacterSystemPrompts[i] = promptContent;
                    _originalCharacterSystemPrompts[i] = promptContent;
                }
            }

            Debug.WriteLine($"辞書を再構築しました: {AppSettings.Instance.CharacterList.Count} キャラクター");
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

            // ItemsSourceを使用
            CharacterSelectComboBox.ItemsSource = appSettings.CharacterList;

            if (appSettings.CharacterList.Count > 0 &&
                appSettings.CurrentCharacterIndex >= 0 &&
                appSettings.CurrentCharacterIndex < appSettings.CharacterList.Count)
            {
                CharacterSelectComboBox.SelectedIndex = appSettings.CurrentCharacterIndex;
            }
        }

        /// <summary>
        /// UI上の現在のキャラクター設定を取得（UIから値を読み取ってディープコピーを返却）
        /// </summary>
        public CharacterSettings? GetCurrentCharacterSettingFromUI()
        {
            if (_currentCharacterIndex < 0 || _currentCharacterIndex >= AppSettings.Instance.CharacterList.Count)
                return null;

            // 既存のキャラクター設定のディープコピーを作成
            var originalCharacter = AppSettings.Instance.CharacterList[_currentCharacterIndex];
            var character = originalCharacter.DeepCopy();

            // UIから最新の値を取得してコピーに設定
            character.modelName = CharacterNameTextBox.Text;
            character.vrmFilePath = VRMFilePathTextBox.Text;
            character.isConvertMToon = ConvertMToonCheckBox.IsChecked ?? false;
            character.isEnableShadowOff = EnableShadowOffCheckBox.IsChecked ?? false;
            character.shadowOffMesh = ShadowOffMeshTextBox.Text;
            character.isUseLLM = IsUseLLMCheckBox.IsChecked ?? false;
            character.apiKey = ApiKeyPasswordBox.Text;
            character.llmModel = LlmModelTextBox.Text;
            if (int.TryParse(MaxTurnsWindowTextBox.Text, out int maxTurns))
                character.max_turns_window = maxTurns;
            character.localLLMBaseUrl = BaseUrlTextBox.Text;
            // 画像分析用設定
            character.visionApiKey = VisionApiKeyPasswordBox.Text;
            character.visionModel = VisionModelTextBox.Text;
            // systemPromptはOK押下時まで一時保存（ファイル生成は後で）
            _tempSystemPromptText = SystemPromptTextBox.Text;

            // systemPromptFilePathの準備（実際のファイル生成はしない）
            if (string.IsNullOrEmpty(character.systemPromptFilePath))
            {
                character.systemPromptFilePath = AppSettings.Instance.GenerateSystemPromptFilePath(character.modelName);
            }
            else
            {
                // modelName変更時はファイル名も更新（実際のファイル移動はしない）
                var newFileName = $"{character.modelName}_{AppSettings.Instance.ExtractUuidFromFileName(character.systemPromptFilePath)}.txt";
                character.systemPromptFilePath = newFileName;
            }
            character.memoryId = MemoryIdTextBox.Text;
            character.isUseSTT = IsUseSTTCheckBox.IsChecked ?? false;
            character.sttEngine = STTEngineComboBox.SelectedItem is ComboBoxItem selectedSttEngine ? selectedSttEngine.Tag?.ToString() ?? "amivoice" : "amivoice";
            character.sttWakeWord = STTWakeWordTextBox.Text;
            character.sttApiKey = STTApiKeyPasswordBox.Text;
            character.isUseTTS = IsUseTTSCheckBox.IsChecked ?? false;

            // TTSエンジンタイプ
            character.ttsType = TTSEngineComboBox.SelectedItem is ComboBoxItem selectedTtsEngine ? selectedTtsEngine.Tag?.ToString() ?? "voicevox" : "voicevox";

            // VOICEVOX詳細設定
            character.voicevoxConfig.endpointUrl = VoicevoxEndpointUrlTextBox.Text;
            if (int.TryParse(VoicevoxSpeakerIdTextBox.Text, out int voicevoxSpeakerId))
                character.voicevoxConfig.speakerId = voicevoxSpeakerId;
            character.voicevoxConfig.speedScale = (float)VoicevoxSpeedScaleSlider.Value;
            character.voicevoxConfig.pitchScale = (float)VoicevoxPitchScaleSlider.Value;
            character.voicevoxConfig.intonationScale = (float)VoicevoxIntonationScaleSlider.Value;
            character.voicevoxConfig.volumeScale = (float)VoicevoxVolumeScaleSlider.Value;
            character.voicevoxConfig.prePhonemeLength = (float)VoicevoxPrePhonemeLengthSlider.Value;
            character.voicevoxConfig.postPhonemeLength = (float)VoicevoxPostPhonemeLengthSlider.Value;

            // サンプリングレート設定
            if (VoicevoxOutputSamplingRateComboBox.SelectedItem is ComboBoxItem selectedSampleRate &&
                int.TryParse(selectedSampleRate.Tag?.ToString(), out int samplingRate))
                character.voicevoxConfig.outputSamplingRate = samplingRate;

            character.voicevoxConfig.outputStereo = VoicevoxOutputStereoCheckBox.IsChecked ?? false;

            // Style-Bert-VITS2設定
            character.styleBertVits2Config.endpointUrl = SBV2EndpointUrlTextBox.Text;
            character.styleBertVits2Config.modelName = SBV2ModelNameTextBox.Text;
            if (int.TryParse(SBV2ModelIdTextBox.Text, out int modelId))
                character.styleBertVits2Config.modelId = modelId;
            character.styleBertVits2Config.speakerName = SBV2SpeakerNameTextBox.Text;
            if (int.TryParse(SBV2SpeakerIdTextBox.Text, out int speakerId))
                character.styleBertVits2Config.speakerId = speakerId;
            character.styleBertVits2Config.style = SBV2StyleTextBox.Text;
            if (float.TryParse(SBV2StyleWeightTextBox.Text, out float styleWeight))
                character.styleBertVits2Config.styleWeight = styleWeight;
            character.styleBertVits2Config.language = SBV2LanguageTextBox.Text;
            if (float.TryParse(SBV2SdpRatioTextBox.Text, out float sdpRatio))
                character.styleBertVits2Config.sdpRatio = sdpRatio;
            if (float.TryParse(SBV2NoiseTextBox.Text, out float noise))
                character.styleBertVits2Config.noise = noise;
            if (float.TryParse(SBV2NoiseWTextBox.Text, out float noiseW))
                character.styleBertVits2Config.noiseW = noiseW;
            if (float.TryParse(SBV2LengthTextBox.Text, out float length))
                character.styleBertVits2Config.length = length;
            character.styleBertVits2Config.autoSplit = SBV2AutoSplitCheckBox.IsChecked ?? true;
            if (float.TryParse(SBV2SplitIntervalTextBox.Text, out float splitInterval))
                character.styleBertVits2Config.splitInterval = splitInterval;

            // AivisCloud設定
            character.aivisCloudConfig.endpointUrl = String.Empty; // AivisCloudのエンドポイントURLはCocoroShellで設定
            character.aivisCloudConfig.apiKey = AivisCloudApiKeyPasswordBox.Text;
            character.aivisCloudConfig.modelUuid = AivisCloudModelUuidTextBox.Text;
            character.aivisCloudConfig.speakerUuid = AivisCloudSpeakerUuidTextBox.Text;
            if (int.TryParse(AivisCloudStyleIdTextBox.Text, out int styleId))
                character.aivisCloudConfig.styleId = styleId;
            if (float.TryParse(AivisCloudSpeakingRateTextBox.Text, out float speakingRate))
                character.aivisCloudConfig.speakingRate = speakingRate;
            if (float.TryParse(AivisCloudEmotionalIntensityTextBox.Text, out float emotionalIntensity))
                character.aivisCloudConfig.emotionalIntensity = emotionalIntensity;
            if (float.TryParse(AivisCloudTempoDynamicsTextBox.Text, out float tempoDynamics))
                character.aivisCloudConfig.tempoDynamics = tempoDynamics;
            if (float.TryParse(AivisCloudVolumeTextBox.Text, out float volume))
                character.aivisCloudConfig.volume = volume;

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

            // 前のキャラクターの内容を保存
            if (_currentCharacterIndex >= 0 && _currentCharacterIndex < AppSettings.Instance.CharacterList.Count)
            {
                _allCharacterSystemPrompts[_currentCharacterIndex] = SystemPromptTextBox.Text;
                Debug.WriteLine($"キャラクター切り替え: インデックス {_currentCharacterIndex} の内容を保存");
            }

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
            ApiKeyPasswordBox.Text = character.apiKey;
            LlmModelTextBox.Text = character.llmModel;
            MaxTurnsWindowTextBox.Text = character.max_turns_window.ToString();
            BaseUrlTextBox.Text = character.localLLMBaseUrl;
            UpdateBaseUrlPlaceholder(); // プレースホルダー更新

            // 画像分析用設定
            VisionApiKeyPasswordBox.Text = character.visionApiKey;
            VisionModelTextBox.Text = character.visionModel;

            // systemPromptは必ず辞書から読み込む（初期化時に全キャラクター分読み込み済み）
            string promptText = _allCharacterSystemPrompts.ContainsKey(_currentCharacterIndex)
                ? _allCharacterSystemPrompts[_currentCharacterIndex]
                : string.Empty;

            SystemPromptTextBox.Text = promptText;
            _tempSystemPromptText = promptText; // 一時保存も初期化

            Debug.WriteLine($"キャラクター復元: インデックス {_currentCharacterIndex} の内容を辞書から取得");

            // 記憶機能
            MemoryIdTextBox.Text = character.memoryId;

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
            STTApiKeyPasswordBox.Text = character.sttApiKey;

            // TTS設定
            IsUseTTSCheckBox.IsChecked = character.isUseTTS;

            // VOICEVOX詳細設定の読み込み
            VoicevoxEndpointUrlTextBox.Text = character.voicevoxConfig.endpointUrl;
            VoicevoxSpeakerIdTextBox.Text = character.voicevoxConfig.speakerId.ToString();
            VoicevoxSpeedScaleSlider.Value = character.voicevoxConfig.speedScale;
            VoicevoxPitchScaleSlider.Value = character.voicevoxConfig.pitchScale;
            VoicevoxIntonationScaleSlider.Value = character.voicevoxConfig.intonationScale;
            VoicevoxVolumeScaleSlider.Value = character.voicevoxConfig.volumeScale;
            VoicevoxPrePhonemeLengthSlider.Value = character.voicevoxConfig.prePhonemeLength;
            VoicevoxPostPhonemeLengthSlider.Value = character.voicevoxConfig.postPhonemeLength;
            VoicevoxOutputStereoCheckBox.IsChecked = character.voicevoxConfig.outputStereo;

            // サンプリングレート設定
            foreach (ComboBoxItem item in VoicevoxOutputSamplingRateComboBox.Items)
            {
                if (item.Tag?.ToString() == character.voicevoxConfig.outputSamplingRate.ToString())
                {
                    VoicevoxOutputSamplingRateComboBox.SelectedItem = item;
                    break;
                }
            }

            // TTSエンジンComboBox設定
            foreach (ComboBoxItem item in TTSEngineComboBox.Items)
            {
                if (item.Tag?.ToString() == character.ttsType)
                {
                    TTSEngineComboBox.SelectedItem = item;
                    break;
                }
            }

            // Style-Bert-VITS2設定の読み込み
            SBV2EndpointUrlTextBox.Text = character.styleBertVits2Config.endpointUrl;
            SBV2ModelNameTextBox.Text = character.styleBertVits2Config.modelName;
            SBV2ModelIdTextBox.Text = character.styleBertVits2Config.modelId.ToString();
            SBV2SpeakerNameTextBox.Text = character.styleBertVits2Config.speakerName;
            SBV2SpeakerIdTextBox.Text = character.styleBertVits2Config.speakerId.ToString();
            SBV2StyleTextBox.Text = character.styleBertVits2Config.style;
            SBV2StyleWeightTextBox.Text = character.styleBertVits2Config.styleWeight.ToString("F1");
            SBV2LanguageTextBox.Text = character.styleBertVits2Config.language;
            SBV2SdpRatioTextBox.Text = character.styleBertVits2Config.sdpRatio.ToString("F1");
            SBV2NoiseTextBox.Text = character.styleBertVits2Config.noise.ToString("F1");
            SBV2NoiseWTextBox.Text = character.styleBertVits2Config.noiseW.ToString("F1");
            SBV2LengthTextBox.Text = character.styleBertVits2Config.length.ToString("F1");
            SBV2AutoSplitCheckBox.IsChecked = character.styleBertVits2Config.autoSplit;
            SBV2SplitIntervalTextBox.Text = character.styleBertVits2Config.splitInterval.ToString("F1");

            // AivisCloud設定の読み込み
            AivisCloudApiKeyPasswordBox.Text = character.aivisCloudConfig.apiKey;
            AivisCloudModelUuidTextBox.Text = character.aivisCloudConfig.modelUuid;
            AivisCloudSpeakerUuidTextBox.Text = character.aivisCloudConfig.speakerUuid;
            AivisCloudStyleIdTextBox.Text = character.aivisCloudConfig.styleId.ToString();
            AivisCloudSpeakingRateTextBox.Text = character.aivisCloudConfig.speakingRate.ToString("F1");
            AivisCloudEmotionalIntensityTextBox.Text = character.aivisCloudConfig.emotionalIntensity.ToString("F1");
            AivisCloudTempoDynamicsTextBox.Text = character.aivisCloudConfig.tempoDynamics.ToString("F1");
            AivisCloudVolumeTextBox.Text = character.aivisCloudConfig.volume.ToString("F1");

            // TTSパネルの表示を更新
            UpdateTTSPanelVisibility(character.ttsType);

            // 読み取り専用の場合は削除ボタン、VRMファイル欄、開くボタンを無効化
            DeleteCharacterButton.IsEnabled = !character.isReadOnly;
            VRMFilePathTextBox.IsEnabled = !character.isReadOnly;
            BrowseVrmFileButton.IsEnabled = !character.isReadOnly;
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

                // 既存のキャラクターから記憶用設定を取得（現在選択中のキャラクターまたは最初のキャラクター）
                string inheritedEmbeddedApiKey = string.Empty;
                string inheritedEmbeddedModel = "openai/text-embedding-3-large";
                string inheritedEmbeddedDimension = "3072";

                if (_currentCharacterIndex >= 0 && _currentCharacterIndex < AppSettings.Instance.CharacterList.Count)
                {
                    // 現在選択中のキャラクターから引き継ぐ
                    var currentChar = AppSettings.Instance.CharacterList[_currentCharacterIndex];
                    inheritedEmbeddedApiKey = currentChar.embeddedApiKey;
                    inheritedEmbeddedModel = currentChar.embeddedModel;
                    inheritedEmbeddedDimension = currentChar.embeddedDimension;
                }
                else if (AppSettings.Instance.CharacterList.Count > 0)
                {
                    // 最初のキャラクターから引き継ぐ
                    var firstChar = AppSettings.Instance.CharacterList[0];
                    inheritedEmbeddedApiKey = firstChar.embeddedApiKey;
                    inheritedEmbeddedModel = firstChar.embeddedModel;
                    inheritedEmbeddedDimension = firstChar.embeddedDimension;
                }

                var newCharacter = new CharacterSettings
                {
                    modelName = newName,
                    vrmFilePath = string.Empty,
                    isUseLLM = false,
                    apiKey = string.Empty,
                    llmModel = "openai/gpt-4o-mini",
                    max_turns_window = 100,
                    localLLMBaseUrl = string.Empty,
                    visionApiKey = string.Empty,
                    visionModel = "openai/gpt-4o-mini",
                    systemPromptFilePath = string.Empty,
                    isUseTTS = false,
                    ttsType = "voicevox",
                    voicevoxConfig = new VoicevoxConfig(),
                    styleBertVits2Config = new StyleBertVits2Config(),
                    aivisCloudConfig = new AivisCloudConfig(),
                    isEnableMemory = true,
                    memoryId = "",
                    // 既存のキャラクターから記憶用埋め込みモデル設定を引き継ぐ
                    embeddedApiKey = inheritedEmbeddedApiKey,
                    embeddedModel = inheritedEmbeddedModel,
                    embeddedDimension = inheritedEmbeddedDimension,
                    isUseSTT = false,
                    sttEngine = "amivoice",
                    sttWakeWord = string.Empty,
                    sttApiKey = string.Empty,
                    sttLanguage = "ja",
                    isConvertMToon = false,
                    isEnableShadowOff = true,
                    shadowOffMesh = "Face, U_Char_1",
                    isReadOnly = false
                };

                AppSettings.Instance.CharacterList.Add(newCharacter);

                // 新しいキャラクターを辞書に追加
                int newIndex = AppSettings.Instance.CharacterList.Count - 1;
                _allCharacterSystemPrompts[newIndex] = string.Empty;
                _originalCharacterSystemPrompts[newIndex] = string.Empty;

                // ComboBoxのItemsSourceを更新
                CharacterSelectComboBox.ItemsSource = null;
                CharacterSelectComboBox.ItemsSource = AppSettings.Instance.CharacterList;
                CharacterSelectComboBox.SelectedIndex = newIndex;

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

                // systemPromptファイルを削除予定リストに追加（実際の削除はOK押下時）
                if (!string.IsNullOrEmpty(character.systemPromptFilePath))
                {
                    _filesToDelete.Add(character.systemPromptFilePath);
                    Debug.WriteLine($"削除予定に追加: {character.systemPromptFilePath}");
                }

                AppSettings.Instance.CharacterList.RemoveAt(_currentCharacterIndex);

                // 辞書を再構築（インデックスが変わるため）
                RebuildCharacterSystemPromptsDictionaries();

                // ComboBoxのItemsSourceを更新
                CharacterSelectComboBox.ItemsSource = null;
                CharacterSelectComboBox.ItemsSource = AppSettings.Instance.CharacterList;

                if (AppSettings.Instance.CharacterList.Count > 0)
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
                    max_turns_window = sourceCharacter.max_turns_window,
                    localLLMBaseUrl = sourceCharacter.localLLMBaseUrl,
                    visionApiKey = sourceCharacter.visionApiKey,
                    visionModel = sourceCharacter.visionModel,
                    // systemPromptFilePathは空にして、ConfirmSettings時に生成（追加と同じ動作）
                    systemPromptFilePath = string.Empty,
                    isUseTTS = sourceCharacter.isUseTTS,
                    ttsType = sourceCharacter.ttsType,

                    // VOICEVOX詳細設定のコピー
                    voicevoxConfig = new VoicevoxConfig
                    {
                        endpointUrl = sourceCharacter.voicevoxConfig.endpointUrl,
                        speakerId = sourceCharacter.voicevoxConfig.speakerId,
                        speedScale = sourceCharacter.voicevoxConfig.speedScale,
                        pitchScale = sourceCharacter.voicevoxConfig.pitchScale,
                        intonationScale = sourceCharacter.voicevoxConfig.intonationScale,
                        volumeScale = sourceCharacter.voicevoxConfig.volumeScale,
                        prePhonemeLength = sourceCharacter.voicevoxConfig.prePhonemeLength,
                        postPhonemeLength = sourceCharacter.voicevoxConfig.postPhonemeLength,
                        outputSamplingRate = sourceCharacter.voicevoxConfig.outputSamplingRate,
                        outputStereo = sourceCharacter.voicevoxConfig.outputStereo
                    },
                    styleBertVits2Config = new StyleBertVits2Config
                    {
                        endpointUrl = sourceCharacter.styleBertVits2Config.endpointUrl,
                        modelName = sourceCharacter.styleBertVits2Config.modelName,
                        modelId = sourceCharacter.styleBertVits2Config.modelId,
                        speakerName = sourceCharacter.styleBertVits2Config.speakerName,
                        speakerId = sourceCharacter.styleBertVits2Config.speakerId,
                        style = sourceCharacter.styleBertVits2Config.style,
                        styleWeight = sourceCharacter.styleBertVits2Config.styleWeight,
                        language = sourceCharacter.styleBertVits2Config.language,
                        sdpRatio = sourceCharacter.styleBertVits2Config.sdpRatio,
                        noise = sourceCharacter.styleBertVits2Config.noise,
                        noiseW = sourceCharacter.styleBertVits2Config.noiseW,
                        length = sourceCharacter.styleBertVits2Config.length,
                        autoSplit = sourceCharacter.styleBertVits2Config.autoSplit,
                        splitInterval = sourceCharacter.styleBertVits2Config.splitInterval,
                        assistText = sourceCharacter.styleBertVits2Config.assistText,
                        assistTextWeight = sourceCharacter.styleBertVits2Config.assistTextWeight,
                        referenceAudioPath = sourceCharacter.styleBertVits2Config.referenceAudioPath
                    },
                    aivisCloudConfig = new AivisCloudConfig
                    {
                        apiKey = sourceCharacter.aivisCloudConfig.apiKey,
                        endpointUrl = sourceCharacter.aivisCloudConfig.endpointUrl,
                        modelUuid = sourceCharacter.aivisCloudConfig.modelUuid,
                        speakerUuid = sourceCharacter.aivisCloudConfig.speakerUuid,
                        styleId = sourceCharacter.aivisCloudConfig.styleId,
                        styleName = sourceCharacter.aivisCloudConfig.styleName,
                        useSSML = sourceCharacter.aivisCloudConfig.useSSML,
                        language = sourceCharacter.aivisCloudConfig.language,
                        speakingRate = sourceCharacter.aivisCloudConfig.speakingRate,
                        emotionalIntensity = sourceCharacter.aivisCloudConfig.emotionalIntensity,
                        tempoDynamics = sourceCharacter.aivisCloudConfig.tempoDynamics,
                        pitch = sourceCharacter.aivisCloudConfig.pitch,
                        volume = sourceCharacter.aivisCloudConfig.volume,
                        outputFormat = sourceCharacter.aivisCloudConfig.outputFormat,
                        outputBitrate = sourceCharacter.aivisCloudConfig.outputBitrate,
                        outputSamplingRate = sourceCharacter.aivisCloudConfig.outputSamplingRate,
                        outputAudioChannels = sourceCharacter.aivisCloudConfig.outputAudioChannels,
                    },
                    isEnableMemory = sourceCharacter.isEnableMemory,
                    memoryId = sourceCharacter.memoryId,
                    // 記憶用埋め込みモデル設定のコピー
                    embeddedApiKey = sourceCharacter.embeddedApiKey,
                    embeddedModel = sourceCharacter.embeddedModel,
                    embeddedDimension = sourceCharacter.embeddedDimension,
                    isUseSTT = sourceCharacter.isUseSTT,
                    sttEngine = sourceCharacter.sttEngine,
                    sttWakeWord = sourceCharacter.sttWakeWord,
                    sttApiKey = sourceCharacter.sttApiKey,
                    sttLanguage = sourceCharacter.sttLanguage,
                    isConvertMToon = sourceCharacter.isConvertMToon,
                    isEnableShadowOff = sourceCharacter.isEnableShadowOff,
                    shadowOffMesh = sourceCharacter.shadowOffMesh,
                    isReadOnly = false
                };

                // リストに追加
                AppSettings.Instance.CharacterList.Add(newCharacter);

                // 新しいキャラクターを辞書に追加（複製元のプロンプト内容を使用）
                // ファイル作成とパス生成は ConfirmSettings時に統一実行
                int newIndex = AppSettings.Instance.CharacterList.Count - 1;
                string sourcePromptContent = _allCharacterSystemPrompts.ContainsKey(_currentCharacterIndex)
                    ? _allCharacterSystemPrompts[_currentCharacterIndex]
                    : (!string.IsNullOrEmpty(sourceCharacter.systemPromptFilePath)
                        ? AppSettings.Instance.LoadSystemPrompt(sourceCharacter.systemPromptFilePath)
                        : string.Empty);

                _allCharacterSystemPrompts[newIndex] = sourcePromptContent;
                _originalCharacterSystemPrompts[newIndex] = sourcePromptContent;

                // ComboBoxのItemsSourceを更新（ItemsSourceとItemsの併用を避ける）
                CharacterSelectComboBox.ItemsSource = null;
                CharacterSelectComboBox.ItemsSource = AppSettings.Instance.CharacterList;

                // 新しく追加したキャラクターを選択
                CharacterSelectComboBox.SelectedIndex = newIndex;

                // 設定変更イベントを発生
                SettingsChanged?.Invoke(this, EventArgs.Empty);

                Debug.WriteLine($"キャラクター複製: {sourceCharacter.modelName} -> {newName}（ファイル作成とパス生成は遅延実行）");
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

        /// <summary>
        /// Base URLテキストボックスのプレースホルダー制御
        /// </summary>
        private void BaseUrlTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateBaseUrlPlaceholder();
        }

        private void BaseUrlTextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            UpdateBaseUrlPlaceholder();
        }

        private void BaseUrlTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            UpdateBaseUrlPlaceholder();
        }

        private void UpdateBaseUrlPlaceholder()
        {
            if (BaseUrlTextBox != null && BaseUrlPlaceholder != null)
            {
                BaseUrlPlaceholder.Visibility = string.IsNullOrEmpty(BaseUrlTextBox.Text) ?
                    Visibility.Visible : Visibility.Collapsed;
            }
        }

        /// <summary>
        /// TTSエンジン選択変更処理
        /// </summary>
        private void TTSEngineComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isInitialized || TTSEngineComboBox.SelectedItem == null)
                return;

            var selectedItem = (ComboBoxItem)TTSEngineComboBox.SelectedItem;
            var engineType = selectedItem.Tag?.ToString();

            // エンジンタイプに応じて表示パネルを切り替え
            UpdateTTSPanelVisibility(engineType);

            // 設定変更イベントを発火
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// TTSパネルの表示/非表示を切り替え
        /// </summary>
        private void UpdateTTSPanelVisibility(string? engineType)
        {
            if (VoicevoxSettingsPanel == null || StyleBertVits2BasicPanel == null || StyleBertVits2SettingsPanel == null || AivisCloudSettingsPanel == null)
                return;

            switch (engineType)
            {
                case "voicevox":
                    VoicevoxSettingsPanel.Visibility = Visibility.Visible;
                    StyleBertVits2BasicPanel.Visibility = Visibility.Collapsed;
                    StyleBertVits2SettingsPanel.Visibility = Visibility.Collapsed;
                    AivisCloudSettingsPanel.Visibility = Visibility.Collapsed;
                    break;
                case "style-bert-vits2":
                    VoicevoxSettingsPanel.Visibility = Visibility.Collapsed;
                    StyleBertVits2BasicPanel.Visibility = Visibility.Visible;
                    StyleBertVits2SettingsPanel.Visibility = Visibility.Visible;
                    AivisCloudSettingsPanel.Visibility = Visibility.Collapsed;
                    break;
                case "aivis-cloud":
                    VoicevoxSettingsPanel.Visibility = Visibility.Collapsed;
                    StyleBertVits2BasicPanel.Visibility = Visibility.Collapsed;
                    StyleBertVits2SettingsPanel.Visibility = Visibility.Collapsed;
                    AivisCloudSettingsPanel.Visibility = Visibility.Visible;
                    break;
                default:
                    VoicevoxSettingsPanel.Visibility = Visibility.Visible;
                    StyleBertVits2BasicPanel.Visibility = Visibility.Collapsed;
                    StyleBertVits2SettingsPanel.Visibility = Visibility.Collapsed;
                    AivisCloudSettingsPanel.Visibility = Visibility.Collapsed;
                    break;
            }
        }

        /// <summary>
        /// キャラクター名のテキスト変更イベント（リアルタイム更新）
        /// </summary>
        private void CharacterNameTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_isInitialized || _currentCharacterIndex < 0)
                return;

            // タイマーがすでに動作中の場合はリセット
            if (_characterNameChangeTimer != null)
            {
                _characterNameChangeTimer.Stop();
                _characterNameChangeTimer.Start();
            }
        }

        /// <summary>
        /// キャラクター名変更タイマーのTickイベント（デバウンス処理）
        /// </summary>
        private void CharacterNameChangeTimer_Tick(object? sender, EventArgs e)
        {
            if (_characterNameChangeTimer != null)
            {
                _characterNameChangeTimer.Stop();
            }

            if (!_isInitialized || _currentCharacterIndex < 0 || _currentCharacterIndex >= AppSettings.Instance.CharacterList.Count)
                return;

            var newName = CharacterNameTextBox.Text;
            if (!string.IsNullOrWhiteSpace(newName))
            {
                // 現在選択されているアイテムのインデックスを保存
                var currentSelectedIndex = _currentCharacterIndex;

                // キャラクター設定の名前を更新
                AppSettings.Instance.CharacterList[_currentCharacterIndex].modelName = newName;

                // ComboBoxのItemsSourceを一時的に無効にしてSelectionChangedイベントを防ぐ
                CharacterSelectComboBox.SelectionChanged -= CharacterSelectComboBox_SelectionChanged;

                // ComboBoxのItemsSourceを更新
                CharacterSelectComboBox.ItemsSource = null;
                CharacterSelectComboBox.ItemsSource = AppSettings.Instance.CharacterList;

                // 選択状態を復元
                CharacterSelectComboBox.SelectedIndex = currentSelectedIndex;

                // SelectionChangedイベントハンドラーを再設定
                CharacterSelectComboBox.SelectionChanged += CharacterSelectComboBox_SelectionChanged;

                SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
        }


        /// <summary>
        /// systemPromptファイルを削除
        /// </summary>
        /// <param name="filePath">削除するファイルパス</param>
        private void DeleteSystemPromptFile(string filePath)
        {
            try
            {
                var fullPath = Path.Combine(AppSettings.Instance.SystemPromptsDirectory, filePath);

                if (File.Exists(fullPath))
                {
                    File.Delete(fullPath);
                    Debug.WriteLine($"systemPromptファイルを削除しました: {filePath}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"systemPromptファイル削除エラー: {ex.Message}");
            }
        }

        /// <summary>
        /// OK押下時の実際のファイル生成と設定確定処理
        /// </summary>
        public void ConfirmSettings()
        {
            try
            {
                // 現在表示中のキャラクターの内容を保存
                if (_currentCharacterIndex >= 0 && _currentCharacterIndex < AppSettings.Instance.CharacterList.Count)
                {
                    _allCharacterSystemPrompts[_currentCharacterIndex] = SystemPromptTextBox.Text;
                }

                // 全キャラクターを処理
                for (int i = 0; i < AppSettings.Instance.CharacterList.Count; i++)
                {
                    var character = AppSettings.Instance.CharacterList[i];

                    // systemPromptFilePathが空の場合、新しいファイルを生成
                    if (string.IsNullOrEmpty(character.systemPromptFilePath))
                    {
                        character.systemPromptFilePath = AppSettings.Instance.GenerateSystemPromptFilePath(character.modelName);
                        Debug.WriteLine($"新しいファイルパスを生成: インデックス {i}, ファイル '{character.systemPromptFilePath}'");
                    }

                    // 既存ファイルがある場合は、名前変更が必要かチェック
                    var uuid = AppSettings.Instance.ExtractUuidFromFileName(character.systemPromptFilePath);
                    if (uuid != null)
                    {
                        var currentFileName = $"{character.modelName}_{uuid}.txt";
                        var oldFilePath = AppSettings.Instance.FindSystemPromptFileByUuid(uuid);

                        if (oldFilePath != null && oldFilePath != currentFileName)
                        {
                            // ファイル名変更
                            character.systemPromptFilePath = AppSettings.Instance.UpdateSystemPromptFileName(oldFilePath, character.modelName);
                            Debug.WriteLine($"ファイル名を変更: インデックス {i}, 新ファイル '{character.systemPromptFilePath}'");
                        }
                    }

                    // 各キャラクターのシステムプロンプト内容を取得してファイルに保存
                    // 辞書に必ず存在するはず（初期化時に全キャラクター分読み込み済み）
                    if (_allCharacterSystemPrompts.ContainsKey(i))
                    {
                        string promptContent = _allCharacterSystemPrompts[i];
                        AppSettings.Instance.SaveSystemPrompt(character.systemPromptFilePath, promptContent);
                        Debug.WriteLine($"設定確定: インデックス {i}, キャラクター '{character.modelName}', ファイル '{character.systemPromptFilePath}'");
                    }
                    else
                    {
                        Debug.WriteLine($"警告: インデックス {i} のキャラクター '{character.modelName}' が辞書に存在しません");
                    }
                }

                Debug.WriteLine($"全 {AppSettings.Instance.CharacterList.Count} キャラクターの設定確定完了");

                // 削除予定ファイルを実際に削除
                if (_filesToDelete.Count > 0)
                {
                    Debug.WriteLine($"{_filesToDelete.Count} 個のファイルを削除開始");
                    foreach (var fileToDelete in _filesToDelete)
                    {
                        DeleteSystemPromptFile(fileToDelete);
                    }
                    _filesToDelete.Clear();
                    Debug.WriteLine("削除予定ファイルの削除完了");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"設定確定エラー: {ex.Message}");
            }
        }

        /// <summary>
        /// キャンセル時の状態リセット処理
        /// </summary>
        public void ResetPendingChanges()
        {
            try
            {
                // 削除予定リストをクリア
                _filesToDelete.Clear();

                // 一時保存されたプロンプト内容を元の状態に復元
                _allCharacterSystemPrompts.Clear();
                foreach (var kvp in _originalCharacterSystemPrompts)
                {
                    _allCharacterSystemPrompts[kvp.Key] = kvp.Value;
                }

                // 現在選択中のキャラクターのUIも復元
                if (_currentCharacterIndex >= 0 && _allCharacterSystemPrompts.ContainsKey(_currentCharacterIndex))
                {
                    SystemPromptTextBox.Text = _allCharacterSystemPrompts[_currentCharacterIndex];
                    _tempSystemPromptText = _allCharacterSystemPrompts[_currentCharacterIndex];
                }

                Debug.WriteLine("キャンセル処理: 全ての変更を元の状態に復元しました");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"キャンセル処理エラー: {ex.Message}");
            }
        }

        /// <summary>
        /// キャラクターリストのUIを更新（キャンセル時の復元用）
        /// </summary>
        public void RefreshCharacterList()
        {
            try
            {
                // システムプロンプト辞書を_originalから復元（ファイル読み込みはしない）
                _allCharacterSystemPrompts.Clear();
                foreach (var kvp in _originalCharacterSystemPrompts)
                {
                    _allCharacterSystemPrompts[kvp.Key] = kvp.Value;
                }

                // ComboBoxのItemsSourceを更新
                CharacterSelectComboBox.ItemsSource = null;
                CharacterSelectComboBox.ItemsSource = AppSettings.Instance.CharacterList;

                // 最初のキャラクターを選択（または既存のインデックスを維持）
                if (AppSettings.Instance.CharacterList.Count > 0)
                {
                    int indexToSelect = Math.Min(_currentCharacterIndex, AppSettings.Instance.CharacterList.Count - 1);
                    if (indexToSelect < 0) indexToSelect = 0;

                    _currentCharacterIndex = indexToSelect;
                    CharacterSelectComboBox.SelectedIndex = indexToSelect;
                    UpdateCharacterUI(); // 引数なしで呼び出し
                }
                else
                {
                    _currentCharacterIndex = -1;
                    // キャラクターが存在しない場合はUIをクリア
                    CharacterNameTextBox.Text = string.Empty;
                    VRMFilePathTextBox.Text = string.Empty;
                    SystemPromptTextBox.Text = string.Empty;
                }

                Debug.WriteLine($"キャラクターリスト復元完了: {AppSettings.Instance.CharacterList.Count}件（プロンプト内容はバックアップから復元）");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"キャラクターリスト復元エラー: {ex.Message}");
            }
        }

        /// <summary>
        /// SystemPromptTextBoxのテキスト変更イベント
        /// </summary>
        private void SystemPromptTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isInitialized)
            {
                _tempSystemPromptText = SystemPromptTextBox.Text;
                // 現在のキャラクターの内容もDictionaryに保存
                if (_currentCharacterIndex >= 0)
                {
                    _allCharacterSystemPrompts[_currentCharacterIndex] = SystemPromptTextBox.Text;
                }
            }
        }

        /// <summary>
        /// 全キャラクターの現在のシステムプロンプト内容を取得
        /// </summary>
        /// <returns>キャラクターインデックスをキーとするシステムプロンプト内容の辞書</returns>
        public Dictionary<int, string> GetCurrentSystemPrompts()
        {
            // 現在表示中のキャラクターの内容も最新化
            if (_currentCharacterIndex >= 0 && SystemPromptTextBox != null)
            {
                _allCharacterSystemPrompts[_currentCharacterIndex] = SystemPromptTextBox.Text;
            }

            return new Dictionary<int, string>(_allCharacterSystemPrompts);
        }
    }
}