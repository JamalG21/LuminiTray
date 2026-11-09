using System;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace LuminiTray;

public enum MonitorLabMode
{
    DeadPixels,
    Uniformity,
    Contrast,
    Gradients,
    Sharpness,
    UfoGhosting
}

public partial class MonitorLabWindow : Window
{
    private readonly MonitorLabMode _mode;
    private int _solidIndex;
    private readonly DispatcherTimer _hudTimer;

    // Sólidos para Dead Pixels
    private static readonly (Color Color, string Name)[] DeadPixelColors =
    {
        (Colors.Black, "PRETO (BLACK)"),
        (Colors.White, "BRANCO (WHITE)"),
        (Colors.Red, "VERMELHO (RED)"),
        (Color.FromRgb(0, 255, 0), "VERDE (GREEN)"),
        (Colors.Blue, "AZUL (BLUE)"),
        (Colors.Yellow, "AMARELO (YELLOW)"),
        (Colors.Cyan, "CIANO (CYAN)"),
        (Colors.Magenta, "MAGENTA (MAGENTA)")
    };

    // Níveis de cinza para Uniformidade e Vazamento de Luz (Backlight Bleed)
    private static readonly (Color Color, string Name)[] UniformityColors =
    {
        (Colors.Black, "PRETO 0% (Vazamento de Luz nos Cantos)"),
        (Color.FromRgb(13, 13, 13), "CINZA 5% (Near-Black Uniformity)"),
        (Color.FromRgb(26, 26, 26), "CINZA 10% (Dark Gray Uniformity)"),
        (Color.FromRgb(128, 128, 128), "CINZA 50% (DSE / Efeito Tela Suja)")
    };

    // UFO State
    private bool _ufoActive;
    private double _posX960;
    private double _posX480;
    private double _posX240;
    private TimeSpan _lastRenderTime;
    private int _frameCount;
    private readonly Stopwatch _fpsStopwatch = new();

    private readonly MonitorDeviceItem? _targetMonitor;

