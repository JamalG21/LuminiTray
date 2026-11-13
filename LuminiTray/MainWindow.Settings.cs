using System;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace LuminiTray;

public partial class MainWindow
{
    private bool _isSettingsLoading = false;

    private void InitSettingsPage()
    {
        _isSettingsLoading = true;
        try
        {
            // 0. Modo Baixo Consumo
            if (ChkSettingsLowPowerMode != null) ChkSettingsLowPowerMode.IsChecked = _settings.LowPowerMode;

            // 1. Aparência e Tema
            if (ChkSettingsDarkTheme != null) ChkSettingsDarkTheme.IsChecked = !_settings.LightTheme;
            if (ChkSettingsLightTheme != null) ChkSettingsLightTheme.IsChecked = _settings.LightTheme;

            // 2. Comportamento e Bandeja
            if (ChkSettingsMinimizeToTray != null) ChkSettingsMinimizeToTray.IsChecked = _settings.MinimizeToTrayOnClose;
            if (ChkSettingsStartMinimized != null) ChkSettingsStartMinimized.IsChecked = _settings.StartMinimizedToTray;
            if (ChkSettingsShowMicTray != null) ChkSettingsShowMicTray.IsChecked = _settings.ShowMicTrayIcon;
            if (ChkSettingsShowAudioTray != null) ChkSettingsShowAudioTray.IsChecked = _settings.ShowAudioTrayIcon;
            if (ChkSettingsShowMiniTaskTray != null) ChkSettingsShowMiniTaskTray.IsChecked = _settings.ShowMiniTaskTrayIcon;

            // 3. Taxa dos Sensores
            if (CmbSettingsSensorRate != null)
            {
                CmbSettingsSensorRate.SelectedIndex = _settings.SensorIntervalMs switch
                {
                    500 => 0,
                    2000 => 2,
                    _ => 1
                };
            }

            
            // 4. Atalhos Rápidos & Controles do Programa
            if (ChkFastMouseWheelVolume != null)
                ChkFastMouseWheelVolume.IsChecked = _audioSettings.EnableTaskbarVolumeScroll;

            if (ChkFastPlaybackMute != null)
                ChkFastPlaybackMute.IsChecked = _audioSettings.EnablePlaybackMuteFeature;

            if (ChkFastMicMute != null)
                ChkFastMicMute.IsChecked = _audioSettings.EnableMicMuteFeature;

            if (ChkFastPlaybackCycle != null)
                ChkFastPlaybackCycle.IsChecked = _audioSettings.EnablePlaybackCycleFeature;

            if (ChkFastRecordingCycle != null)
                ChkFastRecordingCycle.IsChecked = _audioSettings.EnableRecordingCycleFeature;

            if (ChkFastNoiseSuppressor != null)
                ChkFastNoiseSuppressor.IsChecked = MicrophoneSuppressorService.Instance.MasterEnabled;

            if (ChkFastOverlay != null)
                ChkFastOverlay.IsChecked = (ChkOverlay?.IsChecked == true);

            try
            {
                if (CboFastAudioOutput != null)
                {
                    CboFastAudioOutput.IsEnabled = _audioSettings.EnablePlaybackCycleFeature;
                    var outputs = AudioCore.GetDevices(AudioInterop.EDataFlow.Render);
                    CboFastAudioOutput.ItemsSource = outputs;
                    CboFastAudioOutput.DisplayMemberPath = "Name";
                    CboFastAudioOutput.SelectedItem = outputs.FirstOrDefault(d => d.IsDefaultConsole) ?? outputs.FirstOrDefault();
                }

                if (CboFastAudioInput != null)
                {
                    CboFastAudioInput.IsEnabled = _audioSettings.EnableRecordingCycleFeature;
                    var inputs = AudioCore.GetDevices(AudioInterop.EDataFlow.Capture);
                    CboFastAudioInput.ItemsSource = inputs;
                    CboFastAudioInput.DisplayMemberPath = "Name";
                    CboFastAudioInput.SelectedItem = inputs.FirstOrDefault(d => d.IsDefaultConsole) ?? inputs.FirstOrDefault();
                }
            }
            catch { }

            UpdateAccentCardsUi();
        }
        finally
        {
            _isSettingsLoading = false;
        }
    }

