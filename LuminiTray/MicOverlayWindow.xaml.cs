using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace LuminiTray;

public partial class MicOverlayWindow : Window
{
    private readonly AudioSettings _settings;
    private readonly Action _onToggleMute;
    private bool _isMuted = false;

    public MicOverlayWindow(AudioSettings settings, Action onToggleMute)
    {
        InitializeComponent();
        _settings = settings;
        _onToggleMute = onToggleMute;

        Left = Math.Max(0, _settings.OverlayPositionX);
        Top = Math.Max(0, _settings.OverlayPositionY);

        UpdateVisuals();
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            // Duplo clique alterna mute
            _onToggleMute?.Invoke();
            return;
        }

        try
        {
            DragMove();
            _settings.OverlayPositionX = Left;
            _settings.OverlayPositionY = Top;
            _settings.Save();
        }
        catch
        {
            // Ignora se o arrasto for cancelado
        }
    }

    public void UpdateState(bool isMuted, string? micName = null)
    {
        _isMuted = isMuted;
        Dispatcher.Invoke(() =>
        {
            if (!_settings.OverlayEnabled || !_settings.EnableMicMuteFeature)
            {
                Visibility = Visibility.Collapsed;
                return;
            }

            if (_settings.OverlayOnlyWhenMuted && !_isMuted)
            {
                Visibility = Visibility.Collapsed;
                return;
            }

            Visibility = Visibility.Visible;
            UpdateVisuals(micName);
        });
    }

    private void UpdateVisuals(string? micName = null)
    {
        if (_isMuted)
        {
            IndicatorCircle.Background = new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44)); // Vermelho
            IconMic.Text = "\uE74F"; // MicOff / Muted
            TxtStatus.Text = "MIC MUTADO";
            TxtStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44));
            TxtHint.Text = string.IsNullOrWhiteSpace(micName) ? "Clique duplo para reativar" : micName;
        }
        else
        {
            IndicatorCircle.Background = new SolidColorBrush(Color.FromRgb(0x10, 0xB9, 0x81)); // Verde
            IconMic.Text = "\uE720"; // Mic Active
            TxtStatus.Text = "MIC ABERTO";
            TxtStatus.Foreground = new SolidColorBrush(Color.FromRgb(0x10, 0xB9, 0x81));
            TxtHint.Text = string.IsNullOrWhiteSpace(micName) ? "Clique duplo para mutar" : micName;
        }
    }
}
