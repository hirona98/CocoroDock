using CocoroDock.Services;
using CocoroDock.Communication;
using CocoroDock.Utilities;
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
    public partial class DisplaySettingsControl : UserControl
    {
        // 設定のスナップショット
        private Dictionary<string, object> _displaySettings = new();

        // 通信サービス参照（必要なら親から提供）
        private ICommunicationService? _communicationService;

        // キーボードフック用（Win/Alt検出とキャプチャ）
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

        public DisplaySettingsControl()
        {
            InitializeComponent();
            _proc = HookCallback;
            _hookID = SetHook(_proc);

            // UIイベント
            CaptureKeyButton.Click += CaptureKeyButton_Click;

            // 初期表示へ反映
            InitializeFromAppSettings();

            // Loaded/UnloadedでWndProcフックを管理
            Loaded += (s, e) =>
            {
                _source = PresentationSource.FromVisual(this) as HwndSource;
                _source?.AddHook(WndProc);
            };
            Unloaded += (s, e) =>
            {
                if (_source != null)
                {
                    _source.RemoveHook(WndProc);
                    _source = null;
                }
                if (_hookID != IntPtr.Zero)
                {
                    UnhookWindowsHookEx(_hookID);
                    _hookID = IntPtr.Zero;
                }
            };
        }

        public void SetCommunicationService(ICommunicationService? service)
        {
            _communicationService = service;
            if (service != null)
            {
                EscapePositionControl.SetCommunicationService(service);
            }
        }

        public void InitializeFromAppSettings()
        {
            var appSettings = AppSettings.Instance;
            RestoreWindowPositionCheckBox.IsChecked = appSettings.IsRestoreWindowPosition;
            TopMostCheckBox.IsChecked = appSettings.IsTopmost;
            EscapeCursorCheckBox.IsChecked = appSettings.IsEscapeCursor;
            InputVirtualKeyCheckBox.IsChecked = appSettings.IsInputVirtualKey;
            VirtualKeyStringTextBox.Text = appSettings.VirtualKeyString;
            AutoMoveCheckBox.IsChecked = appSettings.IsAutoMove;
            ShowMessageWindowCheckBox.IsChecked = appSettings.ShowMessageWindow;

            // メッセージウィンドウ設定の初期化
            MaxMessageCountTextBox.Text = appSettings.MessageWindowSettings.maxMessageCount.ToString();
            MaxTotalCharactersTextBox.Text = appSettings.MessageWindowSettings.maxTotalCharacters.ToString();
            MinWindowSizeTextBox.Text = appSettings.MessageWindowSettings.minWindowSize.ToString();
            MaxWindowSizeTextBox.Text = appSettings.MessageWindowSettings.maxWindowSize.ToString();
            FontSizeTextBox.Text = appSettings.MessageWindowSettings.fontSize.ToString();
            HorizontalOffsetTextBox.Text = appSettings.MessageWindowSettings.horizontalOffset.ToString();
            VerticalOffsetTextBox.Text = appSettings.MessageWindowSettings.verticalOffset.ToString();
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
        }

        public void SaveToSnapshot()
        {
            _displaySettings["RestoreWindowPosition"] = RestoreWindowPositionCheckBox.IsChecked ?? false;
            _displaySettings["TopMost"] = TopMostCheckBox.IsChecked ?? false;
            _displaySettings["EscapeCursor"] = EscapeCursorCheckBox.IsChecked ?? false;

            // 逃げ先座標
            _displaySettings["EscapePositions"] = EscapePositionControl.GetEscapePositions();

            _displaySettings["InputVirtualKey"] = InputVirtualKeyCheckBox.IsChecked ?? false;
            _displaySettings["VirtualKeyString"] = VirtualKeyStringTextBox.Text;
            _displaySettings["AutoMove"] = AutoMoveCheckBox.IsChecked ?? false;
            _displaySettings["ShowMessageWindow"] = ShowMessageWindowCheckBox.IsChecked ?? false;

            // メッセージウィンドウ設定のスナップショット保存
            var messageWindowSettings = new Dictionary<string, object>
            {
                ["maxMessageCount"] = int.TryParse(MaxMessageCountTextBox.Text, out int maxCount) ? maxCount : 3,
                ["maxTotalCharacters"] = int.TryParse(MaxTotalCharactersTextBox.Text, out int maxChars) ? maxChars : 300,
                ["minWindowSize"] = float.TryParse(MinWindowSizeTextBox.Text, out float minSize) ? minSize : 200f,
                ["maxWindowSize"] = float.TryParse(MaxWindowSizeTextBox.Text, out float maxSize) ? maxSize : 600f,
                ["fontSize"] = float.TryParse(FontSizeTextBox.Text, out float fontSize) ? fontSize : 14f,
                ["horizontalOffset"] = float.TryParse(HorizontalOffsetTextBox.Text, out float hOffset) ? hOffset : -0.2f,
                ["verticalOffset"] = float.TryParse(VerticalOffsetTextBox.Text, out float vOffset) ? vOffset : 0f
            };
            _displaySettings["MessageWindowSettings"] = messageWindowSettings;
            _displaySettings["IsEnableAmbientOcclusion"] = AmbientOcclusionCheckBox.IsChecked ?? false;

            int msaaLevel = 0;
            if (MSAAComboBox.SelectedItem is ComboBoxItem msaaItem && msaaItem.Tag != null)
                int.TryParse(msaaItem.Tag.ToString(), out msaaLevel);
            _displaySettings["MsaaLevel"] = msaaLevel;

            int charaShadow = 0;
            if (CharacterShadowComboBox.SelectedItem is ComboBoxItem charaShadowItem && charaShadowItem.Tag != null)
                int.TryParse(charaShadowItem.Tag.ToString(), out charaShadow);
            _displaySettings["CharacterShadow"] = charaShadow;

            int shadowRes = 0;
            if (CharacterShadowResolutionComboBox.SelectedItem is ComboBoxItem shadowResItem && shadowResItem.Tag != null)
                int.TryParse(shadowResItem.Tag.ToString(), out shadowRes);
            _displaySettings["CharacterShadowResolution"] = shadowRes;

            int backShadow = 0;
            if (BackgroundShadowComboBox.SelectedItem is ComboBoxItem backShadowItem && backShadowItem.Tag != null)
                int.TryParse(backShadowItem.Tag.ToString(), out backShadow);
            _displaySettings["BackgroundShadow"] = backShadow;

            int backShadowRes = 0;
            if (BackgroundShadowResolutionComboBox.SelectedItem is ComboBoxItem backShadowResItem && backShadowResItem.Tag != null)
                int.TryParse(backShadowResItem.Tag.ToString(), out backShadowRes);
            _displaySettings["BackgroundShadowResolution"] = backShadowRes;

            _displaySettings["WindowSize"] = WindowSizeSlider.Value;
        }

        public Dictionary<string, object> GetSnapshot()
        {
            return new Dictionary<string, object>(_displaySettings);
        }

        public void ApplySnapshotToAppSettings(Dictionary<string, object> snapshot)
        {
            var appSettings = AppSettings.Instance;
            appSettings.IsRestoreWindowPosition = (bool)snapshot["RestoreWindowPosition"];
            appSettings.IsTopmost = (bool)snapshot["TopMost"];
            appSettings.IsEscapeCursor = (bool)snapshot["EscapeCursor"];
            if (snapshot.ContainsKey("EscapePositions"))
            {
                appSettings.EscapePositions = (System.Collections.Generic.List<EscapePosition>)snapshot["EscapePositions"];
                EscapePositionControl.SetEscapePositions(appSettings.EscapePositions);
            }
            appSettings.IsInputVirtualKey = (bool)snapshot["InputVirtualKey"];
            appSettings.VirtualKeyString = (string)snapshot["VirtualKeyString"];
            appSettings.IsAutoMove = (bool)snapshot["AutoMove"];
            appSettings.ShowMessageWindow = (bool)snapshot["ShowMessageWindow"];

            // メッセージウィンドウ設定の適用
            if (snapshot.ContainsKey("MessageWindowSettings") && snapshot["MessageWindowSettings"] is Dictionary<string, object> msgSettings)
            {
                if (msgSettings.ContainsKey("maxMessageCount"))
                    appSettings.MessageWindowSettings.maxMessageCount = Convert.ToInt32(msgSettings["maxMessageCount"]);
                if (msgSettings.ContainsKey("maxTotalCharacters"))
                    appSettings.MessageWindowSettings.maxTotalCharacters = Convert.ToInt32(msgSettings["maxTotalCharacters"]);
                if (msgSettings.ContainsKey("minWindowSize"))
                    appSettings.MessageWindowSettings.minWindowSize = Convert.ToSingle(msgSettings["minWindowSize"]);
                if (msgSettings.ContainsKey("maxWindowSize"))
                    appSettings.MessageWindowSettings.maxWindowSize = Convert.ToSingle(msgSettings["maxWindowSize"]);
                if (msgSettings.ContainsKey("fontSize"))
                    appSettings.MessageWindowSettings.fontSize = Convert.ToSingle(msgSettings["fontSize"]);
                if (msgSettings.ContainsKey("horizontalOffset"))
                    appSettings.MessageWindowSettings.horizontalOffset = Convert.ToSingle(msgSettings["horizontalOffset"]);
                if (msgSettings.ContainsKey("verticalOffset"))
                    appSettings.MessageWindowSettings.verticalOffset = Convert.ToSingle(msgSettings["verticalOffset"]);
            }
            appSettings.IsEnableAmbientOcclusion = (bool)snapshot["IsEnableAmbientOcclusion"];
            appSettings.MsaaLevel = (int)snapshot["MsaaLevel"];
            appSettings.CharacterShadow = (int)snapshot["CharacterShadow"];
            appSettings.CharacterShadowResolution = (int)snapshot["CharacterShadowResolution"];
            appSettings.BackgroundShadow = (int)snapshot["BackgroundShadow"];
            appSettings.BackgroundShadowResolution = (int)snapshot["BackgroundShadowResolution"];
            appSettings.WindowSize = (double)snapshot["WindowSize"] > 0 ? (int)(double)snapshot["WindowSize"] : 650;
        }

        private void CaptureKeyButton_Click(object sender, RoutedEventArgs e)
        {
            string originalText = VirtualKeyStringTextBox.Text;
            VirtualKeyStringTextBox.Text = "Press the key";
            VirtualKeyStringTextBox.Focus();

            _isCapturingKey = true;
            _isWinKeyPressed = false;
            _isAltKeyPressed = false;

            this.PreviewKeyDown += CaptureKey_PreviewKeyDown;
        }

        private void CaptureKey_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            e.Handled = true;
            string keyName = e.Key.ToString();

            bool isAltPressed = _isAltKeyPressed;
            bool isCtrlPressed = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
            bool isShiftPressed = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;
            bool isWinPressed = _isWinKeyPressed;

            if (keyName == "LeftAlt" || keyName == "RightAlt" ||
                keyName == "LeftCtrl" || keyName == "RightCtrl" ||
                keyName == "LeftShift" || keyName == "RightShift" ||
                keyName == "LWin" || keyName == "RWin")
            {
                return;
            }

            string result = "";
            if (isCtrlPressed) result += "Ctrl+";
            if (isAltPressed) result += "Alt+";
            if (isShiftPressed) result += "Shift+";
            if (isWinPressed) result += "Win+";
            result += keyName;

            VirtualKeyStringTextBox.Text = result;

            _isCapturingKey = false;
            _isWinKeyPressed = false;
            _isAltKeyPressed = false;
            this.PreviewKeyDown -= CaptureKey_PreviewKeyDown;
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
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
                            handled = true;
                        }
                        break;
                }
            }
            return IntPtr.Zero;
        }

        private IntPtr SetHook(LowLevelKeyboardProc proc)
        {
            using (var curProcess = Process.GetCurrentProcess())
            using (var curModule = curProcess.MainModule)
            {
                return SetWindowsHookEx(WH_KEYBOARD_LL, proc,
                    GetModuleHandle(curModule?.ModuleName ?? string.Empty), 0);
            }
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && _isCapturingKey)
            {
                int vkCode = Marshal.ReadInt32(lParam);
                if (vkCode == VK_LWIN || vkCode == VK_RWIN)
                {
                    if ((int)wParam == WM_KEYDOWN || (int)wParam == WM_SYSKEYDOWN)
                    {
                        _isWinKeyPressed = true;
                        return (IntPtr)1;
                    }
                    else if ((int)wParam == WM_KEYUP || (int)wParam == WM_SYSKEYUP)
                    {
                        _isWinKeyPressed = false;
                    }
                }
                else if (vkCode == VK_LALT || vkCode == VK_RALT)
                {
                    if ((int)wParam == WM_KEYDOWN || (int)wParam == WM_SYSKEYDOWN)
                    {
                        _isAltKeyPressed = true;
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

        private void ResetCharacterPositionButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var appSettings = AppSettings.Instance;
                appSettings.WindowPositionX = float.MinValue;
                appSettings.WindowPositionY = float.MinValue;
                appSettings.IsRestoreWindowPosition = false;
                RestoreWindowPositionCheckBox.IsChecked = false;
                appSettings.SaveAppSettings();
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
    }
}
