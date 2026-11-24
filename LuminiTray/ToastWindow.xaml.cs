using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace LuminiTray;

/// <summary>Aviso no canto inferior direito da tela (dispositivo conectado/removido ou alerta térmico crítico). Não rouba o foco.</summary>
public partial class ToastWindow : Window
{
    private readonly DispatcherTimer _hide = new() { Interval = TimeSpan.FromSeconds(5) };
    private static readonly Brush DefaultBorderBrush = FreezeBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF));
    private static readonly Brush DefaultIconBgBrush = FreezeBrush(Color.FromRgb(0xF5, 0xF5, 0xF5));
    private static readonly Brush DefaultIconFgBrush = FreezeBrush(Color.FromRgb(0x00, 0x00, 0x00));
    private static readonly Brush CriticalBorderBrush = FreezeBrush(Color.FromRgb(0xEF, 0x44, 0x44));
    private static readonly Brush CriticalIconBgBrush = FreezeBrush(Color.FromRgb(0xEF, 0x44, 0x44));
    private static readonly Brush CriticalIconFgBrush = FreezeBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));

    public event Action? DismissedByUser;
    public bool IsPersistentActive { get; private set; }

    private static SolidColorBrush FreezeBrush(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    public ToastWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) =>
        {
            var h = new WindowInteropHelper(this).Handle;
            SetWindowLong(h, -20, GetWindowLong(h, -20) | 0x80 | 0x8000000); // WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE
        };
        SizeChanged += (_, _) => Place();
        _hide.Tick += (_, _) => { _hide.Stop(); Fade(0, () => Hide()); };
    }

    /// <summary>Mostra (ou atualiza, se já visível) o aviso por 5 s. icon: glifo Segoe MDL2.</summary>
    public void Notify(string title, string text, string icon)
        => ShowToast(title, text, icon, persistent: false, isCritical: false);

    /// <summary>Exibe um aviso crítico persistente que só fecha quando o usuário clicar no botão fechar.</summary>
    public void NotifyCriticalPersistent(string title, string text, string icon = "\uE7BA")
        => ShowToast(title, text, icon, persistent: true, isCritical: true);

    private void ShowToast(string title, string text, string icon, bool persistent, bool isCritical)
    {
        IsPersistentActive = persistent;
        TTitle.Text = title;
        TText.Text = text;
        TIcon.Text = icon;

        ToastBorder.BorderBrush = isCritical ? CriticalBorderBrush : DefaultBorderBrush;
        TIconBox.Background = isCritical ? CriticalIconBgBrush : DefaultIconBgBrush;
        TIcon.Foreground = isCritical ? CriticalIconFgBrush : DefaultIconFgBrush;
        BtnClose.Visibility = persistent ? Visibility.Visible : Visibility.Collapsed;
        TFooter.Text = persistent ? "Lumini Tray · Clique no X para fechar" : "Lumini Tray";

        if (!IsVisible) { Show(); Place(); }
        Fade(1, null);
        _hide.Stop();
        if (!persistent)
            _hide.Start();
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        _hide.Stop();
        IsPersistentActive = false;
        Fade(0, () => Hide());
        DismissedByUser?.Invoke();
    }

    private void Place()
    {
        var wa = SystemParameters.WorkArea;
        Left = wa.Right - ActualWidth - 16;
        Top = wa.Bottom - ActualHeight - 16;
    }

    private void Fade(double to, Action? done)
    {
        var anim = new DoubleAnimation(to, TimeSpan.FromMilliseconds(180));
        if (done != null) anim.Completed += (_, _) => done();
        BeginAnimation(OpacityProperty, anim);
    }

    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr h, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr h, int index, int value);
}
