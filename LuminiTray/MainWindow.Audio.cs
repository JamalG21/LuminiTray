using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace LuminiTray;

public partial class MainWindow
{
    private AudioSettings _audioSettings = null!;
    private AudioHotkeyHook? _audioHook;
    private MicOverlayWindow? _micOverlay;
    private SystemTrayManager? _trayManager;
    private DispatcherTimer? _audioTimer;
    private TaskbarVolumeHook? _taskbarVolumeHook;
    private TaskbarVolumeOsdWindow? _taskbarVolumeOsd;
    private bool _isUpdatingAudioUi = false;

    private void InitAudioModule()
    {
        _audioSettings = AudioSettings.Load();
        AudioCore.FeedbackVolume = _audioSettings.SoundFeedbackVolume;
        AudioCore.StopConflictingExternalAudioTools();
        AudioCore.DesiredMicMuteState = AudioCore.GetDefaultMute(AudioInterop.EDataFlow.Capture);
        if (_audioSettings.SoundFeedback)
        {
            Task.Run(() => KeySoundEngine.Initialize());
        }

        try
        {
            _audioHook = new AudioHotkeyHook(_audioSettings)
            {
                OnPlaybackCycle = () => Dispatcher.BeginInvoke(AudioCyclePlayback),
                OnRecordingCycle = () => Dispatcher.BeginInvoke(AudioCycleRecording),
                OnPlaybackMute = () => Dispatcher.BeginInvoke(AudioTogglePlaybackMute),
                OnMicMuteKeyDown = () => Dispatcher.BeginInvoke(AudioOnMicMuteKeyDown),
                OnMicMuteKeyUp = (elapsed) => Dispatcher.BeginInvoke(() => AudioOnMicMuteKeyUp(elapsed)),
                OnMicUnmuteKeyDown = () => Dispatcher.BeginInvoke(AudioOnMicUnmuteKeyDown),
                OnMicUnmuteKeyUp = (elapsed) => Dispatcher.BeginInvoke(() => AudioOnMicUnmuteKeyUp(elapsed))
            };
        }
        catch
        {
            // Fallback se hook não puder ser registrado
        }

        try
        {
            _taskbarVolumeOsd = new TaskbarVolumeOsdWindow();
            _taskbarVolumeHook = new TaskbarVolumeHook(_audioSettings, (title, vol, x, y) =>
            {
                Dispatcher.BeginInvoke(() => _taskbarVolumeOsd?.ShowVolume(title, vol, x, y));
            });
        }
        catch
        {
            // Fallback se hook de mouse não puder ser registrado
        }

        try
        {
            _micOverlay = new MicOverlayWindow(_audioSettings, () => Dispatcher.Invoke(AudioToggleMicMute));
            if (_audioSettings.OverlayEnabled)
            {
                _micOverlay.Show();
                bool micMuted = AudioCore.GetDefaultMute(AudioInterop.EDataFlow.Capture);
                _micOverlay.UpdateState(micMuted);
            }
        }
        catch
        {
            // Silencioso se overlay falhar
        }

        try
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            _trayManager = new SystemTrayManager
            {
                ShowMicIcon = _settings.ShowMicTrayIcon,
                ShowAudioIcon = _settings.ShowAudioTrayIcon,
                ShowMiniTaskIcon = _settings.ShowMiniTaskTrayIcon
            };
            _trayManager.Initialize(
                hwnd,
                () => Dispatcher.Invoke(AudioToggleMicMute),
                () => Dispatcher.Invoke(AudioCyclePlayback),
                () => Dispatcher.Invoke(ShowAndRestoreWindow),
                () => Dispatcher.BeginInvoke(ExitApplication),
                () => AudioCore.GetDevices(AudioInterop.EDataFlow.Render, _audioSettings.PlaybackCycleDeviceIds, _audioSettings.DisabledPlaybackDeviceIds).Where(d => d.IsEnabled).ToList(),
                (id) => Dispatcher.Invoke(() => SelectPlaybackDeviceById(id)));

            MiniTaskManagerService.Instance.OnMetricsUpdated += (snap) =>
            {
                _trayManager?.UpdateMiniTaskTooltip(snap.SummaryTooltip);
            };

            LivelyWallpaperService.Instance.Initialize(_settings, _trayManager);
        }
        catch
        {
            // Silencioso se bandeja falhar
        }

        // Timer para detecção de AFK (inatividade) e Volume Lock a cada 1 segundo
        _audioTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _audioTimer.Tick += AudioTimer_Tick;

