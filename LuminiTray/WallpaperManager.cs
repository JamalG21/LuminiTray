using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace LuminiTray;

/// <summary>
/// Informações de um monitor conectado ao sistema para atribuição individual de wallpaper.
/// </summary>
public sealed class WallpaperMonitorInfo
{
    public int Index { get; init; }
    public string DeviceId { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public int Left { get; init; }
    public int Top { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public bool IsPrimary { get; init; }
    public string? CurrentWallpaperPath { get; set; }

    public string ResolutionText => $"{Width} × {Height}";
    public string Label => IsPrimary
        ? $"Monitor {Index + 1}: {ResolutionText} (Principal)"
        : $"Monitor {Index + 1}: {ResolutionText}";
}

/// <summary>
/// Representa um arquivo de wallpaper com miniatura, dimensões e status.
/// </summary>
public sealed class WallpaperItem : INotifyPropertyChanged
{
    public string FilePath { get; init; } = "";
    public string FileName => Path.GetFileName(FilePath);
    public string NameWithoutExtension => Path.GetFileNameWithoutExtension(FilePath);
    public string FileSizeText { get; init; } = "";
    public long FileSizeBytes { get; init; }

    private string _dimensionsText = "";
    public string DimensionsText
    {
        get => _dimensionsText;
        set { _dimensionsText = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DimensionsText))); }
    }

    private bool _isCurrent;
    public bool IsCurrent
    {
        get => _isCurrent;
        set { _isCurrent = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsCurrent))); }
    }

    private ImageSource? _thumbnail;
    public ImageSource? Thumbnail
    {
        get => _thumbnail;
        set { _thumbnail = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Thumbnail))); }
    }

    public bool IsVideo => WallpaperManager.IsVideoFile(FilePath);
    public string TypeBadge => IsVideo ? "🎬 ANIMADO" : "IMAGEM";
    public string ActionButtonText => IsVideo ? "▶ APLICAR WALLPAPER ANIMADO" : "DEFINIR COMO WALLPAPER";
    public Visibility VideoBadgeVisibility => IsVideo ? Visibility.Visible : Visibility.Collapsed;

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>
/// Gerencia a integração com o Windows para alteração de papéis de parede (wallpapers),
/// geração e instalação da biblioteca oficial RESET e controle de pastas.
/// </summary>
public static class WallpaperManager
{
    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern int SystemParametersInfo(int uAction, int uParam, string lpvParam, int fuWinIni);

    private const int SPI_SETDESKWALLPAPER = 0x0014;
    private const int SPI_GETDESKWALLPAPER = 0x0073;
    private const int SPIF_UPDATEINIFILE = 0x01;
    private const int SPIF_SENDCHANGE = 0x02;

    [ComImport]
    [Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDesktopWallpaper
    {
        void SetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string? monitorID, [MarshalAs(UnmanagedType.LPWStr)] string wallpaper);

        [return: MarshalAs(UnmanagedType.LPWStr)]
        string GetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string? monitorID);

        [return: MarshalAs(UnmanagedType.LPWStr)]
        string GetMonitorDevicePathAt([MarshalAs(UnmanagedType.U4)] uint monitorIndex);

        [return: MarshalAs(UnmanagedType.U4)]
        uint GetMonitorDevicePathCount();

        void GetMonitorRECT([MarshalAs(UnmanagedType.LPWStr)] string monitorID, [MarshalAs(UnmanagedType.Struct)] out RECT displayRect);

        void SetBackgroundColor([MarshalAs(UnmanagedType.U4)] uint color);

        [return: MarshalAs(UnmanagedType.U4)]
        uint GetBackgroundColor();

        void SetPosition([MarshalAs(UnmanagedType.I4)] DesktopWallpaperPosition position);

        [return: MarshalAs(UnmanagedType.I4)]
        DesktopWallpaperPosition GetPosition();

        void SetSlideshow(IntPtr items);

        IntPtr GetSlideshow();

        void AdvanceSlideshow([MarshalAs(UnmanagedType.LPWStr)] string? monitorID, [MarshalAs(UnmanagedType.I4)] int direction);

        void GetStatus(out uint state);

        void Enable([MarshalAs(UnmanagedType.Bool)] bool enableWallpaper);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private enum DesktopWallpaperPosition
    {
        Center = 0,
        Tile = 1,
        Stretch = 2,
        Fit = 3,
        Fill = 4,
        Span = 5
    }

