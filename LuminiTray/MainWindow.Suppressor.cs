using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;


namespace LuminiTray;


/// <summary>
/// Controlador da aba Supressor de Ruído (RESET FERRAMENTAS).
/// Integra o serviço MicrophoneSuppressorService com a interface gráfica:
/// - Driver de Microfone Virtual (detecção e instalação com 1 clique);
/// - Roteamento duplo (para Discord/Jogos via CABLE Input e para Fone de Ouvido);
/// - Opção de ouvir somente o Pós-Processamento ou Áudio Bruto (A/B);
/// - Gravador e reprodutor de teste rápido de voz de 5 segundos;
/// - Telemetria em tempo real (VU meters e VAD neural) e persistência de parâmetros.
/// </summary>
public partial class MainWindow
{
    private DispatcherTimer? _suppressorMeterTimer;
    private bool _isUpdatingSuppressorUi;
    private bool _isDraggingMicSlider;
    private DateTime _lastMicSliderInteraction = DateTime.MinValue;


    public void EnsureSuppressorServiceRunning()
    {
        if (!_settings.SuppressorMasterEnabled && !_settings.SuppressorListenSelf) return;

        try
        {
            var svc = MicrophoneSuppressorService.Instance;
            var eng = svc.Engine;

            svc.SelectedInputDeviceId = _settings.SuppressorSelectedInputId;
            VirtualCableManager.EnsureCableInputEnabled();

            if (!string.IsNullOrEmpty(_settings.SuppressorVirtualMicDeviceId))
            {
                var cableDev = AudioCore.GetDevices(AudioInterop.EDataFlow.Render)
                    .FirstOrDefault(d => d.Id.Equals(_settings.SuppressorVirtualMicDeviceId, StringComparison.OrdinalIgnoreCase));
                if (cableDev != null && VirtualCableManager.IsCableDevice(cableDev.Name))
                {
                    svc.SelectedVirtualCableDeviceId = _settings.SuppressorVirtualMicDeviceId;
                }
            }

            if (string.IsNullOrEmpty(svc.SelectedVirtualCableDeviceId))
            {
                var autoCable = VirtualCableManager.GetCableInputDevice();
                if (autoCable != null)
                {
                    svc.SelectedVirtualCableDeviceId = autoCable.Id;
                    _settings.SuppressorVirtualMicDeviceId = autoCable.Id;
                }
            }

            svc.SelectedOutputDeviceId = _settings.SuppressorSelectedOutputId;
            svc.IsMonitoringEnabled = _settings.SuppressorListenSelf;
            svc.MonitorMode = (SuppressorMonitorMode)_settings.SuppressorMonitorMode;

            eng.SuppressionIntensity = _settings.SuppressorIntensity;
            eng.GainDb = _settings.SuppressorGainDb;
            eng.GateThresholdDb = _settings.SuppressorGateThresholdDb;
            eng.GateReleaseMs = _settings.SuppressorGateReleaseMs;
            eng.LowEqDb = _settings.SuppressorLowEqDb;
            eng.MidEqDb = _settings.SuppressorMidEqDb;
            eng.HighEqDb = _settings.SuppressorHighEqDb;
            eng.VadThreshold = _settings.SuppressorVadThreshold;
            eng.VadHoldMs = _settings.SuppressorVadHoldMs;
            eng.LimiterEnabled = _settings.SuppressorLimiterEnabled;
            eng.DeesserEnabled = _settings.SuppressorDeesserEnabled;

            svc.MasterEnabled = _settings.SuppressorMasterEnabled;

            if (svc.MasterEnabled || svc.IsMonitoringEnabled)
            {
                svc.Start();
                SubtaskManager.RegisterSubtask("audio-suppressor", "Lumini Tray - Supressor de Ruído", () =>
                {
                    svc.MasterEnabled = false;
                    svc.IsMonitoringEnabled = false;
                    _settings.SuppressorMasterEnabled = false;
                    _settings.SuppressorListenSelf = false;
                    _settings.Save();
                    svc.Stop();
                    UpdateSuppressorMasterUi(false);
                    SubtaskManager.UnregisterSubtask("audio-suppressor");
                });
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Suppressor] Falha ao iniciar serviço na inicialização: {ex.Message}");
        }
    }