        // Configuração inicial da UI de Áudio
        InitAudioControls();
        UpdateAudioTimerLifecycle();
    }

    private void UpdateAudioTimerLifecycle()
    {
        bool needsTimer = (_audioSettings.EnableMicMuteFeature && (AudioCore.DesiredMicMuteState.HasValue || _audioSettings.AfkTimeoutMinutes > 0)) ||
                          _audioSettings.VolumeLockEnabled;

        if (needsTimer)
        {
            if (_audioTimer != null && !_audioTimer.IsEnabled) _audioTimer.Start();
        }
        else
        {
            if (_audioTimer != null && _audioTimer.IsEnabled) _audioTimer.Stop();
        }
    }

    private void ShowAndRestoreWindow()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
        Focus();
        UpdateAutoOptStatusDisplay();
        UpdateRamStatus();
        if (_timer != null)
        {
            _timer.Interval = TimeSpan.FromSeconds(1);
            if (!_timer.IsEnabled) _timer.Start();
        }
        UpdateSensorPollingState();
    }

    private void ExitApplication()
    {
        PerformFullExit();
    }

    private void SelectPlaybackDeviceById(string devId)
    {
        if (AudioCore.SetDefaultDevice(devId, _audioSettings.ChangeCommunications, _audioSettings.KeepVolumeEnabled))
        {
            RefreshAudioUi();
            if (_audioSettings.SoundFeedback) AudioCore.PlayFeedback(AudioFeedbackType.DeviceSwitched);
            Toast("Saída de Áudio", "Dispositivo de reprodução alterado.", "\uE767");
        }
    }

    private void AudioTimer_Tick(object? sender, EventArgs e)
    {
        // 0. Garante o estado de mudo desejado (force_current_state=1 do AHK_MicMute) apenas se houver alvo definido
        if (AudioCore.DesiredMicMuteState.HasValue)
        {
            Task.Run(() =>
            {
                try
                {
                    if (AudioCore.EnforceDesiredMicMuteState())
                    {
                        Dispatcher.BeginInvoke(RefreshAudioUi);
                    }
                }
                catch { }
            });
        }

        // 1. Verificação de AFK Timeout (Auto-mute por inatividade)
        if (_audioSettings.AfkTimeoutMinutes > 0)
        {
            uint idleMs = AudioHotkeyHook.GetIdleTimeMs();
            uint thresholdMs = (uint)(_audioSettings.AfkTimeoutMinutes * 60 * 1000);
            if (idleMs >= thresholdMs)
            {
                bool isMuted = AudioCore.GetDefaultMute(AudioInterop.EDataFlow.Capture);
                if (!isMuted)
                {
                    AudioCore.SetDefaultMute(AudioInterop.EDataFlow.Capture, true);
                    AudioCore.SetAllCaptureMute(true);
                    _micOverlay?.UpdateState(true);
                    _trayManager?.UpdateMic(true, null);
                    if (_audioSettings.SoundFeedback) AudioCore.PlayFeedback(AudioFeedbackType.MicMuted);
                    if (_audioSettings.OsdFeedback)
                    {
                        Toast("Áudio - AFK", $"Microfone mutado por inatividade ({_audioSettings.AfkTimeoutMinutes} min).", "\uE74F");
                    }
                    RefreshAudioUi();
                }
            }
        }

        // 2. Verificação de Volume Lock no Microfone
        if (_audioSettings.VolumeLockEnabled)
        {
            float targetScalar = Math.Clamp(_audioSettings.VolumeLockPercent / 100f, 0f, 1f);
            float currentScalar = AudioCore.GetDefaultVolumeScalar(AudioInterop.EDataFlow.Capture);
            if (Math.Abs(currentScalar - targetScalar) > 0.02f)
            {
                AudioCore.SetDefaultVolumeScalar(AudioInterop.EDataFlow.Capture, targetScalar);
            }
        }
    }

    private void AudioCyclePlayback()
    {
        if (!_audioSettings.EnablePlaybackCycleFeature) return;

        var nextDev = AudioCore.CycleNextDevice(
            AudioInterop.EDataFlow.Render,
            _audioSettings.PlaybackCycleDeviceIds,
            _audioSettings.DisabledPlaybackDeviceIds,
            _audioSettings.ChangeCommunications,
            _audioSettings.KeepVolumeEnabled);

        if (_audioSettings.SoundFeedback)
        {
            AudioCore.PlayFeedback(AudioFeedbackType.DeviceSwitched);
        }

        if (_audioSettings.OsdFeedback)
        {
            string name = nextDev?.Name ?? "Dispositivo de Som";
            Toast("Saída de Áudio", $"Ativado: {name}", "\uE767");
        }

        RefreshAudioUi();
    }

    private void AudioCycleRecording()
    {
        if (!_audioSettings.EnableRecordingCycleFeature) return;

        var nextDev = AudioCore.CycleNextDevice(
            AudioInterop.EDataFlow.Capture,
            _audioSettings.RecordingCycleDeviceIds,
            _audioSettings.DisabledRecordingDeviceIds,
            _audioSettings.ChangeCommunications,
            _audioSettings.KeepVolumeEnabled);

        if (_audioSettings.SoundFeedback)
        {
            AudioCore.PlayFeedback(AudioFeedbackType.DeviceSwitched);
        }

        if (_audioSettings.OsdFeedback)
        {
            string name = nextDev?.Name ?? "Microfone";
            Toast("Microfone Padrão", $"Ativado: {name}", "\uE720");
        }

        bool isMuted = AudioCore.GetDefaultMute(AudioInterop.EDataFlow.Capture);
        _micOverlay?.UpdateState(isMuted, nextDev?.Name);
        RefreshAudioUi();
    }

    private void AudioTogglePlaybackMute()
    {
        if (!_audioSettings.EnablePlaybackMuteFeature) return;

        bool isMuted = AudioCore.ToggleDefaultMute(AudioInterop.EDataFlow.Render);

        if (_audioSettings.SoundFeedback)
        {
            AudioCore.PlayFeedback(isMuted ? AudioFeedbackType.PlaybackMuted : AudioFeedbackType.PlaybackUnmuted);
        }

        if (_audioSettings.OsdFeedback)
        {
            Toast(
                "Saída de Áudio",
                isMuted ? "Som Silenciado (Mutado)" : "Som Reativado",
                isMuted ? "\uE74F" : "\uE767");
        }

        RefreshAudioUi();
    }

    private void AudioToggleMicMute()
    {
        if (!_audioSettings.EnableMicMuteFeature) return;

        bool isMuted = AudioCore.ToggleDefaultMute(AudioInterop.EDataFlow.Capture);

        if (_audioSettings.SoundFeedback)
        {
            AudioCore.PlayFeedback(isMuted ? AudioFeedbackType.MicMuted : AudioFeedbackType.MicUnmuted);
        }

        if (_audioSettings.OsdFeedback)
        {
            Toast(
                "Microfone",
                isMuted ? "Microfone Silenciado (Mutado)" : "Microfone Aberto (Ativado)",
                isMuted ? "\uE74F" : "\uE720");
        }

        _micOverlay?.UpdateState(isMuted);
        RefreshAudioUi();
    }

    private void AudioOnMicMuteKeyDown()
    {
        if (!_audioSettings.EnableMicMuteFeature) return;

        switch (_audioSettings.MicMode)
        {
            case AudioMicMode.Toggle:
                AudioToggleMicMute();
                break;

            case AudioMicMode.PushToTalk:
            case AudioMicMode.HybridPtt:
                // Desmuta imediatamente ao pressionar
                AudioCore.SetDefaultMute(AudioInterop.EDataFlow.Capture, false);
                AudioCore.SetAllCaptureMute(false);
                if (_audioSettings.SoundFeedback) AudioCore.PlayFeedback(AudioFeedbackType.PttOn);
                _micOverlay?.UpdateState(false);
                RefreshAudioUi();
                break;

            case AudioMicMode.PushToMute:
                // Muta imediatamente ao pressionar
                AudioCore.SetDefaultMute(AudioInterop.EDataFlow.Capture, true);
                AudioCore.SetAllCaptureMute(true);
                if (_audioSettings.SoundFeedback) AudioCore.PlayFeedback(AudioFeedbackType.MicMuted);
                _micOverlay?.UpdateState(true);
                RefreshAudioUi();
                break;

            case AudioMicMode.SeparateHotkeys:
                // Ação dedicada de mutar
                AudioCore.SetDefaultMute(AudioInterop.EDataFlow.Capture, true);
                AudioCore.SetAllCaptureMute(true);
                if (_audioSettings.SoundFeedback) AudioCore.PlayFeedback(AudioFeedbackType.MicMuted);
                if (_audioSettings.OsdFeedback) Toast("Microfone", "Microfone Silenciado (Mutado)", "\uE74F");
                _micOverlay?.UpdateState(true);
                RefreshAudioUi();
                break;
        }
    }

    private void AudioOnMicMuteKeyUp(long elapsedMs)
    {
        if (!_audioSettings.EnableMicMuteFeature) return;

        switch (_audioSettings.MicMode)
        {
            case AudioMicMode.PushToTalk:
                // Aguarda PttDelayMs e então muta
                int delay = Math.Max(0, _audioSettings.PttDelayMs);
                Task.Delay(delay).ContinueWith(_ =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        AudioCore.SetDefaultMute(AudioInterop.EDataFlow.Capture, true);
                        AudioCore.SetAllCaptureMute(true);
                        if (_audioSettings.SoundFeedback) AudioCore.PlayFeedback(AudioFeedbackType.PttOff);
                        _micOverlay?.UpdateState(true);
                        RefreshAudioUi();
                    });
                });
                break;

            case AudioMicMode.PushToMute:
                // Aguarda PttDelayMs e então reabre
                int ptmDelay = Math.Max(0, _audioSettings.PttDelayMs);
                Task.Delay(ptmDelay).ContinueWith(_ =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        AudioCore.SetDefaultMute(AudioInterop.EDataFlow.Capture, false);
                        AudioCore.SetAllCaptureMute(false);
                        if (_audioSettings.SoundFeedback) AudioCore.PlayFeedback(AudioFeedbackType.MicUnmuted);
                        _micOverlay?.UpdateState(false);
                        RefreshAudioUi();
                    });
                });
                break;

            case AudioMicMode.HybridPtt:
                // Se pressionamento durou menos de 200ms, trata como toggle (permanece aberto!)
                if (elapsedMs < 200)
                {
                    // Ficou aberto como toggle
                }
                else
                {
                    // Mantido pressionado -> comporta-se como PTT: muta após delay
                    int hybridDelay = Math.Max(0, _audioSettings.PttDelayMs);
                    Task.Delay(hybridDelay).ContinueWith(_ =>
                    {
                        Dispatcher.Invoke(() =>
                        {
                            AudioCore.SetDefaultMute(AudioInterop.EDataFlow.Capture, true);
                            AudioCore.SetAllCaptureMute(true);
                            if (_audioSettings.SoundFeedback) AudioCore.PlayFeedback(AudioFeedbackType.PttOff);
                            _micOverlay?.UpdateState(true);
                            RefreshAudioUi();
                        });
                    });
                }
                break;
        }
    }

    private void AudioOnMicUnmuteKeyDown()
    {
        if (_audioSettings.MicMode == AudioMicMode.SeparateHotkeys)
        {
            AudioCore.SetDefaultMute(AudioInterop.EDataFlow.Capture, false);
            AudioCore.SetAllCaptureMute(false);
            if (_audioSettings.SoundFeedback) AudioCore.PlayFeedback(AudioFeedbackType.MicUnmuted);
            if (_audioSettings.OsdFeedback) Toast("Microfone", "Microfone Aberto", "\uE720");
            _micOverlay?.UpdateState(false);
            RefreshAudioUi();
        }
    }

    private void AudioOnMicUnmuteKeyUp(long elapsedMs)
    {
        // Sem ação adicional necessária no KeyUp de desmutar separado
    }

    private void InitAudioControls()
    {
        if (ChkChangeComms != null) ChkChangeComms.IsChecked = _audioSettings.ChangeCommunications;
        if (ChkKeepVolume != null) ChkKeepVolume.IsChecked = _audioSettings.KeepVolumeEnabled;
        if (ChkSoundFeedback != null) ChkSoundFeedback.IsChecked = _audioSettings.SoundFeedback;
        AudioCore.FeedbackVolume = _audioSettings.SoundFeedbackVolume;
        if (SldSoundFeedbackVolume != null) SldSoundFeedbackVolume.Value = _audioSettings.SoundFeedbackVolume;
        if (TxtSoundFeedbackVolume != null) TxtSoundFeedbackVolume.Text = $"{_audioSettings.SoundFeedbackVolume}%";
        if (ChkOsdFeedback != null) ChkOsdFeedback.IsChecked = _audioSettings.OsdFeedback;
        if (ChkPassthrough != null) ChkPassthrough.IsChecked = _audioSettings.Passthrough;
        if (ChkOverlayEnabled != null) ChkOverlayEnabled.IsChecked = _audioSettings.OverlayEnabled;
        if (ChkOverlayOnlyMuted != null) ChkOverlayOnlyMuted.IsChecked = _audioSettings.OverlayOnlyWhenMuted;
        if (ChkVolumeLock != null) ChkVolumeLock.IsChecked = _audioSettings.VolumeLockEnabled;
        if (SliderVolumeLock != null) SliderVolumeLock.Value = _audioSettings.VolumeLockPercent;
        if (TxtVolumeLockVal != null) TxtVolumeLockVal.Text = $"{_audioSettings.VolumeLockPercent}%";

        if (SliderPttDelay != null) SliderPttDelay.Value = _audioSettings.PttDelayMs;
        if (TxtPttDelayVal != null) TxtPttDelayVal.Text = $"{_audioSettings.PttDelayMs} ms";

        if (CmbMicMode != null) CmbMicMode.SelectedIndex = (int)_audioSettings.MicMode;
        if (ChkEnableGlobalHotkeys != null) ChkEnableGlobalHotkeys.IsChecked = _audioSettings.EnableGlobalHotkeys;
        if (ChkEnablePlaybackCycle != null) ChkEnablePlaybackCycle.IsChecked = _audioSettings.EnablePlaybackCycleFeature;
        if (ChkEnableRecordingCycle != null) ChkEnableRecordingCycle.IsChecked = _audioSettings.EnableRecordingCycleFeature;
        if (ChkEnablePlaybackMute != null) ChkEnablePlaybackMute.IsChecked = _audioSettings.EnablePlaybackMuteFeature;
        if (ChkEnableMicMute != null) ChkEnableMicMute.IsChecked = _audioSettings.EnableMicMuteFeature;
        if (ChkEnableTaskbarVolume != null) ChkEnableTaskbarVolume.IsChecked = _audioSettings.EnableTaskbarVolumeScroll;
        if (CmbTaskbarStep != null)
        {
            CmbTaskbarStep.SelectedIndex = _audioSettings.TaskbarVolumeScrollStep switch
            {
                1 => 0,
                2 => 1,
                5 => 2,
                10 => 3,
                _ => 1
            };
        }
        if (CmbTaskbarModifier != null)
        {
            CmbTaskbarModifier.SelectedIndex = _audioSettings.TaskbarPerAppModifierKey switch
            {
                "Ctrl Esquerdo" => 0,
                "Ctrl Direito" => 1,
                "Ctrl" => 2,
                "Shift" => 3,
                "Alt" => 4,
                "Win" => 5,
                "Nenhum" => 6,
                _ => 0
            };
        }
        if (CmbTaskbarTargetMode != null)
        {
            CmbTaskbarTargetMode.SelectedIndex = _audioSettings.TaskbarPerAppTargetMode switch
            {
                "Janela Ativa em Primeiro Plano" => 1,
                "App Específico Configurado" => 2,
                _ => 0
            };
        }
        if (TxtTaskbarSpecificApp != null)
        {
            TxtTaskbarSpecificApp.Text = _audioSettings.TaskbarSpecificAppName;
        }
        if (PanelTaskbarSpecificApp != null)
        {
            PanelTaskbarSpecificApp.Visibility = _audioSettings.TaskbarPerAppTargetMode == "App Específico Configurado"
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
        if (ChkInvertTaskbarModifier != null) ChkInvertTaskbarModifier.IsChecked = _audioSettings.InvertTaskbarModifier;
        if (ChkMiddleClickToMute != null) ChkMiddleClickToMute.IsChecked = _audioSettings.MiddleClickToMute;
        if (ChkScrollAnywhere != null) ChkScrollAnywhere.IsChecked = _audioSettings.ScrollAnywhereWithModifier;
        if (ChkDisableInFullscreen != null) ChkDisableInFullscreen.IsChecked = _audioSettings.DisableTaskbarVolumeInFullscreen;
        if (CmbAfkTimeout != null)
        {
            int idx = _audioSettings.AfkTimeoutMinutes switch
            {
                1 => 1,
                2 => 2,
                5 => 3,
                10 => 4,
                15 => 5,
                30 => 6,
                _ => 0
            };
            CmbAfkTimeout.SelectedIndex = idx;
        }

        UpdateHotkeyButtonsLabels();
        InitExcludedAppsUi();
        RefreshAudioUi();
    }

    private void UpdateHotkeyButtonsLabels()
    {
        if (BtnHotkeyPlaybackCycle != null) BtnHotkeyPlaybackCycle.Content = _audioSettings.HotkeyPlaybackCycle.ToString();
        if (BtnHotkeyRecordingCycle != null) BtnHotkeyRecordingCycle.Content = _audioSettings.HotkeyRecordingCycle.ToString();
        if (BtnHotkeyPlaybackMute != null) BtnHotkeyPlaybackMute.Content = _audioSettings.HotkeyPlaybackMute.ToString();
        if (BtnHotkeyMicMute != null) BtnHotkeyMicMute.Content = _audioSettings.HotkeyMicMute.ToString();
        if (BtnHotkeyMicUnmute != null) BtnHotkeyMicUnmute.Content = _audioSettings.HotkeyMicUnmute.ToString();

        if (RowMicUnmuteHotkey != null)
        {
            RowMicUnmuteHotkey.Visibility = _audioSettings.MicMode == AudioMicMode.SeparateHotkeys
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
    }

    public void RefreshAudioUi()
    {
        if (_isUpdatingAudioUi) return;
        _isUpdatingAudioUi = true;

        try
        {
            // 1. Saídas de Reprodução (Playback)
            var playbackDevices = AudioCore.GetDevices(
                AudioInterop.EDataFlow.Render,
                _audioSettings.PlaybackCycleDeviceIds,
                _audioSettings.DisabledPlaybackDeviceIds);
            if (ListPlaybackDevices != null)
            {
                ListPlaybackDevices.ItemsSource = null;
                ListPlaybackDevices.ItemsSource = playbackDevices;
            }

            // 2. Microfones (Recording)
            var recordingDevices = AudioCore.GetDevices(
                AudioInterop.EDataFlow.Capture,
                _audioSettings.RecordingCycleDeviceIds,
                _audioSettings.DisabledRecordingDeviceIds);
            if (ListRecordingDevices != null)
            {
                ListRecordingDevices.ItemsSource = null;
                ListRecordingDevices.ItemsSource = recordingDevices;
            }

            // 3. Atualizar Overlay e Status do Microfone Principal
            bool micMuted = AudioCore.GetDefaultMute(AudioInterop.EDataFlow.Capture);
            var currentMic = recordingDevices.FirstOrDefault(d => d.IsDefaultConsole && d.IsEnabled) 
                             ?? recordingDevices.FirstOrDefault(d => d.IsEnabled)
                             ?? recordingDevices.FirstOrDefault();
            var currentSpeaker = playbackDevices.FirstOrDefault(d => d.IsDefaultConsole && d.IsEnabled) 
                                 ?? playbackDevices.FirstOrDefault(d => d.IsEnabled)
                                 ?? playbackDevices.FirstOrDefault();

            int outputNumber = currentSpeaker?.OutputNumber ?? 1;
            if (outputNumber <= 0)
            {
                int allIdx = playbackDevices.Where(d => d.IsEnabled).ToList().FindIndex(d => string.Equals(d.Id, currentSpeaker?.Id, StringComparison.OrdinalIgnoreCase));
                outputNumber = allIdx >= 0 ? allIdx + 1 : 1;
            }

            _micOverlay?.UpdateState(micMuted, currentMic?.Name);
            _trayManager?.UpdateMic(micMuted, currentMic?.Name);
            _trayManager?.UpdateAudioOutput(currentSpeaker?.Name, currentSpeaker?.VolumePercent ?? 100, outputNumber);

            // Card Mestre do Microfone
            if (TxtMasterMicStatus != null)
            {
                TxtMasterMicStatus.Text = micMuted ? "MICROFONE SILENCIADO (MUTADO)" : "MICROFONE ABERTO (TRANSMITINDO)";
                TxtMasterMicStatus.Foreground = micMuted
                    ? new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44))
                    : new SolidColorBrush(Color.FromRgb(0x10, 0xB9, 0x81));
            }
            if (TxtMasterMicName != null)
            {
                TxtMasterMicName.Text = currentMic != null ? $"Dispositivo ativo: {currentMic.Name}" : "Nenhum microfone padrão";
            }
            if (IndicatorMasterMic != null)
            {
                IndicatorMasterMic.Background = micMuted
                    ? new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44))
                    : new SolidColorBrush(Color.FromRgb(0x10, 0xB9, 0x81));
            }
            if (IconMasterMic != null)
            {
                IconMasterMic.Text = micMuted ? "\uE74F" : "\uE720";
            }
            if (BtnMasterToggleMic != null)
            {
                BtnMasterToggleMic.Content = micMuted ? "DESMUTAR AGORA" : "MUTAR AGORA";
                BtnMasterToggleMic.Style = micMuted
                    ? (Style)FindResource("PrimaryButton")
                    : (Style)FindResource("OutlineButton");
            }
        }
        catch
        {
            // Silencioso em caso de transição de dispositivo
        }
        finally
        {
            _isUpdatingAudioUi = false;
        }
    }

    private void BtnMasterToggleMic_Click(object sender, RoutedEventArgs e)
    {
        AudioToggleMicMute();
    }

    private void BtnAudioRefresh_Click(object sender, RoutedEventArgs e)
    {
        RefreshAudioUi();
        Toast("Dispositivos de Áudio", "Lista de dispositivos atualizada com sucesso.", "\uE767");
    }

    private void PlaybackCycleCheckbox_Click(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox cb && cb.Tag is string devId)
        {
            if (cb.IsChecked == true)
            {
                _audioSettings.DisabledPlaybackDeviceIds.RemoveAll(id => string.Equals(id, devId, StringComparison.OrdinalIgnoreCase));
                if (!_audioSettings.PlaybackCycleDeviceIds.Contains(devId, StringComparer.OrdinalIgnoreCase))
                {
                    _audioSettings.PlaybackCycleDeviceIds.Add(devId);
                }
            }
            else
            {
                _audioSettings.PlaybackCycleDeviceIds.RemoveAll(id => string.Equals(id, devId, StringComparison.OrdinalIgnoreCase));
            }
            _audioSettings.Save();
            RefreshAudioUi();
        }
    }

    private void RecordingCycleCheckbox_Click(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox cb && cb.Tag is string devId)
        {
            if (cb.IsChecked == true)
            {
                _audioSettings.DisabledRecordingDeviceIds.RemoveAll(id => string.Equals(id, devId, StringComparison.OrdinalIgnoreCase));
                if (!_audioSettings.RecordingCycleDeviceIds.Contains(devId, StringComparer.OrdinalIgnoreCase))
                {
                    _audioSettings.RecordingCycleDeviceIds.Add(devId);
                }
            }
            else
            {
                _audioSettings.RecordingCycleDeviceIds.RemoveAll(id => string.Equals(id, devId, StringComparison.OrdinalIgnoreCase));
            }
            _audioSettings.Save();
            RefreshAudioUi();
        }
    }

    private void BtnSetDefaultPlayback_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string devId)
        {
            if (AudioCore.SetDefaultDevice(devId, _audioSettings.ChangeCommunications, _audioSettings.KeepVolumeEnabled))
            {
                RefreshAudioUi();
                if (_audioSettings.SoundFeedback) AudioCore.PlayFeedback(AudioFeedbackType.DeviceSwitched);
                Toast("Saída de Áudio", "Dispositivo padrão definido.", "\uE767");
            }
        }
    }

    private void BtnSetDefaultRecording_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string devId)
        {
            if (AudioCore.SetDefaultDevice(devId, _audioSettings.ChangeCommunications, _audioSettings.KeepVolumeEnabled))
            {
                RefreshAudioUi();
                if (_audioSettings.SoundFeedback) AudioCore.PlayFeedback(AudioFeedbackType.DeviceSwitched);
                Toast("Microfone", "Microfone padrão definido.", "\uE720");
            }
        }
    }

    private void PlaybackVolume_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isUpdatingAudioUi) return;
        if (sender is Slider slider && slider.Tag is string devId)
        {
            if (!slider.IsLoaded || (!slider.IsMouseCaptureWithin && !slider.IsKeyboardFocusWithin)) return;
            float scalar = (float)(slider.Value / 100.0);
            AudioCore.SetDeviceVolumeScalar(devId, scalar);
        }
    }

    private void RecordingVolume_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isUpdatingAudioUi) return;
        if (sender is Slider slider && slider.Tag is string devId)
        {
            if (!slider.IsLoaded || (!slider.IsMouseCaptureWithin && !slider.IsKeyboardFocusWithin)) return;
            float scalar = (float)(slider.Value / 100.0);
            AudioCore.SetDeviceVolumeScalar(devId, scalar);
        }
    }

    private void BtnTogglePlaybackMute_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string devId)
        {
            var dev = AudioCore.GetDevices(AudioInterop.EDataFlow.Render).FirstOrDefault(d => d.Id == devId);
            if (dev != null)
            {
                bool nextMute = !dev.IsMuted;
                AudioCore.SetDeviceMute(devId, nextMute);
                RefreshAudioUi();
            }
        }
    }

    private void BtnToggleRecordingMute_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string devId)
        {
            AudioCore.StopConflictingExternalAudioTools();
            var dev = AudioCore.GetDevices(AudioInterop.EDataFlow.Capture).FirstOrDefault(d => d.Id == devId);
            if (dev != null)
            {
                bool nextMute = !dev.IsMuted;
                AudioCore.SetDeviceMute(devId, nextMute);
                if (dev.IsDefaultConsole || dev.IsDefaultCommunications)
                {
                    AudioCore.SetDefaultMute(AudioInterop.EDataFlow.Capture, nextMute);
                    AudioCore.SetAllCaptureMute(nextMute);
                }
                else
                {
                    AudioCore.DesiredMicMuteState = null;
                }
                bool currentMasterMute = AudioCore.GetDefaultMute(AudioInterop.EDataFlow.Capture);
                _trayManager?.UpdateMic(currentMasterMute, dev.Name);
                _micOverlay?.UpdateState(currentMasterMute, dev.Name);
                RefreshAudioUi();
                if (_audioSettings.SoundFeedback)
                {
                    AudioCore.PlayFeedback(nextMute ? AudioFeedbackType.MicMuted : AudioFeedbackType.MicUnmuted);
                }
                if (_audioSettings.OsdFeedback)
                {
                    Toast(
                        "Microfone",
                        nextMute ? "Microfone Silenciado (Mutado)" : "Microfone Aberto (Ativado)",
                        nextMute ? "\uE74F" : "\uE720");
                }
            }
        }
    }

    private void BtnTogglePlaybackEnable_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string devId)
        {
            var playbackDevices = AudioCore.GetDevices(
                AudioInterop.EDataFlow.Render,
                _audioSettings.PlaybackCycleDeviceIds,
                _audioSettings.DisabledPlaybackDeviceIds);

            var target = playbackDevices.FirstOrDefault(d => string.Equals(d.Id, devId, StringComparison.OrdinalIgnoreCase));
            if (target == null) return;

            bool willDisable = target.IsEnabled;

            if (willDisable)
            {
                // 1. Marca como desativado
                if (!_audioSettings.DisabledPlaybackDeviceIds.Contains(devId, StringComparer.OrdinalIgnoreCase))
                {
                    _audioSettings.DisabledPlaybackDeviceIds.Add(devId);
                }

                // 2. Remove da lista de rotação cíclica
                _audioSettings.PlaybackCycleDeviceIds.RemoveAll(id => string.Equals(id, devId, StringComparison.OrdinalIgnoreCase));

                // 3. Aplica visibilidade no Windows
                AudioCore.SetDeviceVisibility(devId, false);

                // 4. Se era o padrão, transfere para outro dispositivo ativo
                if (target.IsDefaultConsole)
                {
                    var fallback = playbackDevices.FirstOrDefault(d => !string.Equals(d.Id, devId, StringComparison.OrdinalIgnoreCase) && d.IsEnabled);
                    if (fallback != null)
                    {
                        AudioCore.SetDefaultDevice(fallback.Id, _audioSettings.ChangeCommunications, _audioSettings.KeepVolumeEnabled);
                    }
                }

                Toast("Saída de Áudio", $"{target.Name} foi desativado e removido da rotação.", "\uE767");
            }
            else
            {
                // 1. Remove da lista de desativados
                _audioSettings.DisabledPlaybackDeviceIds.RemoveAll(id => string.Equals(id, devId, StringComparison.OrdinalIgnoreCase));

                // 2. Restaura visibilidade no Windows
                AudioCore.SetDeviceVisibility(devId, true);

                Toast("Saída de Áudio", $"{target.Name} foi ativado com sucesso.", "\uE767");
            }

            _audioSettings.Save();
            RefreshAudioUi();
        }
    }

    private void BtnToggleRecordingEnable_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string devId)
        {
            var recordingDevices = AudioCore.GetDevices(
                AudioInterop.EDataFlow.Capture,
                _audioSettings.RecordingCycleDeviceIds,
                _audioSettings.DisabledRecordingDeviceIds);

            var target = recordingDevices.FirstOrDefault(d => string.Equals(d.Id, devId, StringComparison.OrdinalIgnoreCase));
            if (target == null) return;

            bool willDisable = target.IsEnabled;

            if (willDisable)
            {
                // 1. Marca como desativado
                if (!_audioSettings.DisabledRecordingDeviceIds.Contains(devId, StringComparer.OrdinalIgnoreCase))
                {
                    _audioSettings.DisabledRecordingDeviceIds.Add(devId);
                }

                // 2. Remove da rotação cíclica
                _audioSettings.RecordingCycleDeviceIds.RemoveAll(id => string.Equals(id, devId, StringComparison.OrdinalIgnoreCase));

                // 3. Aplica visibilidade no Windows
                AudioCore.SetDeviceVisibility(devId, false);

                // 4. Se era o microfone padrão, transfere para outro ativo
                if (target.IsDefaultConsole)
                {
                    var fallback = recordingDevices.FirstOrDefault(d => !string.Equals(d.Id, devId, StringComparison.OrdinalIgnoreCase) && d.IsEnabled);
                    if (fallback != null)
                    {
                        AudioCore.SetDefaultDevice(fallback.Id, _audioSettings.ChangeCommunications, _audioSettings.KeepVolumeEnabled);
                    }
                }

                Toast("Microfone", $"{target.Name} foi desativado e removido da rotação.", "\uE720");
            }
            else
            {
                // 1. Remove da lista de desativados
                _audioSettings.DisabledRecordingDeviceIds.RemoveAll(id => string.Equals(id, devId, StringComparison.OrdinalIgnoreCase));

                // 2. Restaura visibilidade no Windows
                AudioCore.SetDeviceVisibility(devId, true);

                Toast("Microfone", $"{target.Name} foi ativado com sucesso.", "\uE720");
            }

            _audioSettings.Save();
            RefreshAudioUi();
        }
    }

    private void AudioOption_Changed(object sender, RoutedEventArgs e)
    {
        if (_audioSettings == null) return;

        if (ChkChangeComms != null) _audioSettings.ChangeCommunications = ChkChangeComms.IsChecked == true;
        if (ChkKeepVolume != null) _audioSettings.KeepVolumeEnabled = ChkKeepVolume.IsChecked == true;
        if (ChkSoundFeedback != null) _audioSettings.SoundFeedback = ChkSoundFeedback.IsChecked == true;
        if (ChkOsdFeedback != null) _audioSettings.OsdFeedback = ChkOsdFeedback.IsChecked == true;
        if (ChkPassthrough != null) _audioSettings.Passthrough = ChkPassthrough.IsChecked == true;

        if (ChkOverlayEnabled != null)
        {
            _audioSettings.OverlayEnabled = ChkOverlayEnabled.IsChecked == true;
            if (_micOverlay != null)
            {
                if (_audioSettings.OverlayEnabled && _audioSettings.EnableMicMuteFeature)
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

        if (ChkOverlayOnlyMuted != null)
        {
            _audioSettings.OverlayOnlyWhenMuted = ChkOverlayOnlyMuted.IsChecked == true;
            bool micMuted = AudioCore.GetDefaultMute(AudioInterop.EDataFlow.Capture);
            _micOverlay?.UpdateState(micMuted);
        }

        if (ChkVolumeLock != null) _audioSettings.VolumeLockEnabled = ChkVolumeLock.IsChecked == true;

        if (ChkEnableGlobalHotkeys != null)
        {
            _audioSettings.EnableGlobalHotkeys = ChkEnableGlobalHotkeys.IsChecked == true;
            if (_audioSettings.EnableGlobalHotkeys)
            {
                _audioHook?.StartHook();
            }
            else
            {
                _audioHook?.StopHook();
            }
        }

        if (ChkEnableTaskbarVolume != null)
        {
            _audioSettings.EnableTaskbarVolumeScroll = ChkEnableTaskbarVolume.IsChecked == true;
            if (ChkFastMouseWheelVolume != null) ChkFastMouseWheelVolume.IsChecked = _audioSettings.EnableTaskbarVolumeScroll;
            if (_audioSettings.EnableTaskbarVolumeScroll)
            {
                _taskbarVolumeHook?.StartHook();
            }
            else
            {
                _taskbarVolumeHook?.StopHook();
            }
        }

        if (ChkEnablePlaybackCycle != null)
        {
            _audioSettings.EnablePlaybackCycleFeature = ChkEnablePlaybackCycle.IsChecked == true;
            if (ChkFastPlaybackCycle != null) ChkFastPlaybackCycle.IsChecked = _audioSettings.EnablePlaybackCycleFeature;
            if (CboFastAudioOutput != null) CboFastAudioOutput.IsEnabled = _audioSettings.EnablePlaybackCycleFeature;
        }

        if (ChkEnableRecordingCycle != null)
        {
            _audioSettings.EnableRecordingCycleFeature = ChkEnableRecordingCycle.IsChecked == true;
            if (ChkFastRecordingCycle != null) ChkFastRecordingCycle.IsChecked = _audioSettings.EnableRecordingCycleFeature;
            if (CboFastAudioInput != null) CboFastAudioInput.IsEnabled = _audioSettings.EnableRecordingCycleFeature;
        }

        if (ChkEnablePlaybackMute != null)
        {
            _audioSettings.EnablePlaybackMuteFeature = ChkEnablePlaybackMute.IsChecked == true;
            if (ChkFastPlaybackMute != null) ChkFastPlaybackMute.IsChecked = _audioSettings.EnablePlaybackMuteFeature;
        }

        if (ChkEnableMicMute != null)
        {
            _audioSettings.EnableMicMuteFeature = ChkEnableMicMute.IsChecked == true;
            if (ChkFastMicMute != null) ChkFastMicMute.IsChecked = _audioSettings.EnableMicMuteFeature;
        }

        _audioHook?.UpdateHookState();
        UpdateAudioTimerLifecycle();

        if (ChkInvertTaskbarModifier != null)
        {
            _audioSettings.InvertTaskbarModifier = ChkInvertTaskbarModifier.IsChecked == true;
        }

        if (ChkMiddleClickToMute != null)
        {
            _audioSettings.MiddleClickToMute = ChkMiddleClickToMute.IsChecked == true;
        }

        if (ChkScrollAnywhere != null)
        {
            _audioSettings.ScrollAnywhereWithModifier = ChkScrollAnywhere.IsChecked == true;
        }

        if (ChkDisableInFullscreen != null)
        {
            _audioSettings.DisableTaskbarVolumeInFullscreen = ChkDisableInFullscreen.IsChecked == true;
        }

        _audioSettings.Save();
    }

    private void CmbTaskbarStep_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingAudioUi || CmbTaskbarStep == null) return;
        _audioSettings.TaskbarVolumeScrollStep = CmbTaskbarStep.SelectedIndex switch
        {
            0 => 1,
            1 => 2,
            2 => 5,
            3 => 10,
            _ => 2
        };
        _audioSettings.Save();
    }

    private void CmbTaskbarModifier_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingAudioUi || CmbTaskbarModifier == null) return;
        _audioSettings.TaskbarPerAppModifierKey = CmbTaskbarModifier.SelectedIndex switch
        {
            0 => "Ctrl Esquerdo",
            1 => "Ctrl Direito",
            2 => "Ctrl",
            3 => "Shift",
            4 => "Alt",
            5 => "Win",
            6 => "Nenhum",
            _ => "Ctrl Esquerdo"
        };
        _audioSettings.Save();
    }

    private void CmbTaskbarTargetMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingAudioUi || CmbTaskbarTargetMode == null) return;
        _audioSettings.TaskbarPerAppTargetMode = CmbTaskbarTargetMode.SelectedIndex switch
        {
            1 => "Janela Ativa em Primeiro Plano",
            2 => "App Específico Configurado",
            _ => "Player Atual em Execução"
        };
        if (PanelTaskbarSpecificApp != null)
        {
            PanelTaskbarSpecificApp.Visibility = CmbTaskbarTargetMode.SelectedIndex == 2
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
        _audioSettings.Save();
    }

    private void TxtTaskbarSpecificApp_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isUpdatingAudioUi || TxtTaskbarSpecificApp == null) return;
        _audioSettings.TaskbarSpecificAppName = TxtTaskbarSpecificApp.Text.Trim();
        _audioSettings.Save();
    }

    private void SliderPttDelay_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_audioSettings == null) return;
        _audioSettings.PttDelayMs = (int)SliderPttDelay.Value;
        if (TxtPttDelayVal != null) TxtPttDelayVal.Text = $"{_audioSettings.PttDelayMs} ms";
        _audioSettings.Save();
    }

    private void SliderVolumeLock_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_audioSettings == null) return;
        _audioSettings.VolumeLockPercent = (int)SliderVolumeLock.Value;
        if (TxtVolumeLockVal != null) TxtVolumeLockVal.Text = $"{_audioSettings.VolumeLockPercent}%";
        _audioSettings.Save();
    }

    private void SldSoundFeedbackVolume_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_audioSettings == null || SldSoundFeedbackVolume == null) return;
        _audioSettings.SoundFeedbackVolume = (int)SldSoundFeedbackVolume.Value;
        AudioCore.FeedbackVolume = _audioSettings.SoundFeedbackVolume;
        if (TxtSoundFeedbackVolume != null) TxtSoundFeedbackVolume.Text = $"{_audioSettings.SoundFeedbackVolume}%";
        _audioSettings.Save();
    }

    private void BtnTestSoundFeedback_Click(object sender, RoutedEventArgs e)
    {
        AudioCore.PlayFeedback(AudioFeedbackType.DeviceSwitched, _audioSettings?.SoundFeedbackVolume ?? 50);
    }

    private void CmbMicMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_audioSettings == null || CmbMicMode == null) return;
        _audioSettings.MicMode = (AudioMicMode)CmbMicMode.SelectedIndex;
        _audioSettings.Save();
        UpdateHotkeyButtonsLabels();
    }

    private void CmbAfkTimeout_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_audioSettings == null || CmbAfkTimeout == null) return;
        _audioSettings.AfkTimeoutMinutes = CmbAfkTimeout.SelectedIndex switch
        {
            1 => 1,
            2 => 2,
            3 => 5,
            4 => 10,
            5 => 15,
            6 => 30,
            _ => 0
        };
        _audioSettings.Save();
    }

    private void RecordHotkeyGeneric(AudioHotkeyModel target, Button btn, string actionName)
    {
        if (_audioHook == null) return;

        btn.Content = "Pressione as teclas...";
        _audioHook.StartRecording(newHotkey =>
        {
            Dispatcher.Invoke(() =>
            {
                target.VirtualKey = newHotkey.VirtualKey;
                target.Ctrl = newHotkey.Ctrl;
                target.Alt = newHotkey.Alt;
                target.Shift = newHotkey.Shift;
                target.Win = newHotkey.Win;
                target.IsEnabled = true;

                btn.Content = target.ToString();
                _audioSettings.Save();
                Toast("Atalho Configurado", $"{actionName}: {target}", "\uE765");
            });
        });
    }

    private void BtnHotkeyPlaybackCycle_Click(object sender, RoutedEventArgs e)
    {
        RecordHotkeyGeneric(_audioSettings.HotkeyPlaybackCycle, BtnHotkeyPlaybackCycle, "Alternar Saída");
    }

    private void BtnHotkeyRecordingCycle_Click(object sender, RoutedEventArgs e)
    {
        RecordHotkeyGeneric(_audioSettings.HotkeyRecordingCycle, BtnHotkeyRecordingCycle, "Alternar Microfone");
    }

    private void BtnHotkeyPlaybackMute_Click(object sender, RoutedEventArgs e)
    {
        RecordHotkeyGeneric(_audioSettings.HotkeyPlaybackMute, BtnHotkeyPlaybackMute, "Mutar Saída");
    }

    private void BtnHotkeyMicMute_Click(object sender, RoutedEventArgs e)
    {
        RecordHotkeyGeneric(_audioSettings.HotkeyMicMute, BtnHotkeyMicMute, "Atalho do Microfone");
    }

    private void BtnHotkeyMicUnmute_Click(object sender, RoutedEventArgs e)
    {
        RecordHotkeyGeneric(_audioSettings.HotkeyMicUnmute, BtnHotkeyMicUnmute, "Desmutar Microfone");
    }

    private void BtnResetHotkeys_Click(object sender, RoutedEventArgs e)
    {
        _audioSettings.HotkeyPlaybackCycle = new(0x7A, ctrl: true, alt: true); // Ctrl + Alt + F11
        _audioSettings.HotkeyRecordingCycle = new(0x76, ctrl: true, alt: true); // Ctrl + Alt + F7
        _audioSettings.HotkeyPlaybackMute = new(0x4F, ctrl: true, alt: true);   // Ctrl + Alt + O
        _audioSettings.HotkeyMicMute = new(0x4D, ctrl: true, alt: true);        // Ctrl + Alt + M
        _audioSettings.HotkeyMicUnmute = new(0);

        _audioSettings.Save();
        UpdateHotkeyButtonsLabels();
        Toast("Atalhos Padrão", "Os atalhos de áudio foram restaurados para o padrão original.", "\uE765");
    }

    #region Excluded Player Apps UI

    private Brush GetAudioThemeBrush(string key, Brush fallback)
    {
        try
        {
            if (TryFindResource(key) is Brush b) return b;
        }
        catch { }
        return fallback;
    }

    private void InitExcludedAppsUi()
    {
        PopulateRunningAudioAppsCombo();
        RefreshExcludedAppsChips();
    }

    private void PopulateRunningAudioAppsCombo()
    {
        if (CmbRunningAudioApps == null) return;

        CmbRunningAudioApps.Items.Clear();
        CmbRunningAudioApps.Items.Add(new ComboBoxItem
        {
            Content = "-- Selecionar app com áudio ativo --",
            Tag = ""
        });

        try
        {
            var sessions = AudioCore.GetActiveAudioSessions();
            var addedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var session in sessions)
            {
                if (string.IsNullOrWhiteSpace(session.ProcessName)) continue;
                string procName = session.ProcessName.Trim();
                if (procName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    procName = procName[..^4];

                if (string.Equals(procName, "LuminiTray", StringComparison.OrdinalIgnoreCase)) continue;
                if (string.Equals(procName, "explorer", StringComparison.OrdinalIgnoreCase)) continue;

                if (!addedNames.Add(procName)) continue;

                string label = $"{procName}.exe";
                if (!string.IsNullOrWhiteSpace(session.DisplayName) && !session.DisplayName.Contains(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    label += $" ({session.DisplayName})";
                }

                CmbRunningAudioApps.Items.Add(new ComboBoxItem
                {
                    Content = label,
                    Tag = $"{procName}.exe"
                });
            }
        }
        catch { }

        CmbRunningAudioApps.SelectedIndex = 0;
    }

    private void RefreshExcludedAppsChips()
    {
        if (PanelExcludedAppChips == null) return;

        PanelExcludedAppChips.Children.Clear();

        var list = _audioSettings?.ExcludedPlayerApps;
        if (list == null || list.Count == 0)
        {
            if (TxtNoExcludedApps != null) TxtNoExcludedApps.Visibility = Visibility.Visible;
            return;
        }

        if (TxtNoExcludedApps != null) TxtNoExcludedApps.Visibility = Visibility.Collapsed;

        var cardBrush = GetAudioThemeBrush("Card", new SolidColorBrush(Color.FromRgb(14, 14, 14)));
        var lineStrongBrush = GetAudioThemeBrush("LineStrong", new SolidColorBrush(Color.FromRgb(58, 58, 58)));
        var accentBrush = GetAudioThemeBrush("Accent", new SolidColorBrush(Color.FromRgb(0, 210, 255)));
        var fgBrush = GetAudioThemeBrush("Fg", new SolidColorBrush(Color.FromRgb(245, 245, 245)));
        var mutedBrush = GetAudioThemeBrush("Muted", new SolidColorBrush(Color.FromRgb(140, 140, 140)));
        var iconsFont = (FontFamily)TryFindResource("Icons") ?? new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");

        foreach (var app in list)
        {
            if (string.IsNullOrWhiteSpace(app)) continue;

            var chip = new Border
            {
                Background = cardBrush,
                BorderBrush = lineStrongBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(10, 4, 8, 4),
                Margin = new Thickness(0, 0, 8, 8)
            };

            var sp = new StackPanel { Orientation = Orientation.Horizontal };

            var icon = new TextBlock
            {
                Text = "\uE711",
                FontFamily = iconsFont,
                FontSize = 10,
                Foreground = accentBrush,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0)
            };

            var txt = new TextBlock
            {
                Text = app,
                Foreground = fgBrush,
                FontSize = 11,
                FontWeight = FontWeights.Medium,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            };

            var btnRemove = new Button
            {
                Content = "✕",
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Foreground = mutedBrush,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                Padding = new Thickness(2),
                Tag = app,
                ToolTip = $"Remover {app} da lista de exclusão"
            };

            btnRemove.MouseEnter += (s, e) => btnRemove.Foreground = Brushes.Red;
            btnRemove.MouseLeave += (s, e) => btnRemove.Foreground = mutedBrush;

            string appToRemove = app;
            btnRemove.Click += (s, e) =>
            {
                if (_audioSettings?.ExcludedPlayerApps != null)
                {
                    _audioSettings.ExcludedPlayerApps.RemoveAll(a => string.Equals(a.Trim(), appToRemove.Trim(), StringComparison.OrdinalIgnoreCase));
                    _audioSettings.Save();
                    RefreshExcludedAppsChips();
                }
            };

            sp.Children.Add(icon);
            sp.Children.Add(txt);
            sp.Children.Add(btnRemove);
            chip.Child = sp;
            PanelExcludedAppChips.Children.Add(chip);
        }
    }

    private void BtnOpenAudioAppPicker_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new AppPickerWindow(_audioSettings?.ExcludedPlayerApps)
            {
                Owner = this
            };

            if (picker.ShowDialog() == true && picker.SelectedExecutables.Count > 0)
            {
                int addedCount = 0;
                if (_audioSettings != null)
                {
                    foreach (var exe in picker.SelectedExecutables)
                    {
                        bool alreadyExists = _audioSettings.ExcludedPlayerApps.Any(a =>
                        {
                            string clean1 = a.Trim();
                            if (clean1.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) clean1 = clean1[..^4];
                            string clean2 = exe.Trim();
                            if (clean2.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) clean2 = clean2[..^4];
                            return string.Equals(clean1, clean2, StringComparison.OrdinalIgnoreCase);
                        });

                        if (!alreadyExists)
                        {
                            _audioSettings.ExcludedPlayerApps.Add(exe);
                            addedCount++;
                        }
                    }

                    if (addedCount > 0)
                    {
                        _audioSettings.Save();
                        RefreshExcludedAppsChips();
                        Toast("Controle de Áudio", $"{addedCount} aplicativo(s) excluído(s) do controle de player.", "\uE767");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Não foi possível abrir o seletor de programas: {ex.Message}", "Lumini Tray", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void BtnRefreshAudioApps_Click(object sender, RoutedEventArgs e)
    {
        PopulateRunningAudioAppsCombo();
    }

    private void CmbRunningAudioApps_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CmbRunningAudioApps?.SelectedItem is ComboBoxItem item && item.Tag is string tag && !string.IsNullOrWhiteSpace(tag))
        {
            if (TxtAddExcludedApp != null)
            {
                TxtAddExcludedApp.Text = tag;
            }
        }
    }

    private void BtnAddExcludedApp_Click(object sender, RoutedEventArgs e)
    {
        if (TxtAddExcludedApp == null || _audioSettings == null) return;

        string app = TxtAddExcludedApp.Text.Trim();
        if (string.IsNullOrWhiteSpace(app))
        {
            if (CmbRunningAudioApps?.SelectedItem is ComboBoxItem item && item.Tag is string tag && !string.IsNullOrWhiteSpace(tag))
            {
                app = tag.Trim();
            }
        }

        if (string.IsNullOrWhiteSpace(app)) return;

        if (!app.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && !app.Contains('.'))
        {
            app += ".exe";
        }

        bool alreadyExists = _audioSettings.ExcludedPlayerApps.Any(a =>
        {
            string clean1 = a.Trim();
            if (clean1.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) clean1 = clean1[..^4];
            string clean2 = app;
            if (clean2.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) clean2 = clean2[..^4];
            return string.Equals(clean1, clean2, StringComparison.OrdinalIgnoreCase);
        });

        if (!alreadyExists)
        {
            _audioSettings.ExcludedPlayerApps.Add(app);
            _audioSettings.Save();
            RefreshExcludedAppsChips();
        }

        TxtAddExcludedApp.Text = "";
    }

    private void TxtAddExcludedApp_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            BtnAddExcludedApp_Click(sender, e);
            e.Handled = true;
        }
    }

    #endregion

    private void AudioModuleCleanup()
    {
        try
        {
            _audioTimer?.Stop();
            _audioHook?.Dispose();
            _taskbarVolumeHook?.Dispose();
            _micOverlay?.Close();
            _taskbarVolumeOsd?.Close();
            _trayManager?.Dispose();
            MiniTaskManagerService.Instance.Dispose();
        }
        catch { }
    }
}
