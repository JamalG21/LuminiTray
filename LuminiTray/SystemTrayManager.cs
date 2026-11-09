using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace LuminiTray;

/// <summary>
/// Gerenciador robusto de ícones na Bandeja do Sistema (System Tray) usando Windows Forms NotifyIcon nativo.
/// Mantém o ícone do aplicativo Lumini Tray e ícones de status dinâmicos (Microfone e Saída de Som com numeração).
/// </summary>
public sealed class SystemTrayManager : IDisposable
{
    private Forms.NotifyIcon? _mainAppTray;
    private Forms.NotifyIcon? _micTray;
    private Forms.NotifyIcon? _audioTray;
    private Forms.NotifyIcon? _miniTaskTray;
    private Forms.NotifyIcon? _wallpaperTray;
    private Drawing.Icon? _cachedWallpaperPlayingIcon;
    private Drawing.Icon? _cachedWallpaperPausedIcon;
    private bool? _lastAppliedWallpaperPausedState;
    private Action? _onToggleWallpaperPlayPause;
    private Action? _onNextWallpaper;
    private Action? _onPrevWallpaper;
    private Action<int>? _onSetWallpaperInterval;
    private Action? _onDeactivateWallpaper;
    public bool ShowWallpaperIcon { get; set; } = false;
    private bool _lastWallpaperPaused = false;
    private string _lastWallpaperName = "Wallpaper Animado";



    private Action? _onToggleMicMute;
    private Action? _onCycleAudio;
    private Action? _onOpenApp;
    private Action? _onExitApp;
    private Func<List<AudioDeviceInfo>>? _getPlaybackDevices;
    private Action<string>? _onSelectPlaybackDevice;

    public bool ShowMicIcon { get; set; } = true;
    public bool ShowAudioIcon { get; set; } = true;
    public bool ShowMiniTaskIcon { get; set; } = true;

    private bool _lastMicMuted = false;
    private string _lastMicName = "Microfone";
    private string _lastAudioName = "Alto-falantes";
    private int _lastAudioVolume = 100;
    private int _lastOutputNumber = 1;

