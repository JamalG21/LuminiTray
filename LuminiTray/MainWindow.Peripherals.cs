using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Media;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace LuminiTray;

public partial class MainWindow
{
    private readonly KeyboardTester _keyboardTester = new();
    private readonly MouseTester _mouseTester = new();
    private readonly AudioPeripheralsTester _audioTester = new();
    private readonly GamepadTester _gamepadTester = new();

    private List<MonitorDeviceItem> _monitorsList = new();
    private MonitorDeviceItem? _selectedMonitor;

    // Animação UFO motion blur (clamped dt)
    private bool _ufoRunning;
    private double _ufoPosX960;
    private double _ufoPosX480;
    private TimeSpan _lastUfoRenderTime = TimeSpan.Zero;
    private int _ufoFrameCount;
    private double _ufoFpsAccumulator;

    #region Inicialização dos Periféricos

        private bool _perifInitialized;

    private void InitPerifericosPage()
    {
        if (_perifInitialized)
        {
            // Se já inicializado e nenhum painel estiver visível, default para Teclado
            if ((PanelPerifTeclado?.Visibility != Visibility.Visible) &&
                (PanelPerifMouse?.Visibility != Visibility.Visible) &&
                (PanelPerifMonitor?.Visibility != Visibility.Visible) &&
                (PanelPerifAudio?.Visibility != Visibility.Visible) &&
                (PanelPerifMic?.Visibility != Visibility.Visible) &&
                (PanelPerifGamepad?.Visibility != Visibility.Visible) &&
                (PanelPerifDevices?.Visibility != Visibility.Visible))
            {
                ShowPerifPanel("PagePerifTeclado");
            }
            return;
        }
        _perifInitialized = true;
        // 1. Keyboard
        _keyboardTester.StateChanged += () => Dispatcher.InvokeAsync(UpdateKeyboardUi);
        RebuildKeyboardMatrix();
        InitKeyboardRemapperUi();

        // 2. Mouse
        _mouseTester.StateChanged += () => { if (PanelPerifMouse != null && PanelPerifMouse.Visibility == Visibility.Visible) Dispatcher.InvokeAsync(UpdateMouseUi); };
        _mouseTester.CpsTestFinished += () => Dispatcher.InvokeAsync(() =>
        {
            Toast("LUMINI MOUSE", $"Teste de CPS finalizado: {_mouseTester.CpsScore:F1} CPS ({_mouseTester.CpsRank})", "\uE962");
        });

        // 3. Audio & Mic
        _audioTester.MicStateUpdated += () => { if (PanelPerifMic != null && PanelPerifMic.Visibility == Visibility.Visible) Dispatcher.InvokeAsync(UpdateMicUi); };
        _audioTester.SweepProgress += (hz, frac) => Dispatcher.InvokeAsync(() => UpdateSweepUi(hz, frac));
        _audioTester.VoiceRecordingFinished += () => Dispatcher.InvokeAsync(() =>
        {
            Toast("LUMINI MICROFONE", "Gravação de voz (5s) concluída! Clique em 'Ouvir Gravação' para conferir.", "\uE720");
        });

        RefreshAudioDevicesList();
        RefreshMicDevicesList();

        // 4. Gamepad (inicia apenas sob demanda quando a sub-aba Gamepad for aberta)
        _gamepadTester.StateUpdated += snap => { if (PanelPerifGamepad != null && PanelPerifGamepad.Visibility == Visibility.Visible) Dispatcher.InvokeAsync(() => UpdateGamepadUi(snap)); };

        // 5. Monitores
        RefreshMonitorsList();

        // 6. Tema alterado: atualizar elementos visuais para combinar com modo claro/escuro
        ThemeManager.ThemeChanged += () => Dispatcher.InvokeAsync(() =>
        {
            RebuildKeyboardMatrix();
            UpdateSelectedMonitorUi();
            if (_gamepadTester.CurrentState != null)
                UpdateGamepadUi(_gamepadTester.CurrentState);
            SetButtonVisualState(BorderMouseLeft, _mouseTester.IsLeftDown);
            SetButtonVisualState(BorderMouseRight, _mouseTester.IsRightDown);
            SetButtonVisualState(BorderMouseMiddle, _mouseTester.IsMiddleDown);
            SetButtonVisualState(BorderMouseSide1, _mouseTester.IsXButton1Down);
            SetButtonVisualState(BorderMouseSide2, _mouseTester.IsXButton2Down);
        });
    }

    #endregion

    #region Navegação entre Sub-Abas de Periféricos

    public void ShowPerifPanel(string panelName)
    {
        // Ocultar todos os painéis
        if (PanelPerifTeclado != null) PanelPerifTeclado.Visibility = Visibility.Collapsed;
        if (PanelPerifMouse != null) PanelPerifMouse.Visibility = Visibility.Collapsed;
        if (PanelPerifMonitor != null) PanelPerifMonitor.Visibility = Visibility.Collapsed;
        if (PanelPerifAudio != null) PanelPerifAudio.Visibility = Visibility.Collapsed;
        if (PanelPerifMic != null) PanelPerifMic.Visibility = Visibility.Collapsed;
        if (PanelPerifGamepad != null) PanelPerifGamepad.Visibility = Visibility.Collapsed;
        if (PanelPerifDevices != null) PanelPerifDevices.Visibility = Visibility.Collapsed;

        // Parar atividades ativas por padrão
        StopUfoAnimation();
        _gamepadTester.Stop();
        _audioTester.StopSweep();

        // Só interrompe o microfone se o retorno (loopback) de teste não estiver ativo
        if (!_audioTester.IsLoopbackEnabled)
        {
            _audioTester.StopMicCapture();
        }

        switch (panelName)
        {
            case "PagePerifTeclado":
                if (PanelPerifTeclado != null) PanelPerifTeclado.Visibility = Visibility.Visible;
                if (NavPerifTeclado != null) NavPerifTeclado.IsChecked = true;
                break;
            case "PagePerifMouse":
                if (PanelPerifMouse != null) PanelPerifMouse.Visibility = Visibility.Visible;
                if (NavPerifMouse != null) NavPerifMouse.IsChecked = true;
                break;
            case "PagePerifMonitor":
                if (PanelPerifMonitor != null) PanelPerifMonitor.Visibility = Visibility.Visible;
                if (NavPerifMonitor != null) NavPerifMonitor.IsChecked = true;
                StartUfoAnimation();
                break;
            case "PagePerifAudio":
                if (PanelPerifAudio != null) PanelPerifAudio.Visibility = Visibility.Visible;
                if (NavPerifAudio != null) NavPerifAudio.IsChecked = true;
                RefreshAudioDevicesList();
                break;
            case "PagePerifMic":
                if (PanelPerifMic != null) PanelPerifMic.Visibility = Visibility.Visible;
                if (NavPerifMic != null) NavPerifMic.IsChecked = true;
                RefreshMicDevicesList();
                _audioTester.StartMicCapture();
                break;
            case "PagePerifGamepad":
                if (PanelPerifGamepad != null) PanelPerifGamepad.Visibility = Visibility.Visible;
                if (NavPerifGamepad != null) NavPerifGamepad.IsChecked = true;
                _gamepadTester.Start();
                break;
            case "PagePerifDevices":
                if (PanelPerifDevices != null) PanelPerifDevices.Visibility = Visibility.Visible;
                if (NavPerifDevices != null) NavPerifDevices.IsChecked = true;
                RefreshDevicesSubTab();
                break;
            default:
                if (PanelPerifTeclado != null) PanelPerifTeclado.Visibility = Visibility.Visible;
                if (NavPerifTeclado != null) NavPerifTeclado.IsChecked = true;
                break;
        }
    }

    public void UpdatePeripheralsActivityState()
    {
        bool isPerifPage = PagePerifericos?.Visibility == Visibility.Visible && WindowState != WindowState.Minimized;

        // 1. Gamepad: ativo apenas na sub-aba Gamepad
        if (isPerifPage && PanelPerifGamepad?.Visibility == Visibility.Visible)
        {
            _gamepadTester.Start();
        }
        else
        {
            _gamepadTester.Stop();
        }

        // 2. Animação UFO: ativa apenas na sub-aba Monitor
        if (isPerifPage && PanelPerifMonitor?.Visibility == Visibility.Visible)
        {
            StartUfoAnimation();
        }
        else
        {
            StopUfoAnimation();
        }

        // 3. Captura do microfone: ativa na sub-aba Microfone OU se o retorno (loopback) estiver ligado
        if (isPerifPage && PanelPerifMic?.Visibility == Visibility.Visible)
        {
            _audioTester.StartMicCapture();
        }
        else if (!_audioTester.IsLoopbackEnabled)
        {
            _audioTester.StopMicCapture();
        }

        // 4. Áudio Sweep
        if (!isPerifPage || PanelPerifAudio?.Visibility != Visibility.Visible)
        {
            _audioTester.StopSweep();
        }
    }

    #endregion

    #region Teclado (Matriz Interativa, Latência, KPS, Rollover & mestresdainformatica)

    private struct KeyItem
    {
        public Key Code;
        public string Label;
        public double Width;
        public KeyItem(Key c, string l, double w = 1.0) { Code = c; Label = l; Width = w; }
    }

    private void RebuildKeyboardMatrix()
    {
        if (KeyboardMatrixContainer == null) return;
        KeyboardMatrixContainer.Children.Clear();

        int layoutIdx = CmbKeyboardLayout?.SelectedIndex ?? 0; // 0=ABNT2, 1=ANSI, 2=ISO
        int sizeIdx = CmbKeyboardSize?.SelectedIndex ?? 0;     // 0=100%, 1=80% TKL, 2=60%

        var rows = GetKeyboardLayoutMatrix(layoutIdx, sizeIdx);
        int totalKeyCount = 0;

        foreach (var row in rows)
        {
            var rowPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 2, 0, 2),
                HorizontalAlignment = HorizontalAlignment.Center
            };

            foreach (var key in row)
            {
                if (key.Code != Key.None) totalKeyCount++;
                var btn = CreateKeyVisual(key);
                rowPanel.Children.Add(btn);
            }

