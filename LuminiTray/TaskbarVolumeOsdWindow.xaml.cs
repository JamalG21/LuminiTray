using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace LuminiTray;

public partial class TaskbarVolumeOsdWindow : Window
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_TOOLWINDOW = 0x00000080;

    [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
    private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
    private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    private readonly DispatcherTimer _hideTimer;
    private static readonly SolidColorBrush MuteBrush = new((Color)ColorConverter.ConvertFromString("#FF5555"));

    public TaskbarVolumeOsdWindow()
    {
        InitializeComponent();
        _hideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
        _hideTimer.Tick += (_, _) =>
        {
            _hideTimer.Stop();
            Hide();
        };

        SourceInitialized += OnSourceInitialized;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        try
        {
            var helper = new WindowInteropHelper(this);
            IntPtr hwnd = helper.Handle;
            if (hwnd != IntPtr.Zero)
            {
                if (IntPtr.Size == 8)
                {
                    IntPtr exStyle = GetWindowLongPtr64(hwnd, GWL_EXSTYLE);
                    long newStyle = exStyle.ToInt64() | WS_EX_NOACTIVATE | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW;
                    SetWindowLongPtr64(hwnd, GWL_EXSTYLE, new IntPtr(newStyle));
                }
                else
                {
                    int exStyle = GetWindowLong32(hwnd, GWL_EXSTYLE);
                    int newStyle = exStyle | WS_EX_NOACTIVATE | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW;
                    SetWindowLong32(hwnd, GWL_EXSTYLE, newStyle);
                }
            }
        }
        catch { }
    }

    public void ShowVolume(string title, int percent, int cursorX, int cursorY)
    {
        if (TaskbarVolumeHook.IsForegroundFullscreenOrGame(cursorX, cursorY))
        {
            return;
        }

        TxtTitle.Text = title;
        var accentBrush = (Application.Current?.TryFindResource("Accent") as Brush) ?? Brushes.White;

        if (percent == -2)
        {
            TxtIcon.Text = "\uE74F"; // Sem som
            TxtIcon.Foreground = accentBrush;
            TxtPercent.Text = "SEM SOM";
            TxtPercent.Foreground = accentBrush;
            PbVolume.Foreground = accentBrush;
            PbVolume.Value = 0;
        }
        else if (percent < 0)
        {
            TxtIcon.Text = "\uE74F"; // Mudo
            TxtIcon.Foreground = MuteBrush;
            TxtPercent.Text = "MUTADO";
            TxtPercent.Foreground = MuteBrush;
            PbVolume.Foreground = MuteBrush;
            PbVolume.Value = 0;
        }
        else
        {
            TxtIcon.Text = percent == 0 ? "\uE74F" : (percent < 33 ? "\uE992" : (percent < 66 ? "\uE993" : "\uE767"));
            TxtIcon.Foreground = accentBrush;
            TxtPercent.Text = $"{percent}%";
            TxtPercent.Foreground = accentBrush;
            PbVolume.Foreground = accentBrush;
            PbVolume.Value = percent;
        }

        // Posiciona ligeiramente acima do cursor adaptando-se a múltiplos monitores
        double x = cursorX - 95;
        double y = cursorY - 75;

        try
        {
            var screen = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(cursorX, cursorY));
            if (x < screen.WorkingArea.Left + 10) x = screen.WorkingArea.Left + 10;
            if (x + 190 > screen.WorkingArea.Right) x = screen.WorkingArea.Right - 195;
            if (y < screen.WorkingArea.Top + 10) y = cursorY + 25;
            if (y + 70 > screen.WorkingArea.Bottom) y = cursorY - 75;
        }
        catch { }

        Left = x;
        Top = y;

        if (!IsVisible) Show();
        _hideTimer.Stop();
        _hideTimer.Start();
    }
}