    private void InitSuppressorPage()
    {
        if (_suppressorMeterTimer == null)
        {
            _suppressorMeterTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(66) // ~15 FPS
            };
            _suppressorMeterTimer.Tick += SuppressorMeterTimer_Tick;
        }


        if (!_suppressorMeterTimer.IsEnabled)
        {
            _suppressorMeterTimer.Start();
        }


        // Carrega configurações persistidas para o serviço
        var svc = MicrophoneSuppressorService.Instance;
        var eng = svc.Engine;


        svc.SelectedInputDeviceId = _settings.SuppressorSelectedInputId;
        // Garante que o cabo virtual esteja ativo no Windows caso instalado
        VirtualCableManager.EnsureCableInputEnabled();

        // Validação estrita: O dispositivo do Cabo Virtual NUNCA pode ser o fone de ouvido ou alto-falante físico
        if (!string.IsNullOrEmpty(_settings.SuppressorVirtualMicDeviceId))
        {
            var cableDev = AudioCore.GetDevices(AudioInterop.EDataFlow.Render)
                .FirstOrDefault(d => d.Id.Equals(_settings.SuppressorVirtualMicDeviceId, StringComparison.OrdinalIgnoreCase));
            if (cableDev != null && VirtualCableManager.IsCableDevice(cableDev.Name))
            {
                svc.SelectedVirtualCableDeviceId = _settings.SuppressorVirtualMicDeviceId;
            }
            else
            {
                svc.SelectedVirtualCableDeviceId = null;
                _settings.SuppressorVirtualMicDeviceId = null;
            }
        }
        else
        {
            svc.SelectedVirtualCableDeviceId = null;
        }

        if (string.IsNullOrEmpty(svc.SelectedVirtualCableDeviceId))
        {
            var autoCable = VirtualCableManager.GetCableInputDevice();
            if (autoCable != null)
            {
                svc.SelectedVirtualCableDeviceId = autoCable.Id;
                _settings.SuppressorVirtualMicDeviceId = autoCable.Id;
            }
        }
        svc.SelectedOutputDeviceId = _settings.SuppressorSelectedOutputId;
        svc.IsMonitoringEnabled = _settings.SuppressorListenSelf;
        svc.MonitorMode = (SuppressorMonitorMode)_settings.SuppressorMonitorMode;


        if (!svc.IsRunning && _settings.SuppressorMasterEnabled)
        {
            eng.SuppressionIntensity = _settings.SuppressorIntensity;
            eng.GainDb = _settings.SuppressorGainDb;
            eng.GateThresholdDb = _settings.SuppressorGateThresholdDb;
            eng.GateReleaseMs = _settings.SuppressorGateReleaseMs;
            eng.LowEqDb = _settings.SuppressorLowEqDb;
            eng.MidEqDb = _settings.SuppressorMidEqDb;
            eng.HighEqDb = _settings.SuppressorHighEqDb;
            eng.VadThreshold = _settings.SuppressorVadThreshold;
            eng.VadHoldMs = _settings.SuppressorVadHoldMs;
            eng.LimiterEnabled = _settings.SuppressorLimiterEnabled;
            eng.DeesserEnabled = _settings.SuppressorDeesserEnabled;
            svc.MasterEnabled = true;

            SubtaskManager.RegisterSubtask("audio-suppressor", "Lumini Tray - Supressor de Ruído", () =>
            {
                svc.MasterEnabled = false;
                _settings.SuppressorMasterEnabled = false;
                _settings.Save();
                UpdateSuppressorMasterUi(false);
                SubtaskManager.UnregisterSubtask("audio-suppressor");
            });
        }