    public MonitorLabWindow(MonitorLabMode mode = MonitorLabMode.Contrast, MonitorDeviceItem? targetMonitor = null)
    {
        InitializeComponent();
        _mode = mode;
        _targetMonitor = targetMonitor;

        PositionOnTargetMonitor(targetMonitor);

        _hudTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3.5) };
        _hudTimer.Tick += (_, _) =>
        {
            _hudTimer.Stop();
            HudNotice.Visibility = Visibility.Collapsed;
        };
        _hudTimer.Start();
    }

    private void PositionOnTargetMonitor(MonitorDeviceItem? targetMonitor)
    {
        try
        {
            System.Windows.Forms.Screen? screen = null;
            if (targetMonitor != null && !string.IsNullOrWhiteSpace(targetMonitor.DeviceName))
            {
                screen = System.Windows.Forms.Screen.AllScreens
                    .FirstOrDefault(s => s.DeviceName.Equals(targetMonitor.DeviceName, StringComparison.OrdinalIgnoreCase));
            }

            screen ??= System.Windows.Forms.Screen.PrimaryScreen ?? System.Windows.Forms.Screen.AllScreens.FirstOrDefault();

            if (screen != null)
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                WindowState = WindowState.Normal;
                // Posiciona a janela dentro dos limites do monitor alvo para maximização garantida nele
                Left = screen.Bounds.Left + 50;
                Top = screen.Bounds.Top + 50;
                Width = Math.Max(400, screen.Bounds.Width - 100);
                Height = Math.Max(300, screen.Bounds.Height - 100);
            }
        }
        catch
        {
            // Fallback silencioso
        }
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        // Garante que maximiza diretamente no monitor posicionado
        WindowState = WindowState.Maximized;

        string monitorInfo = _targetMonitor != null ? $" · [{_targetMonitor.FriendlyName}]" : "";

        switch (_mode)
        {
            case MonitorLabMode.DeadPixels:
                TxtHudTitle.Text = "MONITORLAB · VERIFICADOR DE DEAD PIXELS" + monitorInfo;
                ContainerSolidColor.Visibility = Visibility.Visible;
                UpdateSolidColor();
                break;

            case MonitorLabMode.Uniformity:
                TxtHudTitle.Text = "MONITORLAB · UNIFORMIDADE & VAZAMENTO DE LUZ" + monitorInfo;
                ContainerSolidColor.Visibility = Visibility.Visible;
                UpdateUniformityColor();
                break;

            case MonitorLabMode.Contrast:
                TxtHudTitle.Text = "MONITORLAB · TESTE DE CONTRASTE & ESCALA DE CINZA" + monitorInfo;
                ContainerContrast.Visibility = Visibility.Visible;
                BuildGrayscaleSteps();
                break;

            case MonitorLabMode.Gradients:
                TxtHudTitle.Text = "MONITORLAB · CALIBRAÇÃO DE GRADIENTES" + monitorInfo;
                ContainerGradients.Visibility = Visibility.Visible;
                break;

            case MonitorLabMode.Sharpness:
                TxtHudTitle.Text = "MONITORLAB · TESTE DE NITIDEZ & SUBPIXELS" + monitorInfo;
                ContainerSharpness.Visibility = Visibility.Visible;
                BuildSharpnessPattern();
                break;

            case MonitorLabMode.UfoGhosting:
                TxtHudTitle.Text = "MONITORLAB · UFO GHOSTING & TEMPO DE RESPOSTA" + monitorInfo;
                ContainerUfo.Visibility = Visibility.Visible;
                StartUfoEngine();
                break;
        }
    }

    private void BuildGrayscaleSteps()
    {
        GridGrayscaleSteps.Children.Clear();
        for (int i = 0; i < 16; i++)
        {
            byte val = (byte)(i * 255 / 15);
            var b = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(val, val, val)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(40, 40, 40)),
                BorderThickness = new Thickness(0.5)
            };
            var tb = new TextBlock
            {
                Text = $"{Math.Round(i * 100.0 / 15.0)}%",
                FontSize = 9.5,
                FontWeight = FontWeights.Bold,
                Foreground = val > 128 ? Brushes.Black : Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            b.Child = tb;
            GridGrayscaleSteps.Children.Add(b);
        }
    }

    private void BuildSharpnessPattern()
    {
        CanvasSharpnessGrid.Children.Clear();
        // Linhas pretas e brancas de 1px alternadas
        for (int x = 0; x < 450; x += 4)
        {
            var line = new Line
            {
                X1 = x,
                Y1 = 0,
                X2 = x,
                Y2 = 140,
                Stroke = Brushes.White,
                StrokeThickness = 1,
                SnapsToDevicePixels = true
            };
            CanvasSharpnessGrid.Children.Add(line);
        }
        for (int y = 0; y < 140; y += 4)
        {
            var line = new Line
            {
                X1 = 0,
                Y1 = y,
                X2 = 450,
                Y2 = y,
                Stroke = Brushes.White,
                StrokeThickness = 1,
                SnapsToDevicePixels = true
            };
            CanvasSharpnessGrid.Children.Add(line);
        }
    }

    private void UpdateSolidColor()
    {
        var (c, name) = DeadPixelColors[_solidIndex];
        ContainerSolidColor.Background = new SolidColorBrush(c);
        TxtHudTitle.Text = $"COR ATUAL: {name} ({_solidIndex + 1}/{DeadPixelColors.Length})";
    }

    private void UpdateUniformityColor()
    {
        var (c, name) = UniformityColors[_solidIndex];
        ContainerSolidColor.Background = new SolidColorBrush(c);
        TxtHudTitle.Text = $"PADRÃO: {name} ({_solidIndex + 1}/{UniformityColors.Length})";
    }

    private void StartUfoEngine()
    {
        _ufoActive = true;
        _lastRenderTime = TimeSpan.Zero;
        _frameCount = 0;
        _fpsStopwatch.Restart();
        CompositionTarget.Rendering += OnUfoFrame;
    }

    private void StopUfoEngine()
    {
        if (!_ufoActive) return;
        _ufoActive = false;
        CompositionTarget.Rendering -= OnUfoFrame;
    }

    private void OnUfoFrame(object? sender, EventArgs e)
    {
        if (!_ufoActive) return;

        if (e is RenderingEventArgs args)
        {
            if (_lastRenderTime == args.RenderingTime) return;

            double dt = (args.RenderingTime - _lastRenderTime).TotalSeconds;
            _lastRenderTime = args.RenderingTime;
            if (dt <= 0 || dt > 0.1) dt = 1.0 / 60.0;

            double width = CanvasTrack960.ActualWidth;
            if (width <= 0) width = ActualWidth > 0 ? ActualWidth : 1200;

            _posX960 = (_posX960 + 960.0 * dt) % (width + 100);
            _posX480 = (_posX480 + 480.0 * dt) % (width + 100);
            _posX240 = (_posX240 + 240.0 * dt) % (width + 100);

            Canvas.SetLeft(UfoSprite960, _posX960 - 80);
            Canvas.SetLeft(UfoSprite480, _posX480 - 80);
            Canvas.SetLeft(UfoSprite240, _posX240 - 80);

            _frameCount++;
            if (_fpsStopwatch.ElapsedMilliseconds >= 500)
            {
                double fps = Math.Round(_frameCount / (_fpsStopwatch.ElapsedMilliseconds / 1000.0), 0);
                TxtUfoFpsHz.Text = $"Taxa: {fps} FPS / Hz · V-Sync Ativo";
                _frameCount = 0;
                _fpsStopwatch.Restart();
            }
        }
    }

    private void BtnUfoBgDark_Click(object sender, RoutedEventArgs e)
    {
        ContainerUfo.Background = new SolidColorBrush(Color.FromRgb(20, 20, 20));
    }

    private void BtnUfoBgGray_Click(object sender, RoutedEventArgs e)
    {
        ContainerUfo.Background = new SolidColorBrush(Color.FromRgb(127, 127, 127));
    }

    private void BtnUfoBgCyan_Click(object sender, RoutedEventArgs e)
    {
        ContainerUfo.Background = new SolidColorBrush(Color.FromRgb(20, 48, 70));
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            return;
        }

        if (_mode == MonitorLabMode.DeadPixels)
        {
            if (e.Key is Key.Right or Key.Down or Key.Space or Key.Enter)
            {
                _solidIndex = (_solidIndex + 1) % DeadPixelColors.Length;
                UpdateSolidColor();
            }
            else if (e.Key is Key.Left or Key.Up)
            {
                _solidIndex = (_solidIndex - 1 + DeadPixelColors.Length) % DeadPixelColors.Length;
                UpdateSolidColor();
            }
        }
        else if (_mode == MonitorLabMode.Uniformity)
        {
            if (e.Key is Key.Right or Key.Down or Key.Space or Key.Enter)
            {
                _solidIndex = (_solidIndex + 1) % UniformityColors.Length;
                UpdateUniformityColor();
            }
            else if (e.Key is Key.Left or Key.Up)
            {
                _solidIndex = (_solidIndex - 1 + UniformityColors.Length) % UniformityColors.Length;
                UpdateUniformityColor();
            }
        }
    }

    private void Window_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount >= 2)
        {
            Close();
            return;
        }

        if (_mode == MonitorLabMode.DeadPixels)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                _solidIndex = (_solidIndex + 1) % DeadPixelColors.Length;
                UpdateSolidColor();
            }
            else if (e.ChangedButton == MouseButton.Right)
            {
                _solidIndex = (_solidIndex - 1 + DeadPixelColors.Length) % DeadPixelColors.Length;
                UpdateSolidColor();
            }
        }
        else if (_mode == MonitorLabMode.Uniformity)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                _solidIndex = (_solidIndex + 1) % UniformityColors.Length;
                UpdateUniformityColor();
            }
            else if (e.ChangedButton == MouseButton.Right)
            {
                _solidIndex = (_solidIndex - 1 + UniformityColors.Length) % UniformityColors.Length;
                UpdateUniformityColor();
            }
        }
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        StopUfoEngine();
    }
}
