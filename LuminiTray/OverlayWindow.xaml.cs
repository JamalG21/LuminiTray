using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace LuminiTray;

/// <summary>
/// OSD sobre o jogo (estilo RTSS/Afterburner): janela transparente, sempre no topo.
/// Inicia solta e arrastável/redimensionável, com ajuste de opacidade pela roda do mouse,
/// fixação por duplo clique e fechamento instantâneo pelo botão do meio do mouse.
/// </summary>
public partial class OverlayWindow : Window
{
    public string Corner { get; set; } = "TL"; // TL, TR, BL, BR ou CUSTOM

    public bool IsLocked { get; private set; } = false;
    public bool CustomPosition { get; set; }
    public double CustomX { get; set; } = 20;
    public double CustomY { get; set; } = 20;

    public event Action<double, double>? PositionChanged;
    public event Action<double, double>? SizePersisted;
    public event Action<double>? OpacityChanged;
    public event Action<double>? BackgroundOpacityChanged;
    public event Action<bool>? LockChanged;
    public event Action? RequestClose;
    public double BackgroundOpacity { get; private set; } = 0.85;

    private bool _isDragging;
    private IntPtr _hwnd, _hook;
    private IntPtr _monitor;                     // monitor do jogo ou monitor atual do overlay
    private readonly WinEventProc _onForeground; // guardado num campo para o GC não coletar o callback
    private DispatcherTimer? _badgeTimer;

    public OverlayWindow()
    {
        InitializeComponent();
        _onForeground = (_, _, hwnd, _, _, _, _) => { TrackGame(hwnd); KeepOnTop(); };
        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            int ex = GetWindowLong(_hwnd, GWL_EXSTYLE);
            int flags = ex | WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
            if (IsLocked) flags |= WS_EX_TRANSPARENT;
            SetWindowLong(_hwnd, GWL_EXSTYLE, flags);

            // Hook para redimensionamento nativo estilo janela tradicional via WM_NCHITTEST
            HwndSource.FromHwnd(_hwnd)?.AddHook(WndProc);

            if (CustomPosition)
            {
                SetWindowPos(_hwnd, HWND_TOPMOST, (int)CustomX, (int)CustomY, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOOWNERZORDER);
            }
        };

        SizeChanged += (_, e) =>
        {
            UpdateLayoutScale();
            if (e.NewSize.Width >= MinWidth && e.NewSize.Height >= MinHeight && !IsLocked && _hwnd != IntPtr.Zero)
            {
                SizePersisted?.Invoke(e.NewSize.Width, e.NewSize.Height);
            }
            Reposition();
        };

