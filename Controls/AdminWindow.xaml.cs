using CocoroDock.Communication;
using CocoroDock.Services;
using CocoroDock.Utilities;
using CocoroDock.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
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


        // 通信サービス
        private ICommunicationService? _communicationService;



        // キーボードフック用
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

            // 逃げ先座標設定の初期化
            InitializeEscapePositionControl();

            // ボタンイベントの登録
            RegisterButtonEvents();

            // MCPタブの初期化
            McpSettingsControl.Initialize();

            // システム設定コントロールを初期化
            SystemSettingsControl.Initialize();

            // システム設定変更イベントを登録
            SystemSettingsControl.SettingsChanged += (sender, args) =>
            {
                // 設定変更の記録（必要に応じて処理を追加）
            };

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

            // システム設定（通知API、スクリーンショット、マイク）はSystemSettingsControlで初期化済み

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
            // CharacterManagementControlの初期化
            CharacterManagementControl.SetCommunicationService(_communicationService);
            CharacterManagementControl.Initialize();

            // 設定変更イベントを登録
            CharacterManagementControl.SettingsChanged += (sender, args) =>
            {
                // 設定が変更されたときの処理（必要に応じて）
            };

            // キャラクター変更イベントを登録
            CharacterManagementControl.CharacterChanged += (sender, args) =>
            {
                // 現在のキャラクターインデックスを更新
                _currentCharacterIndex = CharacterManagementControl.GetCurrentCharacterIndex();

                // アニメーション設定を更新
                AnimationSettingsControl.Initialize();
            };

            // 現在のキャラクターインデックスを取得
            _currentCharacterIndex = CharacterManagementControl.GetCurrentCharacterIndex();

            // アニメーション設定コントロールを初期化
            AnimationSettingsControl.SetCommunicationService(_communicationService);
            AnimationSettingsControl.Initialize();

            // アニメーション設定変更イベントを登録
            AnimationSettingsControl.SettingsChanged += (sender, args) =>
            {
                // 設定変更の記録（必要に応じて処理を追加）
            };
        }

        /// <summary>
        /// 逃げ先座標設定コントロールの初期化
        /// </summary>
        private void InitializeEscapePositionControl()
        {
            // UserControlに設定変更イベントを登録
            EscapePositionControl.SettingsChanged += (sender, args) =>
            {
                // 設定が変更されたときの処理（必要に応じて）
            };

            // 設定から逃げ先座標を読み込み
            EscapePositionControl.LoadEscapePositionsFromSettings();
        }

        /// <summary>
        /// 現在の設定をバックアップする
        /// </summary>
        private void BackupSettings()
        {
            // 表示設定のバックアップ
            _originalDisplaySettings = new Dictionary<string, object>(_displaySettings);

            // 通知API設定が含まれていない場合は現在の値を追加
            if (!_originalDisplaySettings.ContainsKey("IsEnableNotificationApi"))
            {
                _originalDisplaySettings["IsEnableNotificationApi"] = AppSettings.Instance.IsEnableNotificationApi;
            }

            // キャラクター設定のディープコピー
            _originalCharacterSettings = new List<Dictionary<string, string>>();
            foreach (var character in _characterSettings)
            {
                _originalCharacterSettings.Add(new Dictionary<string, string>(character));
            }

        }

        #endregion

        #region 表示設定メソッド


        /// <summary>
        /// UIの設定値をメモリに保存
        /// </summary>
        private void UpdateDisplaySettings()
        {
            // SystemSettingsControlから設定を取得
            _displaySettings["IsEnableNotificationApi"] = SystemSettingsControl.GetIsEnableNotificationApi();

            var screenshotSettings = SystemSettingsControl.GetScreenshotSettings();
            _displaySettings["ScreenshotEnabled"] = screenshotSettings.enabled;
            _displaySettings["ScreenshotInterval"] = screenshotSettings.intervalMinutes;
            _displaySettings["IdleTimeout"] = screenshotSettings.idleTimeoutMinutes;
            _displaySettings["CaptureActiveWindowOnly"] = screenshotSettings.captureActiveWindowOnly;

            var microphoneSettings = SystemSettingsControl.GetMicrophoneSettings();
            _displaySettings["MicAutoAdjustment"] = microphoneSettings.autoAdjustment;
            _displaySettings["MicInputThreshold"] = microphoneSettings.inputThreshold;
        }

        #endregion

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
        /// キャラクター情報をUIに反映（CharacterManagementControlに移行済み）
        /// </summary>
        private void UpdateCharacterUI(int index)
        {
            // CharacterManagementControlに移行済み - このメソッドは使用されません
            return;
        }

        #region キャラクター設定イベントハンドラ





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
                // CharacterManagementControlに移行済み

                // CharacterManagementControlでバリデーション実行済み

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
                if (McpSettingsControl.GetViewModel() != null)
                {
                    await McpSettingsControl.GetViewModel()!.ApplyMcpSettingsAsync();
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

                // ウィンドウ位置を未設定状態にリセット（CocoroShellが初期位置を使用する）
                appSettings.WindowPositionX = float.MinValue;
                appSettings.WindowPositionY = float.MinValue;
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
                // SystemSettingsControlから設定を取得して辞書に保存
                UpdateDisplaySettings();

                SaveDisplaySettings();
                // CharacterManagementControlに移行済み
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
                bool currentIsEnableNotificationApi = SystemSettingsControl.GetIsEnableNotificationApi();
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

        }

        #endregion

        #region 設定保存メソッド

        /// <summary>
        /// ウィンドウが閉じられる前に呼び出されるイベントハンドラ
        /// </summary>
        protected override void OnClosed(EventArgs e)
        {
            // MCPタブのViewModelを破棄
            McpSettingsControl.GetViewModel()?.Dispose();

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

            // 逃げ先座標の保存
            _displaySettings["EscapePositions"] = EscapePositionControl.GetEscapePositions();

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
            // SystemSettingsControlから設定を取得
            _displaySettings["IsEnableNotificationApi"] = SystemSettingsControl.GetIsEnableNotificationApi();
            // MCPタブの設定を取得（ViewModelから）
            _displaySettings["IsEnableMcp"] = McpSettingsControl.GetMcpEnabled();

            // スクリーンショット設定を保存
            var screenshotSettings = SystemSettingsControl.GetScreenshotSettings();
            _displaySettings["ScreenshotEnabled"] = screenshotSettings.enabled;
            _displaySettings["ScreenshotInterval"] = screenshotSettings.intervalMinutes;
            _displaySettings["IdleTimeout"] = screenshotSettings.idleTimeoutMinutes;
            _displaySettings["CaptureActiveWindowOnly"] = screenshotSettings.captureActiveWindowOnly;

            // マイク設定を保存
            var microphoneSettings = SystemSettingsControl.GetMicrophoneSettings();
            _displaySettings["MicAutoAdjustment"] = microphoneSettings.autoAdjustment;
            _displaySettings["MicInputThreshold"] = microphoneSettings.inputThreshold;
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

            // 逃げ先座標の更新
            if (_displaySettings.ContainsKey("EscapePositions"))
            {
                appSettings.EscapePositions = (List<EscapePosition>)_displaySettings["EscapePositions"];
                // UserControlにも反映
                EscapePositionControl.SetEscapePositions(appSettings.EscapePositions);
            }

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
            McpSettingsControl.SetMcpEnabled((bool)_displaySettings["IsEnableMcp"]);

            // スクリーンショット設定の更新
            appSettings.ScreenshotSettings.enabled = (bool)_displaySettings["ScreenshotEnabled"];
            appSettings.ScreenshotSettings.intervalMinutes = (int)_displaySettings["ScreenshotInterval"];
            appSettings.ScreenshotSettings.idleTimeoutMinutes = (int)_displaySettings["IdleTimeout"];
            appSettings.ScreenshotSettings.captureActiveWindowOnly = (bool)_displaySettings["CaptureActiveWindowOnly"];

            // マイク設定の更新
            appSettings.MicrophoneSettings.autoAdjustment = (bool)_displaySettings["MicAutoAdjustment"];
            appSettings.MicrophoneSettings.inputThreshold = (int)_displaySettings["MicInputThreshold"];

            // キャラクター設定の更新
            appSettings.CurrentCharacterIndex = CharacterManagementControl.GetCurrentCharacterIndex();

            // CharacterManagementControlから現在のキャラクター設定を取得
            var currentCharacterSetting = CharacterManagementControl.GetCurrentCharacterSetting();
            if (currentCharacterSetting != null)
            {
                // 現在選択されているキャラクターの設定を更新
                var currentIndex = CharacterManagementControl.GetCurrentCharacterIndex();
                if (currentIndex >= 0 && currentIndex < appSettings.CharacterList.Count)
                {
                    appSettings.CharacterList[currentIndex] = currentCharacterSetting;
                }
            }

            // アニメーション設定の更新
            appSettings.CurrentAnimationSettingIndex = AnimationSettingsControl.GetCurrentAnimationSettingIndex();
            appSettings.AnimationSettings = AnimationSettingsControl.GetAnimationSettings();
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
                // CharacterManagementControlに移行済み
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
            // API説明はSystemSettingsControlで管理される
            // 必要に応じて削除可能
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
        /// ログ表示ボタンのクリックイベント
        /// </summary>
        private void LogViewerButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 通信サービスからログビューアーを開く
                _communicationService?.OpenLogViewer();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"ログビューアーの起動に失敗しました: {ex.Message}",
                               "エラー",
                               MessageBoxButton.OK,
                               MessageBoxImage.Error);
            }
        }



    }
}
