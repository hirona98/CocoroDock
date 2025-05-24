using CocoroDock.Communication;
using CocoroDock.Services;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
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
        private CommunicationService? _communicationService;        // キーボードフック用
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

        public AdminWindow()
        {
            InitializeComponent();

            // グローバルキーボードフックの設定
            _proc = HookCallback;
            _hookID = SetHook(_proc);

            // 表示設定の初期化
            InitializeDisplaySettings();

            // キャラクター設定の初期化
            InitializeCharacterSettings();

            // ボタンイベントの登録
            RegisterButtonEvents();

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
        }

        /// <summary>
        /// 表示設定の初期化
        /// </summary>
        private void InitializeDisplaySettings()
        {
            // アプリ設定からの初期値を取得
            var appSettings = AppSettings.Instance;

            // UIに反映
            TopMostCheckBox.IsChecked = appSettings.IsTopmost;
            EscapeCursorCheckBox.IsChecked = appSettings.IsEscapeCursor;
            InputVirtualKeyCheckBox.IsChecked = appSettings.IsInputVirtualKey;
            VirtualKeyStringTextBox.Text = appSettings.VirtualKeyString;
            AutoMoveCheckBox.IsChecked = appSettings.IsAutoMove;
            AmbientOcclusionCheckBox.IsChecked = appSettings.IsEnableAmbientOcclusion;
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
                { "WindowSize", appSettings.WindowSize }
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
                    { "UserId", character.userId ?? "User01" }
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
        }

        #endregion

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
                UserIdTextBox.Text = _characterSettings[index].ContainsKey("UserId") ? _characterSettings[index]["UserId"] : "User01";

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
                { "UserId", "User01" }
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
                var userId = UserIdTextBox.Text;

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
                bool userIdChanged = !_characterSettings[_currentCharacterIndex].ContainsKey("UserId") ||
                                     _characterSettings[_currentCharacterIndex]["UserId"] != userId;

                if (_characterSettings[_currentCharacterIndex]["Name"] != name ||
                    _characterSettings[_currentCharacterIndex]["SystemPrompt"] != systemPrompt ||
                    isUseLLMChanged || isUseTTSChanged || vrmFilePathChanged || apiKeyChanged || llmModelChanged ||
                    ttsEndpointURLChanged || ttsSperkerIDChanged || userIdChanged)
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
                    _characterSettings[_currentCharacterIndex]["UserId"] = userId;

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
        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            // 設定変更前の値を保存
            int lastSelectedIndex = AppSettings.Instance.CurrentCharacterIndex;
            string lastVRMFilePath = string.Empty;
            bool lastIsUseLLM = false;
            if (lastSelectedIndex >= 0 && lastSelectedIndex < AppSettings.Instance.CharacterList.Count)
            {
                lastVRMFilePath = AppSettings.Instance.CharacterList[lastSelectedIndex].vrmFilePath ?? string.Empty;
                lastIsUseLLM = AppSettings.Instance.CharacterList[lastSelectedIndex].isUseLLM;
            }

            // すべてのタブの設定を保存
            SaveAllSettings();

            // VRMFilePathかSelectedIndexが変更されたかチェック
            bool isNeedsRestart = false;
            int currentSelectedIndex = AppSettings.Instance.CurrentCharacterIndex;
            string currentVRMFilePath = string.Empty;
            bool currentIsUseLLM = false;
            if (currentSelectedIndex >= 0 && currentSelectedIndex < AppSettings.Instance.CharacterList.Count)
            {
                currentVRMFilePath = AppSettings.Instance.CharacterList[currentSelectedIndex].vrmFilePath ?? string.Empty;
                currentIsUseLLM = AppSettings.Instance.CharacterList[currentSelectedIndex].isUseLLM;
            }
            // SelectedIndexが変更された場合
            if (lastSelectedIndex != currentSelectedIndex)
            {
                isNeedsRestart = true;
            }
            // VRMFilePathが変更された場合（同じキャラクターの場合のみチェック）
            if (lastSelectedIndex == currentSelectedIndex && lastVRMFilePath != currentVRMFilePath)
            {
                isNeedsRestart = true;
            }
            // IsUseLLMが変更された場合（同じキャラクターの場合のみチェック）
            if (lastSelectedIndex == currentSelectedIndex && lastIsUseLLM != currentIsUseLLM)
            {
                isNeedsRestart = true;
            }
            // 設定が変更された場合、メッセージボックスを表示して CocoroCore と CocoroShell を再起動
            if (isNeedsRestart)
            {
                if (Owner is MainWindow mainWindow)
                {
                    // チャット履歴をクリア
                    mainWindow.ChatControlInstance.ClearChat(); var launchCocoroCore = typeof(MainWindow).GetMethod("LaunchCocoroCore", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    var launchCocoroShell = typeof(MainWindow).GetMethod("LaunchCocoroShell", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    if (launchCocoroCore != null && launchCocoroShell != null)
                    {
                        // ProcessOperation.RestartIfRunning (デフォルト値) を引数として渡す
                        object[] parameters = new object[] { 0 }; // ProcessOperation.RestartIfRunning = 0
                        launchCocoroCore.Invoke(mainWindow, parameters);
                        launchCocoroShell.Invoke(mainWindow, parameters);
                    }
                }
            }

            // ウィンドウを閉じる
            DialogResult = true;
            Close();
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
            DialogResult = false;
            Close();
        }

        /// <summary>
        /// すべてのタブの設定を保存する
        /// </summary>
        private async void SaveAllSettings()
        {
            try
            {
                SaveDisplaySettings();
                SaveCurrentCharacterSettings();
                // AppSettings に設定を反映
                UpdateAppSettings();

                // 設定をファイルに保存
                AppSettings.Instance.SaveAppSettings();

                // WebSocketを通じて設定を更新（クライアントに通知）
                bool configUpdateSuccessful = false;
                string errorMessage = string.Empty;

                if (_communicationService != null && _communicationService.IsServerRunning)
                {
                    try
                    {
                        // 通信サービスによる設定更新を実行
                        await _communicationService.UpdateConfigAsync(AppSettings.Instance.GetConfigSettings());
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
            }
            catch (System.Exception ex)
            {
                MessageBox.Show($"設定の保存中にエラーが発生しました: {ex.Message}",
                    "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
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
        }

        #endregion

        #region 設定保存メソッド

        /// <summary>
        /// ウィンドウが閉じられる前に呼び出されるイベントハンドラ
        /// </summary>
        protected override void OnClosed(EventArgs e)
        {
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
            _displaySettings["TopMost"] = TopMostCheckBox.IsChecked ?? false;
            _displaySettings["EscapeCursor"] = EscapeCursorCheckBox.IsChecked ?? false;
            _displaySettings["InputVirtualKey"] = InputVirtualKeyCheckBox.IsChecked ?? false;
            _displaySettings["VirtualKeyString"] = VirtualKeyStringTextBox.Text;
            _displaySettings["AutoMove"] = AutoMoveCheckBox.IsChecked ?? false;
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
        }

        /// <summary>
        /// AppSettingsを更新する
        /// </summary>
        private void UpdateAppSettings()
        {
            var appSettings = AppSettings.Instance;

            // 表示設定の更新
            appSettings.IsTopmost = (bool)_displaySettings["TopMost"];
            appSettings.IsEscapeCursor = (bool)_displaySettings["EscapeCursor"];
            appSettings.IsInputVirtualKey = (bool)_displaySettings["InputVirtualKey"];
            appSettings.VirtualKeyString = (string)_displaySettings["VirtualKeyString"];
            appSettings.IsAutoMove = (bool)_displaySettings["AutoMove"];
            appSettings.IsEnableAmbientOcclusion = (bool)_displaySettings["IsEnableAmbientOcclusion"];
            appSettings.MsaaLevel = (int)_displaySettings["MsaaLevel"];
            appSettings.CharacterShadow = (int)_displaySettings["CharacterShadow"];
            appSettings.CharacterShadowResolution = (int)_displaySettings["CharacterShadowResolution"];
            appSettings.BackgroundShadow = (int)_displaySettings["BackgroundShadow"];
            appSettings.BackgroundShadowResolution = (int)_displaySettings["BackgroundShadowResolution"];
            appSettings.WindowSize = (double)_displaySettings["WindowSize"] > 0 ? (int)(double)_displaySettings["WindowSize"] : 650;

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

                // UserIdの設定を更新
                if (character.ContainsKey("UserId"))
                {
                    newCharacter.userId = character["UserId"];
                }

                // 既存の設定を保持（null になることはないという前提）
                newCharacter.isReadOnly = existingCharacter?.isReadOnly ?? false;

                // リストに追加
                newCharacterList.Add(newCharacter);
            }

            // 更新したリストをAppSettingsに設定
            appSettings.CharacterList = newCharacterList;
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
                // License.txtファイルのパスを取得
                string licenseFilePath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "License.txt");

                // ファイルが存在するか確認
                if (System.IO.File.Exists(licenseFilePath))
                {
                    // ファイルの内容を読み込む
                    string licenseText = System.IO.File.ReadAllText(licenseFilePath);

                    // LicenseTextBoxに表示
                    LicenseTextBox.Text = licenseText;
                }
                else
                {
                    // ファイルが見つからない場合
                    LicenseTextBox.Text = "ライセンスファイルが見つかりませんでした。";
                }
            }
            catch (Exception ex)
            {
                // エラーが発生した場合
                LicenseTextBox.Text = $"ライセンスファイルの読み込み中にエラーが発生しました: {ex.Message}";
            }
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
    }
}