    private void SettingsTab_Checked(object sender, RoutedEventArgs e)
    {
        if (PanelSettingsGeneral == null || PanelSettingsAudio == null || PanelSettingsWallpapers == null) return;

        bool isGeneral = TabSettingsGeneral?.IsChecked == true;
        bool isThemes = TabSettingsThemes?.IsChecked == true;
        bool isAudio = TabSettingsAudio?.IsChecked == true;
        bool isWallpapers = TabSettingsWallpapers?.IsChecked == true;
        bool isInteg = TabSettingsInteg?.IsChecked == true;
        bool isUndo = TabSettingsUndo?.IsChecked == true;

        PanelSettingsGeneral.Visibility = isGeneral ? Visibility.Visible : Visibility.Collapsed;
        if (PanelSettingsThemes != null)
            PanelSettingsThemes.Visibility = isThemes ? Visibility.Visible : Visibility.Collapsed;
        PanelSettingsAudio.Visibility = isAudio ? Visibility.Visible : Visibility.Collapsed;
        PanelSettingsWallpapers.Visibility = isWallpapers ? Visibility.Visible : Visibility.Collapsed;
        if (PanelSettingsInteg != null)
            PanelSettingsInteg.Visibility = isInteg ? Visibility.Visible : Visibility.Collapsed;
        if (PanelSettingsUndo != null)
            PanelSettingsUndo.Visibility = isUndo ? Visibility.Visible : Visibility.Collapsed;

        if (isAudio)
        {
            RefreshAudioUi();
        }
        else if (isWallpapers)
        {
            InitWallpapers();
        }
        else
        {
            ReleaseWallpaperGalleryMemory();
            if (isInteg)
            {
                InitInteg();
            }
            else if (isUndo)
            {
                RefreshUndoCenter();
            }
            else if (isThemes)
            {
                InitSettingsPage();
            }
            else if (isGeneral)
            {
                InitSettingsPage();
            }
        }
    }

    private void UpdateAccentCardsUi()
    {
        string current = _settings.AccentColorTheme ?? "Monochrome";
        if (TxtCurrentAccentName != null)
        {
            if (ThemeManager.AccentThemes.TryGetValue(current, out var info))
            {
                TxtCurrentAccentName.Text = $"Paleta Atual: {info.Name} ({info.Description})";
            }
            else
            {
                TxtCurrentAccentName.Text = "Paleta Atual: Monocromático Clássico";
            }
        }

        if (GridAccentColors != null)
        {
            foreach (var child in GridAccentColors.Children)
            {
                if (child is Button btn && btn.Tag is string tag)
                {
                    bool isSelected = string.Equals(tag, current, StringComparison.OrdinalIgnoreCase);
                    if (isSelected)
                    {
                        btn.BorderBrush = (Brush)Application.Current.Resources["Accent"];
                        btn.BorderThickness = new Thickness(2);
                    }
                    else
                    {
                        btn.BorderBrush = (Brush)Application.Current.Resources["LineStrong"];
                        btn.BorderThickness = new Thickness(1);
                    }
                }
            }
        }
    }

