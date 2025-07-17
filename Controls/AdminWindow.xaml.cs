using CocoroDock.Communication;
using CocoroDock.Services;
using CocoroDock.Utilities;
using CocoroDock.ViewModels;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;

namespace CocoroDock.Controls
{
    /// <summary>
    /// AdminWindow.xaml の相互作用ロジック
    /// </summary>
    public partial class AdminWindow : Window
    {
        // 表示設定を保存するための辞書
        private Dictionary<string, object> _displaySettings = new Dictionary<string, object>();
        private Dictionary<string, object> _originalDisplaySettings = new Dictionary<string, object>();

        // キャラクター設定を保存するための辞書のリスト
        private List<Dictionary<string, string>> _characterSettings = new List<Dictionary<string, string>>();
        private List<Dictionary<string, string>> _originalCharacterSettings = new List<Dictionary<string, string>>();

        // 現在選択されているキャラクターのインデックス
        private int _currentCharacterIndex = 0;

        // 設定が変更されたかどうかを追跡するフラグ
        private bool _settingsChanged = false;

        // 通信サービス
        private ICommunicationService? _communicationService;

        // MCPタブ用ViewModel
        private McpTabViewModel? _mcpTabViewModel;

        // アニメーション設定を保存するためのリスト
        private List<AnimationSetting> _animationSettings = new List<AnimationSetting>();
        private List<AnimationSetting> _originalAnimationSettings = new List<AnimationSetting>();        // キーボードフック用
        private HwndSource? _source;
        private bool _isCapturingKey = false;
        private bool _isWinKeyPressed = false; // Windowsキーの状態
        private bool _isAltKeyPressed = false; // Altキーの状態
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_KEYUP = 0x0101;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int WM_SYSKEYUP = 0x0105;
        private const int VK_LWIN = 0x5B;  // 左Windowsキー
        private const int VK_RWIN = 0x5C;  // 右Windowsキー
        private const int VK_LALT = 0xA4;  // 左Altキー
        private const int VK_RALT = 0xA5;  // 右Altキー

        // グローバルキーボードフック用
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_HOTKEY = 0x0312;
        private LowLevelKeyboardProc _proc;
        private IntPtr _hookID = IntPtr.Zero;

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [StructLayout(LayoutKind.Sequential)]
        private struct KBDLLHOOKSTRUCT
        {
            public uint vkCode;
            public uint scanCode;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        public AdminWindow() : this(null)
        {
        }

        public AdminWindow(ICommunicationService? communicationService)
        {
            InitializeComponent();

            _communicationService = communicationService;

            // グローバルキーボードフックの設定
            _proc = HookCallback;
            _hookID = SetHook(_proc);

            // 表示設定の初期化
            InitializeDisplaySettings();

            // キャラクター設定の初期化
            InitializeCharacterSettings();

            // ボタンイベントの登録
            RegisterButtonEvents();

            // MCPタブの初期化
            InitializeMcpTab();

            // 元の設定のバックアップを作成
            BackupSettings();
        }

        /// <summary>
        /// ボタンイベントの登録
        /// </summary>
        private void RegisterButtonEvents()
        {
            CaptureKeyButton.Click += CaptureKeyButton_Click;
        }

        /// <summary>
        /// キーキャプチャボタンのクリックイベントハンドラ
        /// </summary>
        private void CaptureKeyButton_Click(object sender, RoutedEventArgs e)
        {
            // 現在のテキストを保存
            string originalText = VirtualKeyStringTextBox.Text;

            // テキストボックスに入力待ちの表示
            VirtualKeyStringTextBox.Text = "Press the key";

            // キー入力待ちのためにフォーカスを設定
            VirtualKeyStringTextBox.Focus();

            // キー捕捉モードをオンにする
            _isCapturingKey = true;
            _isWinKeyPressed = false;
            _isAltKeyPressed = false;

            // キー押下イベントハンドラを一時的に追加
            this.PreviewKeyDown += CaptureKey_PreviewKeyDown;
        }

        /// <summary>
        /// キーキャプチャ用のキー押下イベントハンドラ
        /// </summary>
        private void CaptureKey_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            e.Handled = true; // キー名とモディファイアキー（Shift, Ctrl, Alt, Win）の組み合わせを取得
            string keyName = e.Key.ToString();

            // システムキーが押されているかチェック
            // Altキーはフックからのデータを使用（システムメニュー対策）
            bool isAltPressed = _isAltKeyPressed;
            bool isCtrlPressed = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
            bool isShiftPressed = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;
            // WinキーはフックからのデータをCheckし、ModifierKeysは使わない
            bool isWinPressed = _isWinKeyPressed; // Win検出に低レベルフックの状態を使用

            // 修飾キーの場合は単体では処理しない（他のキーと組み合わせる）
            if (keyName == "LeftAlt" || keyName == "RightAlt" ||
                keyName == "LeftCtrl" || keyName == "RightCtrl" ||
                keyName == "LeftShift" || keyName == "RightShift" ||
                keyName == "LWin" || keyName == "RWin")
            {
                return;
            }

            // 修飾キーの状態を文字列に追加
            string result = "";
            if (isCtrlPressed)
                result += "Ctrl+";
            if (isAltPressed)
                result += "Alt+";
            if (isShiftPressed)
                result += "Shift+";
            if (isWinPressed)
                result += "Win+";

            // キー名を追加
            result += keyName;

            // テキストボックスに表示
            VirtualKeyStringTextBox.Text = result;

            // 設定が変更されたことを記録
            _settingsChanged = true;

            // キー捕捉モードを解除
            _isCapturingKey = false;
            _isWinKeyPressed = false;
            _isAltKeyPressed = false;

            // イベントハンドラを削除
            this.PreviewKeyDown -= CaptureKey_PreviewKeyDown;
        }

        /// <summary>
        /// ウィンドウがロードされた後に呼び出されるイベントハンドラ
        /// </summary>
        protected override void OnSourceInitialized(System.EventArgs e)
        {
            base.OnSourceInitialized(e);

            // Owner設定後にメインサービスを初期化
            InitializeMainServices();

            // フックの初期化
            InitializeHook();
        }

        /// <summary>
        /// キーボードフックを初期化する
        /// </summary>
        private void InitializeHook()
        {
            // ウィンドウハンドルを取得してフックを設定
            _source = PresentationSource.FromVisual(this) as HwndSource;
            if (_source != null)
            {
                _source.AddHook(WndProc);
            }
        }

        /// <summary>
        /// ウィンドウプロシージャフック
        /// </summary>
        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            // キャプチャモード中のみWindowsキーとAltキーの処理を止める
            // 注: キーの状態管理は HookCallback で一元的に行う
            if (_isCapturingKey)
            {
                switch (msg)
                {
                    case WM_KEYDOWN:
                    case WM_SYSKEYDOWN:
                        int vkCode = wParam.ToInt32();
                        if (vkCode == VK_LWIN || vkCode == VK_RWIN ||
                            vkCode == VK_LALT || vkCode == VK_RALT)
                        {
                            // Altキーとシステムキーメニューを抑制
                            handled = true;
                        }
                        break;
                }
            }
            return IntPtr.Zero;
        }

        #region 初期化メソッド