    private static IDesktopWallpaper? CreateDesktopWallpaper()
    {
        try
        {
            Type? type = Type.GetTypeFromCLSID(new Guid("C2CF3110-460E-4fc1-B9D0-8A1C0C9CC4BD"));
            if (type != null)
            {
                return Activator.CreateInstance(type) as IDesktopWallpaper;
            }
        }
        catch { }
        return null;
    }

    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);

    private const uint MONITORINFOF_PRIMARY = 0x00000001;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    public static string DefaultLibraryFolder =>
        Path.Combine(AppSettings.Folder, "Wallpapers", "BibliotecaLumini");

    public static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".webm", ".mkv", ".avi", ".wmv"
    };

    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".bmp", ".webp",
        ".mp4", ".webm", ".mkv", ".avi", ".wmv"
    };

    public static bool IsVideoFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        return VideoExtensions.Contains(Path.GetExtension(path));
    }

    /// <summary>
    /// Detecta automaticamente todos os monitores conectados ao sistema operacional.
    /// </summary>
    public static List<WallpaperMonitorInfo> GetConnectedMonitors()
    {
        var list = new List<WallpaperMonitorInfo>();
        var desktopWallpaper = CreateDesktopWallpaper();

        if (desktopWallpaper != null)
        {
            try
            {
                uint count = desktopWallpaper.GetMonitorDevicePathCount();
                for (uint i = 0; i < count; i++)
                {
                    try
                    {
                        string id = desktopWallpaper.GetMonitorDevicePathAt(i);
                        desktopWallpaper.GetMonitorRECT(id, out RECT rect);
                        int w = Math.Abs(rect.Right - rect.Left);
                        int h = Math.Abs(rect.Bottom - rect.Top);
                        bool isPrimary = (rect.Left == 0 && rect.Top == 0);
                        string? currentWp = null;
                        try { currentWp = desktopWallpaper.GetWallpaper(id); } catch { }

                        list.Add(new WallpaperMonitorInfo
                        {
                            Index = (int)i,
                            DeviceId = id,
                            DisplayName = isPrimary ? $"Monitor {i + 1} (Principal)" : $"Monitor {i + 1}",
                            Left = rect.Left,
                            Top = rect.Top,
                            Width = w > 0 ? w : 1920,
                            Height = h > 0 ? h : 1080,
                            IsPrimary = isPrimary,
                            CurrentWallpaperPath = currentWp
                        });
                    }
                    catch { }
                }
            }
            catch { }
            finally
            {
                try { Marshal.ReleaseComObject(desktopWallpaper); } catch { }
            }
        }

        // Fallback caso IDesktopWallpaper não retorne telas
        if (list.Count == 0)
        {
            list = GetMonitorsFallback();
        }

        if (list.Count == 0)
        {
            list.Add(new WallpaperMonitorInfo
            {
                Index = 0,
                DeviceId = "ALL",
                DisplayName = "Monitor Principal",
                Width = (int)SystemParameters.PrimaryScreenWidth,
                Height = (int)SystemParameters.PrimaryScreenHeight,
                IsPrimary = true
            });
        }

        return list;
    }

    private static List<WallpaperMonitorInfo> GetMonitorsFallback()
    {
        var result = new List<WallpaperMonitorInfo>();
        int index = 0;

        try
        {
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMon, IntPtr hdc, ref RECT rc, IntPtr data) =>
            {
                var mi = new MONITORINFOEX();
                mi.cbSize = Marshal.SizeOf<MONITORINFOEX>();
                if (GetMonitorInfo(hMon, ref mi))
                {
                    int w = Math.Abs(mi.rcMonitor.Right - mi.rcMonitor.Left);
                    int h = Math.Abs(mi.rcMonitor.Bottom - mi.rcMonitor.Top);
                    bool primary = (mi.dwFlags & MONITORINFOF_PRIMARY) != 0;

                    result.Add(new WallpaperMonitorInfo
                    {
                        Index = index++,
                        DeviceId = mi.szDevice,
                        DisplayName = primary ? $"Monitor {index} (Principal)" : $"Monitor {index}",
                        Left = mi.rcMonitor.Left,
                        Top = mi.rcMonitor.Top,
                        Width = w > 0 ? w : 1920,
                        Height = h > 0 ? h : 1080,
                        IsPrimary = primary
                    });
                }
                return true;
            }, IntPtr.Zero);
        }
        catch { }

        return result;
    }

    /// <summary>
    /// Aplica o arquivo como wallpaper da área de trabalho do Windows com o estilo desejado.
    /// Se <paramref name="monitorDeviceId"/> for informado e diferente de "ALL", aplica apenas ao monitor correspondente.
    /// </summary>
    public static void SetWallpaper(string filePath, string style = "fill", string? monitorDeviceId = null)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            throw new FileNotFoundException("O arquivo de imagem selecionado não foi encontrado.", filePath);

        var pos = style.ToLowerInvariant() switch
        {
            "fit" => DesktopWallpaperPosition.Fit,
            "stretch" => DesktopWallpaperPosition.Stretch,
            "center" => DesktopWallpaperPosition.Center,
            "tile" => DesktopWallpaperPosition.Tile,
            "span" => DesktopWallpaperPosition.Span,
            _ => DesktopWallpaperPosition.Fill
        };

        bool targetSpecificMonitor = !string.IsNullOrWhiteSpace(monitorDeviceId) &&
                                     !string.Equals(monitorDeviceId, "ALL", StringComparison.OrdinalIgnoreCase);

        bool comSuccess = false;
        var desktopWallpaper = CreateDesktopWallpaper();
        if (desktopWallpaper != null)
        {
            try
            {
                desktopWallpaper.SetPosition(pos);
                string? targetId = targetSpecificMonitor ? monitorDeviceId : null;
                desktopWallpaper.SetWallpaper(targetId, filePath);
                comSuccess = true;
            }
            catch { }
            finally
            {
                try { Marshal.ReleaseComObject(desktopWallpaper); } catch { }
            }
        }

        // Se for para todos os monitores (ou fallback COM), aplica também via Registro + SystemParametersInfo
        if (!targetSpecificMonitor || !comSuccess)
        {
            ApplyLegacyWallpaper(filePath, style);
        }
    }

    private static void ApplyLegacyWallpaper(string filePath, string style)
    {
        // 1. Configura o estilo no Registro do Windows (HKCU\Control Panel\Desktop)
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop", writable: true);
            if (key != null)
            {
                string styleVal = "10"; // Fill (Preencher)
                string tileVal = "0";

                switch (style.ToLowerInvariant())
                {
                    case "fit":
                        styleVal = "6";   // Ajustar
                        tileVal = "0";
                        break;
                    case "stretch":
                        styleVal = "2";   // Esticar
                        tileVal = "0";
                        break;
                    case "center":
                        styleVal = "0";   // Centralizar
                        tileVal = "0";
                        break;
                    case "tile":
                        styleVal = "0";   // Lado a lado
                        tileVal = "1";
                        break;
                    case "span":
                        styleVal = "22";  // Estender (múltiplos monitores)
                        tileVal = "0";
                        break;
                    case "fill":
                    default:
                        styleVal = "10";  // Preencher
                        tileVal = "0";
                        break;
                }

                key.SetValue("WallpaperStyle", styleVal);
                key.SetValue("TileWallpaper", tileVal);
            }
        }
        catch { }

        // 2. Chama API nativa do Windows para aplicar e notificar todos os monitores
        int res = SystemParametersInfo(SPI_SETDESKWALLPAPER, 0, filePath, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
        if (res == 0)
        {
            int err = Marshal.GetLastWin32Error();
            throw new InvalidOperationException($"Não foi possível aplicar o wallpaper (código Win32: {err}).");
        }
    }

    /// <summary>
    /// Obtém o wallpaper atualmente atribuído a um monitor específico (ou o padrão do sistema se "ALL" ou nulo).
    /// </summary>
    public static string? GetWallpaperForMonitor(string? monitorDeviceId)
    {
        if (string.IsNullOrWhiteSpace(monitorDeviceId) || string.Equals(monitorDeviceId, "ALL", StringComparison.OrdinalIgnoreCase))
            return GetCurrentWallpaper();

        var desktopWallpaper = CreateDesktopWallpaper();
        if (desktopWallpaper != null)
        {
            try
            {
                string path = desktopWallpaper.GetWallpaper(monitorDeviceId);
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    return path;
            }
            catch { }
            finally
            {
                try { Marshal.ReleaseComObject(desktopWallpaper); } catch { }
            }
        }

        return GetCurrentWallpaper();
    }

    /// <summary>
    /// Retorna o caminho do wallpaper atualmente configurado no Windows.
    /// </summary>
    public static string? GetCurrentWallpaper()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop");
            string? wp = key?.GetValue("WallPaper")?.ToString();
            if (!string.IsNullOrEmpty(wp) && File.Exists(wp)) return wp;

            string transcoded = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                @"Microsoft\Windows\Themes\TranscodedWallpaper");
            if (File.Exists(transcoded)) return transcoded;
        }
        catch { }
        return null;
    }

    /// <summary>
    /// Varre uma pasta em busca de arquivos de imagem suportados.
    /// </summary>
    public static List<WallpaperItem> ScanFolder(string folder, string? currentWallpaperPath = null)
    {
        var list = new List<WallpaperItem>();
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            return list;

        string? current = currentWallpaperPath ?? GetCurrentWallpaper();

        try
        {
            var files = Directory.GetFiles(folder)
                                 .Where(f => SupportedExtensions.Contains(Path.GetExtension(f)))
                                 .OrderBy(f => f, StringComparer.CurrentCultureIgnoreCase);

            foreach (var f in files)
            {
                try
                {
                    var fi = new FileInfo(f);
                    string sz = fi.Length >= 1048576
                        ? $"{fi.Length / 1048576.0:0.0} MB"
                        : $"{fi.Length / 1024.0:0} KB";

                    bool isCur = !string.IsNullOrEmpty(current) &&
                                 (f.Equals(current, StringComparison.OrdinalIgnoreCase) ||
                                  Path.GetFileName(f).Equals(Path.GetFileName(current), StringComparison.OrdinalIgnoreCase));

                    list.Add(new WallpaperItem
                    {
                        FilePath = f,
                        FileSizeBytes = fi.Length,
                        FileSizeText = sz,
                        IsCurrent = isCur
                    });
                }
                catch { }
            }
        }
        catch { }

        return list;
    }

    /// <summary>
    /// Carrega miniatura otimizada de forma leve para a interface.
    /// </summary>
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(
        [In] string pszPath,
        [In] IntPtr pbc,
        [In, MarshalAs(UnmanagedType.LPStruct)] Guid riid,
        [Out, MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory ppv);

    [ComImport]
    [Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig]
        int GetImage(SIZE size, int flags, out IntPtr phbm);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE
    {
        public int cx;
        public int cy;
        public SIZE(int cx, int cy) { this.cx = cx; this.cy = cy; }
    }

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr hObject);

    public static BitmapSource? LoadThumbnail(string path, int pixelWidth = 320)
    {
        if (IsVideoFile(path))
        {
            try
            {
                var iid = new Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b");
                SHCreateItemFromParsingName(path, IntPtr.Zero, iid, out var factory);
                if (factory != null)
                {
                    int hr = factory.GetImage(new SIZE(pixelWidth, (pixelWidth * 9) / 16), 0x0, out IntPtr hBitmap);
                    if (hr == 0 && hBitmap != IntPtr.Zero)
                    {
                        try
                        {
                            var src = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                                hBitmap,
                                IntPtr.Zero,
                                Int32Rect.Empty,
                                BitmapSizeOptions.FromEmptyOptions());
                            src.Freeze();
                            return src;
                        }
                        finally
                        {
                            DeleteObject(hBitmap);
                        }
                    }
                }
            }
            catch { }
            return null;
        }

        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri(path);
            bmp.DecodePixelWidth = pixelWidth;
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Obtém as dimensões reais (Largura x Altura) da imagem.
    /// </summary>
    public static string GetDimensions(string path)
    {
        if (IsVideoFile(path)) return "Vídeo Animado";

        try
        {
            using var stream = File.OpenRead(path);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
            if (decoder.Frames.Count > 0)
            {
                var f = decoder.Frames[0];
                return $"{f.PixelWidth} × {f.PixelHeight}";
            }
        }
        catch { }
        return "";
    }

    /// <summary>
    /// Cria e instala a Biblioteca Oficial de Wallpapers do Lumini Tray
    /// em alta resolução (1920x1080) na pasta %AppData%\LuminiTray\Wallpapers\BibliotecaLumini.
    /// </summary>
    public static List<string> InstallDefaultLibrary(IProgress<string>? progress = null)
    {
        string dir = DefaultLibraryFolder;
        Directory.CreateDirectory(dir);

        var generated = new List<string>();

        var specs = new (string FileName, Action<DrawingContext, int, int> Draw)[]
        {
            ("01-Lumini-Tray-Minimal-Dark.png", DrawResetMinimal),
            ("02-Cyber-Neon-Grid.png", DrawCyberGrid),
            ("03-Carbon-Fiber-Hexagon.png", DrawCarbonHex),
            ("04-Circuit-Board-Dark.png", DrawCircuitDark),
            ("05-Deep-Space-Aurora.png", DrawDeepAurora),
            ("06-Abstract-Geometry-OLED.png", DrawAbstractGeometry)
        };

        const int w = 1920;
        const int h = 1080;

        foreach (var (fileName, drawAction) in specs)
        {
            string outPath = Path.Combine(dir, fileName);
            progress?.Report($"Gerando {fileName}...");
            if (!File.Exists(outPath))
            {
                try
                {
                    var visual = new DrawingVisual();
                    using (var dc = visual.RenderOpen())
                    {
                        drawAction(dc, w, h);
                    }

                    var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
                    rtb.Render(visual);

                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(rtb));

                    using var fs = File.Create(outPath);
                    encoder.Save(fs);
                }
                catch { }
            }
            if (File.Exists(outPath))
                generated.Add(outPath);
        }

        return generated;
    }

    // --- Desenhos vetoriais dos wallpapers da biblioteca ---

    private static void DrawResetMinimal(DrawingContext dc, int w, int h)
    {
        // Fundo preto puro OLED com gradiente radial sutil
        var bgGrad = new RadialGradientBrush(Color.FromRgb(22, 22, 22), Color.FromRgb(6, 6, 6))
        {
            Center = new Point(0.5, 0.5),
            RadiusX = 0.6,
            RadiusY = 0.6
        };
        dc.DrawRectangle(bgGrad, null, new Rect(0, 0, w, h));

        // Linhas sutis de grade técnica no fundo
        var penLine = new Pen(new SolidColorBrush(Color.FromArgb(18, 255, 255, 255)), 1);
        for (int x = 120; x < w; x += 120)
            dc.DrawLine(penLine, new Point(x, 0), new Point(x, h));
        for (int y = 90; y < h; y += 90)
            dc.DrawLine(penLine, new Point(0, y), new Point(w, y));

        // Emblema Central
        double cx = w / 2.0;
        double cy = h / 2.0;

        // Caixa central chanfrada
        var cardBrush = new SolidColorBrush(Color.FromRgb(14, 14, 14));
        var cardBorder = new Pen(new SolidColorBrush(Color.FromRgb(45, 45, 45)), 1.5);
        dc.DrawRoundedRectangle(cardBrush, cardBorder, new Rect(cx - 240, cy - 90, 480, 180), 8, 8);

        // Logo Lumini Tray
        var typeFaceTitle = new Typeface(new FontFamily("Segoe UI Variable Display, Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
        var titleText = new FormattedText(
            "LUMINI TRAY",
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            typeFaceTitle,
            38,
            new SolidColorBrush(Color.FromRgb(245, 245, 245)),
            1.0);
        titleText.TextAlignment = TextAlignment.Center;
        dc.DrawText(titleText, new Point(cx, cy - 48));

        // Subtítulo
        var typeFaceSub = new Typeface(new FontFamily("Segoe UI Variable Text, Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
        var subText = new FormattedText(
            "HARDWARE · OVERCLOCK · GAMING SUITE",
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            typeFaceSub,
            12.5,
            new SolidColorBrush(Color.FromRgb(140, 140, 140)),
            1.0);
        subText.TextAlignment = TextAlignment.Center;
        dc.DrawText(subText, new Point(cx, cy + 14));

        // Pequenos detalhes nos cantos
        var cornerPen = new Pen(new SolidColorBrush(Color.FromRgb(100, 100, 100)), 2);
        dc.DrawLine(cornerPen, new Point(cx - 220, cy - 70), new Point(cx - 200, cy - 70));
        dc.DrawLine(cornerPen, new Point(cx - 220, cy - 70), new Point(cx - 220, cy - 50));
        dc.DrawLine(cornerPen, new Point(cx + 220, cy + 70), new Point(cx + 200, cy + 70));
        dc.DrawLine(cornerPen, new Point(cx + 220, cy + 70), new Point(cx + 220, cy + 50));
    }

    private static void DrawCyberGrid(DrawingContext dc, int w, int h)
    {
        // Fundo Gradiente Cyber
        var bg = new LinearGradientBrush(Color.FromRgb(12, 6, 24), Color.FromRgb(4, 4, 8), new Point(0.5, 0), new Point(0.5, 1));
        dc.DrawRectangle(bg, null, new Rect(0, 0, w, h));

        // Linha do horizonte
        double horizY = h * 0.58;
        var horizonPen = new Pen(new SolidColorBrush(Color.FromArgb(160, 180, 60, 255)), 2);
        dc.DrawLine(horizonPen, new Point(0, horizY), new Point(w, horizY));

        // Brilho do horizonte
        var glowBrush = new RadialGradientBrush(Color.FromArgb(90, 180, 80, 255), Colors.Transparent)
        {
            Center = new Point(0.5, 0.58),
            RadiusX = 0.5,
            RadiusY = 0.15
        };
        dc.DrawRectangle(glowBrush, null, new Rect(0, horizY - 150, w, 300));

        // Grade de perspectiva no chão
        var gridPen = new Pen(new SolidColorBrush(Color.FromArgb(80, 140, 60, 240)), 1.2);
        double vpX = w / 2.0;

        for (int i = -16; i <= 16; i++)
        {
            double bx = vpX + i * 140;
            dc.DrawLine(gridPen, new Point(vpX + i * 20, horizY), new Point(bx, h));
        }

        double step = 12;
        double y = horizY + 4;
        while (y < h)
        {
            dc.DrawLine(gridPen, new Point(0, y), new Point(w, y));
            step *= 1.35;
            y += step;
        }

        // Estrelas discretas no céu
        var rnd = new Random(42);
        var starBrush = new SolidColorBrush(Color.FromArgb(200, 220, 220, 255));
        for (int s = 0; s < 120; s++)
        {
            double sx = rnd.Next(w);
            double sy = rnd.Next((int)horizY - 30);
            double r = rnd.NextDouble() * 1.5 + 0.5;
            dc.DrawEllipse(starBrush, null, new Point(sx, sy), r, r);
        }
    }

    private static void DrawCarbonHex(DrawingContext dc, int w, int h)
    {
        // Fundo titânio escuro
        var bg = new SolidColorBrush(Color.FromRgb(10, 12, 14));
        dc.DrawRectangle(bg, null, new Rect(0, 0, w, h));

        // Padrão de favo de mel / hexágonos
        var hexPen = new Pen(new SolidColorBrush(Color.FromArgb(25, 255, 255, 255)), 1.2);
        var hexFill = new SolidColorBrush(Color.FromArgb(12, 255, 255, 255));
        double size = 32;
        double hDist = Math.Sqrt(3) * size;
        double vDist = 1.5 * size;

        for (double r = -size; r < h + size * 2; r += vDist)
        {
            bool offset = ((int)(r / vDist) % 2) != 0;
            for (double c = -hDist; c < w + hDist * 2; c += hDist)
            {
                double px = c + (offset ? hDist / 2.0 : 0);
                DrawSingleHex(dc, px, r, size * 0.92, hexFill, hexPen);
            }
        }

        // Faixa diagonal de luz
        var sweep = new LinearGradientBrush(
            Colors.Transparent,
            Color.FromArgb(28, 255, 255, 255),
            new Point(0, 0),
            new Point(1, 1));
        dc.DrawRectangle(sweep, null, new Rect(0, 0, w, h));
    }

    private static void DrawSingleHex(DrawingContext dc, double cx, double cy, double r, Brush fill, Pen pen)
    {
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            for (int i = 0; i < 6; i++)
            {
                double angle = (60 * i - 30) * Math.PI / 180.0;
                var pt = new Point(cx + r * Math.Cos(angle), cy + r * Math.Sin(angle));
                if (i == 0) ctx.BeginFigure(pt, isFilled: true, isClosed: true);
                else ctx.LineTo(pt, isStroked: true, isSmoothJoin: true);
            }
        }
        geo.Freeze();
        dc.DrawGeometry(fill, pen, geo);
    }

    private static void DrawCircuitDark(DrawingContext dc, int w, int h)
    {
        // Fundo Placa de Circuito Fosca
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(9, 11, 14)), null, new Rect(0, 0, w, h));

        var trackPen = new Pen(new SolidColorBrush(Color.FromArgb(60, 56, 189, 248)), 1.5);
        var dimPen = new Pen(new SolidColorBrush(Color.FromArgb(30, 255, 255, 255)), 1.0);
        var cyanDot = new SolidColorBrush(Color.FromRgb(56, 189, 248));

        var rnd = new Random(101);
        for (int i = 0; i < 50; i++)
        {
            double x1 = rnd.Next(0, w);
            double y1 = rnd.Next(0, h);
            double seg1 = rnd.Next(40, 200);
            double seg2 = rnd.Next(40, 200);

            var p1 = new Point(x1, y1);
            var p2 = new Point(x1 + seg1, y1);
            var p3 = new Point(x1 + seg1 + seg2, y1 + (rnd.Next(2) == 0 ? seg2 : -seg2));

            dc.DrawLine(i % 2 == 0 ? trackPen : dimPen, p1, p2);
            dc.DrawLine(i % 2 == 0 ? trackPen : dimPen, p2, p3);

            dc.DrawEllipse(cyanDot, null, p1, 3, 3);
            dc.DrawEllipse(cyanDot, null, p3, 3, 3);
        }
    }

    private static void DrawDeepAurora(DrawingContext dc, int w, int h)
    {
        // Espaço Profundo
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(4, 7, 14)), null, new Rect(0, 0, w, h));

        // Gradiente Aurora Suave
        var auroraGrad1 = new RadialGradientBrush(Color.FromArgb(120, 16, 185, 129), Colors.Transparent)
        {
            Center = new Point(0.3, 0.45),
            RadiusX = 0.55,
            RadiusY = 0.35
        };
        dc.DrawRectangle(auroraGrad1, null, new Rect(0, 0, w, h));

        var auroraGrad2 = new RadialGradientBrush(Color.FromArgb(110, 6, 182, 212), Colors.Transparent)
        {
            Center = new Point(0.7, 0.55),
            RadiusX = 0.55,
            RadiusY = 0.35
        };
        dc.DrawRectangle(auroraGrad2, null, new Rect(0, 0, w, h));

        var auroraGrad3 = new RadialGradientBrush(Color.FromArgb(80, 99, 102, 241), Colors.Transparent)
        {
            Center = new Point(0.5, 0.3),
            RadiusX = 0.45,
            RadiusY = 0.25
        };
        dc.DrawRectangle(auroraGrad3, null, new Rect(0, 0, w, h));
    }

    private static void DrawAbstractGeometry(DrawingContext dc, int w, int h)
    {
        // Fundo OLED profundo
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(8, 8, 8)), null, new Rect(0, 0, w, h));

        // Polígonos e fatias angulares com degradês discretos
        var polyPen = new Pen(new SolidColorBrush(Color.FromArgb(35, 255, 255, 255)), 1.2);

        var p1 = new Point(0, h * 0.75);
        var p2 = new Point(w * 0.45, h * 0.4);
        var p3 = new Point(w * 0.8, h);
        var p4 = new Point(0, h);

        var geo1 = new PathGeometry();
        var fig1 = new PathFigure { StartPoint = p1, IsClosed = true, IsFilled = true };
        fig1.Segments.Add(new LineSegment(p2, true));
        fig1.Segments.Add(new LineSegment(p3, true));
        fig1.Segments.Add(new LineSegment(p4, true));
        geo1.Figures.Add(fig1);

        var grad1 = new LinearGradientBrush(Color.FromRgb(22, 22, 24), Color.FromRgb(12, 12, 14), new Point(0, 0), new Point(1, 1));
        dc.DrawGeometry(grad1, polyPen, geo1);

        var p5 = new Point(w * 0.35, 0);
        var p6 = new Point(w, h * 0.6);
        var p7 = new Point(w, 0);

        var geo2 = new PathGeometry();
        var fig2 = new PathFigure { StartPoint = p5, IsClosed = true, IsFilled = true };
        fig2.Segments.Add(new LineSegment(p6, true));
        fig2.Segments.Add(new LineSegment(p7, true));
        geo2.Figures.Add(fig2);

        var grad2 = new LinearGradientBrush(Color.FromRgb(28, 28, 30), Color.FromRgb(14, 14, 16), new Point(0, 0), new Point(1, 1));
        dc.DrawGeometry(grad2, polyPen, geo2);
    }
}