    private void BtnAccentColor_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string colorKey)
        {
            _settings.AccentColorTheme = colorKey;
            _settings.Save();

            ThemeManager.Apply(_settings.LightTheme, colorKey);
            UpdateAccentCardsUi();
            Toast("Aparência & Cores", $"Paleta alterada para {ThemeManager.AccentThemes[colorKey].Name}.", "\uE790");
        }
    }

    private void SettingsTheme_Changed(object sender, RoutedEventArgs e)
    {
        if (_isSettingsLoading) return;
        bool isLight = ChkSettingsLightTheme?.IsChecked == true;
        if (_settings.LightTheme != isLight)
        {
            _settings.LightTheme = isLight;
            _settings.Save();

            ThemeManager.Apply(isLight, _settings.AccentColorTheme);
            UpdateThemeButton();
            Toast("Tema Alterado", isLight ? "Modo Claro Ativado." : "Modo Escuro Ativado.", isLight ? "\uE706" : "\uE708");
        }
    }

    private void SettingsOption_Changed(object sender, RoutedEventArgs e)
    {
        if (_isSettingsLoading) return;
        if (ChkSettingsMinimizeToTray != null) _settings.MinimizeToTrayOnClose = ChkSettingsMinimizeToTray.IsChecked == true;
        if (ChkSettingsStartMinimized != null) _settings.StartMinimizedToTray = ChkSettingsStartMinimized.IsChecked == true;

        if (ChkSettingsShowMicTray != null)
        {
            _settings.ShowMicTrayIcon = ChkSettingsShowMicTray.IsChecked == true;
            if (_trayManager != null)
            {
                _trayManager.ShowMicIcon = _settings.ShowMicTrayIcon;
                bool micMuted = AudioCore.GetDefaultMute(AudioInterop.EDataFlow.Capture);
                _trayManager.UpdateMic(micMuted, null);
            }
        }

        if (ChkSettingsShowAudioTray != null)
        {
            _settings.ShowAudioTrayIcon = ChkSettingsShowAudioTray.IsChecked == true;
            if (_trayManager != null)
            {
                _trayManager.ShowAudioIcon = _settings.ShowAudioTrayIcon;
                var currentSpeaker = AudioCore.GetDevices(AudioInterop.EDataFlow.Render).FirstOrDefault(d => d.IsDefaultConsole);
                _trayManager.UpdateAudioOutput(currentSpeaker?.Name, currentSpeaker?.VolumePercent ?? 100);
            }
        }

        if (ChkSettingsShowMiniTaskTray != null)
        {
            _settings.ShowMiniTaskTrayIcon = ChkSettingsShowMiniTaskTray.IsChecked == true;
            if (_trayManager != null)
            {
                _trayManager.ShowMiniTaskIcon = _settings.ShowMiniTaskTrayIcon;
                _trayManager.UpdateMiniTaskIcon();
            }
        }

        _settings.Save();
    }

    private void CmbSettingsSensorRate_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_settings == null || CmbSettingsSensorRate == null) return;
        _settings.SensorIntervalMs = CmbSettingsSensorRate.SelectedIndex switch
        {
            0 => 500,
            2 => 2000,
            _ => 1000
        };
        _settings.Save();

        if (_timer != null)
        {
            _timer.Interval = TimeSpan.FromMilliseconds(_settings.SensorIntervalMs);
        }
    }

    private void BtnExitAppComplete_Click(object sender, RoutedEventArgs e)
    {
        var r = MessageBox.Show(
            "Deseja realmente fechar o Lumini Tray por completo?\nOs atalhos de áudio, overlay e ícones da bandeja serão encerrados.",
            "Encerrar Lumini Tray",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (r == MessageBoxResult.Yes)
        {
            PerformFullExit();
        }
    }

    // ---------------- Modo Baixo Consumo ----------------

    private void SettingsLowPowerMode_Click(object sender, RoutedEventArgs e)
    {
        bool enable = ChkSettingsLowPowerMode?.IsChecked == true;
        _settings.LowPowerMode = enable;

        if (enable)
        {
            // Salva o snapshot fiel do estado anterior antes de desativar
            if (_settings.LowPowerPreviousState == null || !_settings.LowPowerPreviousState.IsSaved)
            {
                _settings.LowPowerPreviousState = new LowPowerSnapshot
                {
                    IsSaved = true,
                    TaskbarVolumeScroll = _audioSettings.EnableTaskbarVolumeScroll,
                    PlaybackMute = _audioSettings.EnablePlaybackMuteFeature,
                    MicMute = _audioSettings.EnableMicMuteFeature,
                    PlaybackCycle = _audioSettings.EnablePlaybackCycleFeature,
                    RecordingCycle = _audioSettings.EnableRecordingCycleFeature,
                    SuppressorMaster = _settings.SuppressorMasterEnabled
                };
            }

            _settings.Save();

            // Ao ativar o modo baixo consumo, desliga tweaks automáticas secundárias
            _audioSettings.EnableTaskbarVolumeScroll = false;
            _audioSettings.EnablePlaybackMuteFeature = false;
            _audioSettings.EnablePlaybackCycleFeature = false;
            _audioSettings.EnableRecordingCycleFeature = false;
            _audioSettings.Save();

            _taskbarVolumeHook?.StopHook();
            _audioHook?.UpdateHookState();

            // Sincroniza UI
            if (ChkFastMouseWheelVolume != null) ChkFastMouseWheelVolume.IsChecked = false;
            if (ChkFastPlaybackMute != null) ChkFastPlaybackMute.IsChecked = false;
            if (ChkFastPlaybackCycle != null) ChkFastPlaybackCycle.IsChecked = false;
            if (ChkFastRecordingCycle != null) ChkFastRecordingCycle.IsChecked = false;
            if (CboFastAudioOutput != null) CboFastAudioOutput.IsEnabled = false;
            if (CboFastAudioInput != null) CboFastAudioInput.IsEnabled = false;

            if (ChkEnableTaskbarVolume != null) ChkEnableTaskbarVolume.IsChecked = false;
            if (ChkEnablePlaybackCycle != null) ChkEnablePlaybackCycle.IsChecked = false;
            if (ChkEnableRecordingCycle != null) ChkEnableRecordingCycle.IsChecked = false;
            if (ChkEnablePlaybackMute != null) ChkEnablePlaybackMute.IsChecked = false;

            // Desativa supressor se não tiver retorno ativo
            if (!_audioTester.IsLoopbackEnabled && MicrophoneSuppressorService.Instance.MasterEnabled)
            {
                MicrophoneSuppressorService.Instance.MasterEnabled = false;
                _settings.SuppressorMasterEnabled = false;
                _settings.Save();
                MicrophoneSuppressorService.Instance.Stop();
                UpdateSuppressorMasterUi(false);
                if (ChkFastNoiseSuppressor != null) ChkFastNoiseSuppressor.IsChecked = false;
            }

            UpdateSensorPollingState();

            Toast("Modo Baixo Consumo", "Modo Baixo Consumo ATIVADO. Atividades secundárias pausadas.", "\uE7E8");
        }
        else
        {
            // Restauração inteligente: recupera o que estava ativo antes do Modo Econômico
            if (_settings.LowPowerPreviousState != null && _settings.LowPowerPreviousState.IsSaved)
            {
                var snap = _settings.LowPowerPreviousState;
                _audioSettings.EnableTaskbarVolumeScroll = snap.TaskbarVolumeScroll || _audioSettings.EnableTaskbarVolumeScroll;
                _audioSettings.EnablePlaybackMuteFeature = snap.PlaybackMute || _audioSettings.EnablePlaybackMuteFeature;
                _audioSettings.EnableMicMuteFeature = snap.MicMute || _audioSettings.EnableMicMuteFeature;
                _audioSettings.EnablePlaybackCycleFeature = snap.PlaybackCycle || _audioSettings.EnablePlaybackCycleFeature;
                _audioSettings.EnableRecordingCycleFeature = snap.RecordingCycle || _audioSettings.EnableRecordingCycleFeature;

                bool restoreSuppressor = snap.SuppressorMaster;
                if (restoreSuppressor)
                {
                    _settings.SuppressorMasterEnabled = true;
                    EnsureSuppressorServiceRunning();
                }

                _settings.LowPowerPreviousState.IsSaved = false;
                _audioSettings.Save();
                _settings.Save();

                // Sincroniza controles rápidos (Aba Geral)
                if (ChkFastMouseWheelVolume != null) ChkFastMouseWheelVolume.IsChecked = _audioSettings.EnableTaskbarVolumeScroll;
                if (ChkFastPlaybackMute != null) ChkFastPlaybackMute.IsChecked = _audioSettings.EnablePlaybackMuteFeature;
                if (ChkFastMicMute != null) ChkFastMicMute.IsChecked = _audioSettings.EnableMicMuteFeature;
                if (ChkFastPlaybackCycle != null) ChkFastPlaybackCycle.IsChecked = _audioSettings.EnablePlaybackCycleFeature;
                if (ChkFastRecordingCycle != null) ChkFastRecordingCycle.IsChecked = _audioSettings.EnableRecordingCycleFeature;
                if (CboFastAudioOutput != null) CboFastAudioOutput.IsEnabled = _audioSettings.EnablePlaybackCycleFeature;
                if (CboFastAudioInput != null) CboFastAudioInput.IsEnabled = _audioSettings.EnableRecordingCycleFeature;
                if (ChkFastNoiseSuppressor != null) ChkFastNoiseSuppressor.IsChecked = _settings.SuppressorMasterEnabled;

                // Sincroniza controles detalhados (Aba Áudio)
                if (ChkEnableTaskbarVolume != null) ChkEnableTaskbarVolume.IsChecked = _audioSettings.EnableTaskbarVolumeScroll;
                if (ChkEnablePlaybackCycle != null) ChkEnablePlaybackCycle.IsChecked = _audioSettings.EnablePlaybackCycleFeature;
                if (ChkEnableRecordingCycle != null) ChkEnableRecordingCycle.IsChecked = _audioSettings.EnableRecordingCycleFeature;
                if (ChkEnablePlaybackMute != null) ChkEnablePlaybackMute.IsChecked = _audioSettings.EnablePlaybackMuteFeature;
                if (ChkEnableMicMute != null) ChkEnableMicMute.IsChecked = _audioSettings.EnableMicMuteFeature;

                // Restaura hooks ativos
                if (_audioSettings.EnableTaskbarVolumeScroll)
                    _taskbarVolumeHook?.StartHook();
                else
                    _taskbarVolumeHook?.StopHook();

                _audioHook?.UpdateHookState();
            }

            _settings.Save();
            UpdateSensorPollingState();
            Toast("Modo Baixo Consumo", "Modo Baixo Consumo DESATIVADO. Configurações anteriores restauradas.", "\uE7E8");
        }
    }

    // ---------------- Atalhos Rápidos & Controles do Programa (Aba Geral) ----------------

    private void FastPlaybackMute_Click(object sender, RoutedEventArgs e)
    {
        bool enable = ChkFastPlaybackMute?.IsChecked == true;
        _audioSettings.EnablePlaybackMuteFeature = enable;
        _audioSettings.Save();
        _audioHook?.UpdateHookState();
        if (ChkEnablePlaybackMute != null) ChkEnablePlaybackMute.IsChecked = enable;
    }

    private void FastPlaybackCycle_Click(object sender, RoutedEventArgs e)
    {
        bool enable = ChkFastPlaybackCycle?.IsChecked == true;
        _audioSettings.EnablePlaybackCycleFeature = enable;
        _audioSettings.Save();
        _audioHook?.UpdateHookState();
        if (CboFastAudioOutput != null) CboFastAudioOutput.IsEnabled = enable;
        if (ChkEnablePlaybackCycle != null) ChkEnablePlaybackCycle.IsChecked = enable;
    }

    private void FastRecordingCycle_Click(object sender, RoutedEventArgs e)
    {
        bool enable = ChkFastRecordingCycle?.IsChecked == true;
        _audioSettings.EnableRecordingCycleFeature = enable;
        _audioSettings.Save();
        _audioHook?.UpdateHookState();
        if (CboFastAudioInput != null) CboFastAudioInput.IsEnabled = enable;
        if (ChkEnableRecordingCycle != null) ChkEnableRecordingCycle.IsChecked = enable;
    }

    private void FastMicMute_Click(object sender, RoutedEventArgs e)
    {
        bool enable = ChkFastMicMute?.IsChecked == true;
        _audioSettings.EnableMicMuteFeature = enable;
        _audioSettings.Save();
        _audioHook?.RebuildActiveKeys();

        if (_micOverlay != null)
        {
            if (enable && _audioSettings.OverlayEnabled)
            {
                _micOverlay.Show();
                bool micMuted = AudioCore.GetDefaultMute(AudioInterop.EDataFlow.Capture);
                _micOverlay.UpdateState(micMuted);
            }
            else
            {
                _micOverlay.Hide();
            }
        }
    }

    private void FastMouseWheelVolume_Click(object sender, RoutedEventArgs e)
    {
        bool enable = ChkFastMouseWheelVolume?.IsChecked == true;
        _audioSettings.EnableTaskbarVolumeScroll = enable;
        _audioSettings.Save();
        if (enable) _taskbarVolumeHook?.StartHook();
        else _taskbarVolumeHook?.StopHook();
        if (ChkEnableTaskbarVolume != null) ChkEnableTaskbarVolume.IsChecked = enable;
    }

    private void FastNoiseSuppressor_Click(object sender, RoutedEventArgs e)
    {
        var svc = MicrophoneSuppressorService.Instance;
        bool enable = ChkFastNoiseSuppressor?.IsChecked == true;
        if (enable != svc.MasterEnabled)
        {
            svc.MasterEnabled = enable;
            _settings.SuppressorMasterEnabled = enable;
            _settings.Save();
            if (enable) svc.Start();
            else svc.Stop();
            UpdateSuppressorMasterUi(enable);
        }
    }

    private void FastOverlay_Click(object sender, RoutedEventArgs e)
    {
        bool enable = ChkFastOverlay?.IsChecked == true;
        
        if (ChkOverlay != null)
        {
            ChkOverlay.IsChecked = enable;
        }
        else
        {
            ChkOverlay_Changed(sender, e);
        }
    }

    private void CboFastAudioOutput_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isSettingsLoading) return;
        if (CboFastAudioOutput?.SelectedItem is AudioDeviceInfo dev)
        {
            AudioCore.SetDefaultDevice(dev.Id);
            RefreshAudioUi();
        }
    }

    private void CboFastAudioInput_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isSettingsLoading) return;
        if (CboFastAudioInput?.SelectedItem is AudioDeviceInfo dev)
        {
            AudioCore.SetDefaultDevice(dev.Id);
            RefreshAudioUi();
        }
    }

    private void InitTweaksPage()
    {
        InitSettingsPage();
        _isSettingsLoading = true;
        try
        {
            if (ChkTweaksMiniTask != null)
                ChkTweaksMiniTask.IsChecked = _settings.MiniTaskVisible;
            if (ChkTweaksDisableOfficeHotkeys != null)
                ChkTweaksDisableOfficeHotkeys.IsChecked = WindowsDebloatService.AreOfficeHotkeysDisabled();
            if (TxtTweaksOfficeHotkeysStatus != null)
            {
                bool disabled = WindowsDebloatService.AreOfficeHotkeysDisabled();
                TxtTweaksOfficeHotkeysStatus.Text = disabled
                    ? "Ativo: atalhos Ctrl+Shift+Alt+Win bloqueados no Windows."
                    : "Padrão do Windows (atalhos do Office habilitados).";
            }
        }
        finally
        {
            _isSettingsLoading = false;
        }
    }

    private void TweaksMiniTask_Click(object sender, RoutedEventArgs e)
    {
        if (_isSettingsLoading) return;
        bool enable = ChkTweaksMiniTask?.IsChecked == true;
        if (ChkMiniTaskOverlay != null)
        {
            ChkMiniTaskOverlay.IsChecked = enable;
        }
        else
        {
            _settings.MiniTaskVisible = enable;
            _settings.Save();
        }
    }

    private void TweaksDisableOfficeHotkeys_Click(object sender, RoutedEventArgs e)
    {
        if (_isSettingsLoading) return;
        bool disable = ChkTweaksDisableOfficeHotkeys?.IsChecked == true;
        WindowsDebloatService.SetOfficeHotkeysDisabled(disable);
        if (TxtTweaksOfficeHotkeysStatus != null)
        {
            TxtTweaksOfficeHotkeysStatus.Text = disable
                ? "Ativo: atalhos Ctrl+Shift+Alt+Win bloqueados no Windows."
                : "Padrão do Windows (atalhos do Office habilitados).";
        }
    }

    private void TweaksOpenAudio_Click(object sender, RoutedEventArgs e) => ShowPage("PageAudio");
    private void TweaksOpenSuppressor_Click(object sender, RoutedEventArgs e)
    {
        if (NavSuppressor != null) NavSuppressor.IsChecked = true;
        else ShowPage("PageSuppressor");
    }
    private void TweaksOpenOverlay_Click(object sender, RoutedEventArgs e)
    {
        if (NavOverlay != null) NavOverlay.IsChecked = true;
        else ShowPage("PageOverlay");
    }
    private void TweaksOpenWallpapers_Click(object sender, RoutedEventArgs e) => ShowPage("PageWallpapers");
    private void TweaksOpenDebloat_Click(object sender, RoutedEventArgs e)
    {
        if (NavWindowsDebloat != null) NavWindowsDebloat.IsChecked = true;
        else ShowPage("PageWindowsDebloat");
    }
}
