using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace LuminiTray;

/// <summary>
/// Janela flutuante no estilo MicroManager / Dark HUD para o Mini Gerenciador de Tarefas.
/// Possui as mesmas funcionalidades avançadas da Game Overlay:
/// - Fixação/bloqueio com clique passante (WS_EX_TRANSPARENT);
/// - Transparência dupla: scroll normal para transparência de fundo e Ctrl+scroll para opacidade total;
/// - Redimensionamento livre pelas bordas e escala ajustável;
/// - Posicionamento flexível (cantos da tela ou livre por arrasto);
/// - Monitoramento de CPU, GPU, RAM e Disco com histórico de 16 barras.
/// </summary>
public partial class MiniTaskManagerWindow : Window
{
    private static MiniTaskManagerWindow? _instance;
    public static MiniTaskManagerWindow Instance => _instance ??= new MiniTaskManagerWindow();
    public static bool IsWindowVisible => _instance != null && _instance.IsVisible;

    private readonly MiniTaskManagerService _service = MiniTaskManagerService.Instance;
    private int _activeMetric = 0; // 0: CPU, 1: GPU, 2: RAM, 3: DISK
    private MiniTaskManagerService.ProcessTargetInfo? _contextTarget;

    public string Corner { get; set; } = "BR"; // TL, TR, BL, BR ou CUSTOM
    public bool CustomPosition { get; set; } = false;
    public double CustomX { get; set; } = 0;
    public double CustomY { get; set; } = 0;
    public bool IsLocked { get; private set; } = false;
    public double BackgroundOpacity { get; private set; } = 0.90;

    // Configurações de contorno de texto
    private bool _outlineEnabled = true;
    private double _outlineThickness = 3.0;
    private double _outlineOpacity = 0.90;
    private bool _outlineAutoOnly = true;

    public event Action<double>? OpacityChanged;
    public event Action<double>? BackgroundOpacityChanged;
    public event Action<bool>? LockChanged;
    public event Action<double, double>? PositionChanged;
    public event Action<double, double>? SizePersisted;

    private bool _isDragging;
    private IntPtr _hwnd, _hook;
    private IntPtr _monitor;
    private readonly WinEventProc _onForeground;
    private DispatcherTimer? _badgeTimer;

    private static readonly SolidColorBrush BrushCpu = new((Color)ColorConverter.ConvertFromString("#00C8FF"));
    private static readonly SolidColorBrush BrushGpu = new((Color)ColorConverter.ConvertFromString("#00DC64"));
    private static readonly SolidColorBrush BrushRam = new((Color)ColorConverter.ConvertFromString("#FFB446"));
    private static readonly SolidColorBrush BrushDisk = new((Color)ColorConverter.ConvertFromString("#D946EF"));

    private static readonly SolidColorBrush BrushSelectedBorder = new((Color)ColorConverter.ConvertFromString("#4E6478"));
    private static readonly SolidColorBrush BrushSelectedBg = new((Color)ColorConverter.ConvertFromString("#202832"));
    private static readonly SolidColorBrush BrushTransparent = Brushes.Transparent;

    public MiniTaskManagerWindow()
    {
        InitializeComponent();
        ApplyTextOutlineEffect();

        _onForeground = (_, _, hwnd, _, _, _, _) => { TrackGame(hwnd); KeepOnTop(); };

        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            int ex = GetWindowLong(_hwnd, GWL_EXSTYLE);
            int flags = ex | WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
            if (IsLocked) flags |= WS_EX_TRANSPARENT;
            SetWindowLong(_hwnd, GWL_EXSTYLE, flags);

            HwndSource.FromHwnd(_hwnd)?.AddHook(WndProc);

            if (CustomPosition)
            {
                SetWindowPos(_hwnd, HWND_TOPMOST, (int)CustomX, (int)CustomY, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOOWNERZORDER);
            }
        };