        /// <summary>
        /// メインサービスの初期化
        /// </summary>
        private void InitializeMainServices()
        {
            // 通信サービスの取得（メインウィンドウから）
            if (Owner is MainWindow mainWindow &&
                typeof(MainWindow).GetField("_communicationService", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(mainWindow) is CommunicationService service)
            {
                _communicationService = service;
            }

            // License.txtの内容を読み込んでLicenseTextBoxに表示
            LoadLicenseText();

            // API説明テキストを設定
            SetApiDescriptionText();
        }

        /// <summary>
        /// 表示設定の初期化
        /// </summary>
        private void InitializeDisplaySettings()
        {
            // アプリ設定からの初期値を取得
            var appSettings = AppSettings.Instance;

            // UIに反映
            RestoreWindowPositionCheckBox.IsChecked = appSettings.IsRestoreWindowPosition;
            TopMostCheckBox.IsChecked = appSettings.IsTopmost;
            EscapeCursorCheckBox.IsChecked = appSettings.IsEscapeCursor;
            InputVirtualKeyCheckBox.IsChecked = appSettings.IsInputVirtualKey;
            VirtualKeyStringTextBox.Text = appSettings.VirtualKeyString;
            AutoMoveCheckBox.IsChecked = appSettings.IsAutoMove;
            ShowMessageWindowCheckBox.IsChecked = appSettings.ShowMessageWindow;
            AmbientOcclusionCheckBox.IsChecked = appSettings.IsEnableAmbientOcclusion;
            IsEnableNotificationApiCheckBox.IsChecked = appSettings.IsEnableNotificationApi;

            // スクリーンショット設定の初期化
            ScreenshotEnabledCheckBox.IsChecked = appSettings.ScreenshotSettings.enabled;
            ScreenshotIntervalTextBox.Text = appSettings.ScreenshotSettings.intervalMinutes.ToString();
            IdleTimeoutTextBox.Text = appSettings.ScreenshotSettings.idleTimeoutMinutes.ToString();
            CaptureActiveWindowOnlyCheckBox.IsChecked = appSettings.ScreenshotSettings.captureActiveWindowOnly;

            // マイク設定の初期化
            MicAutoAdjustmentCheckBox.IsChecked = appSettings.MicrophoneSettings.autoAdjustment;
            MicThresholdSlider.Value = appSettings.MicrophoneSettings.inputThreshold;

            foreach (ComboBoxItem item in MSAAComboBox.Items)
            {
                if (item.Tag != null &&
                    int.TryParse(item.Tag.ToString(), out int value) &&
                    value == appSettings.MsaaLevel)
                {
                    MSAAComboBox.SelectedItem = item;
                    break;
                }
            }
            foreach (ComboBoxItem item in CharacterShadowComboBox.Items)
            {
                if (item.Tag != null &&
                    int.TryParse(item.Tag.ToString(), out int value) &&
                    value == appSettings.CharacterShadow)
                {
                    CharacterShadowComboBox.SelectedItem = item;
                    break;
                }
            }
            foreach (ComboBoxItem item in CharacterShadowResolutionComboBox.Items)
            {
                if (item.Tag != null &&
                    int.TryParse(item.Tag.ToString(), out int value) &&
                    value == appSettings.CharacterShadowResolution)
                {
                    CharacterShadowResolutionComboBox.SelectedItem = item;
                    break;
                }
            }
            foreach (ComboBoxItem item in BackgroundShadowComboBox.Items)
            {
                if (item.Tag != null &&
                    int.TryParse(item.Tag.ToString(), out int value) &&
                    value == appSettings.BackgroundShadow)
                {
                    BackgroundShadowComboBox.SelectedItem = item;
                    break;
                }
            }
            foreach (ComboBoxItem item in BackgroundShadowResolutionComboBox.Items)
            {
                if (item.Tag != null &&
                    int.TryParse(item.Tag.ToString(), out int value) &&
                    value == appSettings.BackgroundShadowResolution)
                {
                    BackgroundShadowResolutionComboBox.SelectedItem = item;
                    break;
                }
            }
            WindowSizeSlider.Value = appSettings.WindowSize;

            // 設定を辞書に保存
            _displaySettings = new Dictionary<string, object>
            {
                { "RestoreWindowPosition", appSettings.IsRestoreWindowPosition },
                { "TopMost", appSettings.IsTopmost },
                { "EscapeCursor", appSettings.IsEscapeCursor },
                { "InputVirtualKey", appSettings.IsInputVirtualKey },
                { "VirtualKeyString", appSettings.VirtualKeyString },
                { "AutoMove", appSettings.IsAutoMove },
                { "IsEnableAmbientOcclusion", appSettings.IsEnableAmbientOcclusion },
                { "MsaaLevel", appSettings.MsaaLevel },
                { "CharacterShadow", appSettings.CharacterShadow },
                { "CharacterShadowResolution", appSettings.CharacterShadowResolution },
                { "BackgroundShadow", appSettings.BackgroundShadow },
                { "BackgroundShadowResolution", appSettings.BackgroundShadowResolution },
                { "WindowSize", appSettings.WindowSize },
                { "IsEnableNotificationApi", appSettings.IsEnableNotificationApi },
                { "IsEnableMcp", appSettings.IsEnableMcp },
                { "ScreenshotEnabled", appSettings.ScreenshotSettings.enabled },
                { "ScreenshotInterval", appSettings.ScreenshotSettings.intervalMinutes },
                { "IdleTimeout", appSettings.ScreenshotSettings.idleTimeoutMinutes },
                { "CaptureActiveWindowOnly", appSettings.ScreenshotSettings.captureActiveWindowOnly },
                { "MicAutoAdjustment", appSettings.MicrophoneSettings.autoAdjustment},
                { "MicInputThreshold", appSettings.MicrophoneSettings.inputThreshold}
            };
        }

        /// <summary>
        /// キャラクター設定の初期化
        /// </summary>
        private void InitializeCharacterSettings()
        {
            // アプリ設定からキャラクター設定を取得
            var appSettings = AppSettings.Instance;

            // キャラクターリストのクリア
            _characterSettings.Clear();
            CharacterSelectComboBox.Items.Clear();

            // キャラクター設定を辞書のリストに変換
            foreach (var character in appSettings.CharacterList)
            {
                var characterDict = new Dictionary<string, string>
                {
                    { "Name", character.modelName ?? "不明" },
                    { "VRMFilePath", character.vrmFilePath ?? "" },
                    { "IsUseLLM", character.isUseLLM.ToString() },
                    { "ApiKey", character.apiKey ?? "" },
                    { "LLMModel", character.llmModel ?? "" },
                    { "SystemPrompt", character.systemPrompt ?? "" },
                    { "IsUseTTS", character.isUseTTS.ToString() },
                    { "TTSEndpointURL", character.ttsEndpointURL ?? "" },
                    { "TTSSperkerID", character.ttsSperkerID ?? "" },
                    { "IsEnableMemory", character.isEnableMemory.ToString() },
                    { "UserId", character.userId ?? "" },
                    { "EmbeddedApiKey", character.embeddedApiKey ?? "" },
                    { "EmbeddedModel", character.embeddedModel ?? "" },
                    { "IsUseSTT", character.isUseSTT.ToString() },
                    { "STTEngine", character.sttEngine ?? "amivoice" },
                    { "STTWakeWord", character.sttWakeWord ?? "" },
                    { "STTApiKey", character.sttApiKey ?? "" },
                    { "STTLanguage", character.sttLanguage ?? "ja" },
                    { "IsEnableShadowOff", character.isEnableShadowOff.ToString() },
                    { "ShadowOffMesh", character.shadowOffMesh ?? "Face, U_Char_1" },
                    { "IsConvertMToon", character.isConvertMToon.ToString() }
                };
                _characterSettings.Add(characterDict);

                // コンボボックスに項目を追加
                var item = new ComboBoxItem { Content = character.modelName ?? "不明" };
                CharacterSelectComboBox.Items.Add(item);
            }

            // 初期キャラクターの設定をUIに反映
            var currentIndex = appSettings.CurrentCharacterIndex;
            // 選択変更イベントを発生させないようにイベントハンドラを一時的に削除
            CharacterSelectComboBox.SelectionChanged -= CharacterSelectComboBox_SelectionChanged;
            CharacterSelectComboBox.SelectedIndex = currentIndex;
            CharacterSelectComboBox.SelectionChanged += CharacterSelectComboBox_SelectionChanged;
            UpdateCharacterUI(currentIndex);

            // アニメーション設定を初期化
            _animationSettings = new List<AnimationSetting>(appSettings.AnimationSettings);

            // 現在のキャラクターのアニメーション設定を表示
            UpdateAnimationUI();
        }

        /// <summary>
        /// 現在の設定をバックアップする
        /// </summary>
        private void BackupSettings()
        {
            // 表示設定のバックアップ
            _originalDisplaySettings = new Dictionary<string, object>(_displaySettings);

            // キャラクター設定のディープコピー
            _originalCharacterSettings = new List<Dictionary<string, string>>();
            foreach (var character in _characterSettings)
            {
                _originalCharacterSettings.Add(new Dictionary<string, string>(character));
            }

            // アニメーション設定のディープコピー
            _originalAnimationSettings = new List<AnimationSetting>();
            foreach (var animSetting in _animationSettings)
            {
                var newAnimSetting = new AnimationSetting
                {
                    animeSetName = animSetting.animeSetName,
                    animations = new List<AnimationConfig>()
                };
                foreach (var anim in animSetting.animations)
                {
                    newAnimSetting.animations.Add(new AnimationConfig
                    {
                        displayName = anim.displayName,
                        animationType = anim.animationType,
                        animationName = anim.animationName,
                        isEnabled = anim.isEnabled
                    });
                }
                _originalAnimationSettings.Add(newAnimSetting);
            }
        }

        #endregion

        /// <summary>
        /// アニメーション設定をUIに反映
        /// </summary>
        private void UpdateAnimationUI()
        {
            // アニメーションセットをコンボボックスに設定
            AnimationSetComboBox.ItemsSource = _animationSettings;

            if (_currentCharacterIndex >= 0 && _currentCharacterIndex < AppSettings.Instance.CharacterList.Count)
            {
                var animationIndex = AppSettings.Instance.CurrentAnimationSettingIndex;

                // キャラクターのアニメーション設定を取得
                if (animationIndex >= 0 &&
                    animationIndex < _animationSettings.Count)
                {
                    AnimationSetComboBox.SelectedIndex = animationIndex;
                    var animSetting = _animationSettings[animationIndex];

                    // アニメーションリストを更新
                    UpdateAnimationListPanel(animSetting.animations);

                    // PostureChangeLoopCountを表示
                    PostureChangeLoopCountStandingTextBox.Text = animSetting.postureChangeLoopCountStanding.ToString();
                    PostureChangeLoopCountSittingFloorTextBox.Text = animSetting.postureChangeLoopCountSittingFloor.ToString();
                }
                else if (_animationSettings.Count > 0)
                {
                    // インデックスが範囲外の場合は最初の設定を使用
                    AnimationSetComboBox.SelectedIndex = 0;
                    UpdateAnimationListPanel(_animationSettings[0].animations);

                    // PostureChangeLoopCountを表示
                    PostureChangeLoopCountStandingTextBox.Text = _animationSettings[0].postureChangeLoopCountStanding.ToString();
                    PostureChangeLoopCountSittingFloorTextBox.Text = _animationSettings[0].postureChangeLoopCountSittingFloor.ToString();
                }
            }

            // コンボボックスの選択変更イベントを設定
            AnimationSetComboBox.SelectionChanged -= AnimationSetComboBox_SelectionChanged;
            AnimationSetComboBox.SelectionChanged += AnimationSetComboBox_SelectionChanged;

            // テキスト変更イベントを設定（名前の編集用）
            AnimationSetComboBox.LostFocus -= AnimationSetComboBox_LostFocus;
            AnimationSetComboBox.LostFocus += AnimationSetComboBox_LostFocus;

            // コンボボックスのロード時イベントを設定
            AnimationSetComboBox.Loaded -= AnimationSetComboBox_Loaded;
            AnimationSetComboBox.Loaded += AnimationSetComboBox_Loaded;
        }

        /// <summary>
        /// アニメーションリストパネルを更新
        /// </summary>
        private void UpdateAnimationListPanel(List<AnimationConfig> animations)
        {
            AnimationListPanel.Children.Clear();

            // animationTypeでグループ化
            var groupedAnimations = animations.GroupBy(a => a.animationType).OrderBy(g => g.Key);

            foreach (var group in groupedAnimations)
            {
                // グループボックスを作成
                var groupBox = new GroupBox
                {
                    Header = GetAnimationTypeDisplayName(group.Key),
                    Margin = new Thickness(0, 0, 0, 10),
                    Padding = new Thickness(10)
                };

                // グループボックス内のスタックパネル
                var stackPanel = new StackPanel();

                foreach (var animation in group)
                {
                    var grid = new Grid();
                    grid.Margin = new Thickness(0, 5, 0, 5);
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                    // Playボタン
                    var playButton = new Button
                    {
                        Content = "Play",
                        Margin = new Thickness(0, 0, 10, 0),
                        Padding = new Thickness(10, 5, 10, 5),
                        Tag = animation,
                        VerticalAlignment = VerticalAlignment.Center,
                        Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0xFF, 0x48, 0x73, 0xCF))
                    };
                    playButton.Click += PlayAnimationButton_Click;
                    Grid.SetColumn(playButton, 0);

                    // チェックボックス
                    var checkBox = new CheckBox
                    {
                        Content = animation.displayName,
                        IsChecked = animation.isEnabled,
                        Tag = animation,
                        Margin = new Thickness(0, 0, 0, 0),
                        VerticalAlignment = VerticalAlignment.Center,
                        Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0xFF, 0x48, 0x73, 0xCF))
                    };
                    checkBox.Checked += AnimationCheckBox_Checked;
                    checkBox.Unchecked += AnimationCheckBox_Unchecked;
                    Grid.SetColumn(checkBox, 1);

                    grid.Children.Add(playButton);
                    grid.Children.Add(checkBox);