            KeyboardMatrixContainer.Children.Add(rowPanel);
        }

        _keyboardTester.TotalKeysInLayout = totalKeyCount;
        UpdateKeyboardUi();
    }

    private static Brush GetThemeAccentBrush()
    {
        return (Application.Current?.Resources["Accent"] as Brush) ?? new SolidColorBrush(Color.FromRgb(0, 210, 255));
    }

    private static Brush GetThemeAccentFgBrush()
    {
        return (Application.Current?.Resources["AccentFg"] as Brush) ?? Brushes.Black;
    }

    private static (Color bg, Color fg) GetThemeAccentColors()
    {
        var b = Application.Current?.Resources["Accent"] as SolidColorBrush;
        var f = Application.Current?.Resources["AccentFg"] as SolidColorBrush;
        return (b?.Color ?? Color.FromRgb(0, 210, 255), f?.Color ?? Colors.Black);
    }

    private Border CreateKeyVisual(KeyItem key)
    {
        double width = key.Width * 40.0 - 4.0;
        if (width < 2.0) width = 2.0;

        if (key.Code == Key.None)
        {
            return new Border
            {
                Background = Brushes.Transparent,
                BorderBrush = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Width = width,
                Height = 36,
                Margin = new Thickness(2),
                IsHitTestVisible = false
            };
        }
        bool light = ThemeManager.IsLight;
        bool isTested = _keyboardTester.IsKeyTested(key.Code);
        bool isPressed = _keyboardTester.IsKeyPressed(key.Code);
        bool isStuck = _keyboardTester.IsKeyStuck(key.Code);

        Color bg = light ? Color.FromArgb(255, 255, 255, 255) : Color.FromArgb(255, 20, 20, 20);
        Color border = light ? Color.FromArgb(255, 203, 213, 225) : Color.FromArgb(255, 45, 45, 45);
        Color fg = light ? Color.FromArgb(255, 15, 23, 42) : Color.FromArgb(255, 240, 240, 240);

        if (isStuck)
        {
            bg = light ? Color.FromArgb(255, 254, 226, 226) : Color.FromArgb(255, 127, 29, 29);
            border = Color.FromArgb(255, 239, 68, 68);
            fg = Color.FromArgb(255, 239, 68, 68);
        }
        else if (isPressed)
        {
            var (accBg, accFg) = GetThemeAccentColors();
            bg = accBg;
            border = accBg;
            fg = accFg;
        }
        else if (isTested)
        {
            bg = light ? Color.FromArgb(255, 220, 252, 231) : Color.FromArgb(255, 6, 78, 59);
            border = light ? Color.FromArgb(255, 134, 239, 172) : Color.FromArgb(255, 5, 150, 105);
            fg = light ? Color.FromArgb(255, 22, 163, 74) : Color.FromArgb(255, 52, 211, 153);
        }

        var b = new Border
        {
            Tag = key.Code,
            Background = new SolidColorBrush(bg),
            BorderBrush = new SolidColorBrush(border),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Width = width,
            Height = 36,
            Margin = new Thickness(2),
            ClipToBounds = true
        };

        var tb = new TextBlock
        {
            Text = key.Label,
            FontSize = key.Label.Length > 4 ? 9.0 : 10.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(fg),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center
        };

        b.Child = tb;
        return b;
    }

    private void UpdateKeyboardKeyVisual(Key key)
    {
        if (KeyboardMatrixContainer == null) return;

        bool light = ThemeManager.IsLight;
        bool isTested = _keyboardTester.IsKeyTested(key);
        bool isPressed = _keyboardTester.IsKeyPressed(key);
        bool isStuck = _keyboardTester.IsKeyStuck(key);

        foreach (var child in KeyboardMatrixContainer.Children)
        {
            if (child is StackPanel row)
            {
                foreach (var el in row.Children)
                {
                    if (el is Border b && b.Tag is Key k && k == key)
                    {
                        Color bg = light ? Color.FromArgb(255, 255, 255, 255) : Color.FromArgb(255, 20, 20, 20);
                        Color border = light ? Color.FromArgb(255, 203, 213, 225) : Color.FromArgb(255, 45, 45, 45);
                        Color fg = light ? Color.FromArgb(255, 15, 23, 42) : Color.FromArgb(255, 240, 240, 240);

                        if (isStuck)
                        {
                            bg = light ? Color.FromArgb(255, 254, 226, 226) : Color.FromArgb(255, 127, 29, 29);
                            border = Color.FromArgb(255, 239, 68, 68);
                            fg = Color.FromArgb(255, 239, 68, 68);
                        }
                        else if (isPressed)
                        {
                            var (accBg, accFg) = GetThemeAccentColors();
                            bg = accBg;
                            border = accBg;
                            fg = accFg;
                        }
                        else if (isTested)
                        {
                            bg = light ? Color.FromArgb(255, 220, 252, 231) : Color.FromArgb(255, 6, 78, 59);
                            border = light ? Color.FromArgb(255, 134, 239, 172) : Color.FromArgb(255, 5, 150, 105);
                            fg = light ? Color.FromArgb(255, 22, 163, 74) : Color.FromArgb(255, 52, 211, 153);
                        }

                        b.Background = new SolidColorBrush(bg);
                        b.BorderBrush = new SolidColorBrush(border);
                        if (b.Child is TextBlock tb)
                            tb.Foreground = new SolidColorBrush(fg);

                        return;
                    }
                }
            }
        }
    }

    private void UpdateKeyboardUi()
    {
        if (TxtKeyLatency != null) TxtKeyLatency.Text = $"{_keyboardTester.LastLatencyMs:F1} ms";
        if (TxtKeyTestedCount != null) TxtKeyTestedCount.Text = $"{_keyboardTester.TestedCount} / {_keyboardTester.TotalKeysInLayout} teclas";
        if (TxtKeyTestedPercent != null) TxtKeyTestedPercent.Text = $"{_keyboardTester.TestedPercentage:F0}%";
        if (BarKeyTested != null) BarKeyTested.Value = _keyboardTester.TestedPercentage;
        if (TxtKeyRollover != null) TxtKeyRollover.Text = $"{_keyboardTester.CurrentRollover} simultâneas (Pico: {_keyboardTester.PeakRollover})";
        if (TxtKeyKps != null) TxtKeyKps.Text = $"{_keyboardTester.CurrentKps:F1} KPS ({_keyboardTester.CurrentWpm:F0} WPM)";

        if (TxtKeyStuck != null)
        {
            TxtKeyStuck.Text = _keyboardTester.StuckKeysCount > 0 ? $"{_keyboardTester.StuckKeysCount} presa(s)!" : "0 presas";
            TxtKeyStuck.Foreground = _keyboardTester.StuckKeysCount > 0
                ? new SolidColorBrush(Color.FromArgb(255, 239, 68, 68))
                : new SolidColorBrush(Color.FromArgb(255, 16, 185, 129));
        }

        if (TxtLastKey != null) TxtLastKey.Text = _keyboardTester.LastKeyName;
        if (TxtLastKeyCode != null) TxtLastKeyCode.Text = _keyboardTester.LastKeyHex;

        if (TxtRecentKeysStream != null)
        {
            TxtRecentKeysStream.Text = _keyboardTester.RecentKeyLog.Count > 0
                ? string.Join(" ➔ ", _keyboardTester.RecentKeyLog)
                : "[Aguardando teclas...]";
        }

        _keyboardTester.PlaySoundFeedback = ChkKeySound?.IsChecked == true;
        if (CmbKeySoundType != null)
        {
            _keyboardTester.SoundType = CmbKeySoundType.SelectedIndex == 1 ? KeySoundType.Bubble : KeySoundType.Switch;
        }
    }

    private void ChkKeySound_Changed(object sender, RoutedEventArgs e)
    {
        if (_keyboardTester != null)
        {
            _keyboardTester.PlaySoundFeedback = ChkKeySound?.IsChecked == true;
            if (_keyboardTester.PlaySoundFeedback)
            {
                KeySoundEngine.Play(_keyboardTester.SoundType);
            }
        }
    }

    private void CmbKeySoundType_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_keyboardTester != null && CmbKeySoundType != null)
        {
            _keyboardTester.SoundType = CmbKeySoundType.SelectedIndex == 1 ? KeySoundType.Bubble : KeySoundType.Switch;
            if (ChkKeySound?.IsChecked == true)
            {
                KeySoundEngine.Play(_keyboardTester.SoundType);
            }
        }
    }

    private void PagePerif_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (PanelPerifTeclado != null && PanelPerifTeclado.Visibility == Visibility.Visible)
        {
            Key k = e.Key == Key.System ? e.SystemKey : e.Key;
            if (_remapCaptureTarget != 0)
            {
                if (HandleRemapKeyCapture(k))
                {
                    e.Handled = true;
                    return;
                }
            }
            _keyboardTester.OnKeyDown(k);
            UpdateKeyboardKeyVisual(k);
            e.Handled = true;
        }
    }

    private void PagePerif_PreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (PanelPerifTeclado != null && PanelPerifTeclado.Visibility == Visibility.Visible)
        {
            Key k = e.Key == Key.System ? e.SystemKey : e.Key;
            _keyboardTester.OnKeyUp(k);
            UpdateKeyboardKeyVisual(k);
            e.Handled = true;
        }
    }

    private void CmbKeyboardLayout_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        RebuildKeyboardMatrix();
    }

    private void CmbKeyboardSize_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        RebuildKeyboardMatrix();
    }

    private void BtnResetKeyboard_Click(object sender, RoutedEventArgs e)
    {
        _keyboardTester.Reset();
        RebuildKeyboardMatrix();
    }

    private static List<List<KeyItem>> GetKeyboardLayoutMatrix(int layout, int size)
    {
        var result = new List<List<KeyItem>>();
        bool isAbnt2 = layout == 0;
        bool isIso = layout == 2;
        bool hasNumpad = size == 0;
        bool hasNav = size <= 1;

        // F-Row (Row 0)
        // Alpha: Esc (1.0) + Spacer (1.0) + F1-F4 (4.0) + Spacer (0.5) + F5-F8 (4.0) + Spacer (0.5) + F9-F12 (4.0) = 15.0u
        var r0 = new List<KeyItem>
        {
            new(Key.Escape, "Esc"),
            new(Key.None, "", 1.0),
            new(Key.F1, "F1"), new(Key.F2, "F2"), new(Key.F3, "F3"), new(Key.F4, "F4"),
            new(Key.None, "", 0.5),
            new(Key.F5, "F5"), new(Key.F6, "F6"), new(Key.F7, "F7"), new(Key.F8, "F8"),
            new(Key.None, "", 0.5),
            new(Key.F9, "F9"), new(Key.F10, "F10"), new(Key.F11, "F11"), new(Key.F12, "F12")
        };
        if (hasNav)
        {
            r0.Add(new(Key.None, "", 0.5)); // gap
            r0.Add(new(Key.PrintScreen, "PrtSc"));
            r0.Add(new(Key.Scroll, "ScrLk"));
            r0.Add(new(Key.Pause, "Pause"));
        }
        if (hasNumpad)
        {
            r0.Add(new(Key.None, "", 0.5)); // gap
            r0.Add(new(Key.None, "", 4.0)); // numpad empty top
        }
        result.Add(r0);

        // Row 1: Números (15.0u alpha + 0.5u gap + 3.0u nav + 0.5u gap + 4.0u numpad)
        var r1 = new List<KeyItem>
        {
            new(Key.Oem3, isAbnt2 ? "' \"" : "` ~"),
            new(Key.D1, "1"), new(Key.D2, "2"), new(Key.D3, "3"), new(Key.D4, "4"), new(Key.D5, "5"),
            new(Key.D6, "6"), new(Key.D7, "7"), new(Key.D8, "8"), new(Key.D9, "9"), new(Key.D0, "0"),
            new(Key.OemMinus, "- _"), new(Key.OemPlus, "= +"), new(Key.Back, "Backspace", 2.0)
        };
        if (hasNav)
        {
            r1.Add(new(Key.None, "", 0.5));
            r1.Add(new(Key.Insert, "Ins"));
            r1.Add(new(Key.Home, "Home"));
            r1.Add(new(Key.PageUp, "PgUp"));
        }
        if (hasNumpad)
        {
            r1.Add(new(Key.None, "", 0.5));
            r1.Add(new(Key.NumLock, "Num"));
            r1.Add(new(Key.Divide, "/"));
            r1.Add(new(Key.Multiply, "*"));
            r1.Add(new(Key.Subtract, "-"));
        }
        result.Add(r1);

        // Row 2: QWERTY (15.0u alpha + 0.5u gap + 3.0u nav + 0.5u gap + 4.0u numpad)
        var r2 = new List<KeyItem>
        {
            new(Key.Tab, "Tab", 1.5),
            new(Key.Q, "Q"), new(Key.W, "W"), new(Key.E, "E"), new(Key.R, "R"), new(Key.T, "T"),
            new(Key.Y, "Y"), new(Key.U, "U"), new(Key.I, "I"), new(Key.O, "O"), new(Key.P, "P"),
            new(Key.OemOpenBrackets, isAbnt2 ? "´ `" : "[ {"),
            new(Key.OemCloseBrackets, isAbnt2 ? "[ {" : "] }"),
            new(Key.Oem5, isAbnt2 ? "] }" : "\\ |", 1.5)
        };
        if (hasNav)
        {
            r2.Add(new(Key.None, "", 0.5));
            r2.Add(new(Key.Delete, "Del"));
            r2.Add(new(Key.End, "End"));
            r2.Add(new(Key.PageDown, "PgDn"));
        }
        if (hasNumpad)
        {
            r2.Add(new(Key.None, "", 0.5));
            r2.Add(new(Key.NumPad7, "7"));
            r2.Add(new(Key.NumPad8, "8"));
            r2.Add(new(Key.NumPad9, "9"));
            r2.Add(new(Key.Add, "+", 1.0));
        }
        result.Add(r2);

        // Row 3: ASDF (15.0u alpha + 0.5u gap + 3.0u nav + 0.5u gap + 4.0u numpad)
        var r3 = new List<KeyItem>
        {
            new(Key.CapsLock, "Caps", 1.75),
            new(Key.A, "A"), new(Key.S, "S"), new(Key.D, "D"), new(Key.F, "F"), new(Key.G, "G"),
            new(Key.H, "H"), new(Key.J, "J"), new(Key.K, "K"), new(Key.L, "L")
        };
        if (isAbnt2)
        {
            r3.Add(new(Key.Oem1, "Ç"));
            r3.Add(new(Key.Oem7, "~ ^"));
        }
        else
        {
            r3.Add(new(Key.Oem1, "; :"));
            r3.Add(new(Key.Oem7, "' \""));
        }
        r3.Add(new(Key.Enter, "Enter", 2.25));

        if (hasNav)
        {
            r3.Add(new(Key.None, "", 0.5));
            r3.Add(new(Key.None, "", 3.0));
        }
        if (hasNumpad)
        {
            r3.Add(new(Key.None, "", 0.5));
            r3.Add(new(Key.NumPad4, "4"));
            r3.Add(new(Key.NumPad5, "5"));
            r3.Add(new(Key.NumPad6, "6"));
            r3.Add(new(Key.None, "", 1.0));
        }
        result.Add(r3);

        // Row 4: ZXCV (15.0u alpha + 0.5u gap + 3.0u nav + 0.5u gap + 4.0u numpad)
        var r4 = new List<KeyItem>();
        if (isAbnt2)
        {
            r4.Add(new(Key.LeftShift, "Shift", 1.25));
            r4.Add(new(Key.OemBackslash, "\\ |", 1.0));
            r4.AddRange(new[] { new KeyItem(Key.Z, "Z"), new KeyItem(Key.X, "X"), new KeyItem(Key.C, "C"), new KeyItem(Key.V, "V"), new KeyItem(Key.B, "B"), new KeyItem(Key.N, "N"), new KeyItem(Key.M, "M") });
            r4.Add(new(Key.OemComma, ", <"));
            r4.Add(new(Key.OemPeriod, ". >"));
            r4.Add(new(Key.OemQuestion, "; :"));
            r4.Add(new(Key.AbntC1, "/ ?"));
            r4.Add(new(Key.RightShift, "Shift", 1.75));
        }
        else
        {
            r4.Add(new(Key.LeftShift, "Shift", 2.25));
            r4.AddRange(new[] { new KeyItem(Key.Z, "Z"), new KeyItem(Key.X, "X"), new KeyItem(Key.C, "C"), new KeyItem(Key.V, "V"), new KeyItem(Key.B, "B"), new KeyItem(Key.N, "N"), new KeyItem(Key.M, "M") });
            r4.Add(new(Key.OemComma, ", <"));
            r4.Add(new(Key.OemPeriod, ". >"));
            r4.Add(new(Key.OemQuestion, "/ ?"));
            r4.Add(new(Key.RightShift, "Shift", 2.75));
        }

        if (hasNav)
        {
            r4.Add(new(Key.None, "", 0.5));
            r4.Add(new(Key.None, "", 1.0));
            r4.Add(new(Key.Up, "▲"));
            r4.Add(new(Key.None, "", 1.0));
        }
        if (hasNumpad)
        {
            r4.Add(new(Key.None, "", 0.5));
            r4.Add(new(Key.NumPad1, "1"));
            r4.Add(new(Key.NumPad2, "2"));
            r4.Add(new(Key.NumPad3, "3"));
            r4.Add(new(Key.Return, "Ent", 1.0));
        }
        result.Add(r4);

        // Row 5: Barra de Espaço (15.0u alpha + 0.5u gap + 3.0u nav + 0.5u gap + 4.0u numpad)
        var r5 = new List<KeyItem>
        {
            new(Key.LeftCtrl, "Ctrl", 1.25),
            new(Key.LWin, "Win", 1.25),
            new(Key.LeftAlt, "Alt", 1.25),
            new(Key.Space, "Espaço (Space)", 6.25),
            new(Key.RightAlt, isAbnt2 ? "AltGr" : "Alt", 1.25),
            new(Key.RWin, "Win", 1.25),
            new(Key.Apps, "Menu", 1.25),
            new(Key.RightCtrl, "Ctrl", 1.25)
        };
        if (hasNav)
        {
            r5.Add(new(Key.None, "", 0.5));
            r5.Add(new(Key.Left, "◀"));
            r5.Add(new(Key.Down, "▼"));
            r5.Add(new(Key.Right, "▶"));
        }
        if (hasNumpad)
        {
            r5.Add(new(Key.None, "", 0.5));
            r5.Add(new(Key.NumPad0, "0", 2.0));
            r5.Add(new(Key.Decimal, "."));
            r5.Add(new(Key.None, "", 1.0));
        }
        result.Add(r5);

        return result;
    }

    #endregion

    #region Mouse (Polling Rate, Cliques, Duplo Clique & CPS Test)

    private void UpdateMouseUi()
    {
        if (TxtMouseHz != null) TxtMouseHz.Text = $"{_mouseTester.CurrentPollingRateHz} Hz";
        if (TxtMousePeakHz != null) TxtMousePeakHz.Text = $"{_mouseTester.PeakPollingRateHz} Hz";
        if (TxtMouseLatency != null) TxtMouseLatency.Text = $"{_mouseTester.CurrentLatencyMs:F1} ms";
        if (TxtMouseDistance != null) TxtMouseDistance.Text = $"{_mouseTester.TotalDistancePx:F0} px";

        if (TxtLeftClicks != null) TxtLeftClicks.Text = $"Esq (LMB): {_mouseTester.LeftClicks}";
        if (TxtRightClicks != null) TxtRightClicks.Text = $"Dir (RMB): {_mouseTester.RightClicks}";
        if (TxtMiddleClicks != null) TxtMiddleClicks.Text = $"Meio (Scroll Click): {_mouseTester.MiddleClicks}";
        if (TxtScrollCounts != null) TxtScrollCounts.Text = $"Scroll ▲ {_mouseTester.ScrollUpCount} | ▼ {_mouseTester.ScrollDownCount}";

        // Detecção de Duplo Clique Acidental (Debounce / Chatter Bug)
        if (TxtDoubleClickCount != null)
        {
            TxtDoubleClickCount.Text = $"{_mouseTester.DefectiveDoubleClicksCount} falhas";
            TxtDoubleClickCount.Foreground = _mouseTester.DefectiveDoubleClicksCount > 0
                ? new SolidColorBrush(Color.FromArgb(255, 239, 68, 68))
                : new SolidColorBrush(Color.FromArgb(255, 16, 185, 129));
        }

        if (TxtDoubleClickStatus != null)
        {
            TxtDoubleClickStatus.Text = _mouseTester.DefectiveDoubleClicksCount > 0
                ? $"ALERTA: Switch defeituoso ({_mouseTester.DefectiveDoubleClicksCount} cliques duplos acidentais com intervalo de {_mouseTester.LastDoubleClickIntervalMs:F1}ms)!"
                : "Switches saudáveis: nenhum duplo clique acidental (<80ms) registrado.";
        }

        // Detecção de salto invertido de rodinha
        if (TxtScrollJitter != null)
        {
            TxtScrollJitter.Text = _mouseTester.ScrollJitterCount > 0
                ? $"{_mouseTester.ScrollJitterCount} inversões anormais"
                : "Rolagem Fluida";
            TxtScrollJitter.Foreground = _mouseTester.ScrollJitterCount > 0
                ? new SolidColorBrush(Color.FromArgb(255, 239, 68, 68))
                : new SolidColorBrush(Color.FromArgb(255, 16, 185, 129));
        }

        // Teste CPS
        if (TxtCpsScore != null) TxtCpsScore.Text = $"{_mouseTester.CpsScore:F1} CPS";
        if (TxtCpsRank != null) TxtCpsRank.Text = $"Classificação: {_mouseTester.CpsRank}";
        if (TxtCpsTimeRemaining != null)
        {
            TxtCpsTimeRemaining.Text = _mouseTester.IsCpsTestRunning
                ? $"{_mouseTester.CpsTimeRemainingSeconds}s restantes"
                : "Pronto";
        }

        // Destaque visual dos botões
        SetButtonVisualState(BorderMouseLeft, _mouseTester.IsLeftDown);
        SetButtonVisualState(BorderMouseRight, _mouseTester.IsRightDown);
        SetButtonVisualState(BorderMouseMiddle, _mouseTester.IsMiddleDown);
        SetButtonVisualState(BorderMouseSide1, _mouseTester.IsXButton1Down);
        SetButtonVisualState(BorderMouseSide2, _mouseTester.IsXButton2Down);
    }

    private static void SetButtonVisualState(Border? border, bool isPressed)
    {
        if (border == null) return;
        bool light = ThemeManager.IsLight;

        if (isPressed)
        {
            var accent = GetThemeAccentBrush();
            var accentFg = GetThemeAccentFgBrush();
            border.Background = accent;
            border.BorderBrush = accent;
            if (border.Child is TextBlock tb) tb.Foreground = accentFg;
        }
        else
        {
            var cardSubtle = (Application.Current?.Resources["CardSubtleBg"] as Brush) ?? (light ? new SolidColorBrush(Color.FromArgb(255, 248, 250, 252)) : new SolidColorBrush(Color.FromArgb(255, 20, 20, 20)));
            var line = (Application.Current?.Resources["Line"] as Brush) ?? (light ? new SolidColorBrush(Color.FromArgb(255, 226, 232, 240)) : new SolidColorBrush(Color.FromArgb(255, 45, 45, 45)));
            var fg = (Application.Current?.Resources["Fg"] as Brush) ?? (light ? new SolidColorBrush(Color.FromArgb(255, 15, 23, 42)) : new SolidColorBrush(Color.FromArgb(255, 220, 220, 220)));

            border.Background = cardSubtle;
            border.BorderBrush = line;
            if (border.Child is TextBlock tb) tb.Foreground = fg;
        }
    }

    private void MouseTestArea_MouseDown(object sender, MouseButtonEventArgs e)
    {
        _mouseTester.OnMouseDown(e.ChangedButton);
        e.Handled = true; // Prevenir menu de contexto do botão direito
    }

    private void MouseTestArea_MouseUp(object sender, MouseButtonEventArgs e)
    {
        _mouseTester.OnMouseUp(e.ChangedButton);
        e.Handled = true;
    }

    private void MouseTestArea_MouseMove(object sender, MouseEventArgs e)
    {
        if (MouseTestArea != null)
        {
            Point pos = e.GetPosition(MouseTestArea);
            _mouseTester.OnMouseMove(pos);
        }
    }

    private void MouseTestArea_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        _mouseTester.OnMouseWheel(e.Delta);
    }

    private void BtnStartCps5s_Click(object sender, RoutedEventArgs e)
    {
        _mouseTester.StartCpsTest(5);
    }

    private void BtnStartCps10s_Click(object sender, RoutedEventArgs e)
    {
        _mouseTester.StartCpsTest(10);
    }

    private void BtnResetMouse_Click(object sender, RoutedEventArgs e)
    {
        _mouseTester.Reset();
    }

    #endregion

    #region Monitor (DDC/CI, Resolução, Hertz, HDR & MonitorLab Pro)

    private void RefreshMonitorsList()
    {
        if (CmbMonitors == null) return;

        _monitorsList = DisplayManager.GetMonitors();
        CmbMonitors.Items.Clear();

        int primaryIdx = 0;
        for (int i = 0; i < _monitorsList.Count; i++)
        {
            var m = _monitorsList[i];
            string label = $"{m.FriendlyName} ({m.CurrentWidth}x{m.CurrentHeight} @ {m.CurrentRefreshRate}Hz)";
            if (m.IsPrimary)
            {
                label += " [Principal]";
                primaryIdx = i;
            }
            CmbMonitors.Items.Add(label);
        }

        if (_monitorsList.Count > 0)
        {
            CmbMonitors.SelectedIndex = primaryIdx;
            _selectedMonitor = _monitorsList[primaryIdx];
            UpdateSelectedMonitorUi();
        }
    }

    private void CmbMonitors_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CmbMonitors.SelectedIndex >= 0 && CmbMonitors.SelectedIndex < _monitorsList.Count)
        {
            _selectedMonitor = _monitorsList[CmbMonitors.SelectedIndex];
            UpdateSelectedMonitorUi();
            Toast("Monitor Selecionado", $"{_selectedMonitor.FriendlyName} ({_selectedMonitor.CurrentWidth}x{_selectedMonitor.CurrentHeight} @ {_selectedMonitor.CurrentRefreshRate}Hz). Ferramentas direcionadas para esta tela.", "\uE7F4");
        }
    }

    private void UpdateSelectedMonitorUi()
    {
        if (_selectedMonitor == null) return;

        // Atualizar controles de resolução suportados
        if (CmbMonitorRes != null)
        {
            CmbMonitorRes.Items.Clear();
            var modes = _selectedMonitor.SupportedResolutions;
            int selIdx = 0;
            string curRes = $"{_selectedMonitor.CurrentWidth} x {_selectedMonitor.CurrentHeight}";
            for (int i = 0; i < modes.Count; i++)
            {
                CmbMonitorRes.Items.Add(modes[i]);
                if (modes[i].Trim() == curRes)
                    selIdx = i;
            }
            if (CmbMonitorRes.Items.Count > 0) CmbMonitorRes.SelectedIndex = selIdx;
        }

        // Atualizar taxas de atualização (Hertz)
        if (CmbMonitorHz != null)
        {
            CmbMonitorHz.Items.Clear();
            var rates = _selectedMonitor.SupportedRefreshRates;
            int selHzIdx = 0;
            for (int i = 0; i < rates.Count; i++)
            {
                CmbMonitorHz.Items.Add($"{rates[i]} Hz");
                if (rates[i] == _selectedMonitor.CurrentRefreshRate)
                    selHzIdx = i;
            }
            if (CmbMonitorHz.Items.Count > 0) CmbMonitorHz.SelectedIndex = selHzIdx;
        }

        // Orientação
        if (CmbMonitorOrientation != null)
        {
            CmbMonitorOrientation.SelectedIndex = Math.Clamp(_selectedMonitor.CurrentOrientation, 0, 3);
        }

        // HDR
        if (BadgeHdrStatus != null)
        {
            BadgeHdrStatus.Text = _selectedMonitor.HdrEnabled ? "HDR ATIVADO (WIDE COLOR GAMUT)" : "HDR DESATIVADO (SDR)";
            BadgeHdrStatus.Foreground = _selectedMonitor.HdrEnabled
                ? new SolidColorBrush(Color.FromArgb(255, 0, 255, 136))
                : new SolidColorBrush(Color.FromArgb(255, 140, 140, 140));
        }

        // Brilho e Contraste
        int curBright = _selectedMonitor.Brightness;
        if (SliderMonitorBrightness != null) SliderMonitorBrightness.Value = curBright;
        if (TxtMonitorBrightnessVal != null) TxtMonitorBrightnessVal.Text = $"{curBright}%";

        int curContrast = _selectedMonitor.Contrast;
        if (SliderMonitorContrast != null) SliderMonitorContrast.Value = curContrast;
        if (TxtMonitorContrastVal != null) TxtMonitorContrastVal.Text = $"{curContrast}%";
    }

    private void SliderMonitorBrightness_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TxtMonitorBrightnessVal == null) return;
        int val = (int)SliderMonitorBrightness.Value;
        TxtMonitorBrightnessVal.Text = $"{val}%";
        if (_selectedMonitor != null)
            DisplayManager.SetBrightness(_selectedMonitor.DeviceName, val);
    }

    private void SliderMonitorContrast_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TxtMonitorContrastVal == null) return;
        int val = (int)SliderMonitorContrast.Value;
        TxtMonitorContrastVal.Text = $"{val}%";
        if (_selectedMonitor != null)
            DisplayManager.SetContrast(_selectedMonitor.DeviceName, val);
    }

    private void BtnApplyResolution_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedMonitor == null || CmbMonitorRes.SelectedItem is not string resStr) return;

        var parts = resStr.Split('x', StringSplitOptions.TrimEntries);
        if (parts.Length == 2 && int.TryParse(parts[0], out int w) && int.TryParse(parts[1], out int h))
        {
            bool ok = DisplayManager.SetResolutionAndRefreshRate(_selectedMonitor.DeviceName, w, h);
            Toast("Lumini Tray", ok ? $"Resolução alterada para {w}x{h}." : "Falha ao aplicar resolução.", ok ? "\uE890" : "\uE711");
            RefreshMonitorsList();
        }
    }

    private void BtnApplyRefreshRate_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedMonitor == null || CmbMonitorHz.SelectedItem is not string hzStr) return;

        string num = hzStr.Replace("Hz", "").Trim();
        if (int.TryParse(num, out int hz))
        {
            bool ok = DisplayManager.SetRefreshRate(_selectedMonitor.DeviceName, hz);
            Toast("Lumini Tray", ok ? $"Taxa de atualização alterada para {hz} Hz." : "Falha ao alterar taxa de atualização.", ok ? "\uE890" : "\uE711");
            RefreshMonitorsList();
        }
    }

    private void BtnApplyOrientation_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedMonitor == null || CmbMonitorOrientation == null) return;
        int orient = CmbMonitorOrientation.SelectedIndex;
        bool ok = DisplayManager.SetOrientation(_selectedMonitor.DeviceName, orient);
        Toast("Lumini Tray", ok ? "Orientação da tela alterada." : "Falha ao alterar orientação.", ok ? "\uE890" : "\uE711");
        RefreshMonitorsList();
    }

    private void BtnSetPrimaryMonitor_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedMonitor == null) return;
        bool ok = DisplayManager.SetPrimaryMonitor(_selectedMonitor.DeviceName);
        Toast("Lumini Tray", ok ? "Monitor definido como Principal do Windows." : "Falha ao definir monitor principal.", ok ? "\uE890" : "\uE711");
        RefreshMonitorsList();
    }

    private void BtnOpenWindowsHdr_Click(object sender, RoutedEventArgs e)
    {
        DisplayManager.OpenWindowsHdrSettings();
    }

    #region MonitorLab Pro Launcher Buttons

    private void BtnStartDeadPixelTest_Click(object sender, RoutedEventArgs e)
    {
        new MonitorLabWindow(MonitorLabMode.DeadPixels, _selectedMonitor).Show();
    }

    private void BtnStartContrastTest_Click(object sender, RoutedEventArgs e)
    {
        new MonitorLabWindow(MonitorLabMode.Contrast, _selectedMonitor).Show();
    }

    private void BtnStartGradientsTest_Click(object sender, RoutedEventArgs e)
    {
        new MonitorLabWindow(MonitorLabMode.Gradients, _selectedMonitor).Show();
    }

    private void BtnStartSharpnessTest_Click(object sender, RoutedEventArgs e)
    {
        new MonitorLabWindow(MonitorLabMode.Sharpness, _selectedMonitor).Show();
    }

    private void BtnStartUniformityTest_Click(object sender, RoutedEventArgs e)
    {
        new MonitorLabWindow(MonitorLabMode.Uniformity, _selectedMonitor).Show();
    }

    private void BtnStartUfoFullscreenTest_Click(object sender, RoutedEventArgs e)
    {
        new MonitorLabWindow(MonitorLabMode.UfoGhosting, _selectedMonitor).Show();
    }

    #endregion

    #region UFO Motion Animation Engine

    private void StartUfoAnimation()
    {
        if (_ufoRunning) return;
        _ufoRunning = true;
        _lastUfoRenderTime = TimeSpan.Zero;
        CompositionTarget.Rendering += OnUfoRendering;
    }

    private void StopUfoAnimation()
    {
        if (!_ufoRunning) return;
        _ufoRunning = false;
        CompositionTarget.Rendering -= OnUfoRendering;
    }

    private void OnUfoRendering(object? sender, EventArgs e)
    {
        if (!_ufoRunning || UfoTrackCanvas == null || !UfoTrackCanvas.IsVisible) return;

        var renderingArgs = (RenderingEventArgs)e;
        if (_lastUfoRenderTime == TimeSpan.Zero)
        {
            _lastUfoRenderTime = renderingArgs.RenderingTime;
            return;
        }

        double dt = (renderingArgs.RenderingTime - _lastUfoRenderTime).TotalSeconds;
        _lastUfoRenderTime = renderingArgs.RenderingTime;
        dt = Math.Clamp(dt, 0.001, 0.08); // Prevenir congelamentos em abas ocultas

        double trackWidth = UfoTrackCanvas.ActualWidth;
        if (trackWidth < 100) trackWidth = 700;

        _ufoPosX960 += 960.0 * dt;
        if (_ufoPosX960 > trackWidth) _ufoPosX960 = -60;

        _ufoPosX480 += 480.0 * dt;
        if (_ufoPosX480 > trackWidth) _ufoPosX480 = -60;

        if (UfoSprite960 != null) Canvas.SetLeft(UfoSprite960, _ufoPosX960);
        if (UfoSprite != null) Canvas.SetLeft(UfoSprite, _ufoPosX480);

        _ufoFrameCount++;
        _ufoFpsAccumulator += dt;
        if (_ufoFpsAccumulator >= 0.5)
        {
            double fps = Math.Round(_ufoFrameCount / _ufoFpsAccumulator, 0);
            _ufoFrameCount = 0;
            _ufoFpsAccumulator = 0;
            if (TxtUfoFpsStatus != null)
                TxtUfoFpsStatus.Text = $"{fps:0} Hz / FPS · V-Sync Ativo";
        }
    }

    #endregion

    #endregion

    #region Saída de Áudio (Seleção, Estéreo, Fase Acústica, 5.1 & mestresdainformatica)

    private void RefreshAudioDevicesList()
    {
        if (CmbAudioOutputDevices == null) return;
        CmbAudioOutputDevices.Items.Clear();

        var devices = AudioCore.GetDevices(AudioInterop.EDataFlow.Render);
        int selectedIdx = 0;

        for (int i = 0; i < devices.Count; i++)
        {
            var dev = devices[i];
            string name = dev.Name;
            if (dev.IsDefaultConsole)
            {
                name += " [Padrão do Sistema]";
                selectedIdx = i;
            }
            CmbAudioOutputDevices.Items.Add(name);
        }

        if (devices.Count > 0)
        {
            CmbAudioOutputDevices.SelectedIndex = selectedIdx;
            _audioTester.SelectedOutputDeviceId = devices[selectedIdx].Id;
            float currentVol = AudioCore.GetDeviceVolumeScalar(devices[selectedIdx].Id);
            if (SliderAudioTestVolume != null)
            {
                SliderAudioTestVolume.Value = Math.Round(currentVol * 100f, 0);
                if (TxtAudioTestVolumeVal != null)
                    TxtAudioTestVolumeVal.Text = $"{Math.Round(currentVol * 100f, 0)}%";
            }
        }
    }

    private void CmbAudioOutputDevices_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        int idx = CmbAudioOutputDevices?.SelectedIndex ?? -1;
        var devices = AudioCore.GetDevices(AudioInterop.EDataFlow.Render);
        if (idx >= 0 && idx < devices.Count)
        {
            var dev = devices[idx];
            _audioTester.SelectedOutputDeviceId = dev.Id;
            // Define padrão do sistema e padrão de comunicações (Discord, etc.) simultaneamente
            AudioCore.SetDefaultDevice(dev.Id, changeCommunications: true);

            float currentVol = AudioCore.GetDeviceVolumeScalar(dev.Id);
            if (SliderAudioTestVolume != null)
            {
                SliderAudioTestVolume.Value = Math.Round(currentVol * 100f, 0);
                if (TxtAudioTestVolumeVal != null)
                    TxtAudioTestVolumeVal.Text = $"{Math.Round(currentVol * 100f, 0)}%";
            }
        }
    }

    private void BtnRefreshAudioOutputDevices_Click(object sender, RoutedEventArgs e)
    {
        RefreshAudioDevicesList();
        Toast("LUMINI ÁUDIO", "Lista de saídas de áudio atualizada.", "\uE767");
    }

    private void ProfileAudio_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton rb) return;

        if (rb == RadioAudioHeadphones)
        {
            _audioTester.CurrentProfile = AudioOutputProfile.Headphones;
            if (TxtAudioProfileDesc != null)
                TxtAudioProfileDesc.Text = "Perfil Fones de Ouvido: focado em palco sonoro binaural, teste de sub-graves profundos (20-60Hz) e isolamento acústico esquerdo/direito.";
        }
        else if (rb == RadioAudioSpeakers)
        {
            _audioTester.CurrentProfile = AudioOutputProfile.Speakers;
            if (TxtAudioProfileDesc != null)
                TxtAudioProfileDesc.Text = "Perfil Caixas de Som: focado em resposta de transientes, teste de cancelamento de fase estéreo e alcance de crossover.";
        }
    }

    private void BtnTestLeftChannel_Click(object sender, RoutedEventArgs e)
    {
        _audioTester.TestChannel(testLeft: true, testRight: false);
    }

    private void BtnTestBothChannels_Click(object sender, RoutedEventArgs e)
    {
        _audioTester.TestChannel(testLeft: true, testRight: true);
    }

    private void BtnTestRightChannel_Click(object sender, RoutedEventArgs e)
    {
        _audioTester.TestChannel(testLeft: false, testRight: true);
    }

    private void BtnTestPingPong_Click(object sender, RoutedEventArgs e)
    {
        _audioTester.TestPingPong();
    }

    private void BtnTestPhaseIn_Click(object sender, RoutedEventArgs e)
    {
        _audioTester.TestAcousticPhase(inPhase: true);
        Toast("LUMINI ÁUDIO", "Tocando tom EM FASE (o som deve parecer firme e centrado na sua cabeça).", "\uE767");
    }

    private void BtnTestPhaseOut_Click(object sender, RoutedEventArgs e)
    {
        _audioTester.TestAcousticPhase(inPhase: false);
        Toast("LUMINI ÁUDIO", "Tocando tom FORA DE FASE (o som deve parecer oco, difuso e com cancelamento de graves).", "\uE767");
    }

    private void BtnSurroundChannel_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is string channel)
        {
            _audioTester.TestSurroundChannel(channel);
        }
    }

    private void SliderCustomHz_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TxtCustomHzVal != null)
            TxtCustomHzVal.Text = $"{(int)SliderCustomHz.Value} Hz";
    }

    private void BtnPlayCustomTone_Click(object sender, RoutedEventArgs e)
    {
        int hz = (int)(SliderCustomHz?.Value ?? 1000);
        _audioTester.PlayFrequencyTone(hz, 2.5);
    }

    private void BtnStartSweep_Click(object sender, RoutedEventArgs e)
    {
        _audioTester.StartFrequencySweep(8.0);
    }

    private void BtnStopSweep_Click(object sender, RoutedEventArgs e)
    {
        _audioTester.StopSweep();
    }

    private void UpdateSweepUi(double currentHz, double fraction)
    {
        if (TxtSweepHz != null)
            TxtSweepHz.Text = currentHz > 0 ? $"{currentHz:0} Hz" : "Parado";

        if (BarSweepProgress != null)
            BarSweepProgress.Value = fraction * 100.0;
    }

    private void BtnTone_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string freqStr && int.TryParse(freqStr, out int freq))
        {
            _audioTester.PlayFrequencyTone(freq, 1.8);
        }
    }

    #endregion

    #region Microfone (Seleção, VU Meter dBFS, Loopback, Gravador 5s & FFT)

    private void RefreshMicDevicesList()
    {
        if (CmbMicDevices == null) return;
        CmbMicDevices.Items.Clear();

        var devices = AudioCore.GetDevices(AudioInterop.EDataFlow.Capture);
        int selectedIdx = 0;

        for (int i = 0; i < devices.Count; i++)
        {
            var dev = devices[i];
            string name = dev.Name;
            if (dev.IsDefaultConsole)
            {
                name += " [Padrão do Sistema]";
                selectedIdx = i;
            }
            CmbMicDevices.Items.Add(name);
        }

        if (devices.Count > 0)
        {
            CmbMicDevices.SelectedIndex = selectedIdx;
            _audioTester.SelectedMicDeviceId = devices[selectedIdx].Id;
            float currentVol = AudioCore.GetDeviceVolumeScalar(devices[selectedIdx].Id);
            if (SliderMicVolume != null)
            {
                SliderMicVolume.Value = Math.Round(currentVol * 100f, 0);
                if (TxtMicVolumeVal != null)
                    TxtMicVolumeVal.Text = $"{Math.Round(currentVol * 100f, 0)}%";
            }
        }
    }

    private void CmbMicDevices_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        int idx = CmbMicDevices?.SelectedIndex ?? -1;
        var devices = AudioCore.GetDevices(AudioInterop.EDataFlow.Capture);
        if (idx >= 0 && idx < devices.Count)
        {
            var dev = devices[idx];
            _audioTester.SelectedMicDeviceId = dev.Id;
            _audioTester.StopMicCapture();
            _audioTester.StartMicCapture();

            float currentVol = AudioCore.GetDeviceVolumeScalar(dev.Id);
            if (SliderMicVolume != null)
            {
                SliderMicVolume.Value = Math.Round(currentVol * 100f, 0);
                if (TxtMicVolumeVal != null)
                    TxtMicVolumeVal.Text = $"{Math.Round(currentVol * 100f, 0)}%";
            }
        }
    }

    private void BtnRefreshMicDevices_Click(object sender, RoutedEventArgs e)
    {
        RefreshMicDevicesList();
        Toast("LUMINI MICROFONE", "Lista de microfones atualizada.", "\uE720");
    }

    private void UpdateMicUi()
    {
        if (TxtMicLevelDb != null)
            TxtMicLevelDb.Text = $"{_audioTester.MicVolumeDb:F1} dBFS ({_audioTester.MicVolumePercent:F0}%)";

        if (BarMicLevel != null)
            BarMicLevel.Value = _audioTester.MicVolumePercent;

        if (TxtMicVerdict != null)
        {
            TxtMicVerdict.Text = _audioTester.MicStatusVerdict;
            TxtMicVerdict.Foreground = _audioTester.MicVolumeDb > -6.0
                ? new SolidColorBrush(Color.FromArgb(255, 239, 68, 68))
                : (_audioTester.MicVolumeDb > -45.0
                    ? new SolidColorBrush(Color.FromArgb(255, 16, 185, 129))
                    : new SolidColorBrush(Color.FromArgb(255, 140, 140, 140)));
        }

        if (TxtMicNoiseStatus != null)
            TxtMicNoiseStatus.Text = _audioTester.MicNoiseLevel;

        if (TxtMicSampleRate != null)
            TxtMicSampleRate.Text = $"{_audioTester.MicSampleRate:N0} Hz · 16 bits · Mono";

        if (TxtMicBass != null) TxtMicBass.Text = $"Graves: {_audioTester.BassLevel:F0}%";
        if (TxtMicMid != null) TxtMicMid.Text = $"Médios: {_audioTester.MidLevel:F0}%";
        if (TxtMicTreble != null) TxtMicTreble.Text = $"Agudos: {_audioTester.TrebleLevel:F0}%";

        // Gravador de voz (5s)
        if (TxtRecordStatus != null)
        {
            TxtRecordStatus.Text = _audioTester.IsRecordingVoice
                ? $"Gravando... ({_audioTester.RecordSecondsRemaining}s)"
                : (_audioTester.IsPlayingRecordedVoice ? "Reproduzindo áudio..." : "Pronto");
        }
        if (BarRecordProgress != null)
            BarRecordProgress.Value = 5 - _audioTester.RecordSecondsRemaining;

        // Atualizar 16 barras do espectro
        if (MicSpectrumBarsContainer != null)
        {
            for (int i = 0; i < Math.Min(16, MicSpectrumBarsContainer.Children.Count); i++)
            {
                if (MicSpectrumBarsContainer.Children[i] is ProgressBar bar)
                {
                    bar.Value = _audioTester.MicFftBands[i] * 100.0;
                }
            }
        }
    }

    private void BtnRecordVoice_Click(object sender, RoutedEventArgs e)
    {
        _audioTester.StartVoiceRecording(5);
    }

    private void BtnPlayVoice_Click(object sender, RoutedEventArgs e)
    {
        _audioTester.PlayRecordedVoice();
    }

    private void ChkMicLoopback_Click(object sender, RoutedEventArgs e)
    {
        _audioTester.IsLoopbackEnabled = ChkMicLoopback?.IsChecked == true;
        Toast("LUMINI MICROFONE", _audioTester.IsLoopbackEnabled ? "Retorno de voz ativado." : "Retorno de voz desativado.", "\uE720");
    }

    private void SliderLoopbackVol_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (SliderLoopbackVol != null)
            _audioTester.LoopbackVolume = SliderLoopbackVol.Value / 100.0;
    }

    #endregion

    #region Gamepad (SDL2 Native & Fallback)

    private void UpdateGamepadUi(GamepadStateSnapshot snap)
    {
        bool light = ThemeManager.IsLight;

        if (TxtGamepadName != null)
            TxtGamepadName.Text = snap.ControllerName;

        if (TxtGamepadEngine != null)
            TxtGamepadEngine.Text = $"Engine: {snap.DriverType} | Status: {(snap.IsConnected ? "Conectado" : "Desconectado")}";

        if (TxtGamepadHz != null)
            TxtGamepadHz.Text = $"{snap.PollingRateHz} Hz (Latência: {snap.PollingLatencyMs:F1} ms)";

        // Analógico Esquerdo
        if (LeftStickDot != null)
        {
            double cx = 50.0 + snap.LeftStickX * 45.0;
            double cy = 50.0 + snap.LeftStickY * 45.0;
            Canvas.SetLeft(LeftStickDot, Math.Clamp(cx, 0.0, 100.0));
            Canvas.SetTop(LeftStickDot, Math.Clamp(cy, 0.0, 100.0));
        }
        if (TxtLeftStickCoords != null)
            TxtLeftStickCoords.Text = $"X: {snap.LeftStickX:+0.00;-0.00;0.00} | Y: {snap.LeftStickY:+0.00;-0.00;0.00}";
        if (TxtLeftStickDrift != null)
            TxtLeftStickDrift.Text = $"Drift: {snap.LeftStickDriftPercent:F1}% · {(snap.LeftStickDriftPercent < 5.0 ? "Excelente" : (snap.LeftStickDriftPercent < 15.0 ? "Aceitável" : "Drift Detectado!"))}";
        if (TxtLeftCircularity != null)
            TxtLeftCircularity.Text = $"Erro de circularidade: {snap.LeftCircularityError:F1}%";

        // Analógico Direito
        if (RightStickDot != null)
        {
            double cx = 50.0 + snap.RightStickX * 45.0;
            double cy = 50.0 + snap.RightStickY * 45.0;
            Canvas.SetLeft(RightStickDot, Math.Clamp(cx, 0.0, 100.0));
            Canvas.SetTop(RightStickDot, Math.Clamp(cy, 0.0, 100.0));
        }
        if (TxtRightStickCoords != null)
            TxtRightStickCoords.Text = $"X: {snap.RightStickX:+0.00;-0.00;0.00} | Y: {snap.RightStickY:+0.00;-0.00;0.00}";
        if (TxtRightStickDrift != null)
            TxtRightStickDrift.Text = $"Drift: {snap.RightStickDriftPercent:F1}% · {(snap.RightStickDriftPercent < 5.0 ? "Excelente" : (snap.RightStickDriftPercent < 15.0 ? "Aceitável" : "Drift Detectado!"))}";
        if (TxtRightCircularity != null)
            TxtRightCircularity.Text = $"Erro de circularidade: {snap.RightCircularityError:F1}%";

        // Gatilhos
        if (BarLeftTrigger != null) BarLeftTrigger.Value = snap.LeftTrigger * 100.0;
        if (TxtLeftTriggerVal != null) TxtLeftTriggerVal.Text = $"{snap.LeftTrigger * 100.0:F0}%";
        if (BarRightTrigger != null) BarRightTrigger.Value = snap.RightTrigger * 100.0;
        if (TxtRightTriggerVal != null) TxtRightTriggerVal.Text = $"{snap.RightTrigger * 100.0:F0}%";

        // Botões Visuais
        SetGamepadButtonVisual(BtnVisualLB, snap.BtnLB, light);
        SetGamepadButtonVisual(BtnVisualRB, snap.BtnRB, light);
        SetGamepadButtonVisual(BtnVisualBack, snap.BtnBack, light);
        SetGamepadButtonVisual(BtnVisualStart, snap.BtnStart, light);
        SetGamepadButtonVisual(BtnVisualGuide, snap.BtnGuide, light);

        SetGamepadButtonVisual(BtnVisualA, snap.BtnA, light);
        SetGamepadButtonVisual(BtnVisualB, snap.BtnB, light);
        SetGamepadButtonVisual(BtnVisualX, snap.BtnX, light);
        SetGamepadButtonVisual(BtnVisualY, snap.BtnY, light);

        SetGamepadButtonVisual(BtnVisualDpadUp, snap.DpadUp, light);
        SetGamepadButtonVisual(BtnVisualDpadDown, snap.DpadDown, light);
        SetGamepadButtonVisual(BtnVisualDpadLeft, snap.DpadLeft, light);
        SetGamepadButtonVisual(BtnVisualDpadRight, snap.DpadRight, light);
    }

    private static void SetGamepadButtonVisual(Border? border, bool isPressed, bool light)
    {
        if (border == null) return;

        if (isPressed)
        {
            var accent = GetThemeAccentBrush();
            var accentFg = GetThemeAccentFgBrush();
            border.Background = accent;
            border.BorderBrush = accent;
            if (border.Child is TextBlock tb) tb.Foreground = accentFg;
        }
        else
        {
            var cardSubtle = (Application.Current?.Resources["CardSubtleBg"] as Brush) ?? (light ? new SolidColorBrush(Color.FromArgb(255, 248, 250, 252)) : new SolidColorBrush(Color.FromArgb(255, 20, 20, 20)));
            var line = (Application.Current?.Resources["Line"] as Brush) ?? (light ? new SolidColorBrush(Color.FromArgb(255, 226, 232, 240)) : new SolidColorBrush(Color.FromArgb(255, 45, 45, 45)));
            var fg = (Application.Current?.Resources["Fg"] as Brush) ?? (light ? new SolidColorBrush(Color.FromArgb(255, 15, 23, 42)) : new SolidColorBrush(Color.FromArgb(255, 220, 220, 220)));

            border.Background = cardSubtle;
            border.BorderBrush = line;
            if (border.Child is TextBlock tb) tb.Foreground = fg;
        }
    }

    private void BtnTestCircularity_Click(object sender, RoutedEventArgs e)
    {
        _gamepadTester.ResetCircularityTest();
        Toast("LUMINI GAMEPAD", "Rode os analógicos em círculos completos de 360° para testar a circularidade.", "\uE7FC");
    }

    private void BtnTestVibration_Click(object sender, RoutedEventArgs e)
    {
        float low = (float)((SliderRumbleLow?.Value ?? 80.0) / 100.0);
        float high = (float)((SliderRumbleHigh?.Value ?? 80.0) / 100.0);
        _gamepadTester.TestVibration(low, high, 1000);
    }

    #endregion

    private void SliderAudioTestVolume_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        float val = (float)(SliderAudioTestVolume?.Value ?? 100.0);
        float scalar = Math.Clamp(val / 100f, 0f, 1f);
        if (TxtAudioTestVolumeVal != null)
            TxtAudioTestVolumeVal.Text = $"{Math.Round(val, 0)}%";

        _audioTester.TestVolumeScalar = scalar;

        if (!string.IsNullOrEmpty(_audioTester.SelectedOutputDeviceId))
        {
            try
            {
                AudioCore.SetDeviceVolumeScalar(_audioTester.SelectedOutputDeviceId, scalar);
            }
            catch { }
        }
    }

    private void SliderMicVolume_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        float val = (float)(SliderMicVolume?.Value ?? 100.0);
        float scalar = Math.Clamp(val / 100f, 0f, 1f);
        if (TxtMicVolumeVal != null)
            TxtMicVolumeVal.Text = $"{Math.Round(val, 0)}%";

        if (!string.IsNullOrEmpty(_audioTester.SelectedMicDeviceId))
        {
            try
            {
                AudioCore.SetDeviceVolumeScalar(_audioTester.SelectedMicDeviceId, scalar);
            }
            catch { }
        }
    }

    private void SliderMicGain_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        double val = (SliderMicGain?.Value ?? 100.0) / 100.0;
        _audioTester.MicSoftwareGain = val;
        if (TxtMicGainVal != null)
            TxtMicGainVal.Text = $"{val:F1}x";
    }

    // =========================================================================
    // SUB-ABA DISPOSITIVOS & BLUETOOTH
    // =========================================================================

    private bool _btModeInitializing;

    private async void RefreshDevicesSubTab()
    {
        _btModeInitializing = true;
        try
        {
            if (RbBtManualMode != null && RbBtAutoMode != null)
            {
                RbBtManualMode.IsChecked = _settings.BluetoothManualMode;
                RbBtAutoMode.IsChecked = !_settings.BluetoothManualMode;
            }
        }
        finally
        {
            _btModeInitializing = false;
        }

        if (TxtPerifDevicesUsbStatus != null)
            TxtPerifDevicesUsbStatus.Text = "Escaneando dispositivos USB e celulares conectados...";
        if (TxtBtRadioStatus != null)
            TxtBtRadioStatus.Text = "Verificando rádio e dispositivos Bluetooth...";

        var (usbDevices, btRadioOn, btDevices) = await Task.Run(() =>
        {
            var usb = DeviceManagerService.ScanConnectedDevices();
            bool radio = BluetoothManagerService.IsBluetoothRadioActive();
            if (radio && !_settings.BluetoothManualMode)
            {
                BluetoothManagerService.AutoConnectFavoritesIfNeeded(_settings);
            }
            var bt = BluetoothManagerService.ListPairedDevices(_settings.BluetoothFavoriteAddresses);
            return (usb, radio, bt);
        });

        RenderUsbDevicesList(usbDevices);
        RenderBluetoothDevicesList(btRadioOn, btDevices);
    }

    private void RenderUsbDevicesList(List<ConnectedDeviceItem> items)
    {
        if (PanelPerifUsbList == null) return;
        PanelPerifUsbList.Children.Clear();

        if (TxtPerifDevicesUsbStatus != null)
        {
            TxtPerifDevicesUsbStatus.Text = items.Count == 0
                ? "Nenhum pen drive, HD externo ou celular MTP conectado no momento."
                : $"{items.Count} dispositivo(s) conectado(s).";
        }

        if (items.Count == 0)
        {
            PanelPerifUsbList.Children.Add(new TextBlock
            {
                Text = "Conecte um pen drive, HD externo, leitor SD ou celular via cabo USB para gerenciar e analisar.",
                Foreground = (Brush)FindResource("Muted"),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap
            });
            return;
        }

        foreach (var item in items)
        {
            var rowBorder = new Border
            {
                Background = (Brush)FindResource("CardSubtleBg"),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 10, 12, 10),
                Margin = new Thickness(0, 0, 0, 8)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var infoStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            infoStack.Children.Add(new TextBlock
            {
                Text = $"{item.Name} · {item.Category}",
                Foreground = (Brush)FindResource("Fg"),
                FontWeight = FontWeights.SemiBold,
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap
            });
            infoStack.Children.Add(new TextBlock
            {
                Text = item.Details,
                Foreground = (Brush)FindResource("Muted"),
                FontSize = 11.5,
                Margin = new Thickness(0, 2, 0, 0),
                TextWrapping = TextWrapping.Wrap
            });
            Grid.SetColumn(infoStack, 0);
            grid.Children.Add(infoStack);

            var btnStack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 0, 0)
            };

            var btnOpen = new Button
            {
                Content = "Abrir",
                Style = (Style)FindResource("OutlineButton"),
                Padding = new Thickness(10, 5, 10, 5),
                FontSize = 11,
                Margin = new Thickness(0, 0, 6, 0)
            };
            btnOpen.Click += (_, _) => DeviceManagerService.OpenDevice(item);
            btnStack.Children.Add(btnOpen);

            if (item.HasStorage)
            {
                var btnAnalyze = new Button
                {
                    Content = "Analisar Disco",
                    Style = (Style)FindResource("OutlineButton"),
                    Padding = new Thickness(10, 5, 10, 5),
                    FontSize = 11,
                    Margin = new Thickness(0, 0, 6, 0),
                    ToolTip = "Analisar saúde, velocidade e integridade deste armazenamento"
                };
                btnAnalyze.Click += (_, _) =>
                {
                    if (NavDisks != null) NavDisks.IsChecked = true;
                    else ShowPage("PageDisks");
                };
                btnStack.Children.Add(btnAnalyze);
            }

            if (item.CanEject)
            {
                var btnEject = new Button
                {
                    Content = "Ejetar",
                    Style = (Style)FindResource("OutlineButton"),
                    Padding = new Thickness(10, 5, 10, 5),
                    FontSize = 11
                };
                btnEject.Click += async (_, _) =>
                {
                    btnEject.IsEnabled = false;
                    var (ok, msg) = await Task.Run(() =>
                    {
                        bool res = DeviceManagerService.EjectDevice(item.PnpDeviceId, out string m);
                        return (res, m);
                    });
                    Toast(ok ? "Dispositivo ejetado" : "Falha na ejeção", msg, "\uE88E");
                    RefreshDevicesSubTab();
                };
                btnStack.Children.Add(btnEject);
            }

            Grid.SetColumn(btnStack, 1);
            grid.Children.Add(btnStack);
            rowBorder.Child = grid;
            PanelPerifUsbList.Children.Add(rowBorder);
        }
    }

    private void RenderBluetoothDevicesList(bool radioActive, List<BluetoothDeviceModel> devices)
    {
        if (PanelPerifBtList == null) return;
        PanelPerifBtList.Children.Clear();

        if (TxtBtRadioStatus != null)
        {
            string modeLabel = _settings.BluetoothManualMode
                ? "Modo Manual ativo (auto-conexão desabilitada)"
                : $"Auto-conexão de Favoritos ativa ({_settings.BluetoothFavoriteAddresses.Count} favorito(s))";
            TxtBtRadioStatus.Text = radioActive
                ? $"Bluetooth ativo · {devices.Count} dispositivo(s) pareado(s) · {modeLabel}"
                : $"Rádio Bluetooth desligado ou ausente neste PC · {modeLabel}";
        }

        if (devices.Count == 0)
        {
            PanelPerifBtList.Children.Add(new TextBlock
            {
                Text = radioActive
                    ? "Nenhum dispositivo Bluetooth pareado encontrado. Use 'Abrir Windows BT' para parear um novo dispositivo."
                    : "Ative o Bluetooth do Windows para listar, favoritar e conectar dispositivos.",
                Foreground = (Brush)FindResource("Muted"),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap
            });
            return;
        }

        foreach (var dev in devices)
        {
            var rowBorder = new Border
            {
                Background = (Brush)FindResource("CardSubtleBg"),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 10, 12, 10),
                Margin = new Thickness(0, 0, 0, 8)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var infoStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            infoStack.Children.Add(new TextBlock
            {
                Text = $"{dev.Name} {(dev.IsFavorite ? "★" : "")}",
                Foreground = (Brush)FindResource(dev.IsConnected ? "Accent" : "Fg"),
                FontWeight = FontWeights.SemiBold,
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap
            });
            infoStack.Children.Add(new TextBlock
            {
                Text = $"{dev.DeviceTypeLabel} · {dev.AddressText} · {(dev.IsConnected ? "Conectado" : "Pareado (Desconectado)")}",
                Foreground = (Brush)FindResource("Muted"),
                FontSize = 11.5,
                Margin = new Thickness(0, 2, 0, 0),
                TextWrapping = TextWrapping.Wrap
            });
            Grid.SetColumn(infoStack, 0);
            grid.Children.Add(infoStack);

            var btnStack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 0, 0)
            };

            var btnFav = new Button
            {
                Content = dev.IsFavorite ? "★ Favorito" : "☆ Favoritar",
                Style = (Style)FindResource(dev.IsFavorite ? "PrimaryButton" : "OutlineButton"),
                Padding = new Thickness(10, 5, 10, 5),
                FontSize = 11,
                Margin = new Thickness(0, 0, 6, 0)
            };
            btnFav.Click += (_, _) =>
            {
                if (_settings.BluetoothFavoriteAddresses.Contains(dev.AddressText, StringComparer.OrdinalIgnoreCase))
                    _settings.BluetoothFavoriteAddresses.RemoveAll(a => a.Equals(dev.AddressText, StringComparison.OrdinalIgnoreCase));
                else
                    _settings.BluetoothFavoriteAddresses.Add(dev.AddressText);

                _settings.Save();
                RefreshDevicesSubTab();
            };
            btnStack.Children.Add(btnFav);

            var btnConn = new Button
            {
                Content = dev.IsConnected ? "Desconectar" : "Conectar",
                Style = (Style)FindResource(dev.IsConnected ? "OutlineButton" : "PrimaryButton"),
                Padding = new Thickness(10, 5, 10, 5),
                FontSize = 11,
                Margin = new Thickness(0, 0, 6, 0),
                IsEnabled = radioActive
            };
            btnConn.Click += async (_, _) =>
            {
                btnConn.IsEnabled = false;
                bool targetConnect = !dev.IsConnected;
                var (ok, msg) = await Task.Run(() =>
                {
                    bool res = BluetoothManagerService.SetDeviceConnected(dev.Address, targetConnect, out string m);
                    return (res, m);
                });
                Toast(ok ? "Bluetooth" : "Aviso Bluetooth", msg, "\uE702");
                RefreshDevicesSubTab();
            };
            btnStack.Children.Add(btnConn);

            var btnRem = new Button
            {
                Content = "Remover",
                Style = (Style)FindResource("OutlineButton"),
                Padding = new Thickness(10, 5, 10, 5),
                FontSize = 11
            };
            btnRem.Click += async (_, _) =>
            {
                btnRem.IsEnabled = false;
                bool ok = await Task.Run(() => BluetoothManagerService.RemoveDevice(dev.Address));
                if (ok)
                {
                    _settings.BluetoothFavoriteAddresses.RemoveAll(a => a.Equals(dev.AddressText, StringComparison.OrdinalIgnoreCase));
                    _settings.Save();
                }
                Toast(ok ? "Bluetooth removido" : "Falha ao remover", dev.Name, "\uE702");
                RefreshDevicesSubTab();
            };
            btnStack.Children.Add(btnRem);

            Grid.SetColumn(btnStack, 1);
            grid.Children.Add(btnStack);
            rowBorder.Child = grid;
            PanelPerifBtList.Children.Add(rowBorder);
        }
    }

    private void BtnRefreshPerifDevices_Click(object sender, RoutedEventArgs e) => RefreshDevicesSubTab();

    private void BtnOpenWindowsBt_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:bluetooth") { UseShellExecute = true });
        }
        catch { }
    }

    private void BtMode_Changed(object sender, RoutedEventArgs e)
    {
        if (_btModeInitializing) return;
        _settings.BluetoothManualMode = RbBtManualMode?.IsChecked == true;
        _settings.Save();
        RefreshDevicesSubTab();
    }

    // -------------------------------------------------------------------------
    // REMAPEADOR DE TECLAS E ATALHOS (POWERTOYS KEYBOARD MANAGER)
    // -------------------------------------------------------------------------
    private int _remapCaptureTarget = 0; // 0 = inativo, 1 = origem, 2 = destino
    private bool _remapUiInitializing = false;

    private void InitKeyboardRemapperUi()
    {
        if (CmbRemapFromKey == null || CmbRemapToKey == null) return;
        _remapUiInitializing = true;
        try
        {
            var catalog = KeyboardRemapperService.Catalog;
            CmbRemapFromKey.ItemsSource = catalog;
            CmbRemapToKey.ItemsSource = catalog;
            if (catalog.Count > 0)
            {
                CmbRemapFromKey.SelectedIndex = 0;
                CmbRemapToKey.SelectedIndex = Math.Min(1, catalog.Count - 1);
            }
            if (ChkKeyboardRemapEnabled != null)
                ChkKeyboardRemapEnabled.IsChecked = _settings.KeyboardRemapEnabled;
            UpdateRemapModeVisibility();
            RenderRemapLists();
            RefreshRemapExcludedAppsChips();
        }
        finally
        {
            _remapUiInitializing = false;
        }
    }

    private void UpdateRemapModeVisibility()
    {
        bool isShortcut = RbRemapTypeShortcut?.IsChecked == true;
        if (PanelRemapFromMods != null)
            PanelRemapFromMods.Visibility = isShortcut ? Visibility.Visible : Visibility.Collapsed;
        if (PanelRemapTargetApp != null)
            PanelRemapTargetApp.Visibility = Visibility.Visible;

        bool isDisabled = ChkRemapToDisabled?.IsChecked == true;
        if (CmbRemapToKey != null) CmbRemapToKey.IsEnabled = !isDisabled;
        if (BtnCaptureToKey != null) BtnCaptureToKey.IsEnabled = !isDisabled;
        if (ChkRemapToCtrl != null) ChkRemapToCtrl.IsEnabled = !isDisabled;
        if (ChkRemapToAlt != null) ChkRemapToAlt.IsEnabled = !isDisabled;
        if (ChkRemapToShift != null) ChkRemapToShift.IsEnabled = !isDisabled;
        if (ChkRemapToWin != null) ChkRemapToWin.IsEnabled = !isDisabled;
    }

    private void ChkKeyboardRemapEnabled_Click(object sender, RoutedEventArgs e)
    {
        if (_remapUiInitializing) return;
        _settings.KeyboardRemapEnabled = ChkKeyboardRemapEnabled?.IsChecked == true;
        ApplyKeyboardRemaps();
    }

    private void RemapRuleType_Changed(object sender, RoutedEventArgs e)
    {
        if (_remapUiInitializing) return;
        CancelRemapCapture();
        UpdateRemapModeVisibility();
    }

    private void ChkRemapToDisabled_Changed(object sender, RoutedEventArgs e)
    {
        if (_remapUiInitializing) return;
        UpdateRemapModeVisibility();
    }

    private void BtnCaptureFromKey_Click(object sender, RoutedEventArgs e)
    {
        if (_remapCaptureTarget == 1)
        {
            CancelRemapCapture();
            return;
        }
        _remapCaptureTarget = 1;
        KeyboardRemapperService.Instance.PauseForCapture(true);
        if (BtnCaptureFromKey != null) BtnCaptureFromKey.Content = "Cancelar";
        if (BtnCaptureToKey != null) BtnCaptureToKey.Content = "Digitar Tecla";
        if (TxtRemapCaptureHint != null)
        {
            TxtRemapCaptureHint.Text = RbRemapTypeShortcut?.IsChecked == true
                ? "Pressione o atalho de origem desejado (ex: Ctrl+Shift+K)..."
                : "Pressione a tecla de origem desejada no teclado...";
            TxtRemapCaptureHint.Visibility = Visibility.Visible;
        }
        Focus();
    }

    private void BtnCaptureToKey_Click(object sender, RoutedEventArgs e)
    {
        if (_remapCaptureTarget == 2)
        {
            CancelRemapCapture();
            return;
        }
        _remapCaptureTarget = 2;
        KeyboardRemapperService.Instance.PauseForCapture(true);
        if (BtnCaptureToKey != null) BtnCaptureToKey.Content = "Cancelar";
        if (BtnCaptureFromKey != null) BtnCaptureFromKey.Content = "Digitar Tecla";
        if (TxtRemapCaptureHint != null)
        {
            TxtRemapCaptureHint.Text = "Pressione a tecla ou atalho de destino desejado...";
            TxtRemapCaptureHint.Visibility = Visibility.Visible;
        }
        Focus();
    }

    private void CancelRemapCapture()
    {
        _remapCaptureTarget = 0;
        KeyboardRemapperService.Instance.PauseForCapture(false);
        if (BtnCaptureFromKey != null) BtnCaptureFromKey.Content = "Digitar Tecla";
        if (BtnCaptureToKey != null) BtnCaptureToKey.Content = "Digitar Tecla";
        if (TxtRemapCaptureHint != null) TxtRemapCaptureHint.Visibility = Visibility.Collapsed;
    }

    private static bool IsPureModifierVk(int vk) =>
        vk == 0x10 || vk == 0x11 || vk == 0x12 ||
        vk == 0xA0 || vk == 0xA1 || vk == 0xA2 || vk == 0xA3 || vk == 0xA4 || vk == 0xA5 ||
        vk == 0x5B || vk == 0x5C;

    private bool HandleRemapKeyCapture(Key k)
    {
        int vk = KeyInterop.VirtualKeyFromKey(k);
        if (vk <= 0) return false;

        bool isShortcutMode = RbRemapTypeShortcut?.IsChecked == true;
        if ((_remapCaptureTarget == 2 || isShortcutMode) && IsPureModifierVk(vk))
        {
            return true;
        }

        ModifierKeys mods = Keyboard.Modifiers;
        if (_remapCaptureTarget == 1)
        {
            if (isShortcutMode)
            {
                if (ChkRemapFromCtrl != null) ChkRemapFromCtrl.IsChecked = mods.HasFlag(ModifierKeys.Control);
                if (ChkRemapFromAlt != null) ChkRemapFromAlt.IsChecked = mods.HasFlag(ModifierKeys.Alt);
                if (ChkRemapFromShift != null) ChkRemapFromShift.IsChecked = mods.HasFlag(ModifierKeys.Shift);
                if (ChkRemapFromWin != null) ChkRemapFromWin.IsChecked = mods.HasFlag(ModifierKeys.Windows);
            }
            SelectKeyInCombo(CmbRemapFromKey, vk);
        }
        else if (_remapCaptureTarget == 2)
        {
            if (ChkRemapToCtrl != null) ChkRemapToCtrl.IsChecked = mods.HasFlag(ModifierKeys.Control);
            if (ChkRemapToAlt != null) ChkRemapToAlt.IsChecked = mods.HasFlag(ModifierKeys.Alt);
            if (ChkRemapToShift != null) ChkRemapToShift.IsChecked = mods.HasFlag(ModifierKeys.Shift);
            if (ChkRemapToWin != null) ChkRemapToWin.IsChecked = mods.HasFlag(ModifierKeys.Windows);
            SelectKeyInCombo(CmbRemapToKey, vk);
        }

        CancelRemapCapture();
        return true;
    }

    private static void SelectKeyInCombo(System.Windows.Controls.ComboBox? combo, int vk)
    {
        if (combo == null) return;
        foreach (var item in combo.Items)
        {
            if (item is KeyOption opt && opt.Vk == vk)
            {
                combo.SelectedItem = opt;
                return;
            }
        }
    }

    private RemapModifiers ReadModifiersFromCheckboxes(
        System.Windows.Controls.CheckBox? ctrl,
        System.Windows.Controls.CheckBox? alt,
        System.Windows.Controls.CheckBox? shift,
        System.Windows.Controls.CheckBox? win)
    {
        RemapModifiers mods = RemapModifiers.None;
        if (ctrl?.IsChecked == true) mods |= RemapModifiers.Ctrl;
        if (alt?.IsChecked == true) mods |= RemapModifiers.Alt;
        if (shift?.IsChecked == true) mods |= RemapModifiers.Shift;
        if (win?.IsChecked == true) mods |= RemapModifiers.Win;
        return mods;
    }

    private void BtnAddKeyboardRemap_Click(object sender, RoutedEventArgs e)
    {
        CancelRemapCapture();
        if (CmbRemapFromKey?.SelectedValue is not int fromVk || fromVk <= 0)
        {
            Toast("REMAPEAMENTO", "Selecione uma tecla de origem válida.", "\uE7BA");
            return;
        }

        bool isDisabled = ChkRemapToDisabled?.IsChecked == true;
        int toVk = 0;
        RemapModifiers toMods = RemapModifiers.None;
        if (!isDisabled)
        {
            if (CmbRemapToKey?.SelectedValue is not int selectedToVk || selectedToVk <= 0)
            {
                Toast("REMAPEAMENTO", "Selecione uma tecla de destino válida.", "\uE7BA");
                return;
            }
            toVk = selectedToVk;
            toMods = ReadModifiersFromCheckboxes(ChkRemapToCtrl, ChkRemapToAlt, ChkRemapToShift, ChkRemapToWin);
        }

        string targetApp = (TxtRemapTargetApp?.Text ?? "").Trim();
        bool isShortcut = RbRemapTypeShortcut?.IsChecked == true;
        if (isShortcut)
        {
            RemapModifiers fromMods = ReadModifiersFromCheckboxes(ChkRemapFromCtrl, ChkRemapFromAlt, ChkRemapFromShift, ChkRemapFromWin);
            if (fromMods == RemapModifiers.None)
            {
                Toast("REMAPEAMENTO", "Atalhos exigem pelo menos um modificador (Ctrl, Alt, Shift ou Win).", "\uE7BA");
                return;
            }
            _settings.ShortcutRemaps.Add(new ShortcutRemapRule
            {
                FromModifiers = fromMods,
                FromVk = fromVk,
                ToModifiers = toMods,
                ToVk = toVk,
                IsDisabled = isDisabled,
                TargetApp = targetApp,
                IsEnabled = true
            });
        }
        else
        {
            if (!isDisabled && fromVk == toVk && toMods == RemapModifiers.None)
            {
                Toast("REMAPEAMENTO", "A tecla de origem e destino não podem ser idênticas.", "\uE7BA");
                return;
            }
            _settings.KeyRemaps.Add(new KeyRemapRule
            {
                FromVk = fromVk,
                ToModifiers = toMods,
                ToVk = toVk,
                IsDisabled = isDisabled,
                TargetApp = targetApp,
                IsEnabled = true
            });
        }

        if (!_settings.KeyboardRemapEnabled)
        {
            _settings.KeyboardRemapEnabled = true;
            _remapUiInitializing = true;
            if (ChkKeyboardRemapEnabled != null) ChkKeyboardRemapEnabled.IsChecked = true;
            _remapUiInitializing = false;
        }

        ApplyKeyboardRemaps();
        RenderRemapLists();
    }

    private void ApplyKeyboardRemaps()
    {
        _settings.Save();
        KeyboardRemapperService.Instance.UpdateRules(
            _settings.KeyboardRemapEnabled,
            _settings.KeyRemaps,
            _settings.ShortcutRemaps,
            _settings.KeyboardRemapExcludedApps);
    }

    private void RenderRemapLists()
    {
        if (PanelKeyRemapsList == null || PanelShortcutRemapsList == null) return;
        PanelKeyRemapsList.Children.Clear();
        PanelShortcutRemapsList.Children.Clear();

        var fgBrush = TryFindResource("Fg") as Brush ?? Brushes.White;
        var mutedBrush = TryFindResource("Muted") as Brush ?? Brushes.Gray;
        var cardSubtleBg = TryFindResource("CardSubtleBg") as Brush ?? Brushes.Transparent;
        var lineBrush = TryFindResource("Line") as Brush ?? Brushes.DimGray;

        if (_settings.KeyRemaps.Count == 0)
        {
            PanelKeyRemapsList.Children.Add(new TextBlock
            {
                Text = "Nenhuma tecla remapeada.",
                Foreground = mutedBrush,
                FontSize = 11,
                Margin = new Thickness(2, 4, 2, 4)
            });
        }
        else
        {
            foreach (var rule in _settings.KeyRemaps.ToList())
            {
                string fromLabel = KeyboardRemapperService.FormatKeyName(rule.FromVk);
                string toLabel = KeyboardRemapperService.FormatShortcut(rule.ToModifiers, rule.ToVk, rule.IsDisabled);
                string appNote = string.IsNullOrWhiteSpace(rule.TargetApp) ? "Global (todos os apps)" : $"App: {rule.TargetApp}";
                PanelKeyRemapsList.Children.Add(BuildRemapRuleRow(
                    $"{fromLabel}  \u2192  {toLabel}",
                    appNote,
                    rule.IsEnabled,
                    enabled => { rule.IsEnabled = enabled; ApplyKeyboardRemaps(); },
                    () => { _settings.KeyRemaps.Remove(rule); ApplyKeyboardRemaps(); RenderRemapLists(); },
                    fgBrush, mutedBrush, cardSubtleBg, lineBrush));
            }
        }

        if (_settings.ShortcutRemaps.Count == 0)
        {
            PanelShortcutRemapsList.Children.Add(new TextBlock
            {
                Text = "Nenhum atalho remapeado.",
                Foreground = mutedBrush,
                FontSize = 11,
                Margin = new Thickness(2, 4, 2, 4)
            });
        }
        else
        {
            foreach (var rule in _settings.ShortcutRemaps.ToList())
            {
                string fromLabel = KeyboardRemapperService.FormatShortcut(rule.FromModifiers, rule.FromVk);
                string toLabel = KeyboardRemapperService.FormatShortcut(rule.ToModifiers, rule.ToVk, rule.IsDisabled);
                string? appNote = string.IsNullOrWhiteSpace(rule.TargetApp) ? "Global (todos os apps)" : $"App: {rule.TargetApp}";
                PanelShortcutRemapsList.Children.Add(BuildRemapRuleRow(
                    $"{fromLabel}  \u2192  {toLabel}",
                    appNote,
                    rule.IsEnabled,
                    enabled => { rule.IsEnabled = enabled; ApplyKeyboardRemaps(); },
                    () => { _settings.ShortcutRemaps.Remove(rule); ApplyKeyboardRemaps(); RenderRemapLists(); },
                    fgBrush, mutedBrush, cardSubtleBg, lineBrush));
            }
        }
    }

    private Border BuildRemapRuleRow(
        string title,
        string? subtitle,
        bool isEnabled,
        Action<bool> onToggle,
        Action onDelete,
        Brush fgBrush,
        Brush mutedBrush,
        Brush cardSubtleBg,
        Brush lineBrush)
    {
        var border = new Border
        {
            Background = cardSubtleBg,
            BorderBrush = lineBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 6, 10, 6),
            Margin = new Thickness(0, 0, 0, 6)
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var chk = new System.Windows.Controls.CheckBox
        {
            IsChecked = isEnabled,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        };
        chk.Click += (_, _) => onToggle(chk.IsChecked == true);
        Grid.SetColumn(chk, 0);
        grid.Children.Add(chk);

        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(new TextBlock
        {
            Text = title,
            Foreground = fgBrush,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        if (!string.IsNullOrEmpty(subtitle))
        {
            stack.Children.Add(new TextBlock
            {
                Text = subtitle,
                Foreground = mutedBrush,
                FontSize = 10,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
        }
        Grid.SetColumn(stack, 1);
        grid.Children.Add(stack);

        var btnDel = new System.Windows.Controls.Button
        {
            Content = "Remover",
            Padding = new Thickness(8, 3, 8, 3),
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center
        };
        if (TryFindResource("GhostButton") is Style ghostStyle)
            btnDel.Style = ghostStyle;
        btnDel.Click += (_, _) => onDelete();
        Grid.SetColumn(btnDel, 2);
        grid.Children.Add(btnDel);

        border.Child = grid;
        return border;
    }

    private void BtnPickRemapTargetApp_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new AppPickerWindow { Owner = this };
            if (picker.ShowDialog() == true && picker.SelectedExecutables.Count > 0)
            {
                if (TxtRemapTargetApp != null)
                {
                    TxtRemapTargetApp.Text = picker.SelectedExecutables[0];
                    TxtRemapTargetApp.Focus();
                }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Não foi possível abrir o seletor de programas: {ex.Message}", "Lumini Tray", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void BtnClearRemapTargetApp_Click(object sender, RoutedEventArgs e)
    {
        if (TxtRemapTargetApp != null)
        {
            TxtRemapTargetApp.Text = "";
            TxtRemapTargetApp.Focus();
        }
    }

    private void BtnOpenRemapExclusionPicker_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new AppPickerWindow(_settings.KeyboardRemapExcludedApps) { Owner = this };
            if (picker.ShowDialog() == true && picker.SelectedExecutables.Count > 0)
            {
                int added = 0;
                foreach (var exe in picker.SelectedExecutables)
                {
                    if (AddRemapExclusionExe(exe, saveAndApply: false))
                        added++;
                }

                if (added > 0)
                {
                    ApplyKeyboardRemaps();
                    RefreshRemapExcludedAppsChips();
                    Toast("Remapeamento de Teclado", $"{added} aplicativo(s) adicionado(s) à lista de exclusão.", "\uE765");
                }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Não foi possível abrir o seletor de programas: {ex.Message}", "Lumini Tray", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void BtnAddRemapExclusion_Click(object sender, RoutedEventArgs e)
    {
        string exe = TxtNewRemapExclusion?.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(exe)) return;

        if (AddRemapExclusionExe(exe, saveAndApply: true))
        {
            RefreshRemapExcludedAppsChips();
            Toast("Remapeamento de Teclado", $"'{exe}' adicionado à lista de exclusão.", "\uE765");
        }
        else
        {
            Toast("Remapeamento de Teclado", $"'{exe}' já está na lista de exclusão.", "\uE7BA");
        }

        if (TxtNewRemapExclusion != null) TxtNewRemapExclusion.Text = "";
    }

    private void TxtNewRemapExclusion_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter)
        {
            BtnAddRemapExclusion_Click(sender, e);
            e.Handled = true;
        }
    }

    private bool AddRemapExclusionExe(string exe, bool saveAndApply)
    {
        string clean = exe.Trim();
        string normalized = KeyboardRemapperService.NormalizeProcessName(clean);
        if (string.IsNullOrEmpty(normalized)) return false;

        bool exists = _settings.KeyboardRemapExcludedApps.Any(a =>
            KeyboardRemapperService.NormalizeProcessName(a).Equals(normalized, StringComparison.OrdinalIgnoreCase));
        if (exists) return false;

        _settings.KeyboardRemapExcludedApps.Add(clean);
        if (saveAndApply)
            ApplyKeyboardRemaps();
        return true;
    }

    private void RefreshRemapExcludedAppsChips()
    {
        if (PanelRemapExcludedAppChips == null || TxtNoRemapExcludedApps == null) return;
        PanelRemapExcludedAppChips.Children.Clear();

        var apps = _settings.KeyboardRemapExcludedApps;
        TxtNoRemapExcludedApps.Visibility = apps.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        var chipBg = TryFindResource("CardSubtleBg") as Brush ?? Brushes.Transparent;
        var chipBorder = TryFindResource("LineStrong") as Brush ?? Brushes.Gray;
        var fgBrush = TryFindResource("Fg") as Brush ?? Brushes.White;
        var mutedBrush = TryFindResource("Muted") as Brush ?? Brushes.Gray;
        var accentBrush = TryFindResource("Accent") as Brush ?? Brushes.DeepSkyBlue;

        foreach (var app in apps.ToList())
        {
            var chip = new Border
            {
                Background = chipBg,
                BorderBrush = chipBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(10, 4, 6, 4),
                Margin = new Thickness(0, 0, 6, 6)
            };

            var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            sp.Children.Add(new TextBlock
            {
                Text = "\uE765",
                FontFamily = TryFindResource("Icons") as FontFamily ?? new FontFamily("Segoe MDL2 Assets"),
                FontSize = 11,
                Foreground = accentBrush,
                Margin = new Thickness(0, 0, 5, 0),
                VerticalAlignment = VerticalAlignment.Center
            });
            sp.Children.Add(new TextBlock
            {
                Text = app,
                Foreground = fgBrush,
                FontSize = 11.5,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0)
            });

            var btnRemove = new System.Windows.Controls.Button
            {
                Content = "✕",
                FontSize = 10,
                Foreground = mutedBrush,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(4, 0, 4, 0),
                Cursor = System.Windows.Input.Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = $"Remover {app} da lista de exclusão"
            };
            string target = app;
            btnRemove.Click += (_, _) =>
            {
                _settings.KeyboardRemapExcludedApps.RemoveAll(a => string.Equals(a, target, StringComparison.OrdinalIgnoreCase));
                ApplyKeyboardRemaps();
                RefreshRemapExcludedAppsChips();
            };

            sp.Children.Add(btnRemove);
            chip.Child = sp;
            PanelRemapExcludedAppChips.Children.Add(chip);
        }
    }
}