    [DllImport("user32.dll")]
    public static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string? lpszClass, string? lpszWindow);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    private const uint WM_MOUSEMOVE = 0x0200;

    /// <summary>
    /// Limpa ícones fantasmas deixados na bandeja por instâncias anteriores finalizadas abruptamente.
    /// </summary>
    public static void RefreshNotificationArea()
    {
        try
        {
            IntPtr hTray = FindWindow("Shell_TrayWnd", null);
            if (hTray != IntPtr.Zero)
            {
                IntPtr hNotify = FindWindowEx(hTray, IntPtr.Zero, "TrayNotifyWnd", null);
                if (hNotify != IntPtr.Zero)
                {
                    IntPtr hPager = FindWindowEx(hNotify, IntPtr.Zero, "SysPager", null);
                    IntPtr hToolbar = FindWindowEx(hPager != IntPtr.Zero ? hPager : hNotify, IntPtr.Zero, "ToolbarWindow32", null);
                    if (hToolbar != IntPtr.Zero)
                    {
                        RefreshToolbar(hToolbar);
                    }
                }
            }

            IntPtr hOverflow = FindWindow("NotifyIconOverflowWindow", null);
            if (hOverflow != IntPtr.Zero)
            {
                IntPtr hToolbarOverflow = FindWindowEx(hOverflow, IntPtr.Zero, "ToolbarWindow32", null);
                if (hToolbarOverflow != IntPtr.Zero)
                {
                    RefreshToolbar(hToolbarOverflow);
                }
            }
        }
        catch { }
    }

    private static void RefreshToolbar(IntPtr hToolbar)
    {
        if (GetClientRect(hToolbar, out RECT rect))
        {
            for (int x = 0; x < rect.Right; x += 8)
            {
                for (int y = 0; y < rect.Bottom; y += 8)
                {
                    IntPtr lParam = (IntPtr)((y << 16) | (x & 0xFFFF));
                    SendMessage(hToolbar, WM_MOUSEMOVE, IntPtr.Zero, lParam);
                }
            }
        }
    }

    public SystemTrayManager()
    {
        InitializeNotifyIcons();
    }

    public void SetWindowHandle(IntPtr hWnd)
    {
        UpdateAllIcons();
        System.Threading.Tasks.Task.Run(RefreshNotificationArea);
    }

    public void InitializeWallpaperControls(
        Action onTogglePlayPause,
        Action onNext,
        Action onPrev,
        Action<int> onSetInterval,
        Action onDeactivate)
    {
        _onToggleWallpaperPlayPause = onTogglePlayPause;
        _onNextWallpaper = onNext;
        _onPrevWallpaper = onPrev;
        _onSetWallpaperInterval = onSetInterval;
        _onDeactivateWallpaper = onDeactivate;
    }

    public void Initialize(
        IntPtr hWnd,
        Action onToggleMicMute,
        Action onCycleAudio,
        Action onOpenApp,
        Action onExitApp,
        Func<List<AudioDeviceInfo>> getPlaybackDevices,
        Action<string> onSelectPlaybackDevice)
    {
        _onToggleMicMute = onToggleMicMute;
        _onCycleAudio = onCycleAudio;
        _onOpenApp = onOpenApp;
        _onExitApp = onExitApp;
        _getPlaybackDevices = getPlaybackDevices;
        _onSelectPlaybackDevice = onSelectPlaybackDevice;

        UpdateAllIcons();
    }


    private void InitializeNotifyIcons()
    {
        try
        {
            // 1. Ícone Principal da Aplicação
            _mainAppTray = new Forms.NotifyIcon
            {
                Text = "Lumini Tray",
                Visible = true
            };

            _mainAppTray.Icon = CreateMainAppIcon();

            _mainAppTray.DoubleClick += (_, _) => _onOpenApp?.Invoke();
            _mainAppTray.MouseClick += (_, e) =>
            {
                if (e.Button == Forms.MouseButtons.Left)
                {
                    _onOpenApp?.Invoke();
                }
            };
            BuildMainContextMenu();

            // 2. Ícone de Microfone
            _micTray = new Forms.NotifyIcon
            {
                Text = "Microfone",
                Visible = ShowMicIcon
            };
            _micTray.MouseClick += (_, e) =>
            {
                if (e.Button == Forms.MouseButtons.Left)
                {
                    _onToggleMicMute?.Invoke();
                }
            };

            // 3. Ícone de Saída de Áudio
            _audioTray = new Forms.NotifyIcon
            {
                Text = "Saída de Áudio",
                Visible = ShowAudioIcon
            };
            _audioTray.MouseClick += (_, e) =>
            {
                if (e.Button == Forms.MouseButtons.Left)
                {
                    _onCycleAudio?.Invoke();
                }
            };

            UpdateMic(false, "Microfone");
            UpdateAudioOutput("Alto-falantes", 100, 1);

            // 4. Ícone do Mini Gerenciador de Tarefas (MicroManager)
            _miniTaskTray = new Forms.NotifyIcon
            {
                Text = "Mini Gerenciador de Tarefas",
                Visible = ShowMiniTaskIcon
            };
            _miniTaskTray.MouseClick += (_, e) =>
            {
                if (e.Button == Forms.MouseButtons.Left)
                {
                    MiniTaskManagerWindow.Instance.ToggleVisibility();
                }
            };
            UpdateMiniTaskIcon();

            // 5. Ícone de Wallpaper Animado (Lively)
            _wallpaperTray = new Forms.NotifyIcon
            {
                Text = "Lumini Tray - Wallpaper",
                Visible = ShowWallpaperIcon
            };
            _wallpaperTray.MouseClick += (_, e) =>
            {
                if (e.Button == Forms.MouseButtons.Left)
                {
                    _onToggleWallpaperPlayPause?.Invoke();
                }
            };
            _wallpaperTray.DoubleClick += (_, _) => _onOpenApp?.Invoke();
            UpdateWallpaper(_lastWallpaperPaused, _lastWallpaperName);
        }
        catch { }
    }

    public void UpdateAllIcons()
    {
        BuildMainContextMenu();
        UpdateMic(_lastMicMuted, _lastMicName);
        UpdateAudioOutput(_lastAudioName, _lastAudioVolume, _lastOutputNumber);
        UpdateMiniTaskIcon();
        UpdateWallpaper(_lastWallpaperPaused, _lastWallpaperName);
    }

    public void UpdateMic(bool isMuted, string? micName)
    {
        _lastMicMuted = isMuted;
        if (!string.IsNullOrWhiteSpace(micName)) _lastMicName = micName;

        if (_micTray != null)
        {
            _micTray.Visible = ShowMicIcon;
            if (ShowMicIcon)
            {
                IntPtr hIcon = CreateMicIcon(isMuted);
                try
                {
                    using var tempIcon = Drawing.Icon.FromHandle(hIcon);
                    _micTray.Icon = (Drawing.Icon)tempIcon.Clone();
                }
                finally
                {
                    DestroyIcon(hIcon);
                }

                string status = isMuted ? "MUTADO" : "ATIVO";
                string tip = $"Microfone: {_lastMicName} ({status})";
                if (tip.Length >= 63) tip = tip.Substring(0, 60) + "...";
                _micTray.Text = tip;
                BuildMicContextMenu();
            }
        }
    }

    public void UpdateAudioOutput(string? deviceName, int volumePercent, int outputNumber = 1)
    {
        if (!string.IsNullOrWhiteSpace(deviceName)) _lastAudioName = deviceName;
        _lastAudioVolume = volumePercent;
        if (outputNumber > 0) _lastOutputNumber = outputNumber;

        if (_audioTray != null)
        {
            _audioTray.Visible = ShowAudioIcon;
            if (ShowAudioIcon)
            {
                IntPtr hIcon = CreateSpeakerIcon(_lastOutputNumber);
                try
                {
                    using var tempIcon = Drawing.Icon.FromHandle(hIcon);
                    _audioTray.Icon = (Drawing.Icon)tempIcon.Clone();
                }
                finally
                {
                    DestroyIcon(hIcon);
                }

                string tip = $"Saída #{_lastOutputNumber}: {_lastAudioName} ({_lastAudioVolume}%)";
                if (tip.Length >= 63) tip = tip.Substring(0, 60) + "...";
                _audioTray.Text = tip;
                BuildAudioContextMenu();
            }
        }
    }

    public void RemoveMicIcon()
    {
        if (_micTray != null) _micTray.Visible = false;
    }

    public void RemoveAudioIcon()
    {
        if (_audioTray != null) _audioTray.Visible = false;
    }

    public bool HandleWindowMessage(int msg, IntPtr wParam, IntPtr lParam)
    {
        // Gerenciado nativamente pelo Forms.NotifyIcon
        return false;
    }

    private void BuildMainContextMenu()
    {
        if (_mainAppTray == null) return;
        var menu = new Forms.ContextMenuStrip();

        var itemOpen = new Forms.ToolStripMenuItem("🌟 Abrir Lumini Tray", null, (_, _) => _onOpenApp?.Invoke())
        {
            Font = new Drawing.Font(Forms.Control.DefaultFont, Drawing.FontStyle.Bold)
        };
        menu.Items.Add(itemOpen);

        menu.Items.Add(new Forms.ToolStripSeparator());

        menu.Items.Add(new Forms.ToolStripMenuItem("⚡ Mini Gerenciador de Tarefas", null, (_, _) => MiniTaskManagerWindow.Instance.ToggleVisibility()));
        menu.Items.Add(new Forms.ToolStripMenuItem("🎙 Microfone: Mutar / Desmutar", null, (_, _) => _onToggleMicMute?.Invoke()));
        menu.Items.Add(new Forms.ToolStripMenuItem("🔊 Alternar Saída de Som", null, (_, _) => _onCycleAudio?.Invoke()));

        menu.Items.Add(new Forms.ToolStripSeparator());

        menu.Items.Add(new Forms.ToolStripMenuItem("❌ Encerrar Aplicativo", null, (_, _) => _onExitApp?.Invoke()));

        _mainAppTray.ContextMenuStrip = menu;
    }

    private void BuildMicContextMenu()
    {
        if (_micTray == null) return;
        var menu = new Forms.ContextMenuStrip();

        string actionText = _lastMicMuted ? "🟢 Desmutar Microfone" : "🔴 Mutar Microfone";
        var itemToggle = new Forms.ToolStripMenuItem(actionText, null, (_, _) => _onToggleMicMute?.Invoke())
        {
            Font = new Drawing.Font(Forms.Control.DefaultFont, Drawing.FontStyle.Bold)
        };
        menu.Items.Add(itemToggle);

        var itemDevice = new Forms.ToolStripMenuItem($"🎙 Dispositivo: {_lastMicName}") { Enabled = false };
        menu.Items.Add(itemDevice);

        menu.Items.Add(new Forms.ToolStripSeparator());

        menu.Items.Add(new Forms.ToolStripMenuItem("🌟 Abrir Lumini Tray", null, (_, _) => _onOpenApp?.Invoke()));
        menu.Items.Add(new Forms.ToolStripMenuItem("❌ Sair do Aplicativo", null, (_, _) => _onExitApp?.Invoke()));

        _micTray.ContextMenuStrip = menu;
    }

    private void BuildAudioContextMenu()
    {
        if (_audioTray == null) return;
        var menu = new Forms.ContextMenuStrip();

        var itemCycle = new Forms.ToolStripMenuItem("🔊 Próxima Saída (Alternar)", null, (_, _) => _onCycleAudio?.Invoke())
        {
            Font = new Drawing.Font(Forms.Control.DefaultFont, Drawing.FontStyle.Bold)
        };
        menu.Items.Add(itemCycle);

        menu.Items.Add(new Forms.ToolStripSeparator());

        var headerItem = new Forms.ToolStripMenuItem("Dispositivos de Reprodução:") { Enabled = false };
        menu.Items.Add(headerItem);

        var devices = _getPlaybackDevices?.Invoke() ?? new List<AudioDeviceInfo>();
        for (int i = 0; i < devices.Count; i++)
        {
            var dev = devices[i];
            string devId = dev.Id;
            int num = dev.OutputNumber > 0 ? dev.OutputNumber : (i + 1);
            string prefix = dev.IsDefaultConsole ? $"✔ [#{num}] " : $"    [#{num}] ";
            var devItem = new Forms.ToolStripMenuItem($"{prefix}{dev.Name}", null, (_, _) =>
            {
                _onSelectPlaybackDevice?.Invoke(devId);
            })
            {
                Checked = dev.IsDefaultConsole
            };
            menu.Items.Add(devItem);
        }

        menu.Items.Add(new Forms.ToolStripSeparator());

        menu.Items.Add(new Forms.ToolStripMenuItem("🌟 Abrir Lumini Tray", null, (_, _) => _onOpenApp?.Invoke()));
        menu.Items.Add(new Forms.ToolStripMenuItem("❌ Sair do Aplicativo", null, (_, _) => _onExitApp?.Invoke()));

        _audioTray.ContextMenuStrip = menu;
    }

    /// <summary>
    /// Carrega o ícone principal do Lumini Tray para a Bandeja do Sistema (32x32 ARGB de alta nitidez).
    /// </summary>
    public static Drawing.Icon CreateMainAppIcon()
    {
        try
        {
            Drawing.Bitmap? sourceBmp = null;
            try
            {
                var uri = new Uri("pack://application:,,,/Assets/logo.png", UriKind.Absolute);
                var sri = System.Windows.Application.GetResourceStream(uri);
                if (sri?.Stream != null)
                {
                    using (sri.Stream)
                    {
                        sourceBmp = new Drawing.Bitmap(sri.Stream);
                    }
                }
            }
            catch { }

            if (sourceBmp == null)
            {
                string logoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "logo.png");
                if (File.Exists(logoPath))
                    sourceBmp = new Drawing.Bitmap(logoPath);
                else if (File.Exists("Assets\\logo.png"))
                    sourceBmp = new Drawing.Bitmap("Assets\\logo.png");
            }

            if (sourceBmp != null)
            {
                using (sourceBmp)
                using (var bmp = new Drawing.Bitmap(32, 32, Drawing.Imaging.PixelFormat.Format32bppArgb))
                {
                    using (var g = Drawing.Graphics.FromImage(bmp))
                    {
                        g.SmoothingMode = SmoothingMode.HighQuality;
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        g.Clear(Drawing.Color.Transparent);
                        using var clipPath = new GraphicsPath();
                        AddRoundedRectangle(clipPath, new Drawing.RectangleF(0, 0, 32, 32), 7f, 7f);
                        g.SetClip(clipPath);
                        g.DrawImage(sourceBmp, new Drawing.Rectangle(0, 0, 32, 32));
                    }
                    IntPtr hIcon = bmp.GetHicon();
                    try
                    {
                        using var tempIcon = Drawing.Icon.FromHandle(hIcon);
                        return (Drawing.Icon)tempIcon.Clone();
                    }
                    finally
                    {
                        DestroyIcon(hIcon);
                    }
                }
            }
        }
        catch { }

        try
        {
            string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "app.ico");
            if (File.Exists(iconPath))
                return new Drawing.Icon(iconPath, 32, 32);
            if (File.Exists("Assets\\app.ico"))
                return new Drawing.Icon("Assets\\app.ico", 32, 32);
        }
        catch { }

        try
        {
            string? exePath = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
            {
                var assoc = Drawing.Icon.ExtractAssociatedIcon(exePath);
                if (assoc != null) return assoc;
            }
        }
        catch { }

        return Drawing.SystemIcons.Application;
    }

    /// <summary>
    /// Gera um ícone 32x32 moderno e anti-aliasing para o Microfone (Verde = Ativo / Vermelho = Mutado com corte).
    /// </summary>
    public static IntPtr CreateMicIcon(bool isMuted)
    {
        using var bmp = new Drawing.Bitmap(32, 32, Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Drawing.Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.Clear(Drawing.Color.Transparent);

            Drawing.Color mainColor = isMuted ? Drawing.Color.FromArgb(239, 68, 68) : Drawing.Color.FromArgb(16, 185, 129);
            Drawing.Color darkShadow = Drawing.Color.FromArgb(140, 15, 23, 42); // contorno escuro para alto contraste

            // Outline / sombra para máximo contraste em temas claros e escuros
            using (var path = new GraphicsPath())
            {
                AddRoundedRectangle(path, new Drawing.RectangleF(10, 3, 12, 17), 6, 6);
                using (var shadowPen = new Drawing.Pen(darkShadow, 4f))
                {
                    shadowPen.LineJoin = LineJoin.Round;
                    g.DrawPath(shadowPen, path);
                }
                using (var b = new Drawing.SolidBrush(mainColor))
                {
                    g.FillPath(b, path);
                }
            }

            // Linhas de detalhe da cápsula
            using (var grillePen = new Drawing.Pen(Drawing.Color.FromArgb(90, 255, 255, 255), 1.2f))
            {
                g.DrawLine(grillePen, 13, 8, 19, 8);
                g.DrawLine(grillePen, 13, 12, 19, 12);
            }

            // Suporte (Cradle) e Base
            using (var shadowPen = new Drawing.Pen(darkShadow, 4.5f))
            {
                shadowPen.StartCap = LineCap.Round;
                shadowPen.EndCap = LineCap.Round;
                g.DrawArc(shadowPen, 5.5f, 9, 21, 13, 0, 180);
                g.DrawLine(shadowPen, 16, 22, 16, 28);
                g.DrawLine(shadowPen, 9, 28, 23, 28);
            }
            using (var p = new Drawing.Pen(mainColor, 2.5f))
            {
                p.StartCap = LineCap.Round;
                p.EndCap = LineCap.Round;
                g.DrawArc(p, 5.5f, 9, 21, 13, 0, 180);
                g.DrawLine(p, 16, 22, 16, 28);
                g.DrawLine(p, 9, 28, 23, 28);
            }

            // Corte diagonal nítido quando mutado
            if (isMuted)
            {
                using (var darkPen = new Drawing.Pen(Drawing.Color.FromArgb(20, 20, 20), 4.8f))
                {
                    darkPen.StartCap = LineCap.Round;
                    darkPen.EndCap = LineCap.Round;
                    g.DrawLine(darkPen, 4, 28, 28, 4);
                }
                using (var slashPen = new Drawing.Pen(Drawing.Color.White, 2.6f))
                {
                    slashPen.StartCap = LineCap.Round;
                    slashPen.EndCap = LineCap.Round;
                    g.DrawLine(slashPen, 4, 28, 28, 4);
                }
            }
        }
        return bmp.GetHicon();
    }

    /// <summary>
    /// Gera um ícone 32x32 moderno com corpo de alto-falante e badge numérico da saída ativa (1, 2, 3, etc.) com destaque máximo para legibilidade na barra de tarefas.
    /// </summary>
    public static IntPtr CreateSpeakerIcon(int outputNumber = 1)
    {
        if (outputNumber <= 0) outputNumber = 1;
        using var bmp = new Drawing.Bitmap(32, 32, Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Drawing.Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.Clear(Drawing.Color.Transparent);

            Drawing.Color darkShadow = Drawing.Color.FromArgb(220, 15, 23, 42);

            // Corpo do Alto-falante compacto e nítido à esquerda (x: 0.5 a 8)
            using (var spkPath = new GraphicsPath())
            {
                spkPath.AddRectangle(new Drawing.RectangleF(0.5f, 11, 3, 10));
                spkPath.AddPolygon(new Drawing.PointF[] {
                    new Drawing.PointF(3.5f, 11),
                    new Drawing.PointF(8f, 6),
                    new Drawing.PointF(8f, 26),
                    new Drawing.PointF(3.5f, 21)
                });

                using (var shadowPen = new Drawing.Pen(darkShadow, 3f))
                {
                    shadowPen.LineJoin = LineJoin.Round;
                    g.DrawPath(shadowPen, spkPath);
                }
                using (var b = new Drawing.SolidBrush(Drawing.Color.FromArgb(248, 250, 252)))
                {
                    g.FillPath(b, spkPath);
                }
            }

            // Grande Badge de Destaque para o Número (x: 8.5 a 31.5, altura de 30px)
            Drawing.RectangleF badgeRect = new Drawing.RectangleF(8.5f, 1f, 23f, 30f);
            Drawing.Color badgeBg1 = Drawing.Color.FromArgb(14, 165, 233); // Cyan vivo (#0EA5E9)
            Drawing.Color badgeBg2 = Drawing.Color.FromArgb(3, 105, 161);  // Azul oceano (#0369A1)

            using (var badgePath = new GraphicsPath())
            {
                AddRoundedRectangle(badgePath, badgeRect, 6.5f, 6.5f);
                using (var shadowPen = new Drawing.Pen(darkShadow, 3.5f))
                {
                    shadowPen.LineJoin = LineJoin.Round;
                    g.DrawPath(shadowPen, badgePath);
                }
                using (var bgBrush = new LinearGradientBrush(badgeRect, badgeBg1, badgeBg2, 90f))
                {
                    g.FillPath(bgBrush, badgePath);
                }
                using (var borderPen = new Drawing.Pen(Drawing.Color.FromArgb(250, 255, 255, 255), 1.8f))
                {
                    g.DrawPath(borderPen, badgePath);
                }
            }

            // Número da Saída em fonte pesada ultra-legível (Arial Black)
            string text = outputNumber > 99 ? "9+" : outputNumber.ToString();
            float fontSize = text.Length > 1 ? 15f : 21.5f;
            using (var font = new Drawing.Font("Arial Black", fontSize, Drawing.FontStyle.Bold, Drawing.GraphicsUnit.Pixel))
            {
                using var sf = new Drawing.StringFormat
                {
                    Alignment = Drawing.StringAlignment.Center,
                    LineAlignment = Drawing.StringAlignment.Center
                };
                // Sombra escura para contraste extremo
                using (var shadowBrush = new Drawing.SolidBrush(Drawing.Color.FromArgb(180, 0, 0, 0)))
                {
                    g.DrawString(text, font, shadowBrush, new Drawing.RectangleF(badgeRect.X, badgeRect.Y + 2f, badgeRect.Width, badgeRect.Height), sf);
                }
                using (var textBrush = new Drawing.SolidBrush(Drawing.Color.White))
                {
                    g.DrawString(text, font, textBrush, new Drawing.RectangleF(badgeRect.X, badgeRect.Y + 0.5f, badgeRect.Width, badgeRect.Height), sf);
                }
            }
        }
        return bmp.GetHicon();
    }

    private static void AddRoundedRectangle(GraphicsPath p, Drawing.RectangleF r, float rx, float ry)
    {
        p.AddArc(r.X, r.Y, rx * 2, ry * 2, 180, 90);
        p.AddArc(r.Right - rx * 2, r.Y, rx * 2, ry * 2, 270, 90);
        p.AddArc(r.Right - rx * 2, r.Bottom - ry * 2, rx * 2, ry * 2, 0, 90);
        p.AddArc(r.X, r.Bottom - ry * 2, rx * 2, ry * 2, 90, 90);
        p.CloseFigure();
    }

    public void UpdateMiniTaskIcon()
    {
        if (_miniTaskTray != null)
        {
            _miniTaskTray.Visible = ShowMiniTaskIcon;
            if (ShowMiniTaskIcon)
            {
                IntPtr hIcon = CreateMiniTaskIcon();
                try
                {
                    using var tempIcon = Drawing.Icon.FromHandle(hIcon);
                    _miniTaskTray.Icon = (Drawing.Icon)tempIcon.Clone();
                }
                finally
                {
                    DestroyIcon(hIcon);
                }
                BuildMiniTaskContextMenu();
            }
        }
    }

    public void UpdateMiniTaskTooltip(string tooltip)
    {
        if (_miniTaskTray != null && ShowMiniTaskIcon && !string.IsNullOrWhiteSpace(tooltip))
        {
            if (tooltip.Length >= 63) tooltip = tooltip.Substring(0, 60) + "...";
            _miniTaskTray.Text = tooltip;
        }
    }

    private void BuildMiniTaskContextMenu()
    {
        if (_miniTaskTray == null) return;
        var menu = new Forms.ContextMenuStrip();

        var itemOpenMini = new Forms.ToolStripMenuItem("⚡ Mini Gerenciador de Tarefas", null, (_, _) => MiniTaskManagerWindow.Instance.ToggleVisibility())
        {
            Font = new Drawing.Font(Forms.Control.DefaultFont, Drawing.FontStyle.Bold)
        };
        menu.Items.Add(itemOpenMini);

        menu.Items.Add(new Forms.ToolStripSeparator());

        var rateMenu = new Forms.ToolStripMenuItem("⏱ Taxa de Atualização");
        rateMenu.DropDownItems.Add(new Forms.ToolStripMenuItem("300 ms (Alta velocidade)", null, (_, _) => MiniTaskManagerService.Instance.UpdateIntervalMs = 300));
        rateMenu.DropDownItems.Add(new Forms.ToolStripMenuItem("500 ms (Rápido)", null, (_, _) => MiniTaskManagerService.Instance.UpdateIntervalMs = 500));
        rateMenu.DropDownItems.Add(new Forms.ToolStripMenuItem("1.0 s (Padrão)", null, (_, _) => MiniTaskManagerService.Instance.UpdateIntervalMs = 1000));
        rateMenu.DropDownItems.Add(new Forms.ToolStripMenuItem("3.0 s (Econômico)", null, (_, _) => MiniTaskManagerService.Instance.UpdateIntervalMs = 3000));
        menu.Items.Add(rateMenu);

        menu.Items.Add(new Forms.ToolStripMenuItem("📊 Abrir Gerenciador do Windows", null, (_, _) =>
        {
            try { System.Diagnostics.Process.Start("taskmgr.exe"); } catch { }
        }));

        menu.Items.Add(new Forms.ToolStripSeparator());

        menu.Items.Add(new Forms.ToolStripMenuItem("🌟 Abrir Lumini Tray", null, (_, _) => _onOpenApp?.Invoke()));
        menu.Items.Add(new Forms.ToolStripMenuItem("❌ Encerrar Aplicativo", null, (_, _) => _onExitApp?.Invoke()));

        _miniTaskTray.ContextMenuStrip = menu;
    }

    public static IntPtr CreateMiniTaskIcon()
    {
        using var bmp = new Drawing.Bitmap(16, 16);
        using (var g = Drawing.Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Drawing.Color.Transparent);

            // 4 Mini barras coloridas (CPU: Cyan, GPU: Green, RAM: Amber, DISK: Magenta)
            using var bCpu = new Drawing.SolidBrush(Drawing.Color.FromArgb(0, 200, 255));
            g.FillRectangle(bCpu, 1, 6, 2, 9);

            using var bGpu = new Drawing.SolidBrush(Drawing.Color.FromArgb(0, 220, 100));
            g.FillRectangle(bGpu, 5, 2, 2, 13);

            using var bRam = new Drawing.SolidBrush(Drawing.Color.FromArgb(255, 180, 70));
            g.FillRectangle(bRam, 9, 4, 2, 11);

            using var bDisk = new Drawing.SolidBrush(Drawing.Color.FromArgb(217, 70, 239));
            g.FillRectangle(bDisk, 13, 8, 2, 7);
        }
        return bmp.GetHicon();
    }

        public void UpdateWallpaper(bool isPaused, string? wallpaperName)
    {
        _lastWallpaperPaused = isPaused;
        if (!string.IsNullOrWhiteSpace(wallpaperName)) _lastWallpaperName = wallpaperName;

        if (_wallpaperTray != null)
        {
            _wallpaperTray.Visible = ShowWallpaperIcon;
            if (ShowWallpaperIcon)
            {
                if (_lastAppliedWallpaperPausedState != isPaused || _wallpaperTray.Icon == null)
                {
                    _lastAppliedWallpaperPausedState = isPaused;
                    if (isPaused)
                    {
                        if (_cachedWallpaperPausedIcon == null)
                        {
                            IntPtr h = CreateWallpaperIcon(true);
                            try { using var temp = Drawing.Icon.FromHandle(h); _cachedWallpaperPausedIcon = (Drawing.Icon)temp.Clone(); }
                            finally { DestroyIcon(h); }
                        }
                        _wallpaperTray.Icon = _cachedWallpaperPausedIcon;
                    }
                    else
                    {
                        if (_cachedWallpaperPlayingIcon == null)
                        {
                            IntPtr h = CreateWallpaperIcon(false);
                            try { using var temp = Drawing.Icon.FromHandle(h); _cachedWallpaperPlayingIcon = (Drawing.Icon)temp.Clone(); }
                            finally { DestroyIcon(h); }
                        }
                        _wallpaperTray.Icon = _cachedWallpaperPlayingIcon;
                    }
                }

                string status = isPaused ? "PAUSADO" : "ATIVO";
                string tip = $"Wallpaper: {_lastWallpaperName} ({status})";
                if (tip.Length >= 63) tip = tip.Substring(0, 60) + "...";
                _wallpaperTray.Text = tip;

                BuildWallpaperContextMenu();
            }
        }
    }

    private void BuildWallpaperContextMenu()
    {
        if (_wallpaperTray == null) return;
        var menu = new Forms.ContextMenuStrip();

        string playPauseText = _lastWallpaperPaused ? "▶ Retomar Animação" : "⏸ Pausar Animação";
        var itemToggle = new Forms.ToolStripMenuItem(playPauseText, null, (_, _) => _onToggleWallpaperPlayPause?.Invoke())
        {
            Font = new Drawing.Font(Forms.Control.DefaultFont, Drawing.FontStyle.Bold)
        };
        menu.Items.Add(itemToggle);

        menu.Items.Add(new Forms.ToolStripMenuItem("⏭ Próximo Wallpaper", null, (_, _) => _onNextWallpaper?.Invoke()));
        menu.Items.Add(new Forms.ToolStripMenuItem("⏮ Wallpaper Anterior", null, (_, _) => _onPrevWallpaper?.Invoke()));

        menu.Items.Add(new Forms.ToolStripSeparator());

        var intervalMenu = new Forms.ToolStripMenuItem("⏱ Intervalo de Troca Automática");
        intervalMenu.DropDownItems.Add(new Forms.ToolStripMenuItem("Desligado (Fixo)", null, (_, _) => _onSetWallpaperInterval?.Invoke(0)));
        intervalMenu.DropDownItems.Add(new Forms.ToolStripMenuItem("5 minutos", null, (_, _) => _onSetWallpaperInterval?.Invoke(5)));
        intervalMenu.DropDownItems.Add(new Forms.ToolStripMenuItem("15 minutos", null, (_, _) => _onSetWallpaperInterval?.Invoke(15)));
        intervalMenu.DropDownItems.Add(new Forms.ToolStripMenuItem("30 minutos", null, (_, _) => _onSetWallpaperInterval?.Invoke(30)));
        intervalMenu.DropDownItems.Add(new Forms.ToolStripMenuItem("1 hora", null, (_, _) => _onSetWallpaperInterval?.Invoke(60)));
        intervalMenu.DropDownItems.Add(new Forms.ToolStripMenuItem("2 horas", null, (_, _) => _onSetWallpaperInterval?.Invoke(120)));
        menu.Items.Add(intervalMenu);

        menu.Items.Add(new Forms.ToolStripSeparator());

        menu.Items.Add(new Forms.ToolStripMenuItem("🌟 Abrir Lumini Tray", null, (_, _) => _onOpenApp?.Invoke()));
        menu.Items.Add(new Forms.ToolStripMenuItem("❌ Desativar Wallpaper (Sair da Bandeja)", null, (_, _) => _onDeactivateWallpaper?.Invoke()));

        _wallpaperTray.ContextMenuStrip = menu;
    }

    public static IntPtr CreateWallpaperIcon(bool isPaused)
    {
        using var bmp = new Drawing.Bitmap(32, 32, Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Drawing.Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.Clear(Drawing.Color.Transparent);

            // Squircle com fundo Roxo / Violeta (#8B5CF6 -> #6D28D9)
            Drawing.Color bg1 = Drawing.Color.FromArgb(139, 92, 246);
            Drawing.Color bg2 = Drawing.Color.FromArgb(109, 40, 217);

            Drawing.RectangleF rect = new Drawing.RectangleF(1, 1, 30, 30);
            using (var path = new GraphicsPath())
            {
                AddRoundedRectangle(path, rect, 7f, 7f);
                using (var bgBrush = new LinearGradientBrush(rect, bg1, bg2, 90f))
                {
                    g.FillPath(bgBrush, path);
                }
                using (var borderPen = new Drawing.Pen(Drawing.Color.FromArgb(200, 255, 255, 255), 1.2f))
                {
                    g.DrawPath(borderPen, path);
                }
            }

            if (isPaused)
            {
                using var bPause = new Drawing.SolidBrush(Drawing.Color.FromArgb(254, 240, 138));
                g.FillRectangle(bPause, 10, 8, 4, 14);
                g.FillRectangle(bPause, 18, 8, 4, 14);
            }
            else
            {
                using var bPlay = new Drawing.SolidBrush(Drawing.Color.White);
                var points = new[]
                {
                    new Drawing.PointF(11, 8),
                    new Drawing.PointF(11, 22),
                    new Drawing.PointF(23, 15)
                };
                g.FillPolygon(bPlay, points);
            }
        }
        return bmp.GetHicon();
    }

    public void Dispose()
    {
        if (_mainAppTray != null)
        {
            try { _mainAppTray.Visible = false; _mainAppTray.Icon = null; _mainAppTray.Dispose(); } catch { }
            _mainAppTray = null;
        }

        if (_micTray != null)
        {
            try { _micTray.Visible = false; _micTray.Icon = null; _micTray.Dispose(); } catch { }
            _micTray = null;
        }

        if (_audioTray != null)
        {
            try { _audioTray.Visible = false; _audioTray.Icon = null; _audioTray.Dispose(); } catch { }
            _audioTray = null;
        }

        if (_miniTaskTray != null)
        {
            try { _miniTaskTray.Visible = false; _miniTaskTray.Icon = null; _miniTaskTray.Dispose(); } catch { }
            _miniTaskTray = null;
        }

        if (_wallpaperTray != null)
        {
            try { _wallpaperTray.Visible = false; _wallpaperTray.Icon = null; _wallpaperTray.Dispose(); } catch { }
            _wallpaperTray = null;
        }

        try { _cachedWallpaperPlayingIcon?.Dispose(); } catch { }
        _cachedWallpaperPlayingIcon = null;
        try { _cachedWallpaperPausedIcon?.Dispose(); } catch { }
        _cachedWallpaperPausedIcon = null;

        try { Forms.Application.DoEvents(); } catch { }

        System.Threading.Tasks.Task.Run(RefreshNotificationArea);

    }
}