        UpdateVirtualCableUi();
        PopulateSuppressorDevices();
        SyncSuppressorUiFromService();
    }


    private void UpdateVirtualCableUi()
    {
        bool installed = VirtualCableManager.IsVirtualCableInstalled();


        if (BorderVirtualCableBadge != null && TxtVirtualCableBadge != null)
        {
            if (installed)
            {
                BorderVirtualCableBadge.Background = new SolidColorBrush(Color.FromArgb(0x33, 0x10, 0xB9, 0x81));
                TxtVirtualCableBadge.Text = "🟢 Microfone Virtual Ativo: CABLE Output (VB-Audio)";
                TxtVirtualCableBadge.Foreground = new SolidColorBrush(Color.FromRgb(0x10, 0xB9, 0x81));
            }
            else
            {
                BorderVirtualCableBadge.Background = new SolidColorBrush(Color.FromArgb(0x33, 0xF5, 0x9E, 0x0B));
                TxtVirtualCableBadge.Text = "⚠️ Driver Virtual Não Detectado";
                TxtVirtualCableBadge.Foreground = new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B));
            }
        }


        if (PanelVirtualCableInstalled != null)
            PanelVirtualCableInstalled.Visibility = installed ? Visibility.Visible : Visibility.Collapsed;


        if (PanelVirtualCableNotInstalled != null)
            PanelVirtualCableNotInstalled.Visibility = installed ? Visibility.Collapsed : Visibility.Visible;
    }


    private void SyncSuppressorUiFromService()
    {
        _isUpdatingSuppressorUi = true;
        try
        {
            var svc = MicrophoneSuppressorService.Instance;
            var eng = svc.Engine;


            // Motor Neural vs Fallback
            if (BadgeEngineStatus != null && TxtEngineBadge != null)
            {
                if (eng.IsNativeRnNoiseAvailable)
                {
                    TxtEngineBadge.Text = "⚡ RNNoise Neural Ativo";
                    BadgeEngineStatus.BorderBrush = (Brush)FindResource("Accent");
                }
                else
                {
                    TxtEngineBadge.Text = "🛡️ Fallback: HighPass 80Hz + Gate";
                    BadgeEngineStatus.BorderBrush = Brushes.Goldenrod;
                }
            }


            // Master Switch
            UpdateSuppressorMasterUi(svc.MasterEnabled);


            // Ouvir voz e Modo de Retorno
            if (ChkListenSelf != null)
                ChkListenSelf.IsChecked = svc.IsMonitoringEnabled;


            if (RadioMonitorProcessed != null && RadioMonitorRaw != null)
            {
                if (svc.MonitorMode == SuppressorMonitorMode.Processed)
                    RadioMonitorProcessed.IsChecked = true;
                else
                    RadioMonitorRaw.IsChecked = true;
            }


            // Volume do Microfone no Windows
            if (SliderHardwareMicVolume != null && (!_isDraggingMicSlider || (DateTime.UtcNow - _lastMicSliderInteraction).TotalMilliseconds > 1500))
            {
                float currentVol = AudioCore.GetDeviceVolumeScalar(svc.SelectedInputDeviceId ?? "");
                SliderHardwareMicVolume.Value = Math.Round(currentVol * 100f);
                if (TxtHardwareMicVolVal != null)
                    TxtHardwareMicVolVal.Text = $"{Math.Round(currentVol * 100f)}%";
            }


            // Sliders DSP
            if (SliderIntensity != null)
            {
                SliderIntensity.Value = Math.Round(eng.SuppressionIntensity * 100);
                if (TxtIntensityVal != null) TxtIntensityVal.Text = $"{SliderIntensity.Value}%";
            }


            if (SliderGain != null)
            {
                SliderGain.Value = Math.Round(eng.GainDb);
                if (TxtGainVal != null) TxtGainVal.Text = $"{SliderGain.Value:+#;-#;0} dB";
            }


            if (SliderGateThreshold != null)
            {
                SliderGateThreshold.Value = Math.Round(eng.GateThresholdDb);
                if (TxtGateThresholdVal != null) TxtGateThresholdVal.Text = $"{SliderGateThreshold.Value} dB";
            }


            if (SliderGateRelease != null)
            {
                SliderGateRelease.Value = Math.Round(eng.GateReleaseMs);
                if (TxtGateReleaseVal != null) TxtGateReleaseVal.Text = $"{SliderGateRelease.Value} ms";
            }


            // Equalizador
            if (SliderLowEq != null)
            {
                SliderLowEq.Value = Math.Round(eng.LowEqDb);
                if (TxtLowEqVal != null) TxtLowEqVal.Text = $"{SliderLowEq.Value:+#;-#;0} dB";
            }


            if (SliderMidEq != null)
            {
                SliderMidEq.Value = Math.Round(eng.MidEqDb);
                if (TxtMidEqVal != null) TxtMidEqVal.Text = $"{SliderMidEq.Value:+#;-#;0} dB";
            }


            if (SliderHighEq != null)
            {
                SliderHighEq.Value = Math.Round(eng.HighEqDb);
                if (TxtHighEqVal != null) TxtHighEqVal.Text = $"{SliderHighEq.Value:+#;-#;0} dB";
            }
        }
        finally
        {
            _isUpdatingSuppressorUi = false;
        }
    }


    private void PopulateSuppressorDevices()
    {
        _isUpdatingSuppressorUi = true;
        try
        {
            var svc = MicrophoneSuppressorService.Instance;


            // 1. Captura (Microfones Físicos)
            if (CmbInputMic != null)
            {
                var captureDevs = AudioCore.GetDevices(AudioInterop.EDataFlow.Capture).Where(d => d.IsEnabled).ToList();
                CmbInputMic.Items.Clear();
                int selectedCapIdx = -1;
                for (int i = 0; i < captureDevs.Count; i++)
                {
                    var dev = captureDevs[i];
                    string label = string.IsNullOrEmpty(dev.Name) ? dev.Id : dev.Name;
                    if (dev.IsDefaultConsole) label += " (Padrão do Sistema)";
                    CmbInputMic.Items.Add(new ComboBoxItem { Content = label, Tag = dev.Id });


                    if (!string.IsNullOrEmpty(svc.SelectedInputDeviceId) && svc.SelectedInputDeviceId == dev.Id)
                    {
                        selectedCapIdx = i;
                    }
                    else if (selectedCapIdx == -1 && dev.IsDefaultConsole)
                    {
                        selectedCapIdx = i;
                    }
                }
                if (selectedCapIdx >= 0 && selectedCapIdx < CmbInputMic.Items.Count)
                    CmbInputMic.SelectedIndex = selectedCapIdx;
                else if (CmbInputMic.Items.Count > 0)
                    CmbInputMic.SelectedIndex = 0;
            }


            // 2. Renderização (Dispositivos de Reprodução)
            VirtualCableManager.EnsureCableInputEnabled();

            var allRenderDevs = AudioCore.GetDevices(AudioInterop.EDataFlow.Render);
            var renderDevs = allRenderDevs.Where(d => d.IsEnabled).ToList();

            // 2a. Destino do Microfone Virtual (CABLE Input)
            if (CmbVirtualCableDev != null)
            {
                CmbVirtualCableDev.Items.Clear();
                var cableDevs = allRenderDevs.Where(d => VirtualCableManager.IsCableDevice(d.Name)).ToList();
                int selectedCableIdx = -1;

                if (cableDevs.Count > 0)
                {
                    for (int i = 0; i < cableDevs.Count; i++)
                    {
                        var dev = cableDevs[i];
                        string label = "⭐ " + (string.IsNullOrEmpty(dev.Name) ? dev.Id : dev.Name) + " (Recomendado)";
                        CmbVirtualCableDev.Items.Add(new ComboBoxItem { Content = label, Tag = dev.Id });

                        if (!string.IsNullOrEmpty(svc.SelectedVirtualCableDeviceId) && svc.SelectedVirtualCableDeviceId == dev.Id)
                        {
                            selectedCableIdx = i;
                        }
                    }
                    if (selectedCableIdx == -1) selectedCableIdx = 0;
                    CmbVirtualCableDev.SelectedIndex = selectedCableIdx;
                    if (CmbVirtualCableDev.SelectedItem is ComboBoxItem cbi && cbi.Tag is string cid)
                    {
                        svc.SelectedVirtualCableDeviceId = cid;
                        _settings.SuppressorVirtualMicDeviceId = cid;
                    }
                }
                else
                {
                    // Nenhum driver de cabo virtual instalado: NUNCA usar fone de ouvido ou alto-falante
                    CmbVirtualCableDev.Items.Add(new ComboBoxItem { Content = "⚠️ Nenhum Cabo Virtual detectado (Instale o VB-Cable abaixo)", Tag = "" });
                    CmbVirtualCableDev.SelectedIndex = 0;
                    svc.SelectedVirtualCableDeviceId = null;
                    _settings.SuppressorVirtualMicDeviceId = null;
                }
            }


            // 2b. Retorno no Fone de Ouvido (Alto-falantes / Headphone)
            if (CmbOutputDev != null)
            {
                CmbOutputDev.Items.Clear();
                int selectedRenIdx = -1;
                for (int i = 0; i < renderDevs.Count; i++)
                {
                    var dev = renderDevs[i];
                    string label = string.IsNullOrEmpty(dev.Name) ? dev.Id : dev.Name;
                    if (dev.IsDefaultConsole) label += " (Padrão do Sistema)";
                    CmbOutputDev.Items.Add(new ComboBoxItem { Content = label, Tag = dev.Id });


                    if (!string.IsNullOrEmpty(svc.SelectedOutputDeviceId) && svc.SelectedOutputDeviceId == dev.Id)
                    {
                        selectedRenIdx = i;
                    }
                    else if (selectedRenIdx == -1 && dev.IsDefaultConsole)
                    {
                        selectedRenIdx = i;
                    }
                }
                if (selectedRenIdx >= 0 && selectedRenIdx < CmbOutputDev.Items.Count)
                    CmbOutputDev.SelectedIndex = selectedRenIdx;
                else if (CmbOutputDev.Items.Count > 0)
                    CmbOutputDev.SelectedIndex = 0;
            }
        }
        catch { }
        finally
        {
            _isUpdatingSuppressorUi = false;
        }
    }


    private void SuppressorMeterTimer_Tick(object? sender, EventArgs e)
    {
        if (PageSuppressor == null || PageSuppressor.Visibility != Visibility.Visible)
        {
            _suppressorMeterTimer?.Stop();
            return;
        }


        var svc = MicrophoneSuppressorService.Instance;


        float inDb = svc.InputPeakDb;
        float outDb = svc.OutputPeakDb;
        bool voiceDetected = svc.IsVoiceDetected;


        // Atualizar barra de entrada (-60 a 0 dB)
        if (BarInputPeak != null)
            BarInputPeak.Value = Math.Clamp(inDb, -60f, 0f);
        if (TxtInputPeakDb != null)
            TxtInputPeakDb.Text = inDb <= -59f ? "-∞ dB" : $"{inDb:F1} dB";


        // Atualizar barra de saída (-60 a 0 dB)
        if (BarOutputPeak != null)
            BarOutputPeak.Value = Math.Clamp(outDb, -60f, 0f);
        if (TxtOutputPeakDb != null)
            TxtOutputPeakDb.Text = outDb <= -59f ? "-∞ dB" : $"{outDb:F1} dB";


        // VAD Pill
        if (BorderVadPill != null && TxtVadPill != null)
        {
            if (voiceDetected)
            {
                BorderVadPill.Background = new SolidColorBrush(Color.FromArgb(0x33, 0x10, 0xB9, 0x81));
                TxtVadPill.Text = "VOZ DETECTADA";
                TxtVadPill.Foreground = new SolidColorBrush(Color.FromRgb(0x10, 0xB9, 0x81));
            }
            else
            {
                BorderVadPill.Background = new SolidColorBrush(Color.FromArgb(0x22, 0x94, 0xA3, 0xB8));
                TxtVadPill.Text = "SILÊNCIO / ISOLADO";
                TxtVadPill.Foreground = new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8));
            }
        }


        // Teste Rápido de 5s
        if (PbQuickTest != null && TxtQuickTestBtn != null && TxtQuickTestStatus != null)
        {
            if (svc.TestState == SuppressorQuickTestState.Recording)
            {
                PbQuickTest.Visibility = Visibility.Visible;
                PbQuickTest.Value = svc.TestProgress;
                TxtQuickTestBtn.Text = "⏹️ Gravando... (Fale agora!)";
                TxtQuickTestStatus.Text = $"Gravando áudio com supressão: {Math.Round(svc.TestProgress * 100)}%";
            }
            else if (svc.TestState == SuppressorQuickTestState.Playing)
            {
                PbQuickTest.Visibility = Visibility.Visible;
                PbQuickTest.Value = svc.TestProgress;
                TxtQuickTestBtn.Text = "🔊 Reproduzindo Pós-Processado...";
                TxtQuickTestStatus.Text = "Ouvindo no fone exatamente como os outros te ouvem!";
            }
            else
            {
                PbQuickTest.Visibility = Visibility.Collapsed;
                TxtQuickTestBtn.Text = "🎙️ Gravar Amostra de 5s e Reproduzir";
                TxtQuickTestStatus.Text = "Grave 5s para ouvir o resultado sem eco vocal";
            }
        }
    }


    private void UpdateSuppressorMasterUi(bool enabled)
    {
        if (TxtMasterStatusIcon == null || TxtMasterStatusTitle == null || TxtMasterStatusDesc == null ||
            BtnToggleMasterText == null || BtnToggleMaster == null)
            return;


        if (enabled)
        {
            TxtMasterStatusIcon.Text = "🟢";
            TxtMasterStatusTitle.Text = "SUPRESSOR DE RUÍDO: ATIVADO";
            TxtMasterStatusDesc.Text = "O fluxo de áudio do microfone está sendo filtrado ativamente em tempo real e transmitido ao microfone virtual.";
            BtnToggleMasterText.Text = "Desativar Supressor";
            BtnToggleMaster.Style = (Style)FindResource("OutlineButton");
        }
        else
        {
            TxtMasterStatusIcon.Text = "⚪";
            TxtMasterStatusTitle.Text = "SUPRESSOR DE RUÍDO: DESATIVADO";
            TxtMasterStatusDesc.Text = "O fluxo de áudio está passando sem filtros ou com processamento suspenso.";
            BtnToggleMasterText.Text = "Ativar Supressor Neural";
            BtnToggleMaster.Style = (Style)FindResource("PrimaryButton");
        }
    }


    private void BtnToggleMaster_Click(object sender, RoutedEventArgs e)
    {
        var svc = MicrophoneSuppressorService.Instance;
        bool newState = !svc.MasterEnabled;
        svc.MasterEnabled = newState;
        _settings.SuppressorMasterEnabled = newState;
        _settings.Save();

        if (newState)
        {
            svc.Start();
            SubtaskManager.RegisterSubtask("audio-suppressor", "Lumini Tray - Supressor de Ruído", () =>
            {
                svc.MasterEnabled = false;
                _settings.SuppressorMasterEnabled = false;
                _settings.Save();
                if (!svc.IsMonitoringEnabled) svc.Stop();
                UpdateSuppressorMasterUi(false);
                SubtaskManager.UnregisterSubtask("audio-suppressor");
            });
        }
        else
        {
            if (!svc.IsMonitoringEnabled) svc.Stop();
            SubtaskManager.UnregisterSubtask("audio-suppressor");
        }

        UpdateSuppressorMasterUi(newState);


        Toast("SUPRESSOR DE RUÍDO",
            newState ? "Supressão de ruído ativada em segundo plano!" : "Supressão de ruído pausada.",
            newState ? "\uE720" : "\uE71A");
    }


    private void CmbInputMic_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingSuppressorUi) return;
        if (CmbInputMic.SelectedItem is ComboBoxItem item && item.Tag is string id)
        {
            var svc = MicrophoneSuppressorService.Instance;
            if (svc.SelectedInputDeviceId != id)
            {
                svc.SelectedInputDeviceId = id;
                _settings.SuppressorSelectedInputId = id;
                _settings.Save();
                if (svc.IsRunning) svc.Restart();
            }
        }
    }


    private void CmbVirtualCableDev_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingSuppressorUi) return;
        if (CmbVirtualCableDev.SelectedItem is ComboBoxItem item)
        {
            string? id = item.Tag as string;
            var svc = MicrophoneSuppressorService.Instance;
            if (string.IsNullOrEmpty(id))
            {
                svc.SelectedVirtualCableDeviceId = null;
                _settings.SuppressorVirtualMicDeviceId = null;
            }
            else
            {
                svc.SelectedVirtualCableDeviceId = id;
                _settings.SuppressorVirtualMicDeviceId = id;
            }
            _settings.Save();
        }
    }


    private void CmbOutputDev_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingSuppressorUi) return;
        if (CmbOutputDev.SelectedItem is ComboBoxItem item && item.Tag is string id)
        {
            var svc = MicrophoneSuppressorService.Instance;
            svc.SelectedOutputDeviceId = id;
            _settings.SuppressorSelectedOutputId = id;
            _settings.Save();
        }
    }


    private void ChkListenSelf_Checked(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingSuppressorUi) return;
        var svc = MicrophoneSuppressorService.Instance;
        svc.IsMonitoringEnabled = true;
        _settings.SuppressorListenSelf = true;
        _settings.Save();
        if (!svc.IsRunning) svc.Start();
    }


    private void ChkListenSelf_Unchecked(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingSuppressorUi) return;
        var svc = MicrophoneSuppressorService.Instance;
        svc.IsMonitoringEnabled = false;
        _settings.SuppressorListenSelf = false;
        _settings.Save();
        if (!svc.MasterEnabled && svc.IsRunning) svc.Stop();
    }


    private void RadioMonitorMode_Checked(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingSuppressorUi) return;
        var svc = MicrophoneSuppressorService.Instance;
        if (RadioMonitorProcessed?.IsChecked == true)
        {
            svc.MonitorMode = SuppressorMonitorMode.Processed;
            _settings.SuppressorMonitorMode = 0;
            Toast("RETORNO DE ÁUDIO", "Modo: Ouvir Somente Pós-Processamento (Áudio Limpo).", "\uE720");
        }
        else
        {
            svc.MonitorMode = SuppressorMonitorMode.Raw;
            _settings.SuppressorMonitorMode = 1;
            Toast("RETORNO DE ÁUDIO", "Modo: Ouvir Áudio Bruto / Cru (Comparação A/B).", "\uE720");
        }
        _settings.Save();
    }


    private void BtnQuickTest_Click(object sender, RoutedEventArgs e)
    {
        var svc = MicrophoneSuppressorService.Instance;
        if (svc.TestState == SuppressorQuickTestState.Idle)
        {
            svc.StartQuickTest(5);
            Toast("TESTE DE MICROFONE", "Gravando 5 segundos com supressão neural... Fale ou digite!", "\uE720");
        }
        else
        {
            svc.StopQuickTest();
        }
    }


    private async void BtnInstallVirtualCable_Click(object sender, RoutedEventArgs e)
    {
        if (BtnInstallVirtualCable == null) return;
        BtnInstallVirtualCable.IsEnabled = false;
        if (BtnInstallVirtualCableText != null)
            BtnInstallVirtualCableText.Text = "⏳ Instalando driver...";


        try
        {
            var (success, msg) = await VirtualCableManager.InstallDriverAsync();
            PopulateSuppressorDevices();
            UpdateVirtualCableUi();


            Toast("MICROFONE VIRTUAL", msg, success ? "\uE720" : "\uE783");
        }
        catch (Exception ex)
        {
            Toast("MICROFONE VIRTUAL", $"Erro: {ex.Message}", "\uE783");
        }
        finally
        {
            BtnInstallVirtualCable.IsEnabled = true;
            if (BtnInstallVirtualCableText != null)
                BtnInstallVirtualCableText.Text = "📥 Instalar Driver (1-Clique)";
        }
    }


    private void BtnOpenDriverFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string installer = VirtualCableManager.GetInstallerPath();
            string folder = System.IO.Path.GetDirectoryName(installer) ?? AppContext.BaseDirectory;
            if (System.IO.Directory.Exists(folder))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = folder, UseShellExecute = true });
            }
            else
            {
                Toast("PASTA DO DRIVER", "Pasta do instalador não encontrada.", "\uE783");
            }
        }
        catch (Exception ex)
        {
            Toast("PASTA DO DRIVER", ex.Message, "\uE783");
        }
    }


    private void BtnRefreshDevices_Click(object sender, RoutedEventArgs e)
    {
        UpdateVirtualCableUi();
        PopulateSuppressorDevices();
        Toast("LUMINI ÁUDIO", "Lista de microfones e dispositivos de áudio atualizada.", "\uE720");
    }


    private void SliderHardwareMicVolume_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isUpdatingSuppressorUi) return;
        _isDraggingMicSlider = true;
        _lastMicSliderInteraction = DateTime.UtcNow;


        float val = (float)(SliderHardwareMicVolume?.Value ?? 100.0);
        if (TxtHardwareMicVolVal != null)
            TxtHardwareMicVolVal.Text = $"{Math.Round(val)}%";


        var svc = MicrophoneSuppressorService.Instance;
        AudioCore.SetDeviceVolumeScalar(svc.SelectedInputDeviceId ?? "", val / 100f);
    }


    private void SliderIntensity_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isUpdatingSuppressorUi || TxtIntensityVal == null) return;
        float val = (float)SliderIntensity.Value;
        TxtIntensityVal.Text = $"{val:0}%";
        MicrophoneSuppressorService.Instance.Engine.SuppressionIntensity = val / 100f;
        _settings.SuppressorIntensity = val / 100f;
        _settings.Save();
    }


    private void SliderGain_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isUpdatingSuppressorUi || TxtGainVal == null) return;
        float val = (float)SliderGain.Value;
        TxtGainVal.Text = $"{val:+#;-#;0} dB";
        MicrophoneSuppressorService.Instance.Engine.GainDb = val;
        _settings.SuppressorGainDb = val;
        _settings.Save();
    }


    private void SliderGateThreshold_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isUpdatingSuppressorUi || TxtGateThresholdVal == null) return;
        float val = (float)SliderGateThreshold.Value;
        TxtGateThresholdVal.Text = $"{val:0} dB";
        MicrophoneSuppressorService.Instance.Engine.GateThresholdDb = val;
        _settings.SuppressorGateThresholdDb = val;
        _settings.Save();
    }


    private void SliderGateRelease_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isUpdatingSuppressorUi || TxtGateReleaseVal == null) return;
        float val = (float)SliderGateRelease.Value;
        TxtGateReleaseVal.Text = $"{val:0} ms";
        MicrophoneSuppressorService.Instance.Engine.GateReleaseMs = val;
        _settings.SuppressorGateReleaseMs = val;
        _settings.Save();
    }


    private void SliderLowEq_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isUpdatingSuppressorUi || TxtLowEqVal == null) return;
        float val = (float)SliderLowEq.Value;
        TxtLowEqVal.Text = $"{val:+#;-#;0} dB";
        MicrophoneSuppressorService.Instance.Engine.LowEqDb = val;
        _settings.SuppressorLowEqDb = val;
        _settings.Save();
    }


    private void SliderMidEq_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isUpdatingSuppressorUi || TxtMidEqVal == null) return;
        float val = (float)SliderMidEq.Value;
        TxtMidEqVal.Text = $"{val:+#;-#;0} dB";
        MicrophoneSuppressorService.Instance.Engine.MidEqDb = val;
        _settings.SuppressorMidEqDb = val;
        _settings.Save();
    }


    private void SliderHighEq_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isUpdatingSuppressorUi || TxtHighEqVal == null) return;
        float val = (float)SliderHighEq.Value;
        TxtHighEqVal.Text = $"{val:+#;-#;0} dB";
        MicrophoneSuppressorService.Instance.Engine.HighEqDb = val;
        _settings.SuppressorHighEqDb = val;
        _settings.Save();
    }


    private void BtnResetEq_Click(object sender, RoutedEventArgs e)
    {
        if (SliderLowEq != null) SliderLowEq.Value = 0;
        if (SliderMidEq != null) SliderMidEq.Value = 0;
        if (SliderHighEq != null) SliderHighEq.Value = 0;
        Toast("EQUALIZADOR", "Bandas de frequência resetadas para 0 dB (Flat).", "\uE720");
    }
}
