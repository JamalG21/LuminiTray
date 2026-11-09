using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace LuminiTray;

public partial class DeadPixelWindow : Window
{
    private static readonly (Color Color, string Name)[] TestColors =
    {
        (Colors.Black, "PRETO (BLACK)"),
        (Colors.White, "BRANCO (WHITE)"),
        (Colors.Red, "VERMELHO (RED)"),
        (Color.FromRgb(0, 255, 0), "VERDE (GREEN)"),
        (Colors.Blue, "AZUL (BLUE)"),
        (Colors.Yellow, "AMARELO (YELLOW)"),
        (Colors.Cyan, "CIANO (CYAN)"),
        (Colors.Magenta, "MAGENTA (MAGENTA)"),
        (Color.FromRgb(128, 128, 128), "CINZA 50% (GRAY)")
    };

    private int _colorIndex;
    private readonly DispatcherTimer _hudTimer;

    public DeadPixelWindow()
    {
        InitializeComponent();
        UpdateColor();

        // Ocultar HUD após 3.5 segundos
        _hudTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3.5) };
        _hudTimer.Tick += (_, _) =>
        {
            _hudTimer.Stop();
            HudNotice.Visibility = Visibility.Collapsed;
        };
        _hudTimer.Start();
    }

    private void UpdateColor()
    {
        var (c, name) = TestColors[_colorIndex];
        Background = new SolidColorBrush(c);
        TxtColorName.Text = $"COR ATUAL: {name} ({_colorIndex + 1}/{TestColors.Length})";

        // Se o HUD estiver visível, ajustar contraste
        if (c == Colors.White || c == Colors.Yellow || c == Colors.Cyan)
            HudNotice.Background = new SolidColorBrush(Color.FromArgb(220, 20, 20, 20));
        else
            HudNotice.Background = new SolidColorBrush(Color.FromArgb(200, 10, 10, 10));
    }

    private void NextColor()
    {
        _colorIndex = (_colorIndex + 1) % TestColors.Length;
        UpdateColor();
    }

    private void PrevColor()
    {
        _colorIndex = (_colorIndex - 1 + TestColors.Length) % TestColors.Length;
        UpdateColor();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            return;
        }

        if (e.Key is Key.Right or Key.Down or Key.Space or Key.Enter)
            NextColor();
        else if (e.Key is Key.Left or Key.Up)
            PrevColor();
    }

    private void Window_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount >= 2)
        {
            Close();
            return;
        }

        if (e.ChangedButton == MouseButton.Left)
            NextColor();
        else if (e.ChangedButton == MouseButton.Right)
            PrevColor();
    }
}