        // Só escuta a troca de janela ativa enquanto o overlay está na tela.
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible && _hook == IntPtr.Zero)
            {
                _hook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, _onForeground, 0, 0, WINEVENT_OUTOFCONTEXT);
                TrackGame(GetForegroundWindow());
                KeepOnTop();
            }
            else if (!IsVisible) Unhook();
        };
        Closed += (_, _) =>
        {
            _badgeTimer?.Stop();
            Unhook();
        };
    }

    private const double DesignWidth = 210;  // largura-base do conteúdo (padrão visual da v0.17.6)
    private const double PadX = 22, PadY = 18;   // padding + borda do cartão
    private double _userScale = double.NaN;
    private bool _sizeInitialized;

    /// <summary>Altura natural do conteúdo na largura-base (sem escala).</summary>
    private double NaturalHeight()
    {
        ContentStack.Width = DesignWidth;
        ContentStack.Measure(new Size(DesignWidth, double.PositiveInfinity));
        return Math.Max(1, ContentStack.DesiredSize.Height);
    }

    /// <summary>
    /// Ajusta o conteúdo à janela: escala uniforme que cabe na largura e na altura; a largura que sobra
    /// vira largura útil do conteúdo (gráfico e valores dos sensores se esticam até a borda).
    /// </summary>
    public void UpdateLayoutScale()
    {
        double availW = ActualWidth - PadX, availH = ActualHeight - PadY;
        if (availW <= 0 || availH <= 0 || ContentStack == null) return;
        double hn = NaturalHeight();
        double s = Math.Clamp(Math.Min(availW / DesignWidth, availH / hn), 0.5, 6.0);
        Scale.ScaleX = Scale.ScaleY = s;
        ContentStack.Width = Math.Max(DesignWidth * 0.5, availW / s);
    }

    /// <summary>Tamanho do preset (Pequeno/Médio/Grande): redimensiona a janela; a primeira chamada só registra.</summary>
    public void SetScale(double s)
    {
        bool first = double.IsNaN(_userScale);
        bool changed = !first && Math.Abs(_userScale - s) > 0.001;
        _userScale = s;
        if (changed && _sizeInitialized)
        {
            Width = DesignWidth * s + PadX;
            Height = NaturalHeight() * s + PadY;
        }
    }

    public void Reposition() => KeepOnTop();

    /// <summary>
    /// Trava ou destrava o overlay. Quando travado, ignora cliques e ativa WS_EX_TRANSPARENT.
    /// </summary>
    public void SetLocked(bool locked)
    {
        IsLocked = locked;
        Box.BorderBrush = locked
            ? new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF))
            : new SolidColorBrush(Color.FromArgb(0xEE, 0x00, 0xD2, 0xFF));
        Cursor = locked ? Cursors.Arrow : Cursors.SizeAll;

        if (_hwnd != IntPtr.Zero)
        {
            int ex = GetWindowLong(_hwnd, GWL_EXSTYLE);
            if (locked)
            {
                SetWindowLong(_hwnd, GWL_EXSTYLE, ex | WS_EX_TRANSPARENT);
            }
            else
            {
                SetWindowLong(_hwnd, GWL_EXSTYLE, ex & ~WS_EX_TRANSPARENT);
            }
        }
    }

    public void SetBackgroundOpacity(double opacity)
    {
        BackgroundOpacity = Math.Clamp(Math.Round(opacity, 2), 0.00, 1.00);
        byte alpha = (byte)Math.Clamp(Math.Round(BackgroundOpacity * 255), 0, 255);
        Box.Background = new SolidColorBrush(Color.FromArgb(alpha, 0, 0, 0));
    }

    /// <summary>
    /// Controle de opacidade sobre a overlay:
    /// - Scroll normal: ajusta apenas a transparência do fundo (Box.Background), mantendo as informações nítidas.
    /// - Ctrl + Scroll: ajusta a transparência total da overlay (janela inteira, entre 20% e 100%).
    /// </summary>
    private void Window_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (IsLocked) return;

        bool isCtrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
        double step = e.Delta > 0 ? 0.05 : -0.05;

        if (isCtrl)
        {
            // Ctrl + Scroll: ajusta a opacidade total da janela entre 20% e 100%
            double newOpacity = Math.Clamp(Math.Round(this.Opacity + step, 2), 0.20, 1.00);
            this.Opacity = newOpacity;
            OpacityChanged?.Invoke(newOpacity);

            TxtOpacityBadge.Text = $"Total: {Math.Round(newOpacity * 100):0}%";
        }
        else
        {
            // Scroll simples: ajusta apenas o fundo, mantendo textos e gráficos 100% visíveis
            double newBgOpacity = Math.Clamp(Math.Round(BackgroundOpacity + step, 2), 0.00, 1.00);
            SetBackgroundOpacity(newBgOpacity);
            BackgroundOpacityChanged?.Invoke(newBgOpacity);

            TxtOpacityBadge.Text = $"Fundo: {Math.Round(newBgOpacity * 100):0}%";
        }

        // Feedback visual imediato com percentual
        TxtOpacityBadge.Visibility = Visibility.Visible;
        _badgeTimer?.Stop();
        _badgeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
        _badgeTimer.Tick += (_, _) =>
        {
            TxtOpacityBadge.Visibility = Visibility.Collapsed;
            _badgeTimer.Stop();
        };
        _badgeTimer.Start();

        e.Handled = true;
    }

    /// <summary>
    /// Clique simples arrasta; duplo clique fixa no jogo; clique com botão do meio (scroll) fecha a overlay.
    /// </summary>
    private void Window_MouseDown(object sender, MouseButtonEventArgs e)
    {
        // Botão do meio do mouse (scroll click) fecha a overlay
        if (e.ChangedButton == MouseButton.Middle)
        {
            RequestClose?.Invoke();
            e.Handled = true;
            return;
        }

        if (e.ChangedButton == MouseButton.Left && !IsLocked)
        {
            // Duplo clique fixa a overlay no local atual e ativa a transparência de cliques
            if (e.ClickCount == 2)
            {
                SetLocked(true);
                LockChanged?.Invoke(true);
                e.Handled = true;
                return;
            }

            _isDragging = true;
            try
            {
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

    /// <summary>
    /// WndProc hook: habilita redimensionamento nas bordas e cantos como uma janela normal do Windows.
    /// </summary>
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

    /// <summary>
    /// Retorna a identificação textual amigável do monitor onde o overlay está localizado.
    /// </summary>
    public string GetMonitorDescription()
    {
        try
        {
            var pt = CustomPosition
                ? new POINT { X = (int)CustomX, Y = (int)CustomY }
                : new POINT();
            var mon = MonitorFromPoint(pt, MONITOR_DEFAULTTONEAREST);
            var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (GetMonitorInfo(mon, ref mi))
            {
                bool isPrimary = (mi.dwFlags & 1) != 0;
                string label = isPrimary ? "Monitor Principal" : "Monitor Secundário";
                return $"{label} ({mi.rcMonitor.Left}, {mi.rcMonitor.Top} a {mi.rcMonitor.Right}, {mi.rcMonitor.Bottom})";
            }
        }
        catch { }
        return "Monitor Padrão";
    }

    /// <summary>
    /// Mantém o overlay sempre no topo (HWND_TOPMOST) na posição fixada ou no canto escolhido do monitor.
    /// </summary>
    public void KeepOnTop()
    {
        if (_hwnd == IntPtr.Zero || !IsVisible || _isDragging) return;

        // Se o usuário arrastou e fixou em uma coordenada livre (mesmo em outro monitor)
        if (CustomPosition)
        {
            SetWindowPos(_hwnd, HWND_TOPMOST, (int)CustomX, (int)CustomY, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOOWNERZORDER);
            return;
        }

        if (_monitor == IntPtr.Zero) _monitor = MonitorFromPoint(new POINT(), MONITOR_DEFAULTTOPRIMARY);
        var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(_monitor, ref mi) || !GetWindowRect(_hwnd, out var wr)) return;

        // Tudo em pixels do mesmo sistema de coordenadas (GetMonitorInfo e GetWindowRect), sem conversão de DPI.
        int w = wr.Right - wr.Left, h = wr.Bottom - wr.Top;
        int m = (int)Math.Round(12 * VisualTreeHelper.GetDpi(this).DpiScaleX);
        var r = mi.rcMonitor;
        int x = Corner.EndsWith("R") ? r.Right - w - m : r.Left + m;
        int y = Corner.StartsWith("B") ? r.Bottom - h - m : r.Top + m;
        SetWindowPos(_hwnd, HWND_TOPMOST, x, y, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOOWNERZORDER);
    }

    /// <summary>Segue o monitor da janela ativa, a não ser que a posição tenha sido fixada ou seja do próprio app.</summary>
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

    /// <summary>Atualiza o overlay. <paramref name="show"/> = itens escolhidos (chaves de OverlayMetrics).</summary>
    public void Update(FrameStats? f, IReadOnlyList<OverlayLine> lines, IReadOnlySet<string> show, string? extra)
    {
        static Visibility V(bool on) => on ? Visibility.Visible : Visibility.Collapsed;
        bool fps = show.Contains("fps"), ft = show.Contains("frametime");
        OFpsRow.Visibility = V(fps || ft);
        OFps.Visibility = OFpsUnit.Visibility = V(fps);
        OFrameTime.Visibility = V(ft);
        OFrameTime.Margin = new Thickness(fps ? 12 : 0, 0, 0, fps ? 4 : 0);
        OLow.Visibility = V(show.Contains("low1"));
        OGraph.Visibility = V(show.Contains("graph"));

        if (f != null)
        {
            OFps.Text = $"{f.Fps:0}";
            OFrameTime.Text = $"{f.FrameTimeMs:0.0} ms";
            OLow.Text = $"1% low {f.Low1Fps:0}";
        }
        else
        {
            OFps.Text = "—";
            OFrameTime.Text = fps ? "" : "— ms";
            OLow.Text = "aguardando jogo";
        }
        // Rodapé: nome do jogo e/ou hora.
        var foot = new List<string>();
        if (show.Contains("game") && f != null) foot.Add(f.ProcessName);
        if (extra != null) foot.Add(extra);
        OGame.Text = string.Join("  ·  ", foot);
        OGame.Visibility = V(foot.Count > 0);

        ORows.ItemsSource = lines;
        ORows.Visibility = V(lines.Count > 0);
        if (OGraph.Visibility == Visibility.Visible) DrawGraph(f?.RecentFrameTimes);

        if (!_sizeInitialized && IsLoaded)
        {
            _sizeInitialized = true;
            if (double.IsNaN(Height) || Height < MinHeight)
                Height = NaturalHeight() * (double.IsNaN(_userScale) ? 1 : _userScale) + PadY;
        }
        UpdateLayoutScale();
    }

    /// <summary>Frametimes recentes: picos para cima = engasgos.</summary>
    private void DrawGraph(IReadOnlyList<double>? ft)
    {
        OGraph.Children.Clear();
        double w = OGraph.ActualWidth > 10 ? OGraph.ActualWidth : (ContentStack.Width > 10 ? ContentStack.Width : DesignWidth);
        double h = OGraph.ActualHeight > 10 ? OGraph.ActualHeight : 30;
        OGraph.Children.Add(new Line { X1 = 0, X2 = w, Y1 = h - 0.5, Y2 = h - 0.5, Stroke = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)) });
        if (ft == null || ft.Count < 2) return;

        double max = Math.Max(ft.Max(), ft.Average() * 2); // escala que mostra picos sem achatar o normal
        var line = new Polyline { Stroke = Brushes.White, StrokeThickness = 1 };
        double step = w / (ft.Count - 1);
        for (int i = 0; i < ft.Count; i++)
            line.Points.Add(new Point(i * step, h - 1 - (h - 2) * Math.Min(1, ft[i] / max)));
        OGraph.Children.Add(line);
    }

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x20, WS_EX_LAYERED = 0x80000, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x8000000;
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr h, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr h, int index, int value);

    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_NOSIZE = 0x1, SWP_NOACTIVATE = 0x10, SWP_NOOWNERZORDER = 0x200;
    private const uint EVENT_SYSTEM_FOREGROUND = 0x0003, WINEVENT_OUTOFCONTEXT = 0;
    private const uint MONITOR_DEFAULTTOPRIMARY = 1, MONITOR_DEFAULTTONEAREST = 2;

    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; }

    private delegate void WinEventProc(IntPtr hook, uint ev, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);

    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr h, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(POINT p, uint flags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr mon, ref MONITORINFO mi);
    [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, WinEventProc proc, uint pid, uint thread, uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hook);
}