        SizeChanged += (_, e) =>
        {
            if (e.NewSize.Width >= MinWidth && e.NewSize.Height >= MinHeight && !IsLocked && _hwnd != IntPtr.Zero)
            {
                SizePersisted?.Invoke(e.NewSize.Width, e.NewSize.Height);
            }
            Reposition();
        };

        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible)
            {
                _service.SetActive(true);
                if (_hook == IntPtr.Zero)
                {
                    _hook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, _onForeground, 0, 0, WINEVENT_OUTOFCONTEXT);
                    TrackGame(GetForegroundWindow());
                    KeepOnTop();
                }

                if (_service.CurrentSnapshot != null && _service.CurrentSnapshot.TotalCpu >= 0)
                {
                    Service_OnMetricsUpdated(_service.CurrentSnapshot);
                }
            }
            else
            {
                Unhook();
                _service.SetActive(false);
            }
        };

        Closed += (_, _) =>
        {
            _badgeTimer?.Stop();
            Unhook();
            _service.SetActive(false);
            _service.OnMetricsUpdated -= Service_OnMetricsUpdated;
            _instance = null;
        };

        _service.OnMetricsUpdated += Service_OnMetricsUpdated;
        UpdateActiveMetricVisuals();
    }

    public void ApplySettings(
        string corner,
        double scale,
        double opacity,
        double bgOpacity,
        bool customPos,
        double customX,
        double customY,
        bool locked)
    {
        Corner = corner;
        CustomPosition = customPos;
        CustomX = customX;
        CustomY = customY;

        SetScale(scale);
        this.Opacity = Math.Clamp(opacity, 0.20, 1.00);
        SetBackgroundOpacity(bgOpacity);
        SetLocked(locked);

        Reposition();
    }

    public void SetScale(double s)
    {
        if (s <= 0.1) s = 1.0;
        Scale.ScaleX = s;
        Scale.ScaleY = s;
    }

    public void SetBackgroundOpacity(double opacity)
    {
        BackgroundOpacity = Math.Clamp(Math.Round(opacity, 2), 0.00, 1.00);
        byte alpha = (byte)Math.Clamp(Math.Round(BackgroundOpacity * 255), 0, 255);
        Box.Background = new SolidColorBrush(Color.FromArgb(alpha, 28, 28, 30));
        ApplyTextOutlineEffect();
    }

    public void UpdateTextOutline(bool enabled, double thickness, double opacity, bool autoOnly)
    {
        _outlineEnabled = enabled;
        _outlineThickness = thickness;
        _outlineOpacity = opacity;
        _outlineAutoOnly = autoOnly;
        ApplyTextOutlineEffect();
    }

    private void ApplyTextOutlineEffect()
    {
        bool shouldApply = _outlineEnabled && (!_outlineAutoOnly || BackgroundOpacity < 0.50);
        if (shouldApply)
        {
            byte alpha = (byte)Math.Clamp(Math.Round(Math.Clamp(_outlineOpacity, 0.0, 1.0) * 255), 0, 255);
            var brush = new SolidColorBrush(Color.FromArgb(alpha, 0, 0, 0));
            brush.Freeze();
            Resources["OverlayStrokeBrush"] = brush;
            Resources["OverlayStrokeThickness"] = Math.Clamp(_outlineThickness, 0.5, 10.0);
        }
        else
        {
            Resources["OverlayStrokeBrush"] = Brushes.Transparent;
            Resources["OverlayStrokeThickness"] = 0.0;
        }
    }

    public void SetLocked(bool locked)
    {
        IsLocked = locked;
        Box.BorderBrush = locked
            ? new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF))
            : new SolidColorBrush(Color.FromArgb(0xEE, 0x00, 0xD2, 0xFF));

        Cursor = locked ? Cursors.Arrow : Cursors.SizeAll;
        TxtLockIndicator.Visibility = locked ? Visibility.Visible : Visibility.Collapsed;
        BtnCloseWindow.Visibility = locked ? Visibility.Collapsed : Visibility.Visible;

        if (_hwnd != IntPtr.Zero)
        {
            int ex = GetWindowLong(_hwnd, GWL_EXSTYLE);
            if (locked)
                SetWindowLong(_hwnd, GWL_EXSTYLE, ex | WS_EX_TRANSPARENT);
            else
                SetWindowLong(_hwnd, GWL_EXSTYLE, ex & ~WS_EX_TRANSPARENT);
        }

        LockChanged?.Invoke(locked);
    }

    #region Redimensionamento e Eventos de Mouse (Overlay Style)

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_NCHITTEST = 0x0084;
        if (msg == WM_NCHITTEST && !IsLocked)
        {
            int x = unchecked((short)(long)lParam);
            int y = unchecked((short)((long)lParam >> 16));
            try
            {
                var pt = PointFromScreen(new Point(x, y));
                const int border = 8;
                bool left = pt.X <= border;
                bool right = pt.X >= ActualWidth - border;
                bool top = pt.Y <= border;
                bool bottom = pt.Y >= ActualHeight - border;

                if (top && left) { handled = true; return new IntPtr(13); }     // HTTOPLEFT
                if (top && right) { handled = true; return new IntPtr(14); }    // HTTOPRIGHT
                if (bottom && left) { handled = true; return new IntPtr(16); }  // HTBOTTOMLEFT
                if (bottom && right) { handled = true; return new IntPtr(17); } // HTBOTTOMRIGHT
                if (left) { handled = true; return new IntPtr(10); }            // HTLEFT
                if (right) { handled = true; return new IntPtr(11); }           // HTRIGHT
                if (top) { handled = true; return new IntPtr(12); }             // HTTOP
                if (bottom) { handled = true; return new IntPtr(15); }          // HTBOTTOM
            }
            catch { }
        }
        return IntPtr.Zero;
    }

    private void Window_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (IsLocked) return;

        double step = e.Delta > 0 ? 0.05 : -0.05;

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            // Ctrl + Scroll: opacidade total da janela
            double newOpacity = Math.Clamp(Math.Round(this.Opacity + step, 2), 0.20, 1.00);
            this.Opacity = newOpacity;
            OpacityChanged?.Invoke(newOpacity);
            TxtOpacityBadge.Text = $"Total: {Math.Round(newOpacity * 100):0}%";
        }
        else
        {
            // Scroll normal: opacidade do fundo sem alterar textos e barras
            double newBgOpacity = Math.Clamp(Math.Round(BackgroundOpacity + step, 2), 0.00, 1.00);
            SetBackgroundOpacity(newBgOpacity);
            BackgroundOpacityChanged?.Invoke(newBgOpacity);
            TxtOpacityBadge.Text = $"Fundo: {Math.Round(newBgOpacity * 100):0}%";
        }

        TxtOpacityBadge.Visibility = Visibility.Visible;
        _badgeTimer?.Stop();
        _badgeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
        _badgeTimer.Tick += (_, _) =>
        {
            TxtOpacityBadge.Visibility = Visibility.Collapsed;
            _badgeTimer.Stop();
        };
        _badgeTimer.Start();

        e.Handled = true;
    }

    private void Window_MouseDown(object sender, MouseButtonEventArgs e)
    {
        // Botão do meio fecha/oculta
        if (e.ChangedButton == MouseButton.Middle)
        {
            Hide();
            return;
        }

        if (e.ChangedButton == MouseButton.Left && !IsLocked)
        {
            if (e.ClickCount == 2)
            {
                SetLocked(true);
                return;
            }

            try
            {
                _isDragging = true;
                DragMove();
            }
            catch { }
            finally
            {
                _isDragging = false;
                RecordCustomPosition();
            }
        }
    }

    private void RecordCustomPosition()
    {
        if (_hwnd == IntPtr.Zero) return;
        if (GetWindowRect(_hwnd, out var wr))
        {
            CustomPosition = true;
            CustomX = wr.Left;
            CustomY = wr.Top;

            var pt = new POINT { X = wr.Left, Y = wr.Top };
            _monitor = MonitorFromPoint(pt, MONITOR_DEFAULTTONEAREST);

            PositionChanged?.Invoke(CustomX, CustomY);
        }
    }

    public void Reposition() => KeepOnTop();

    public void KeepOnTop()
    {
        if (_hwnd == IntPtr.Zero || !IsVisible || _isDragging) return;

        if (CustomPosition)
        {
            SetWindowPos(_hwnd, HWND_TOPMOST, (int)CustomX, (int)CustomY, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOOWNERZORDER);
            return;
        }

        if (_monitor == IntPtr.Zero) _monitor = MonitorFromPoint(new POINT(), MONITOR_DEFAULTTOPRIMARY);
        var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(_monitor, ref mi) || !GetWindowRect(_hwnd, out var wr)) return;

        int w = wr.Right - wr.Left, h = wr.Bottom - wr.Top;
        int m = (int)Math.Round(14 * VisualTreeHelper.GetDpi(this).DpiScaleX);
        var r = mi.rcMonitor;
        int x = Corner.EndsWith("R") ? r.Right - w - m : r.Left + m;
        int y = Corner.StartsWith("B") ? r.Bottom - h - m : r.Top + m;
        SetWindowPos(_hwnd, HWND_TOPMOST, x, y, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOOWNERZORDER);
    }

    private void TrackGame(IntPtr fg)
    {
        if (fg == IntPtr.Zero || CustomPosition) return;
        GetWindowThreadProcessId(fg, out uint pid);
        if (pid == (uint)Environment.ProcessId) return;
        _monitor = MonitorFromWindow(fg, MONITOR_DEFAULTTONEAREST);
    }

    private void Unhook()
    {
        if (_hook == IntPtr.Zero) return;
        UnhookWinEvent(_hook);
        _hook = IntPtr.Zero;
    }

    #endregion

    #region Atualização de Métricas & Gráfico

    private void Service_OnMetricsUpdated(MiniTaskManagerService.MetricSnapshot snap)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => Service_OnMetricsUpdated(snap));
            return;
        }

        TxtCpuTotal.Text = snap.TotalCpu < 0 ? "--%" : $"{snap.TotalCpu}%";
        if (!string.IsNullOrEmpty(snap.TopCpuName) && snap.TopCpuPct > 0)
            TxtCpuTop.Text = $"{snap.TopCpuName}  {snap.TopCpuPct}%";
        else if (snap.TotalCpu >= 0)
            TxtCpuTop.Text = "Ocioso";
        else
            TxtCpuTop.Text = "Coletando...";

        TxtGpuTotal.Text = snap.TotalGpu < 0 ? "--%" : $"{snap.TotalGpu}%";
        if (!string.IsNullOrEmpty(snap.TopGpuName) && snap.TopGpuPct > 0)
            TxtGpuTop.Text = $"{snap.TopGpuName}  {snap.TopGpuPct}%";
        else if (snap.TotalGpu >= 0)
            TxtGpuTop.Text = "Ocioso";
        else
            TxtGpuTop.Text = "Coletando...";

        TxtRamTotal.Text = snap.TotalRam < 0 ? "--%" : $"{snap.TotalRam}%";
        if (!string.IsNullOrEmpty(snap.TopRamName) && snap.TopRamPct > 0)
            TxtRamTop.Text = $"{snap.TopRamName}  {snap.TopRamPct}%";
        else if (snap.TotalRam >= 0)
            TxtRamTop.Text = "—";
        else
            TxtRamTop.Text = "Coletando...";

        TxtDiskTotal.Text = snap.TotalDiskFormatted;
        if (!string.IsNullOrEmpty(snap.TopDiskName) && !string.IsNullOrEmpty(snap.TopDiskFormatted))
            TxtDiskTop.Text = $"{snap.TopDiskName}  {snap.TopDiskFormatted}";
        else if (snap.TotalDiskBps > 0)
            TxtDiskTop.Text = "Atividade de E/S";
        else
            TxtDiskTop.Text = "Ocioso";

        DrawGraph();
    }

    private void UpdateActiveMetricVisuals()
    {
        BorderCpu.BorderBrush = _activeMetric == 0 ? BrushSelectedBorder : BrushTransparent;
        BorderCpu.Background = _activeMetric == 0 ? BrushSelectedBg : BrushTransparent;

        BorderGpu.BorderBrush = _activeMetric == 1 ? BrushSelectedBorder : BrushTransparent;
        BorderGpu.Background = _activeMetric == 1 ? BrushSelectedBg : BrushTransparent;

        BorderRam.BorderBrush = _activeMetric == 2 ? BrushSelectedBorder : BrushTransparent;
        BorderRam.Background = _activeMetric == 2 ? BrushSelectedBg : BrushTransparent;

        BorderDisk.BorderBrush = _activeMetric == 3 ? BrushSelectedBorder : BrushTransparent;
        BorderDisk.Background = _activeMetric == 3 ? BrushSelectedBg : BrushTransparent;

        switch (_activeMetric)
        {
            case 0:
                TxtGraphLabel.Text = "Histórico CPU";
                TxtGraphValue.Text = TxtCpuTotal.Text;
                break;
            case 1:
                TxtGraphLabel.Text = "Histórico GPU";
                TxtGraphValue.Text = TxtGpuTotal.Text;
                break;
            case 2:
                TxtGraphLabel.Text = "Histórico RAM";
                TxtGraphValue.Text = TxtRamTotal.Text;
                break;
            case 3:
                TxtGraphLabel.Text = "Histórico Disco";
                TxtGraphValue.Text = TxtDiskTotal.Text;
                break;
        }

        DrawGraph();
    }

    private void DrawGraph()
    {
        CanvasGraph.Children.Clear();
        int[] samples = _service.GetHistory(_activeMetric);
        if (samples.Length == 0) return;

        double width = CanvasGraph.ActualWidth > 0 ? CanvasGraph.ActualWidth : 200;
        double height = CanvasGraph.ActualHeight > 0 ? CanvasGraph.ActualHeight : 36;
        int maxSlots = MiniTaskManagerService.HistorySamples;
        double barWidth = Math.Max(2, (width / maxSlots) - 2);

        SolidColorBrush barBrush = _activeMetric switch
        {
            1 => BrushGpu,
            2 => BrushRam,
            3 => BrushDisk,
            _ => BrushCpu
        };

        for (int i = 0; i < samples.Length; i++)
        {
            int val = samples[i];
            if (val < 0) continue;
            double barHeight = Math.Max(2, (Math.Min(100, val) / 100.0) * height);
            var rect = new Rectangle
            {
                Width = barWidth,
                Height = barHeight,
                Fill = barBrush,
                RadiusX = 1,
                RadiusY = 1
            };
            Canvas.SetLeft(rect, i * (width / maxSlots) + 1);
            Canvas.SetTop(rect, height - barHeight);
            CanvasGraph.Children.Add(rect);
        }
    }

    private void CanvasGraph_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        DrawGraph();
    }

    #endregion

    #region Interações de Seleção & Atalhos Locais

    private void RowCpu_Click(object sender, MouseButtonEventArgs e)
    {
        _activeMetric = 0;
        _contextTarget = _service.CurrentSnapshot.TopCpuTarget;
        UpdateActiveMetricVisuals();
    }

    private void RowGpu_Click(object sender, MouseButtonEventArgs e)
    {
        _activeMetric = 1;
        _contextTarget = _service.CurrentSnapshot.TopGpuTarget;
        UpdateActiveMetricVisuals();
    }

    private void RowRam_Click(object sender, MouseButtonEventArgs e)
    {
        _activeMetric = 2;
        _contextTarget = _service.CurrentSnapshot.TopRamTarget;
        UpdateActiveMetricVisuals();
    }

    private void RowDisk_Click(object sender, MouseButtonEventArgs e)
    {
        _activeMetric = 3;
        _contextTarget = _service.CurrentSnapshot.TopDiskTarget;
        UpdateActiveMetricVisuals();
    }

    private void Graph_Click(object sender, MouseButtonEventArgs e)
    {
        _activeMetric = (_activeMetric + 1) % 4;
        UpdateActiveMetricVisuals();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Hide();
            e.Handled = true;
        }
        else if (e.Key == Key.Down)
        {
            _activeMetric = (_activeMetric + 1) % 4;
            UpdateActiveMetricVisuals();
            e.Handled = true;
        }
        else if (e.Key == Key.Up)
        {
            _activeMetric = (_activeMetric + 3) % 4;
            UpdateActiveMetricVisuals();
            e.Handled = true;
        }
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        Hide();
    }

    #endregion

    #region Menu de Contexto do Processo

    private MiniTaskManagerService.ProcessTargetInfo? ResolveContextTarget()
    {
        if (_contextTarget != null && _contextTarget.Pid > 0)
            return _contextTarget;

        return _activeMetric switch
        {
            1 => _service.CurrentSnapshot.TopGpuTarget,
            2 => _service.CurrentSnapshot.TopRamTarget,
            3 => _service.CurrentSnapshot.TopDiskTarget,
            _ => _service.CurrentSnapshot.TopCpuTarget
        };
    }

    private void MenuEndTask_Click(object sender, RoutedEventArgs e)
    {
        var target = ResolveContextTarget();
        if (target != null && target.Pid > 0)
        {
            _service.EndTask(target, this);
        }
        else
        {
            MessageBox.Show(this, "Nenhum processo consumidor ativo para a métrica selecionada.",
                "Aviso", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void MenuEndProcessTree_Click(object sender, RoutedEventArgs e)
    {
        var target = ResolveContextTarget();
        if (target != null && target.Pid > 0)
        {
            _service.EndProcessTree(target, this);
        }
        else
        {
            MessageBox.Show(this, "Nenhum processo consumidor ativo para a métrica selecionada.",
                "Aviso", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void MenuOpenFileLocation_Click(object sender, RoutedEventArgs e)
    {
        var target = ResolveContextTarget();
        if (target != null && target.Pid > 0)
        {
            _service.OpenFileLocation(target, this);
        }
        else
        {
            MessageBox.Show(this, "Nenhum processo consumidor ativo para a métrica selecionada.",
                "Aviso", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    #endregion

    public void ToggleVisibility()
    {
        if (IsVisible)
        {
            Hide();
        }
        else
        {
            Reposition();
            Show();
            KeepOnTop();
        }
    }

    #region Win32 P/Invoke

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x20, WS_EX_LAYERED = 0x80000, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;
    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_NOSIZE = 0x0001, SWP_NOACTIVATE = 0x0010, SWP_NOOWNERZORDER = 0x0200;
    private const uint EVENT_SYSTEM_FOREGROUND = 0x0003, WINEVENT_OUTOFCONTEXT = 0;
    private const uint MONITOR_DEFAULTTONEAREST = 2, MONITOR_DEFAULTTOPRIMARY = 1;

    private delegate void WinEventProc(IntPtr hHook, uint ev, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
    [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc, WinEventProc lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hWinEventHook);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X; public int Y; }
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left; public int Top; public int Right; public int Bottom; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    #endregion
}