                    stackPanel.Children.Add(grid);
                }

                groupBox.Content = stackPanel;
                AnimationListPanel.Children.Add(groupBox);
            }
        }

        /// <summary>
        /// アニメーションタイプの表示名を取得
        /// </summary>
        private string GetAnimationTypeDisplayName(int animationType)
        {
            switch (animationType)
            {
                case 0:
                    return "Standing Animation ON/OFF";
                case 1:
                    return "Sitting Floor Animation ON/OFF";
                case 2:
                    return "Lying Down Animation ON/OFF";
                default:
                    return "Unknown";
            }
        }

        /// <summary>
        /// アニメーションセット選択変更時の処理
        /// </summary>
        private void AnimationSetComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (AnimationSetComboBox.SelectedIndex >= 0 &&
                AnimationSetComboBox.SelectedIndex < _animationSettings.Count)
            {
                var animSetting = _animationSettings[AnimationSetComboBox.SelectedIndex];
                UpdateAnimationListPanel(animSetting.animations);

                // PostureChangeLoopCountを表示
                PostureChangeLoopCountStandingTextBox.Text = animSetting.postureChangeLoopCountStanding.ToString();
                PostureChangeLoopCountSittingFloorTextBox.Text = animSetting.postureChangeLoopCountSittingFloor.ToString();

                // 現在のキャラクターのアニメーション設定インデックスを更新
                if (_currentCharacterIndex >= 0 &&
                    _currentCharacterIndex < AppSettings.Instance.CharacterList.Count)
                {
                    AppSettings.Instance.CurrentAnimationSettingIndex = AnimationSetComboBox.SelectedIndex;
                }

                // テキストの選択を解除
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    var textBox = AnimationSetComboBox.Template.FindName("PART_EditableTextBox", AnimationSetComboBox) as TextBox;
                    if (textBox != null)
                    {
                        textBox.SelectionLength = 0;
                        textBox.CaretIndex = 0;
                    }
                }), System.Windows.Threading.DispatcherPriority.Background);
            }
        }

        /// <summary>
        /// アニメーションチェックボックスのチェック時の処理
        /// </summary>
        private void AnimationCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox checkBox && checkBox.Tag is AnimationConfig animation)
            {
                animation.isEnabled = true;
            }
        }

        /// <summary>
        /// アニメーションチェックボックスのアンチェック時の処理
        /// </summary>
        private void AnimationCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox checkBox && checkBox.Tag is AnimationConfig animation)
            {
                animation.isEnabled = false;
            }
        }

        /// <summary>
        /// アニメーション再生ボタンクリック時の処理
        /// </summary>
        private async void PlayAnimationButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is AnimationConfig animation)
            {
                if (_communicationService != null)
                {
                    try
                    {
                        // CocoroShellにアニメーション再生指示を送信
                        await _communicationService.SendAnimationToShellAsync(animation.animationName);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"アニメーション再生エラー: {ex.Message}");
                        UIHelper.ShowError("アニメーション再生エラー", ex.Message);
                    }
                }
                else
                {
                    UIHelper.ShowError("通信エラー", "通信サービスが利用できません。");
                }
            }
        }

        /// <summary>
        /// アニメーションセット追加ボタンクリック時の処理
        /// </summary>
        private void AddAnimationSetButton_Click(object sender, RoutedEventArgs e)
        {
            // 新しいアニメーションセットを追加
            var newSet = new AnimationSetting
            {
                animeSetName = "新規セット" + (_animationSettings.Count + 1),
                postureChangeLoopCountStanding = 30,
                postureChangeLoopCountSittingFloor = 30,
                animations = new List<AnimationConfig>()
            };

            // デフォルトのアニメーションリストを作成（既存の最初のセットからコピー）
            if (_animationSettings.Count > 0 && _animationSettings[0].animations.Count > 0)
            {
                foreach (var anim in _animationSettings[0].animations)
                {
                    newSet.animations.Add(new AnimationConfig
                    {
                        displayName = anim.displayName,
                        animationType = anim.animationType,
                        animationName = anim.animationName,
                        isEnabled = true
                    });
                }
            }

            _animationSettings.Add(newSet);
            AnimationSetComboBox.ItemsSource = null;
            AnimationSetComboBox.ItemsSource = _animationSettings;
            AnimationSetComboBox.SelectedIndex = _animationSettings.Count - 1;
        }

        /// <summary>
        /// アニメーションセットコンボボックスのロード時の処理
        /// </summary>
        private void AnimationSetComboBox_Loaded(object sender, RoutedEventArgs e)
        {
            // テキストボックス部分の選択を解除
            if (sender is ComboBox comboBox)
            {
                var textBox = comboBox.Template.FindName("PART_EditableTextBox", comboBox) as TextBox;
                if (textBox != null)
                {
                    textBox.SelectionLength = 0;
                    textBox.CaretIndex = 0;
                }
            }
        }

        /// <summary>
        /// アニメーションセットコンボボックスのフォーカス喪失時の処理
        /// </summary>
        private void AnimationSetComboBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (AnimationSetComboBox.SelectedIndex >= 0 &&
                AnimationSetComboBox.SelectedIndex < _animationSettings.Count)
            {
                var newName = AnimationSetComboBox.Text?.Trim();
                if (!string.IsNullOrEmpty(newName))
                {
                    // 選択されているアニメーションセットの名前を更新
                    _animationSettings[AnimationSetComboBox.SelectedIndex].animeSetName = newName;

                    // コンボボックスを更新（表示を反映）
                    var selectedIndex = AnimationSetComboBox.SelectedIndex;
                    AnimationSetComboBox.ItemsSource = null;
                    AnimationSetComboBox.ItemsSource = _animationSettings;
                    AnimationSetComboBox.SelectedIndex = selectedIndex;
                }
            }
        }

        /// <summary>
        /// アニメーションセット削除ボタンクリック時の処理
        /// </summary>
        private void DeleteAnimationSetButton_Click(object sender, RoutedEventArgs e)
        {
            // 最後の1個は削除できないようにする
            if (_animationSettings.Count <= 1)
            {
                MessageBox.Show("最後のアニメーションセットは削除できません。", "削除不可", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 現在選択されているセットのインデックスを取得
            int selectedIndex = AnimationSetComboBox.SelectedIndex;
            if (selectedIndex < 0 || selectedIndex >= _animationSettings.Count)
            {
                return;
            }

            // 削除確認
            var selectedSet = _animationSettings[selectedIndex];
            var result = MessageBox.Show($"アニメーションセット「{selectedSet.animeSetName}」を削除しますか？", "削除確認", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            // セットを削除
            _animationSettings.RemoveAt(selectedIndex);

            // コンボボックスを更新
            AnimationSetComboBox.ItemsSource = null;
            AnimationSetComboBox.ItemsSource = _animationSettings;

            // 新しい選択インデックスを設定
            if (selectedIndex >= _animationSettings.Count)
            {
                selectedIndex = _animationSettings.Count - 1;
            }
            AnimationSetComboBox.SelectedIndex = selectedIndex;

            // アニメーション設定インデックスを調整
            if (AppSettings.Instance.CurrentAnimationSettingIndex >= _animationSettings.Count)
            {
                AppSettings.Instance.CurrentAnimationSettingIndex = 0;
            }
        }

        /// <summary>
        /// キャラクター情報をUIに反映
        /// </summary>
        private void UpdateCharacterUI(int index)
        {
            if (index >= 0 && index < _characterSettings.Count)
            {
                CharacterNameTextBox.Text = _characterSettings[index]["Name"];
                VRMFilePathTextBox.Text = _characterSettings[index]["VRMFilePath"];
                ApiKeyPasswordBox.Password = _characterSettings[index]["ApiKey"];
                LlmModelTextBox.Text = _characterSettings[index]["LLMModel"];
                SystemPromptTextBox.Text = _characterSettings[index]["SystemPrompt"];
                TTSEndpointURLTextBox.Text = _characterSettings[index]["TTSEndpointURL"];
                TTSSperkerIDTextBox.Text = _characterSettings[index]["TTSSperkerID"];
                UserIdTextBox.Text = _characterSettings[index].ContainsKey("UserId") ? _characterSettings[index]["UserId"] : "";
                EmbeddedApiKeyPasswordBox.Password = _characterSettings[index].ContainsKey("EmbeddedApiKey") ? _characterSettings[index]["EmbeddedApiKey"] : "";
                EmbeddedModelTextBox.Text = _characterSettings[index].ContainsKey("EmbeddedModel") ? _characterSettings[index]["EmbeddedModel"] : "";
                STTWakeWordTextBox.Text = _characterSettings[index].ContainsKey("STTWakeWord") ? _characterSettings[index]["STTWakeWord"] : "";
                STTApiKeyPasswordBox.Password = _characterSettings[index].ContainsKey("STTApiKey") ? _characterSettings[index]["STTApiKey"] : "";

                // STTエンジンの設定を更新
                string sttEngine = "amivoice"; // デフォルト
                if (_characterSettings[index].ContainsKey("STTEngine"))
                {
                    sttEngine = _characterSettings[index]["STTEngine"];
                }
                // ComboBoxの選択を更新
                foreach (ComboBoxItem item in STTEngineComboBox.Items)
                {
                    if ((string)item.Tag == sttEngine)
                    {
                        STTEngineComboBox.SelectedItem = item;
                        break;
                    }
                }

                // IsUseLLMチェックボックスの状態を更新
                bool isUseLLM = false;
                if (_characterSettings[index].ContainsKey("IsUseLLM"))
                {
                    bool.TryParse(_characterSettings[index]["IsUseLLM"], out isUseLLM);
                }
                IsUseLLMCheckBox.IsChecked = isUseLLM;

                // IsUseTTSチェックボックスの状態を更新
                bool isUseTTS = false;
                if (_characterSettings[index].ContainsKey("IsUseTTS"))
                {
                    bool.TryParse(_characterSettings[index]["IsUseTTS"], out isUseTTS);
                }
                IsUseTTSCheckBox.IsChecked = isUseTTS;

                // IsEnableMemoryチェックボックスの状態を更新
                bool isEnableMemory = true; // デフォルトはtrue
                if (_characterSettings[index].ContainsKey("IsEnableMemory"))
                {
                    bool.TryParse(_characterSettings[index]["IsEnableMemory"], out isEnableMemory);
                }
                IsEnableMemoryCheckBox.IsChecked = isEnableMemory;

                // 埋め込みモデル設定の更新
                EmbeddedApiKeyPasswordBox.Password = _characterSettings[index].ContainsKey("EmbeddedApiKey") ? _characterSettings[index]["EmbeddedApiKey"] : "";
                EmbeddedModelTextBox.Text = _characterSettings[index].ContainsKey("EmbeddedModel") ? _characterSettings[index]["EmbeddedModel"] : "";

                // IsUseSTTチェックボックスの状態を更新
                bool isUseSTT = false;
                if (_characterSettings[index].ContainsKey("IsUseSTT"))
                {
                    bool.TryParse(_characterSettings[index]["IsUseSTT"], out isUseSTT);
                }
                IsUseSTTCheckBox.IsChecked = isUseSTT;

                // Shadow設定の更新
                bool isEnableShadowOff = true; // デフォルトはtrue
                if (_characterSettings[index].ContainsKey("IsEnableShadowOff"))
                {
                    bool.TryParse(_characterSettings[index]["IsEnableShadowOff"], out isEnableShadowOff);
                }
                EnableShadowOffCheckBox.IsChecked = isEnableShadowOff;

                string shadowOffMesh = "Face, U_Char_1"; // デフォルト値
                if (_characterSettings[index].ContainsKey("ShadowOffMesh"))
                {
                    shadowOffMesh = _characterSettings[index]["ShadowOffMesh"];
                }
                ShadowOffMeshTextBox.Text = shadowOffMesh;
                // EnableShadowOffがチェックされていない場合はテキストボックスを無効化
                ShadowOffMeshTextBox.IsEnabled = isEnableShadowOff;

                // IsConvertMToonチェックボックスの状態を更新
                bool isConvertMToon = false;
                if (_characterSettings[index].ContainsKey("IsConvertMToon"))
                {
                    bool.TryParse(_characterSettings[index]["IsConvertMToon"], out isConvertMToon);
                }
                ConvertMToonCheckBox.IsChecked = isConvertMToon;

                // IsReadOnlyの状態を確認し、該当するUIコントロールの有効/無効を設定
                bool isReadOnly = false;
                if (index < AppSettings.Instance.CharacterList.Count)
                {
                    isReadOnly = AppSettings.Instance.CharacterList[index].isReadOnly;
                }
                CharacterNameTextBox.IsEnabled = !isReadOnly;
                VRMFilePathTextBox.IsEnabled = !isReadOnly;
                BrowseVrmFileButton.IsEnabled = !isReadOnly;

                _currentCharacterIndex = index;
            }
        }

        #region キャラクター設定イベントハンドラ

        /// <summary>
        /// キャラクター選択時のイベントハンドラ
        /// </summary>
        private void CharacterSelectComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // 現在のキャラクター設定を保存
            SaveCurrentCharacterSettings();

            // 新しいキャラクターのUIを更新
            int selectedIndex = CharacterSelectComboBox.SelectedIndex;
            if (selectedIndex >= 0)
            {
                UpdateCharacterUI(selectedIndex);
                // アニメーション設定も更新
                UpdateAnimationUI();
            }
        }

        /// <summary>
        /// 新しいキャラクター作成ボタンのクリックイベントハンドラ
        /// </summary>
        private void AddCharacterButton_Click(object sender, RoutedEventArgs e)
        {
            // 現在のキャラクター設定を保存
            SaveCurrentCharacterSettings();

            // 新しいキャラクターを追加
            var newName = "New Character" + (_characterSettings.Count + 1);
            var newCharacter = new Dictionary<string, string>
            {
                { "Name", "" },
                { "VRMFilePath", "" },
                { "IsUseLLM", "false" },
                { "ApiKey", "" },
                { "LLMModel", "" },
                { "SystemPrompt", "" },
                { "IsUseTTS", "false"},
                { "TTSEndpointURL", "" },
                { "TTSSperkerID", "" },
                { "IsEnableMemory", "true" },
                { "UserId", "" },
                { "EmbeddedApiKey", "" },
                { "EmbeddedModel", "" },
                { "IsUseSTT", "false" },
                { "STTEngine", "amivoice" },
                { "STTWakeWord", "" },
                { "STTApiKey", "" },
                { "IsEnableShadowOff", "true" },
                { "ShadowOffMesh", "Face, U_Char_1" },
                { "IsConvertMToon", "false" }
            };
            _characterSettings.Add(newCharacter);
            var newItem = new ComboBoxItem { Content = newName };
            CharacterSelectComboBox.Items.Add(newItem);
            CharacterSelectComboBox.SelectedIndex = _characterSettings.Count - 1;

            // 設定変更フラグを設定
            _settingsChanged = true;
        }

        /// <summary>
        /// キャラクター削除ボタンのクリックイベントハンドラ
        /// </summary>
        private void DeleteCharacterButton_Click(object sender, RoutedEventArgs e)
        {
            // デフォルトキャラクターは削除不可
            if (_currentCharacterIndex == 0)
            {
                MessageBox.Show("デフォルトキャラクターは削除できません。", "削除不可", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 確認ダイアログを表示
            var name = _characterSettings[_currentCharacterIndex]["Name"];
            var result = MessageBox.Show($"キャラクター「{name}」を削除しますか？", "確認", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                // キャラクター設定を削除
                _characterSettings.RemoveAt(_currentCharacterIndex);
                CharacterSelectComboBox.Items.RemoveAt(_currentCharacterIndex);

                // デフォルトキャラクターを選択
                CharacterSelectComboBox.SelectedIndex = 0;

                // 設定変更フラグを設定
                _settingsChanged = true;
            }
        }

        /// <summary>
        /// 現在のキャラクター設定をメモリに保存
        /// </summary>
        private void SaveCurrentCharacterSettings()
        {
            if (_currentCharacterIndex >= 0 && _currentCharacterIndex < _characterSettings.Count)
            {
                // UIから値を取得して設定を更新
                var name = CharacterNameTextBox.Text;
                var systemPrompt = SystemPromptTextBox.Text;
                var vrmFilePath = VRMFilePathTextBox.Text;
                var apiKey = ApiKeyPasswordBox.Password;
                var llmModel = LlmModelTextBox.Text;
                var isUseLLM = IsUseLLMCheckBox.IsChecked ?? false;
                var isUseTTS = IsUseTTSCheckBox.IsChecked ?? false;
                var ttsEndpointURL = TTSEndpointURLTextBox.Text;
                var ttsSperkerID = TTSSperkerIDTextBox.Text;
                var isEnableMemory = IsEnableMemoryCheckBox.IsChecked ?? true;
                var userId = UserIdTextBox.Text;
                var embeddedApiKey = EmbeddedApiKeyPasswordBox.Password;
                var embeddedModel = EmbeddedModelTextBox.Text;
                var isUseSTT = IsUseSTTCheckBox.IsChecked ?? false;
                var sttEngine = (STTEngineComboBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "amivoice";
                var sttWakeWord = STTWakeWordTextBox.Text;
                var sttApiKey = STTApiKeyPasswordBox.Password;
                var isEnableShadowOff = EnableShadowOffCheckBox.IsChecked ?? true;
                var shadowOffMesh = ShadowOffMeshTextBox.Text;
                var isConvertMToon = ConvertMToonCheckBox.IsChecked ?? false;

                // IsReadOnlyの状態を確認
                bool isReadOnly = false;
                if (_currentCharacterIndex < AppSettings.Instance.CharacterList.Count)
                {
                    isReadOnly = AppSettings.Instance.CharacterList[_currentCharacterIndex].isReadOnly;
                }

                // ReadOnlyの場合、元の名前とVRMファイルパスを保持
                if (isReadOnly)
                {
                    name = _characterSettings[_currentCharacterIndex]["Name"];
                    vrmFilePath = _characterSettings[_currentCharacterIndex]["VRMFilePath"];
                }

                // 値が変更された場合のみ更新
                bool isUseLLMChanged = false;
                if (_characterSettings[_currentCharacterIndex].ContainsKey("IsUseLLM"))
                {
                    bool currentIsUseLLM = false;
                    bool.TryParse(_characterSettings[_currentCharacterIndex]["IsUseLLM"], out currentIsUseLLM);
                    isUseLLMChanged = currentIsUseLLM != isUseLLM;
                }
                else
                {
                    isUseLLMChanged = isUseLLM; // デフォルトはfalseとして扱う
                }
                bool isUseTTSChanged = false;
                if (_characterSettings[_currentCharacterIndex].ContainsKey("IsUseTTS"))
                {
                    bool currentIsUseTTS = false;
                    bool.TryParse(_characterSettings[_currentCharacterIndex]["IsUseTTS"], out currentIsUseTTS);
                    isUseTTSChanged = currentIsUseTTS != isUseTTS;
                }
                else
                {
                    isUseTTSChanged = isUseTTS; // デフォルトはfalseとして扱う
                }

                bool vrmFilePathChanged = !_characterSettings[_currentCharacterIndex].ContainsKey("VRMFilePath") ||
                                         _characterSettings[_currentCharacterIndex]["VRMFilePath"] != vrmFilePath;

                bool apiKeyChanged = !_characterSettings[_currentCharacterIndex].ContainsKey("ApiKey") ||
                                    _characterSettings[_currentCharacterIndex]["ApiKey"] != apiKey;

                bool llmModelChanged = !_characterSettings[_currentCharacterIndex].ContainsKey("LLMModel") ||
                                     _characterSettings[_currentCharacterIndex]["LLMModel"] != llmModel;
                bool ttsEndpointURLChanged = !_characterSettings[_currentCharacterIndex].ContainsKey("TTSEndpointURL") ||
                                            _characterSettings[_currentCharacterIndex]["TTSEndpointURL"] != ttsEndpointURL;
                bool ttsSperkerIDChanged = !_characterSettings[_currentCharacterIndex].ContainsKey("TTSSperkerID") ||
                                            _characterSettings[_currentCharacterIndex]["TTSSperkerID"] != ttsSperkerID;
                bool isEnableMemoryChanged = false;
                if (_characterSettings[_currentCharacterIndex].ContainsKey("IsEnableMemory"))
                {
                    bool currentIsEnableMemory = true;
                    bool.TryParse(_characterSettings[_currentCharacterIndex]["IsEnableMemory"], out currentIsEnableMemory);
                    isEnableMemoryChanged = currentIsEnableMemory != isEnableMemory;
                }
                else
                {
                    isEnableMemoryChanged = !isEnableMemory; // デフォルトはtrueとして扱う
                }
                bool userIdChanged = !_characterSettings[_currentCharacterIndex].ContainsKey("UserId") ||
                                     _characterSettings[_currentCharacterIndex]["UserId"] != userId;
                bool embeddedApiKeyChanged = !_characterSettings[_currentCharacterIndex].ContainsKey("EmbeddedApiKey") ||
                                     _characterSettings[_currentCharacterIndex]["EmbeddedApiKey"] != embeddedApiKey;
                bool embeddedModelChanged = !_characterSettings[_currentCharacterIndex].ContainsKey("EmbeddedModel") ||
                                     _characterSettings[_currentCharacterIndex]["EmbeddedModel"] != embeddedModel;
                bool isUseSTTChanged = false;
                if (_characterSettings[_currentCharacterIndex].ContainsKey("IsUseSTT"))
                {
                    bool currentIsUseSTT = false;
                    bool.TryParse(_characterSettings[_currentCharacterIndex]["IsUseSTT"], out currentIsUseSTT);
                    isUseSTTChanged = currentIsUseSTT != isUseSTT;
                }
                else
                {
                    isUseSTTChanged = isUseSTT; // デフォルトはfalseとして扱う
                }
                bool sttEngineChanged = !_characterSettings[_currentCharacterIndex].ContainsKey("STTEngine") ||
                                     _characterSettings[_currentCharacterIndex]["STTEngine"] != sttEngine;
                bool sttWakeWordChanged = !_characterSettings[_currentCharacterIndex].ContainsKey("STTWakeWord") ||
                                     _characterSettings[_currentCharacterIndex]["STTWakeWord"] != sttWakeWord;
                bool sttApiKeyChanged = !_characterSettings[_currentCharacterIndex].ContainsKey("STTApiKey") ||
                                     _characterSettings[_currentCharacterIndex]["STTApiKey"] != sttApiKey;

                bool isEnableShadowOffChanged = false;
                if (_characterSettings[_currentCharacterIndex].ContainsKey("IsEnableShadowOff"))
                {
                    bool currentIsEnableShadowOff = true;
                    bool.TryParse(_characterSettings[_currentCharacterIndex]["IsEnableShadowOff"], out currentIsEnableShadowOff);
                    isEnableShadowOffChanged = currentIsEnableShadowOff != isEnableShadowOff;
                }
                else
                {
                    isEnableShadowOffChanged = !isEnableShadowOff; // デフォルトはtrueとして扱う
                }

                bool shadowOffMeshChanged = !_characterSettings[_currentCharacterIndex].ContainsKey("ShadowOffMesh") ||
                                         _characterSettings[_currentCharacterIndex]["ShadowOffMesh"] != shadowOffMesh;

                bool isConvertMToonChanged = false;
                if (_characterSettings[_currentCharacterIndex].ContainsKey("IsConvertMToon"))
                {
                    bool currentIsConvertMToon = false;
                    bool.TryParse(_characterSettings[_currentCharacterIndex]["IsConvertMToon"], out currentIsConvertMToon);
                    isConvertMToonChanged = currentIsConvertMToon != isConvertMToon;
                }
                else
                {
                    isConvertMToonChanged = isConvertMToon; // デフォルトはfalseとして扱う
                }


                if (_characterSettings[_currentCharacterIndex]["Name"] != name ||
                    _characterSettings[_currentCharacterIndex]["SystemPrompt"] != systemPrompt ||
                    isUseLLMChanged || isUseTTSChanged || vrmFilePathChanged || apiKeyChanged || llmModelChanged ||
                    ttsEndpointURLChanged || ttsSperkerIDChanged || userIdChanged || isEnableMemoryChanged ||
                    embeddedApiKeyChanged || embeddedModelChanged || isUseSTTChanged || sttEngineChanged || sttWakeWordChanged || sttApiKeyChanged ||
                    isEnableShadowOffChanged || shadowOffMeshChanged || isConvertMToonChanged)
                {
                    _characterSettings[_currentCharacterIndex]["Name"] = name;
                    _characterSettings[_currentCharacterIndex]["SystemPrompt"] = systemPrompt;
                    _characterSettings[_currentCharacterIndex]["VRMFilePath"] = vrmFilePath;
                    _characterSettings[_currentCharacterIndex]["ApiKey"] = apiKey;
                    _characterSettings[_currentCharacterIndex]["LLMModel"] = llmModel;
                    _characterSettings[_currentCharacterIndex]["IsUseLLM"] = isUseLLM.ToString();
                    _characterSettings[_currentCharacterIndex]["TTSEndpointURL"] = ttsEndpointURL;
                    _characterSettings[_currentCharacterIndex]["TTSSperkerID"] = ttsSperkerID;
                    _characterSettings[_currentCharacterIndex]["IsUseTTS"] = isUseTTS.ToString();
                    _characterSettings[_currentCharacterIndex]["IsEnableMemory"] = isEnableMemory.ToString();
                    _characterSettings[_currentCharacterIndex]["UserId"] = userId;
                    _characterSettings[_currentCharacterIndex]["EmbeddedApiKey"] = embeddedApiKey;
                    _characterSettings[_currentCharacterIndex]["EmbeddedModel"] = embeddedModel;
                    _characterSettings[_currentCharacterIndex]["IsUseSTT"] = isUseSTT.ToString();
                    _characterSettings[_currentCharacterIndex]["STTEngine"] = sttEngine;
                    _characterSettings[_currentCharacterIndex]["STTWakeWord"] = sttWakeWord;
                    _characterSettings[_currentCharacterIndex]["STTApiKey"] = sttApiKey;
                    _characterSettings[_currentCharacterIndex]["IsEnableShadowOff"] = isEnableShadowOff.ToString();
                    _characterSettings[_currentCharacterIndex]["ShadowOffMesh"] = shadowOffMesh;
                    _characterSettings[_currentCharacterIndex]["IsConvertMToon"] = isConvertMToon.ToString();

                    // コンボボックスの表示も更新
                    if (_currentCharacterIndex < CharacterSelectComboBox.Items.Count)
                    {
                        var item = CharacterSelectComboBox.Items[_currentCharacterIndex] as ComboBoxItem;
                        if (item != null)
                        {
                            item.Content = name;
                        }
                    }

                    // 設定変更フラグを設定
                    _settingsChanged = true;
                }
            }
        }

        #endregion

        #region 共通ボタンイベントハンドラ
        /// <summary>
        /// OKボタンのクリックイベントハンドラ
        /// </summary>
        private async void OkButton_Click(object sender, RoutedEventArgs e)
        {
            // ボタンを無効化（処理中の重複実行防止）
            OkButton.IsEnabled = false;
            CancelButton.IsEnabled = false;

            try
            {
                // 現在のキャラクター設定を一時保存
                SaveCurrentCharacterSettings();

                // バリデーション（警告のみ）
                if (!ValidateCharacterSettings())
                {
                    // バリデーション失敗時はボタンを再有効化
                    OkButton.IsEnabled = true;
                    CancelButton.IsEnabled = true;
                    return;
                }

                // 設定変更前の値を保存
                int lastSelectedIndex = AppSettings.Instance.CurrentCharacterIndex;
                string lastVRMFilePath = string.Empty;
                bool lastIsUseLLM = false;
                bool lastIsEnableMemory = true;
                bool lastIsEnableNotificationApi = false;
                if (_originalDisplaySettings.ContainsKey("IsEnableNotificationApi"))
                {
                    lastIsEnableNotificationApi = (bool)_originalDisplaySettings["IsEnableNotificationApi"];
                }
                if (lastSelectedIndex >= 0 && lastSelectedIndex < AppSettings.Instance.CharacterList.Count)
                {
                    lastVRMFilePath = AppSettings.Instance.CharacterList[lastSelectedIndex].vrmFilePath ?? string.Empty;
                    lastIsUseLLM = AppSettings.Instance.CharacterList[lastSelectedIndex].isUseLLM;
                    lastIsEnableMemory = AppSettings.Instance.CharacterList[lastSelectedIndex].isEnableMemory;
                }

                // MCP設定の変更をチェックして適用
                if (_mcpTabViewModel != null)
                {
                    await _mcpTabViewModel.ApplyMcpSettingsAsync();
                }

                // すべてのタブの設定を保存
                SaveAllSettings(lastIsEnableNotificationApi);

                // VRMFilePathかSelectedIndexが変更されたかチェック
                bool isNeedsRestart = false;
                int currentSelectedIndex = AppSettings.Instance.CurrentCharacterIndex;
                // SelectedIndexが変更された場合
                if (lastSelectedIndex != currentSelectedIndex)
                {
                    isNeedsRestart = true;
                }

                // キャラクター設定の変更を検出（元の設定と比較）（同じキャラクターの場合のみチェック）
                if (lastSelectedIndex == currentSelectedIndex && _originalCharacterSettings != null)
                {
                    // すべてのキャラクターの設定を比較
                    for (int i = 0; i < _characterSettings.Count && i < _originalCharacterSettings.Count; i++)
                    {
                        var current = _characterSettings[i];
                        var original = _originalCharacterSettings[i];

                        // 各項目を比較
                        foreach (var key in current.Keys)
                        {
                            if (!original.ContainsKey(key) || current[key] != original[key])
                            {
                                isNeedsRestart = true;
                                break;
                            }
                        }

                        // originalにあってcurrentにない項目もチェック
                        foreach (var key in original.Keys)
                        {
                            if (!current.ContainsKey(key))
                            {
                                isNeedsRestart = true;
                                break;
                            }
                        }

                        if (isNeedsRestart)
                        {
                            break;
                        }
                    }

                    // キャラクター数が変更された場合
                    if (_characterSettings.Count != _originalCharacterSettings.Count)
                    {
                        isNeedsRestart = true;
                    }
                }

                // アニメーション設定が変更されたかチェック
                if (!isNeedsRestart)
                {
                    // アニメーション設定数が変更された場合
                    if (_animationSettings.Count != _originalAnimationSettings.Count)
                    {
                        isNeedsRestart = true;
                    }
                    else
                    {
                        // 各アニメーション設定を比較
                        for (int i = 0; i < _animationSettings.Count; i++)
                        {
                            var current = _animationSettings[i];
                            var original = _originalAnimationSettings[i];

                            // セット名が変更された場合
                            if (current.animeSetName != original.animeSetName)
                            {
                                isNeedsRestart = true;
                                break;
                            }

                            // 姿勢変更ループ回数が変更された場合
                            if (current.postureChangeLoopCountStanding != original.postureChangeLoopCountStanding ||
                                current.postureChangeLoopCountSittingFloor != original.postureChangeLoopCountSittingFloor)
                            {
                                isNeedsRestart = true;
                                break;
                            }

                            // アニメーション数が変更された場合
                            if (current.animations.Count != original.animations.Count)
                            {
                                isNeedsRestart = true;
                                break;
                            }

                            // 各アニメーションの設定を比較
                            for (int j = 0; j < current.animations.Count; j++)
                            {
                                var currentAnim = current.animations[j];
                                var originalAnim = original.animations[j];

                                if (currentAnim.displayName != originalAnim.displayName ||
                                    currentAnim.animationType != originalAnim.animationType ||
                                    currentAnim.animationName != originalAnim.animationName ||
                                    currentAnim.isEnabled != originalAnim.isEnabled)
                                {
                                    isNeedsRestart = true;
                                    break;
                                }
                            }

                            if (isNeedsRestart)
                            {
                                break;
                            }
                        }
                    }
                }

                // 設定が変更された場合、メッセージボックスを表示して CocoroCore と CocoroShell を再起動
                if (isNeedsRestart)
                {
                    if (Owner is MainWindow mainWindow)
                    {
                        // チャット履歴をクリア
                        mainWindow.ChatControlInstance.ClearChat();

                        var launchCocoroCore = typeof(MainWindow).GetMethod("LaunchCocoroCore", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        var launchCocoroShell = typeof(MainWindow).GetMethod("LaunchCocoroShell", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        var launchCocoroMemory = typeof(MainWindow).GetMethod("LaunchCocoroMemory", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        if (launchCocoroCore != null && launchCocoroShell != null && launchCocoroMemory != null)
                        {
                            // ProcessOperation.RestartIfRunning (デフォルト値) を引数として渡す
                            object[] parameters = new object[] { 0 }; // ProcessOperation.RestartIfRunning = 0
                            launchCocoroCore.Invoke(mainWindow, parameters);
                            launchCocoroShell.Invoke(mainWindow, parameters);
                            launchCocoroMemory.Invoke(mainWindow, parameters);
                        }
                    }
                }
                // ウィンドウを閉じる
                Close();
            }
            catch (Exception ex)
            {
                // エラー時はボタンを再有効化
                OkButton.IsEnabled = true;
                CancelButton.IsEnabled = true;
                MessageBox.Show($"設定の保存中にエラーが発生しました: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// キャラクター位置リセットボタンのクリックイベントハンドラ
        /// </summary>
        private void ResetCharacterPositionButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var appSettings = AppSettings.Instance;

                // ウィンドウ位置を0.0にリセット
                appSettings.WindowPositionX = 0.0f;
                appSettings.WindowPositionY = 0.0f;
                appSettings.IsRestoreWindowPosition = false;
                RestoreWindowPositionCheckBox.IsChecked = false;
                appSettings.SaveAppSettings();

                // CocoroShellを再起動
#if !DEBUG
                ProcessHelper.LaunchExternalApplication("CocoroShell.exe", "CocoroShell", ProcessOperation.RestartIfRunning);
#endif
            }
            catch (Exception ex)
            {
                MessageBox.Show($"キャラクター位置のリセット中にエラーが発生しました: {ex.Message}",
                    "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// キャンセルボタンのクリックイベントハンドラ
        /// </summary>
        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            // 設定が変更されていた場合は確認ダイアログを表示
            if (_settingsChanged)
            {
                var result = MessageBox.Show("変更した設定は保存されません。よろしいですか？",
                    "確認", MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (result == MessageBoxResult.No)
                {
                    return;
                }
            }

            // 変更を破棄して元の設定に戻す
            RestoreOriginalSettings();

            // ウィンドウを閉じる
            Close();
        }

        /// <summary>
        /// すべてのタブの設定を保存する
        /// </summary>
        /// <param name="lastIsEnableNotificationApi">変更前の通知API有効フラグ</param>
        private async void SaveAllSettings(bool lastIsEnableNotificationApi)
        {
            try
            {
                SaveDisplaySettings();
                SaveCurrentCharacterSettings();
                // AppSettings に設定を反映
                UpdateAppSettings();

                // 設定をファイルに保存
                AppSettings.Instance.SaveAppSettings();

                // REST APIを通じて設定を更新（クライアントに通知）
                bool configUpdateSuccessful = false;
                string errorMessage = string.Empty;

                if (_communicationService != null && _communicationService.IsServerRunning)
                {
                    try
                    {
                        // 設定を保存してCocoroShellに通知
                        _communicationService.UpdateAndSaveConfig(AppSettings.Instance.GetConfigSettings());
                        await _communicationService.SendControlToShellAsync("reloadConfig");
                        configUpdateSuccessful = true;
                    }
                    catch (Exception ex)
                    {
                        errorMessage = ex.Message;
                        configUpdateSuccessful = false;
                    }
                }

                // 処理結果に応じてメッセージを表示
                if (!configUpdateSuccessful)
                {
                    MessageBox.Show($"クライアントへの設定通知に失敗しました。\n\n設定自体は保存されています。\n\nエラー: {errorMessage}",
                        "警告", MessageBoxButton.OK, MessageBoxImage.Warning);
                }

                // 通知APIサーバーの設定が変更された場合の処理
                bool currentIsEnableNotificationApi = (bool)_displaySettings["IsEnableNotificationApi"];
                if (lastIsEnableNotificationApi != currentIsEnableNotificationApi)
                {
                    // 通知APIサーバーの動的な起動/停止は現在サポートされていません
                    // アプリケーションの再起動が必要です
                    MessageBox.Show("通知APIサーバーの有効/無効設定が変更されました。\n変更を反映するにはアプリケーションを再起動してください。",
                        "情報", MessageBoxButton.OK, MessageBoxImage.Information);
                }

                // デスクトップウォッチの設定変更を反映
                UpdateDesktopWatchSettings();

                // マイク設定をCocoroCoreに送信
                await SendMicrophoneSettingsToCore();
            }
            catch (System.Exception ex)
            {
                MessageBox.Show($"設定の保存中にエラーが発生しました: {ex.Message}",
                    "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// マイク設定をCocoroCoreに送信
        /// </summary>
        private async Task SendMicrophoneSettingsToCore()
        {
            try
            {
                if (_communicationService != null && _communicationService.IsServerRunning)
                {
                    bool autoAdjustment = (bool)_displaySettings["MicAutoAdjustment"];
                    float inputThreshold = (float)(int)_displaySettings["MicInputThreshold"];

                    await _communicationService.SendMicrophoneSettingsToCoreAsync(autoAdjustment, inputThreshold);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"マイク設定送信エラー: {ex.Message}");
                // エラーは表示せず、ログのみに残す（CocoroCoreが起動していない場合もあるため）
            }
        }

        /// <summary>
        /// 元の設定に戻す
        /// </summary>
        private void RestoreOriginalSettings()
        {
            // 設定をバックアップから復元
            _displaySettings = new Dictionary<string, object>(_originalDisplaySettings);

            _characterSettings.Clear();
            foreach (var character in _originalCharacterSettings)
            {
                _characterSettings.Add(new Dictionary<string, string>(character));
            }

            // アニメーション設定の復元
            _animationSettings.Clear();
            foreach (var animSetting in _originalAnimationSettings)
            {
                var newAnimSetting = new AnimationSetting
                {
                    animeSetName = animSetting.animeSetName,
                    animations = new List<AnimationConfig>()
                };
                foreach (var anim in animSetting.animations)
                {
                    newAnimSetting.animations.Add(new AnimationConfig
                    {
                        displayName = anim.displayName,
                        animationType = anim.animationType,
                        animationName = anim.animationName,
                        isEnabled = anim.isEnabled
                    });
                }
                _animationSettings.Add(newAnimSetting);
            }
        }

        #endregion

        #region 設定保存メソッド

        /// <summary>
        /// ウィンドウが閉じられる前に呼び出されるイベントハンドラ
        /// </summary>
        protected override void OnClosed(EventArgs e)
        {
            // MCPタブのViewModelを破棄
            _mcpTabViewModel?.Dispose();

            // フックを解除
            if (_source != null)
            {
                _source.RemoveHook(WndProc);
                _source = null;
            }

            // グローバルキーボードフックを解除
            UnhookWindowsHookEx(_hookID);

            base.OnClosed(e);
        }

        /// <summary>
        /// 表示設定を保存する
        /// </summary>
        private void SaveDisplaySettings()
        {
            _displaySettings["RestoreWindowPosition"] = RestoreWindowPositionCheckBox.IsChecked ?? false;
            _displaySettings["TopMost"] = TopMostCheckBox.IsChecked ?? false;
            _displaySettings["EscapeCursor"] = EscapeCursorCheckBox.IsChecked ?? false;
            _displaySettings["InputVirtualKey"] = InputVirtualKeyCheckBox.IsChecked ?? false;
            _displaySettings["VirtualKeyString"] = VirtualKeyStringTextBox.Text;
            _displaySettings["AutoMove"] = AutoMoveCheckBox.IsChecked ?? false;
            _displaySettings["ShowMessageWindow"] = ShowMessageWindowCheckBox.IsChecked ?? false;
            _displaySettings["IsEnableAmbientOcclusion"] = AmbientOcclusionCheckBox.IsChecked ?? false;
            // MSAAレベルの設定を保存
            int msaaLevel = 0;
            if (MSAAComboBox.SelectedItem is ComboBoxItem msaaItem &&
                msaaItem.Tag != null)
            {
                int.TryParse(msaaItem.Tag.ToString(), out msaaLevel);
            }
            _displaySettings["MsaaLevel"] = msaaLevel;
            // キャラクターシャドウの設定を保存
            int charaShadow = 0;
            if (CharacterShadowComboBox.SelectedItem is ComboBoxItem charaShadowItem &&
                charaShadowItem.Tag != null)
            {
                int.TryParse(charaShadowItem.Tag.ToString(), out charaShadow);
            }
            _displaySettings["CharacterShadow"] = charaShadow;
            // 影の解像度を設定
            int shadowRes = 0;
            if (CharacterShadowResolutionComboBox.SelectedItem is ComboBoxItem shadowResItem &&
                shadowResItem.Tag != null)
            {
                int.TryParse(shadowResItem.Tag.ToString(), out shadowRes);
            }
            _displaySettings["CharacterShadowResolution"] = shadowRes;
            // バックグラウンドシャドウの設定を保存
            int backShadow = 0;
            if (BackgroundShadowComboBox.SelectedItem is ComboBoxItem backShadowItem &&
                backShadowItem.Tag != null)
            {
                int.TryParse(backShadowItem.Tag.ToString(), out backShadow);
            }
            _displaySettings["BackgroundShadow"] = backShadow;
            // 影の解像度を設定
            int backShadowRes = 0;
            if (BackgroundShadowResolutionComboBox.SelectedItem is ComboBoxItem backShadowResItem &&
                backShadowResItem.Tag != null)
            {
                int.TryParse(backShadowResItem.Tag.ToString(), out backShadowRes);
            }
            _displaySettings["BackgroundShadowResolution"] = backShadowRes;

            _displaySettings["WindowSize"] = WindowSizeSlider.Value;
            _displaySettings["IsEnableNotificationApi"] = IsEnableNotificationApiCheckBox.IsChecked ?? false;
            // MCPタブの設定を取得（ViewModelから）
            _displaySettings["IsEnableMcp"] = _mcpTabViewModel?.IsMcpEnabled ?? false;

            // スクリーンショット設定を保存
            _displaySettings["ScreenshotEnabled"] = ScreenshotEnabledCheckBox.IsChecked ?? false;
            _displaySettings["ScreenshotInterval"] = int.TryParse(ScreenshotIntervalTextBox.Text, out int interval) ? interval : 10;
            _displaySettings["IdleTimeout"] = int.TryParse(IdleTimeoutTextBox.Text, out int idleTimeout) ? idleTimeout : 0;
            _displaySettings["CaptureActiveWindowOnly"] = CaptureActiveWindowOnlyCheckBox.IsChecked ?? true;

            // マイク設定を保存
            _displaySettings["MicAutoAdjustment"] = MicAutoAdjustmentCheckBox.IsChecked ?? true;
            _displaySettings["MicInputThreshold"] = (int)MicThresholdSlider.Value;
        }

        /// <summary>
        /// AppSettingsを更新する
        /// </summary>
        private void UpdateAppSettings()
        {
            var appSettings = AppSettings.Instance;

            // 表示設定の更新
            appSettings.IsRestoreWindowPosition = (bool)_displaySettings["RestoreWindowPosition"];
            appSettings.IsTopmost = (bool)_displaySettings["TopMost"];
            appSettings.IsEscapeCursor = (bool)_displaySettings["EscapeCursor"];
            appSettings.IsInputVirtualKey = (bool)_displaySettings["InputVirtualKey"];
            appSettings.VirtualKeyString = (string)_displaySettings["VirtualKeyString"];
            appSettings.IsAutoMove = (bool)_displaySettings["AutoMove"];
            appSettings.ShowMessageWindow = (bool)_displaySettings["ShowMessageWindow"];
            appSettings.IsEnableAmbientOcclusion = (bool)_displaySettings["IsEnableAmbientOcclusion"];
            appSettings.MsaaLevel = (int)_displaySettings["MsaaLevel"];
            appSettings.CharacterShadow = (int)_displaySettings["CharacterShadow"];
            appSettings.CharacterShadowResolution = (int)_displaySettings["CharacterShadowResolution"];
            appSettings.BackgroundShadow = (int)_displaySettings["BackgroundShadow"];
            appSettings.BackgroundShadowResolution = (int)_displaySettings["BackgroundShadowResolution"];
            appSettings.WindowSize = (double)_displaySettings["WindowSize"] > 0 ? (int)(double)_displaySettings["WindowSize"] : 650;
            appSettings.IsEnableNotificationApi = (bool)_displaySettings["IsEnableNotificationApi"];
            appSettings.IsEnableMcp = (bool)_displaySettings["IsEnableMcp"];

            // MCPタブのViewModelにも反映
            if (_mcpTabViewModel != null)
            {
                _mcpTabViewModel.IsMcpEnabled = (bool)_displaySettings["IsEnableMcp"];
            }

            // スクリーンショット設定の更新
            appSettings.ScreenshotSettings.enabled = (bool)_displaySettings["ScreenshotEnabled"];
            appSettings.ScreenshotSettings.intervalMinutes = (int)_displaySettings["ScreenshotInterval"];
            appSettings.ScreenshotSettings.idleTimeoutMinutes = (int)_displaySettings["IdleTimeout"];
            appSettings.ScreenshotSettings.captureActiveWindowOnly = (bool)_displaySettings["CaptureActiveWindowOnly"];

            // マイク設定の更新
            appSettings.MicrophoneSettings.autoAdjustment = (bool)_displaySettings["MicAutoAdjustment"];
            appSettings.MicrophoneSettings.inputThreshold = (int)_displaySettings["MicInputThreshold"];

            // キャラクター設定の更新
            appSettings.CurrentCharacterIndex = _currentCharacterIndex;

            // キャラクターリストの更新
            var newCharacterList = new List<CharacterSettings>();

            for (int i = 0; i < _characterSettings.Count; i++)
            {
                var character = _characterSettings[i];

                // 既存のCharacterSettingsオブジェクトを取得（存在する場合）
                CharacterSettings? existingCharacter = null;
                if (i < appSettings.CharacterList.Count)
                {
                    existingCharacter = appSettings.CharacterList[i];
                }

                // 新しいCharacterSettingsオブジェクトを作成または既存のものを更新
                CharacterSettings newCharacter = existingCharacter ?? new CharacterSettings();

                // 基本項目の更新
                newCharacter.modelName = character["Name"];
                newCharacter.systemPrompt = character["SystemPrompt"];

                // IsUseLLMの設定を更新
                bool isUseLLM = false;
                if (character.ContainsKey("IsUseLLM"))
                {
                    bool.TryParse(character["IsUseLLM"], out isUseLLM);
                }
                newCharacter.isUseLLM = isUseLLM;

                // VRMFilePathの設定を更新
                if (character.ContainsKey("VRMFilePath"))
                {
                    newCharacter.vrmFilePath = character["VRMFilePath"];
                }

                // ApiKeyの設定を更新
                if (character.ContainsKey("ApiKey"))
                {
                    newCharacter.apiKey = character["ApiKey"];
                }

                // LLMModelの設定を更新
                if (character.ContainsKey("LLMModel"))
                {
                    newCharacter.llmModel = character["LLMModel"];
                }

                // IsUseTTSの設定を更新
                bool isUseTTS = false;
                if (character.ContainsKey("IsUseTTS"))
                {
                    bool.TryParse(character["IsUseTTS"], out isUseTTS);
                }
                newCharacter.isUseTTS = isUseTTS;

                // TTSEndpointURLの設定を更新
                if (character.ContainsKey("TTSEndpointURL"))
                {
                    newCharacter.ttsEndpointURL = character["TTSEndpointURL"];
                }

                // TTSSperkerIDの設定を更新
                if (character.ContainsKey("TTSSperkerID"))
                {
                    newCharacter.ttsSperkerID = character["TTSSperkerID"];
                }

                // IsEnableMemoryの設定を更新
                bool isEnableMemory = true; // デフォルトはtrue
                if (character.ContainsKey("IsEnableMemory"))
                {
                    bool.TryParse(character["IsEnableMemory"], out isEnableMemory);
                }
                newCharacter.isEnableMemory = isEnableMemory;

                // UserIdの設定を更新
                if (character.ContainsKey("UserId"))
                {
                    newCharacter.userId = character["UserId"];
                }

                // EmbeddedApiKeyの設定を更新
                if (character.ContainsKey("EmbeddedApiKey"))
                {
                    newCharacter.embeddedApiKey = character["EmbeddedApiKey"];
                }

                // EmbeddedModelの設定を更新
                if (character.ContainsKey("EmbeddedModel"))
                {
                    newCharacter.embeddedModel = character["EmbeddedModel"];
                }

                // IsUseSTTの設定を更新
                bool isUseSTT = false;
                if (character.ContainsKey("IsUseSTT"))
                {
                    bool.TryParse(character["IsUseSTT"], out isUseSTT);
                }
                newCharacter.isUseSTT = isUseSTT;

                // STTEngineの設定を更新
                if (character.ContainsKey("STTEngine"))
                {
                    newCharacter.sttEngine = character["STTEngine"];
                }
                else
                {
                    newCharacter.sttEngine = "amivoice"; // デフォルト値
                }

                // STTWakeWordの設定を更新
                if (character.ContainsKey("STTWakeWord"))
                {
                    newCharacter.sttWakeWord = character["STTWakeWord"];
                }

                // STTApiKeyの設定を更新
                if (character.ContainsKey("STTApiKey"))
                {
                    newCharacter.sttApiKey = character["STTApiKey"];
                }

                // STTLanguageの設定を更新（GUIでは設定しないが、保存時は既存の値を保持）
                if (character.ContainsKey("STTLanguage"))
                {
                    newCharacter.sttLanguage = character["STTLanguage"];
                }
                else if (existingCharacter != null)
                {
                    newCharacter.sttLanguage = existingCharacter.sttLanguage;
                }
                else
                {
                    newCharacter.sttLanguage = "ja"; // デフォルト値
                }

                // Shadow設定を更新
                bool isEnableShadowOff = true; // デフォルトはtrue
                if (character.ContainsKey("IsEnableShadowOff"))
                {
                    bool.TryParse(character["IsEnableShadowOff"], out isEnableShadowOff);
                }
                newCharacter.isEnableShadowOff = isEnableShadowOff;

                if (character.ContainsKey("ShadowOffMesh"))
                {
                    newCharacter.shadowOffMesh = character["ShadowOffMesh"];
                }
                else
                {
                    newCharacter.shadowOffMesh = "Face, U_Char_1"; // デフォルト値
                }

                // IsConvertMToonの設定を更新
                bool isConvertMToon = false;
                if (character.ContainsKey("IsConvertMToon"))
                {
                    bool.TryParse(character["IsConvertMToon"], out isConvertMToon);
                }
                newCharacter.isConvertMToon = isConvertMToon;

                // 既存の設定を保持（null になることはないという前提）
                newCharacter.isReadOnly = existingCharacter?.isReadOnly ?? false;

                // リストに追加
                newCharacterList.Add(newCharacter);
            }

            // 更新したリストをAppSettingsに設定
            appSettings.CharacterList = newCharacterList;

            // アニメーション設定の更新
            appSettings.CurrentAnimationSettingIndex = AppSettings.Instance.CurrentAnimationSettingIndex;
            appSettings.AnimationSettings = new List<AnimationSetting>(_animationSettings);
        }

        #endregion

        #region VRMファイル選択イベントハンドラ

        /// <summary>
        /// VRMファイル参照ボタンのクリックイベント
        /// </summary>
        private void BrowseVrmFileButton_Click(object sender, RoutedEventArgs e)
        {
            // ファイルダイアログの設定
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "VRMファイルを選択",
                Filter = "VRMファイル (*.vrm)|*.vrm|すべてのファイル (*.*)|*.*",
                CheckFileExists = true,
                Multiselect = false
            };

            // ダイアログを表示
            if (dialog.ShowDialog() == true)
            {
                // 選択されたファイルのパスをテキストボックスに設定
                VRMFilePathTextBox.Text = dialog.FileName;
            }
        }

        #endregion

        private void LoadLicenseText()
        {
            try
            {
                // 埋め込みリソースからライセンステキストを読み込む
                var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                var resourceName = "CocoroDock.Resource.License.txt";

                using (var stream = assembly.GetManifestResourceStream(resourceName))
                {
                    if (stream != null)
                    {
                        using (var reader = new System.IO.StreamReader(stream))
                        {
                            string licenseText = reader.ReadToEnd();
                            LicenseTextBox.Text = licenseText;
                        }
                    }
                    else
                    {
                        // リソースが見つからない場合
                        LicenseTextBox.Text = "ライセンスリソースが見つかりませんでした。";
                    }
                }
            }
            catch (Exception ex)
            {
                // エラーが発生した場合
                LicenseTextBox.Text = $"ライセンスリソースの読み込み中にエラーが発生しました: {ex.Message}";
            }
        }

        /// <summary>
        /// API説明テキストを設定
        /// </summary>
        private void SetApiDescriptionText()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("エンドポイント:");
            sb.AppendLine("POST http://127.0.0.1:55604/api/v1/notification");
            sb.AppendLine();
            sb.AppendLine("リクエストボディ (JSON):");
            sb.AppendLine("{");
            sb.AppendLine("  \"from\": \"アプリ名\",");
            sb.AppendLine("  \"message\": \"通知メッセージ\",");
            sb.AppendLine("  \"images\": [  // オプション（最大5枚）");
            sb.AppendLine("    \"data:image/jpeg;base64,/9j/4AAQ...\",  // 1枚目");
            sb.AppendLine("    \"data:image/png;base64,iVBORw0KGgo...\"  // 2枚目");
            sb.AppendLine("  ]");
            sb.AppendLine("}");
            sb.AppendLine();
            sb.AppendLine("レスポンス:");
            sb.AppendLine("HTTP/1.1 204 No Content");
            sb.AppendLine();
            sb.AppendLine("使用例 (cURL):");
            sb.AppendLine("# 1枚の画像を送る場合");
            sb.AppendLine("curl -X POST http://127.0.0.1:55604/api/v1/notification \\");
            sb.AppendLine("  -H \"Content-Type: application/json\" \\");
            sb.AppendLine("  -d '{\"from\":\"MyApp\",\"message\":\"処理完了\",\"images\":[\"data:image/jpeg;base64,...\"]}'");
            sb.AppendLine();
            sb.AppendLine("# 複数枚の画像を送る場合");
            sb.AppendLine("curl -X POST http://127.0.0.1:55604/api/v1/notification \\");
            sb.AppendLine("  -H \"Content-Type: application/json\" \\");
            sb.AppendLine("  -d '{\"from\":\"MyApp\",\"message\":\"結果\",\"images\":[\"data:image/jpeg;base64,...\",\"data:image/png;base64,...\"]}'");
            sb.AppendLine();
            sb.AppendLine("使用例 (PowerShell):");
            sb.AppendLine("# 複数枚の画像を送る場合");
            sb.AppendLine("Invoke-RestMethod -Method Post `");
            sb.AppendLine("  -Uri \"http://127.0.0.1:55604/api/v1/notification\" `");
            sb.AppendLine("  -ContentType \"application/json; charset=utf-8\" `");
            sb.AppendLine("  -Body '{\"from\":\"MyApp\",\"message\":\"結果\",\"images\":[\"data:image/jpeg;base64,...\",\"data:image/png;base64,...\"]}'");
            ApiDescriptionTextBox.Text = sb.ToString();
        }

        /// <summary>
        /// グローバルキーボードフックの設定
        /// </summary>
        private IntPtr SetHook(LowLevelKeyboardProc proc)
        {
            using (var curProcess = Process.GetCurrentProcess())
            using (var curModule = curProcess.MainModule)
            {
                return SetWindowsHookEx(WH_KEYBOARD_LL, proc,
                    GetModuleHandle(curModule?.ModuleName ?? string.Empty), 0);
            }
        }

        /// <summary>
        /// グローバルキーボードフックのコールバック
        /// </summary>
        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && _isCapturingKey)
            {
                int vkCode = Marshal.ReadInt32(lParam);

                // WindowsキーとAltキーの処理
                if (vkCode == VK_LWIN || vkCode == VK_RWIN)
                {
                    // WM_KEYDOWNまたはWM_SYSKEYDOWNの場合はWinキーが押された
                    if ((int)wParam == WM_KEYDOWN || (int)wParam == WM_SYSKEYDOWN)
                    {
                        _isWinKeyPressed = true;
                        // キー入力キャプチャ中はキーの通常の処理を止める
                        return (IntPtr)1;
                    }
                    // WM_KEYUPまたはWM_SYSKEYUPの場合はWinキーが離された
                    else if ((int)wParam == WM_KEYUP || (int)wParam == WM_SYSKEYUP)
                    {
                        _isWinKeyPressed = false;
                    }
                }
                // Altキーの処理 (システムキーメニュー表示の抑制)
                else if (vkCode == VK_LALT || vkCode == VK_RALT)
                {
                    if ((int)wParam == WM_KEYDOWN || (int)wParam == WM_SYSKEYDOWN)
                    {
                        _isAltKeyPressed = true;
                        // キー入力キャプチャ中はAltキーの通常の処理を止める
                        return (IntPtr)1;
                    }
                    else if ((int)wParam == WM_KEYUP || (int)wParam == WM_SYSKEYUP)
                    {
                        _isAltKeyPressed = false;
                    }
                }
            }
            return CallNextHookEx(_hookID, nCode, wParam, lParam);
        }

        /// <summary>
        /// ハイパーリンクをクリックしたときにブラウザで開く
        /// </summary>
        private void Hyperlink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = e.Uri.AbsoluteUri,
                    UseShellExecute = true
                });
                e.Handled = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"URLを開けませんでした: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// キャラクター設定のバリデーション（警告のみ）
        /// </summary>
        /// <returns>続行する場合はtrue、キャンセルする場合はfalse</returns>
        private bool ValidateCharacterSettings()
        {
            var warnings = new List<string>();

            foreach (var character in _characterSettings)
            {
                string characterName = character["Name"];

                // LLMが有効なのにLLM Modelが空欄
                bool isUseLLM = false;
                if (character.ContainsKey("IsUseLLM"))
                {
                    bool.TryParse(character["IsUseLLM"], out isUseLLM);
                }
                if (isUseLLM && string.IsNullOrWhiteSpace(character["LLMModel"]))
                {
                    warnings.Add($"・キャラクター「{characterName}」でLLMが有効ですが、LLM Modelが空欄です");
                }

                // 記憶機能が有効なのに関連する各項目が空欄
                bool isEnableMemory = true;
                if (character.ContainsKey("IsEnableMemory"))
                {
                    bool.TryParse(character["IsEnableMemory"], out isEnableMemory);
                }
                if (isEnableMemory)
                {
                    if (string.IsNullOrWhiteSpace(character.ContainsKey("UserId") ? character["UserId"] : ""))
                    {
                        warnings.Add($"・キャラクター「{characterName}」で記憶機能が有効ですが、ユーザーIDが空欄です");
                    }
                    if (string.IsNullOrWhiteSpace(character.ContainsKey("EmbeddedApiKey") ? character["EmbeddedApiKey"] : ""))
                    {
                        warnings.Add($"・キャラクター「{characterName}」で記憶機能が有効ですが、埋め込みAPIキーが空欄です");
                    }
                    if (string.IsNullOrWhiteSpace(character.ContainsKey("EmbeddedModel") ? character["EmbeddedModel"] : ""))
                    {
                        warnings.Add($"・キャラクター「{characterName}」で記憶機能が有効ですが、埋め込みモデルが空欄です");
                    }
                }

                // TTSが有効なのに関連する各項目が空欄
                bool isUseTTS = false;
                if (character.ContainsKey("IsUseTTS"))
                {
                    bool.TryParse(character["IsUseTTS"], out isUseTTS);
                }
                if (isUseTTS)
                {
                    if (string.IsNullOrWhiteSpace(character["TTSEndpointURL"]))
                    {
                        warnings.Add($"・キャラクター「{characterName}」でTTSが有効ですが、TTSエンドポイントURLが空欄です");
                    }
                    if (string.IsNullOrWhiteSpace(character["TTSSperkerID"]))
                    {
                        warnings.Add($"・キャラクター「{characterName}」でTTSが有効ですが、TTSスピーカーIDが空欄です");
                    }
                }

                // STTが有効なのに関連する各項目が空欄
                bool isUseSTT = false;
                if (character.ContainsKey("IsUseSTT"))
                {
                    bool.TryParse(character["IsUseSTT"], out isUseSTT);
                }
                if (isUseSTT)
                {
                    if (string.IsNullOrWhiteSpace(character.ContainsKey("STTWakeWord") ? character["STTWakeWord"] : ""))
                    {
                        warnings.Add($"・キャラクター「{characterName}」でSTTが有効ですが、STT 起動ワードが空欄です");
                    }
                    if (string.IsNullOrWhiteSpace(character.ContainsKey("STTApiKey") ? character["STTApiKey"] : ""))
                    {
                        warnings.Add($"・キャラクター「{characterName}」でSTTが有効ですが、STT APIキーが空欄です");
                    }
                }
            }

            // 警告がある場合はまとめて表示
            if (warnings.Count > 0)
            {
                string message = "以下の設定に問題があります:\n\n" + string.Join("\n", warnings) + "\n\nこのまま保存しますか？";
                var result = MessageBox.Show(message, "設定の警告", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (result == MessageBoxResult.No)
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// デスクトップウォッチ設定の変更を適用
        /// </summary>
        private void UpdateDesktopWatchSettings()
        {
            try
            {
                // MainWindowのインスタンスを取得
                var mainWindow = Application.Current.MainWindow as MainWindow;
                if (mainWindow != null)
                {
                    // MainWindowのUpdateScreenshotServiceメソッドを呼び出す
                    var updateMethod = mainWindow.GetType().GetMethod("UpdateScreenshotService",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                    if (updateMethod != null)
                    {
                        updateMethod.Invoke(mainWindow, null);
                        Debug.WriteLine("デスクトップウォッチ設定を更新しました");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"デスクトップウォッチ設定の更新中にエラーが発生しました: {ex.Message}");
            }
        }

        /// <summary>
        /// 単一キャラクター設定のバリデーション（警告のみ）
        /// </summary>
        private void ValidateSingleCharacterSettings(int characterIndex)
        {
            if (characterIndex < 0 || characterIndex >= _characterSettings.Count)
            {
                return;
            }

            var character = _characterSettings[characterIndex];
            string characterName = character["Name"];
            var warnings = new List<string>();

            // LLMが有効なのにLLM Modelが空欄
            bool isUseLLM = false;
            if (character.ContainsKey("IsUseLLM"))
            {
                bool.TryParse(character["IsUseLLM"], out isUseLLM);
            }
            if (isUseLLM && string.IsNullOrWhiteSpace(character["LLMModel"]))
            {
                warnings.Add($"・LLMが有効ですが、LLM Modelが空欄です");
            }

            // 記憶機能が有効なのに関連する各項目が空欄
            bool isEnableMemory = true;
            if (character.ContainsKey("IsEnableMemory"))
            {
                bool.TryParse(character["IsEnableMemory"], out isEnableMemory);
            }
            if (isEnableMemory)
            {
                if (string.IsNullOrWhiteSpace(character.ContainsKey("UserId") ? character["UserId"] : ""))
                {
                    warnings.Add($"・記憶機能が有効ですが、ユーザーIDが空欄です");
                }
                if (string.IsNullOrWhiteSpace(character.ContainsKey("EmbeddedApiKey") ? character["EmbeddedApiKey"] : ""))
                {
                    warnings.Add($"・記憶機能が有効ですが、埋め込みAPIキーが空欄です");
                }
                if (string.IsNullOrWhiteSpace(character.ContainsKey("EmbeddedModel") ? character["EmbeddedModel"] : ""))
                {
                    warnings.Add($"・記憶機能が有効ですが、埋め込みモデルが空欄です");
                }
            }

            // TTSが有効なのに関連する各項目が空欄
            bool isUseTTS = false;
            if (character.ContainsKey("IsUseTTS"))
            {
                bool.TryParse(character["IsUseTTS"], out isUseTTS);
            }
            if (isUseTTS)
            {
                if (string.IsNullOrWhiteSpace(character["TTSEndpointURL"]))
                {
                    warnings.Add($"・TTSが有効ですが、TTSエンドポイントURLが空欄です");
                }
                if (string.IsNullOrWhiteSpace(character["TTSSperkerID"]))
                {
                    warnings.Add($"・TTSが有効ですが、TTSスピーカーIDが空欄です");
                }
            }

            // STTが有効なのに関連する各項目が空欄
            bool isUseSTT = false;
            if (character.ContainsKey("IsUseSTT"))
            {
                bool.TryParse(character["IsUseSTT"], out isUseSTT);
            }
            if (isUseSTT)
            {
                if (string.IsNullOrWhiteSpace(character.ContainsKey("STTWakeWord") ? character["STTWakeWord"] : ""))
                {
                    warnings.Add($"・STTが有効ですが、STT 起動ワードが空欄です");
                }
                if (string.IsNullOrWhiteSpace(character.ContainsKey("STTApiKey") ? character["STTApiKey"] : ""))
                {
                    warnings.Add($"・STTが有効ですが、STT APIキーが空欄です");
                }
            }

            // 警告がある場合は表示
            if (warnings.Count > 0)
            {
                string message = $"キャラクター「{characterName}」の設定に問題があります:\n\n" + string.Join("\n", warnings);
                MessageBox.Show(message, "設定の警告", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        /// <summary>
        /// PostureChangeLoopCountStandingテキストボックスの値変更時の処理
        /// </summary>
        private void PostureChangeLoopCountStandingTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (AnimationSetComboBox.SelectedIndex >= 0 &&
                AnimationSetComboBox.SelectedIndex < _animationSettings.Count)
            {
                if (int.TryParse(PostureChangeLoopCountStandingTextBox.Text, out int loopCount))
                {
                    if (loopCount > 0 && loopCount <= 100) // 妥当な範囲の値のみ受け付ける
                    {
                        _animationSettings[AnimationSetComboBox.SelectedIndex].postureChangeLoopCountStanding = loopCount;
                    }
                }
            }
        }

        /// <summary>
        /// PostureChangeLoopCountSittingFloorテキストボックスの値変更時の処理
        /// </summary>
        private void PostureChangeLoopCountSittingFloorTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (AnimationSetComboBox.SelectedIndex >= 0 &&
                AnimationSetComboBox.SelectedIndex < _animationSettings.Count)
            {
                if (int.TryParse(PostureChangeLoopCountSittingFloorTextBox.Text, out int loopCount))
                {
                    if (loopCount > 0 && loopCount <= 100) // 妥当な範囲の値のみ受け付ける
                    {
                        _animationSettings[AnimationSetComboBox.SelectedIndex].postureChangeLoopCountSittingFloor = loopCount;
                    }
                }
            }
        }

        /// <summary>
        /// EnableShadowOffCheckBoxのチェック時の処理
        /// </summary>
        private void EnableShadowOffCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            // チェックが入った時はテキストボックスを無効化
            if (ShadowOffMeshTextBox != null)
            {
                ShadowOffMeshTextBox.IsEnabled = true;
            }
        }

        /// <summary>
        /// EnableShadowOffCheckBoxのアンチェック時の処理
        /// </summary>
        private void EnableShadowOffCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            // チェックが外れた時はテキストボックスを有効化
            if (ShadowOffMeshTextBox != null)
            {
                ShadowOffMeshTextBox.IsEnabled = false;
            }
        }

        /// <summary>
        /// MCPタブの初期化
        /// </summary>
        private void InitializeMcpTab()
        {
            try
            {
                var appSettings = AppSettings.Instance;
                _mcpTabViewModel = new McpTabViewModel(appSettings);

                // MCPタブのDataContextを設定
                ((TabItem)AdminTabControl.Items[4]).DataContext = _mcpTabViewModel; // MCPタブは5番目（0ベース）

                // UIエレメントとViewModelのバインディング
                McpEnabledCheckBox.IsChecked = _mcpTabViewModel.IsMcpEnabled;
                McpConfigTextBox.Text = _mcpTabViewModel.McpConfigJson;
                McpServersList.ItemsSource = _mcpTabViewModel.McpServers;
                McpStatusMessage.Text = _mcpTabViewModel.StatusMessage;

                // イベントハンドラーの登録
                McpEnabledCheckBox.Checked += (s, e) =>
                {
                    _mcpTabViewModel.IsMcpEnabled = true;
                };
                McpEnabledCheckBox.Unchecked += (s, e) =>
                {
                    _mcpTabViewModel.IsMcpEnabled = false;
                };

                McpConfigTextBox.TextChanged += (s, e) =>
                {
                    _mcpTabViewModel.McpConfigJson = McpConfigTextBox.Text;
                };


                // ViewModelプロパティ変更イベントの監視
                _mcpTabViewModel.PropertyChanged += (s, e) =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        switch (e.PropertyName)
                        {
                            case nameof(McpTabViewModel.McpServers):
                                McpServersList.ItemsSource = _mcpTabViewModel.McpServers;
                                break;
                            case nameof(McpTabViewModel.StatusMessage):
                                McpStatusMessage.Text = _mcpTabViewModel.StatusMessage;
                                break;
                            case nameof(McpTabViewModel.DiagnosticDetails):
                                DiagnosticDetailsTextBox.Text = _mcpTabViewModel.DiagnosticDetails;
                                break;
                            case nameof(McpTabViewModel.IsLoading):
                                // ローディング表示は削除されたため、何もしない
                                break;
                        }
                    });
                };

                // 初期化完了（監視は自動的に開始される）
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MCPタブ初期化エラー: {ex.Message}");
                McpStatusMessage.Text = $"初期化エラー: {ex.Message}";
            }
        }

    }
}
