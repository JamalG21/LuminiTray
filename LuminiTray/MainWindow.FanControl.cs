using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace LuminiTray;

public partial class MainWindow
{
    private FanControlService? _fanControlService;
    private DispatcherTimer? _fanControlTimer;
    private bool _fanControlInitialized;
    private bool _fanServiceStarted;
    private readonly Dictionary<string, (TextBlock Rpm, TextBlock Pwm)> _fanCardElements = new();

    // Estado de arraste com o mouse nos gráficos
    private int _draggedCpuPoint = -1;
    private int _draggedGpuPoint = -1;

    public void EnsureFanControlServiceRunning()
    {
        if (_fanServiceStarted && _fanControlService != null) return;
        if (_settings.FanControl == null || !_settings.FanControl.IsControlActive) return;

        try
        {
            _fanControlService = new FanControlService(_monitor);
            _fanControlService.DiscoverFans();
            _fanControlService.ApplySettings(_settings.FanControl);
            _fanServiceStarted = true;

            if (_fanControlService.IsControlActive)
            {
                _fanControlService.StartBackgroundLoop(1000);
                SubtaskManager.RegisterSubtask("fan-controller", "Lumini Tray - Controlador de Fans", () =>
                {
                    try
                    {
                        _fanControlService?.SetControlActive(false);
                        SubtaskManager.UnregisterSubtask("fan-controller");
                    }
                    catch { }
                });
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[FanControl] Erro ao iniciar serviço: {ex.Message}");
        }
    }

    private void SaveFanControlSettings()
    {
        try
        {
            if (_fanControlService != null)
            {
                var oldCpuPresets = _settings.FanControl?.SavedCpuPresets ?? new();
                var oldGpuPresets = _settings.FanControl?.SavedGpuPresets ?? new();

                _settings.FanControl = _fanControlService.ExportSettings();
                _settings.FanControl.SavedCpuPresets = oldCpuPresets;
                _settings.FanControl.SavedGpuPresets = oldGpuPresets;
                _settings.Save();
            }
        }
        catch { }
    }

    private void InitFanControlPage()
    {
        if (_fanControlService == null)
        {
            try
            {
                _fanControlService = new FanControlService(_monitor);
                _fanControlService.DiscoverFans();
                _fanControlService.ApplySettings(_settings.FanControl);
                _fanServiceStarted = true;

                if (_fanControlService.IsControlActive)
                {
                    _fanControlService.StartBackgroundLoop(1000);
                    SubtaskManager.RegisterSubtask("fan-controller", "Lumini Tray - Controlador de Fans", () =>
                    {
                        try
                        {
                            _fanControlService?.SetControlActive(false);
                            SubtaskManager.UnregisterSubtask("fan-controller");
                        }
                        catch { }
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[FanControl] Erro ao inicializar serviço sob demanda: {ex.Message}");
            }
        }

        if (_fanControlInitialized)
        {
            UpdateFanUi();
            return;
        }

        try
        {
            if (_fanControlService == null) return;

            _fanControlTimer = new DispatcherTimer(DispatcherPriority.Normal)
            {
                Interval = TimeSpan.FromMilliseconds(1000)
            };
            _fanControlTimer.Tick += (_, _) =>
            {
                if (PageFanControl != null && PageFanControl.Visibility == Visibility.Visible)
                {
                    UpdateFanUi();
                }
            };
            _fanControlTimer.Start();

            // Fail-safe garantido no encerramento REAL da aplicação
            Application.Current.Exit += (_, _) => _fanControlService?.RestoreAllToBios();

            _fanControlInitialized = true;
            BuildFanCards();
            PopulateFanPresets();
            SyncCpuCurveSliders();
            SyncGpuCurveSliders();
            DrawCpuCurve();
            DrawGpuCurve();
            UpdateFanUi();
        }
        catch (Exception ex)
        {
            if (FanStatusBadge != null)
                FanStatusBadge.Text = $"Erro ao iniciar: {ex.Message}";
        }
    }

    // =========================================================================
    // NAVEGAÇÃO DE ABAS: CPU vs GPU
    // =========================================================================

    private void BtnTabFanCpu_Click(object sender, RoutedEventArgs e)
    {
        if (PanelFanCpu != null) PanelFanCpu.Visibility = Visibility.Visible;
        if (PanelFanGpu != null) PanelFanGpu.Visibility = Visibility.Collapsed;

        if (BtnTabFanCpu != null) BtnTabFanCpu.Style = (Style)FindResource("PrimaryButton");
        if (BtnTabFanGpu != null) BtnTabFanGpu.Style = (Style)FindResource("OutlineButton");

        DrawCpuCurve();
    }

    private void BtnTabFanGpu_Click(object sender, RoutedEventArgs e)
    {
        if (PanelFanCpu != null) PanelFanCpu.Visibility = Visibility.Collapsed;
        if (PanelFanGpu != null) PanelFanGpu.Visibility = Visibility.Visible;

        if (BtnTabFanCpu != null) BtnTabFanCpu.Style = (Style)FindResource("OutlineButton");
        if (BtnTabFanGpu != null) BtnTabFanGpu.Style = (Style)FindResource("PrimaryButton");

        DrawGpuCurve();
    }

    // =========================================================================
    // CARDS DE VENTOINHAS (CPU & Gabinete vs GPU)
    // =========================================================================

    private TextBlock? _notebookThermalStatusText;

    private void BuildFanCards()
    {
        if (_fanControlService == null) return;
        _fanCardElements.Clear();
        _notebookThermalStatusText = null;

        if (FanCardsPanelCpu != null) FanCardsPanelCpu.Children.Clear();
        if (FanCardsPanelGpu != null) FanCardsPanelGpu.Children.Clear();

        var cpuChannels = _fanControlService.Channels.Where(c => c.SourceType is FanSourceType.Motherboard or FanSourceType.Controller).ToList();
        var gpuChannels = _fanControlService.Channels.Where(c => c.SourceType is not (FanSourceType.Motherboard or FanSourceType.Controller)).ToList();

        // Cards CPU
        if (FanCardsPanelCpu != null)
        {
            if (cpuChannels.Count == 0)
            {
                FanCardsPanelCpu.Children.Add(CreateCpuDiagnosticCard());
            }
            else
            {
                foreach (var ch in cpuChannels)
                {
                    FanCardsPanelCpu.Children.Add(CreateFanCard(ch));
                }
            }
        }

        // Cards GPU
        if (FanCardsPanelGpu != null)
        {
            if (gpuChannels.Count == 0)
            {
                string gpuMsg = _fanControlService.PlatformInfo.IsLaptop
                    ? "GPU de notebook (Optimus / iGPU) compartilha o cooler principal da CPU via Embedded Controller (EC) e não possui ventoinha dedicada."
                    : "Nenhuma ventoinha dedicada de GPU com controle de cooler detectada pelo driver de vídeo (NvAPI/ADL/IGCL).";

                FanCardsPanelGpu.Children.Add(new TextBlock
                {
                    Text = gpuMsg,
                    Foreground = (Brush)FindResource("Muted"),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 10, 0, 10)
                });
            }
            else
            {
                foreach (var ch in gpuChannels)
                {
                    FanCardsPanelGpu.Children.Add(CreateFanCard(ch));
                }
            }
        }
    }

    private Border CreateCpuDiagnosticCard()
    {
        var p = _fanControlService!.PlatformInfo;
        var border = new Border
        {
            Style = (Style)FindResource("CardBorder"),
            Margin = new Thickness(0, 4, 0, 10),
            Padding = new Thickness(14)
        };

        var stack = new StackPanel();
        string title = p.IsLaptop
            ? $"Controle Térmico para Notebook · {p.Manufacturer} {p.Model}"
            : "Nenhuma ventoinha de placa-mãe (Super I/O) com controle PWM detectada";

        stack.Children.Add(new TextBlock
        {
            Text = title,
            Foreground = (Brush)FindResource("Fg"),
            FontWeight = FontWeights.SemiBold,
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap
        });

        stack.Children.Add(new TextBlock
        {
            Text = $"BIOS: {p.BiosVersion} · Admin: {(p.IsAdmin ? "Sim" : "Não")} · PawnIO: {(p.IsPawnIoDetected ? "Ativo" : "Não detectado")}",
            Foreground = (Brush)FindResource("Muted"),
            FontSize = 11,
            Margin = new Thickness(0, 3, 0, 8),
            TextWrapping = TextWrapping.Wrap
        });

        string desc = p.IsLaptop
            ? "Notebooks gerenciam o cooler pelo Embedded Controller (EC) da BIOS sem expor Super I/O PWM. Com o controle ativo, o Lumini Tray ajusta Política de Resfriamento (Passivo/Ativo) e teto de CPU no Windows seguindo a curva."
            : "Verifique se o aplicativo foi aberto como Administrador e se o driver PawnIO está ativo para acesso ao chip Super I/O.";

        stack.Children.Add(new TextBlock
        {
            Text = desc,
            Foreground = (Brush)FindResource("Muted"),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap
        });

        if (p.IsLaptop)
        {
            _notebookThermalStatusText = new TextBlock
            {
                Text = $"Estado atual: {_fanControlService.ThermalBackend.StatusSummary}",
                Foreground = (Brush)FindResource("Accent"),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 8, 0, 0),
                TextWrapping = TextWrapping.Wrap
            };
            stack.Children.Add(_notebookThermalStatusText);
        }

        var btnCopy = new Button
        {
            Content = "COPIAR DIAGNÓSTICO DE FANS",
            Style = (Style)FindResource("OutlineButton"),
            Padding = new Thickness(12, 6, 12, 6),
            FontSize = 11,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 10, 0, 0)
        };
        btnCopy.Click += (_, _) =>
        {
            try
            {
                Clipboard.SetText(_fanControlService.BuildDiagnosticReport());
                btnCopy.Content = "DIAGNÓSTICO COPIADO!";
            }
            catch { }
        };
        stack.Children.Add(btnCopy);

        border.Child = stack;
        return border;
    }

    private Border CreateFanCard(FanChannel ch)
    {
        var border = new Border
        {
            Style = (Style)FindResource("CardBorder"),
            Margin = new Thickness(0, 0, 0, 12),
            Padding = new Thickness(16)
        };

        var mainStack = new StackPanel();

        // Cabeçalho da ventoinha
        var headerGrid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var titleStack = new StackPanel { Orientation = Orientation.Horizontal };
        titleStack.Children.Add(new TextBlock
        {
            Text = "\xE9CA",
            FontFamily = (FontFamily)FindResource("Icons"),
            FontSize = 16,
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = (Brush)FindResource("Accent")
        });

        var nameStack = new StackPanel();
        nameStack.Children.Add(new TextBlock
        {
            Text = ch.Name,
            FontWeight = FontWeights.SemiBold,
            FontSize = 14
        });
        nameStack.Children.Add(new TextBlock
        {
            Text = ch.HardwareName,
            FontSize = 11,
            Foreground = (Brush)FindResource("Muted")
        });
        titleStack.Children.Add(nameStack);
        Grid.SetColumn(titleStack, 0);
        headerGrid.Children.Add(titleStack);

        if (ch.IsReadOnly)
        {
            var roBadge = new Border
            {
                Background = (Brush)FindResource("CardSubtleBg"),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8, 4, 8, 4),
                VerticalAlignment = VerticalAlignment.Center
            };
            roBadge.Child = new TextBlock
            {
                Text = "SOMENTE LEITURA",
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("Muted")
            };
            Grid.SetColumn(roBadge, 1);
            headerGrid.Children.Add(roBadge);
        }
        else
        {
            // Trava de segurança (100% contínuo)
            var lockBtn = new Button
            {
                Content = ch.IsLocked100 ? "TRAVADO 100%" : "Travar 100%",
                Style = (Style)FindResource(ch.IsLocked100 ? "PrimaryButton" : "OutlineButton"),
                Padding = new Thickness(10, 4, 10, 4),
                FontSize = 11,
                Tag = ch
            };
            lockBtn.Click += (s, e) =>
            {
                ch.IsLocked100 = !ch.IsLocked100;
                lockBtn.Content = ch.IsLocked100 ? "TRAVADO 100%" : "Travar 100%";
                lockBtn.Style = (Style)FindResource(ch.IsLocked100 ? "PrimaryButton" : "OutlineButton");
                if (ch.IsLocked100)
                {
                    ch.SetPwm(100f);
                    ch.LastPulseTime = DateTime.UtcNow;
                }
                UpdateFanUi();
                SaveFanControlSettings();
            };
            Grid.SetColumn(lockBtn, 1);
            headerGrid.Children.Add(lockBtn);
        }

        mainStack.Children.Add(headerGrid);

        // Métricas (RPM e PWM)
        var metricsGrid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        metricsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        metricsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var rpmBlock = new TextBlock
        {
            Text = $"{ch.CurrentRpm:0} RPM",
            FontFamily = (FontFamily)FindResource("Mono"),
            FontSize = 18,
            FontWeight = FontWeights.Bold,
            Tag = $"RPM_{ch.Id}"
        };
        Grid.SetColumn(rpmBlock, 0);
        metricsGrid.Children.Add(rpmBlock);

        var pwmBlock = new TextBlock
        {
            Text = ch.IsReadOnly ? "BIOS/EC" : $"{ch.CurrentPwm:0}% PWM",
            FontFamily = (FontFamily)FindResource("Mono"),
            FontSize = 18,
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Right,
            Tag = $"PWM_{ch.Id}"
        };
        Grid.SetColumn(pwmBlock, 1);
        metricsGrid.Children.Add(pwmBlock);

        _fanCardElements[ch.Id] = (rpmBlock, pwmBlock);

        mainStack.Children.Add(metricsGrid);

        if (ch.IsReadOnly)
        {
            mainStack.Children.Add(new TextBlock
            {
                Text = "Controlado automaticamente pela BIOS/EC (telemetria RPM somente leitura).",
                Foreground = (Brush)FindResource("Muted"),
                FontSize = 11.5,
                TextWrapping = TextWrapping.Wrap
            });
            border.Child = mainStack;
            return border;
        }

        // Controles: Slider manual e botões Auto / Manual
        var ctrlGrid = new Grid();
        ctrlGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        ctrlGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        ctrlGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var slider = new Slider
        {
            Minimum = 0,
            Maximum = 100,
            Value = ch.TargetPwm,
            TickFrequency = 5,
            IsSnapToTickEnabled = true,
            Margin = new Thickness(0, 0, 12, 0),
            Tag = ch,
            IsEnabled = ch.IsManual && !ch.IsLocked100
        };

        var btnAuto = new Button
        {
            Content = ch.IsManual ? "Curva Auto" : "CURVA AUTO",
            Style = (Style)FindResource(ch.IsManual ? "OutlineButton" : "PrimaryButton"),
            Padding = new Thickness(10, 4, 10, 4),
            FontSize = 11,
            Margin = new Thickness(0, 0, 6, 0),
            Tag = ch
        };

        var btnManual = new Button
        {
            Content = ch.IsManual ? "MANUAL" : "Manual",
            Style = (Style)FindResource(ch.IsManual ? "PrimaryButton" : "OutlineButton"),
            Padding = new Thickness(10, 4, 10, 4),
            FontSize = 11,
            Tag = ch
        };

        slider.ValueChanged += (s, e) =>
        {
            if (ch.IsManual && !ch.IsLocked100)
            {
                ch.TargetPwm = (float)e.NewValue;
                ch.SetPwm(ch.TargetPwm);
                UpdateFanUi();
                SaveFanControlSettings();
            }
        };

        btnAuto.Click += (s, e) =>
        {
            ch.IsManual = false;
            slider.IsEnabled = false;
            btnAuto.Style = (Style)FindResource("PrimaryButton");
            btnManual.Style = (Style)FindResource("OutlineButton");
            btnAuto.Content = "CURVA AUTO";
            btnManual.Content = "Manual";
            UpdateFanUi();
            SaveFanControlSettings();
        };

        btnManual.Click += (s, e) =>
        {
            ch.IsManual = true;
            slider.IsEnabled = !ch.IsLocked100;
            btnAuto.Style = (Style)FindResource("OutlineButton");
            btnManual.Style = (Style)FindResource("PrimaryButton");
            btnAuto.Content = "Curva Auto";
            btnManual.Content = "MANUAL";
            ch.SetPwm((float)slider.Value);
            UpdateFanUi();
            SaveFanControlSettings();
        };

        Grid.SetColumn(slider, 0);
        Grid.SetColumn(btnAuto, 1);
        Grid.SetColumn(btnManual, 2);

        ctrlGrid.Children.Add(slider);
        ctrlGrid.Children.Add(btnAuto);
        ctrlGrid.Children.Add(btnManual);

        mainStack.Children.Add(ctrlGrid);
        border.Child = mainStack;
        return border;
    }

    private void BtnToggleFanMaster_Click(object sender, RoutedEventArgs e)
    {
        if (_fanControlService == null) return;
        bool newState = !_fanControlService.IsControlActive;
        _fanControlService.SetControlActive(newState);

        if (newState)
        {
            SubtaskManager.RegisterSubtask("fan-controller", "Lumini Tray - Controlador de Fans", () =>
            {
                try
                {
                    _fanControlService?.SetControlActive(false);
                    SubtaskManager.UnregisterSubtask("fan-controller");
                }
                catch { }
            });
        }
        else
        {
            SubtaskManager.UnregisterSubtask("fan-controller");
        }

        UpdateFanUi();
        DrawCpuCurve();
        DrawGpuCurve();
        SaveFanControlSettings();
    }

    private void UpdateFanUi()
    {
        if (PageFanControl == null || PageFanControl.Visibility != Visibility.Visible || _fanControlService == null) return;

        bool active = _fanControlService.IsControlActive;
        if (FanMasterStatusIcon != null)
            FanMasterStatusIcon.Text = active ? "🟢" : "🛡️";
        if (FanMasterStatusTitle != null)
            FanMasterStatusTitle.Text = active
                ? "CONTROLE INTELIGENTE DE FANS ATIVO"
                : "MODO BIOS / FIRMWARE ATIVO (CONTROLE DE FANS DESATIVADO)";
        if (FanMasterStatusDesc != null)
            FanMasterStatusDesc.Text = active
                ? "O software está aplicando a curva personalizada com histerese anti-spike. Pressione ao lado para devolver o controle às fans para a BIOS / Firmware."
                : "As ventoinhas estão sob controle nativo da placa-mãe (BIOS) e firmware da GPU. O Lumini Tray opera apenas em modo de leitura e telemetria, sem interferir nos controles PWM.";
        if (BtnToggleFanMasterText != null)
            BtnToggleFanMasterText.Text = active
                ? "Desativar Recurso (Modo BIOS / Firmware)"
                : "Ativar Controle Inteligente de Fans";
        if (BtnToggleFanMasterIcon != null)
            BtnToggleFanMasterIcon.Text = active ? "🛡️" : "⚡";
        if (BtnToggleFanMaster != null)
            BtnToggleFanMaster.Style = (Style)FindResource(active ? "OutlineButton" : "PrimaryButton");
        if (FanStatusBadge != null)
            FanStatusBadge.Text = active ? "⚡ Controle Inteligente Ativo" : "🛡️ Modo BIOS / Firmware";

        if (FanLiveCpuTemp != null)
            FanLiveCpuTemp.Text = $"{_fanControlService.LastCpuTemp:0.0} °C";

        if (FanLiveGpuTemp != null)
            FanLiveGpuTemp.Text = $"{_fanControlService.LastGpuTemp:0.0} °C";

        double mixedTemp = Math.Max(_fanControlService.LastCpuTemp, _fanControlService.LastGpuTemp);
        if (FanLiveMixedTemp != null)
            FanLiveMixedTemp.Text = $"{mixedTemp:0.0} °C";

        // Atualiza os valores nos cards em O(1) via dicionário em cache (sem recursão de VisualTree)
        UpdateCardsPanel();

        if (_notebookThermalStatusText != null && _fanControlService != null)
        {
            _notebookThermalStatusText.Text = $"Estado atual: {_fanControlService.ThermalBackend.StatusSummary}";
        }

        // Atualiza os rótulos de RPM nos eixos verticais
        UpdateAxisRpmLabels();

        UpdateCurveLiveDots();
    }

    private double GetCpuMaxRpm()
    {
        double maxCpuRpm = 2200;
        if (_fanControlService != null)
        {
            var cpuChannels = _fanControlService.Channels.Where(c => c.SourceType is FanSourceType.Motherboard or FanSourceType.Controller).ToList();
            if (cpuChannels.Count > 0)
            {
                double maxMeasured = cpuChannels.Max(c => c.CurrentRpm);
                if (maxMeasured > 500)
                {
                    maxCpuRpm = Math.Max(maxCpuRpm, Math.Ceiling(maxMeasured / 100.0) * 100.0);
                }
            }
        }
        return maxCpuRpm;
    }

    private double GetGpuMaxRpm()
    {
        double maxGpuRpm = 3000;
        if (_fanControlService != null)
        {
            var gpuChannels = _fanControlService.Channels.Where(c => c.SourceType is not (FanSourceType.Motherboard or FanSourceType.Controller)).ToList();
            if (gpuChannels.Count > 0)
            {
                double maxMeasured = gpuChannels.Max(c => c.CurrentRpm);
                if (maxMeasured > 500)
                {
                    maxGpuRpm = Math.Max(maxGpuRpm, Math.Ceiling(maxMeasured / 100.0) * 100.0);
                }
            }
        }
        return maxGpuRpm;
    }

    private void UpdateAxisRpmLabels()
    {
        double maxCpuRpm = GetCpuMaxRpm();
        if (TxtCpuAxisY100 != null) TxtCpuAxisY100.Text = $"100% · {maxCpuRpm:0}";
        if (TxtCpuAxisY80 != null) TxtCpuAxisY80.Text = $"80% · {maxCpuRpm * 0.8:0}";
        if (TxtCpuAxisY60 != null) TxtCpuAxisY60.Text = $"60% · {maxCpuRpm * 0.6:0}";
        if (TxtCpuAxisY40 != null) TxtCpuAxisY40.Text = $"40% · {maxCpuRpm * 0.4:0}";
        if (TxtCpuAxisY20 != null) TxtCpuAxisY20.Text = $"20% · {maxCpuRpm * 0.2:0}";
        if (TxtCpuAxisY0 != null) TxtCpuAxisY0.Text = "0% · 0 RPM";

        double maxGpuRpm = GetGpuMaxRpm();
        if (TxtGpuAxisY100 != null) TxtGpuAxisY100.Text = $"100% · {maxGpuRpm:0}";
        if (TxtGpuAxisY80 != null) TxtGpuAxisY80.Text = $"80% · {maxGpuRpm * 0.8:0}";
        if (TxtGpuAxisY60 != null) TxtGpuAxisY60.Text = $"60% · {maxGpuRpm * 0.6:0}";
        if (TxtGpuAxisY40 != null) TxtGpuAxisY40.Text = $"40% · {maxGpuRpm * 0.4:0}";
        if (TxtGpuAxisY20 != null) TxtGpuAxisY20.Text = $"20% · {maxGpuRpm * 0.2:0}";
        if (TxtGpuAxisY0 != null) TxtGpuAxisY0.Text = "0% · 0 RPM";
    }

    private void UpdateCardsPanel()
    {
        if (_fanControlService == null) return;
        foreach (var ch in _fanControlService.Channels)
        {
            if (_fanCardElements.TryGetValue(ch.Id, out var tuple))
            {
                tuple.Rpm.Text = $"{ch.CurrentRpm:0} RPM";
                tuple.Pwm.Text = $"{ch.CurrentPwm:0}% PWM";
            }
        }
    }

    // =========================================================================
    // DESENHO E ARRASTE INTERATIVO NOS GRÁFICOS (CPU e GPU)
    // =========================================================================

    private void DrawCpuCurve()
    {
        if (FanCurveCanvasCpu == null || _fanControlService == null) return;
        DrawGenericCurve(FanCurveCanvasCpu, _fanControlService.CpuCurve, _fanControlService.LastCpuTemp, isGpu: false);
    }

    private void DrawGpuCurve()
    {
        if (FanCurveCanvasGpu == null || _fanControlService == null) return;
        DrawGenericCurve(FanCurveCanvasGpu, _fanControlService.GpuCurve, _fanControlService.LastGpuTemp, isGpu: true);
    }

    private void DrawGenericCurve(Canvas canvas, FanCurve curve, double currentTemp, bool isGpu)
    {
        canvas.Children.Clear();

        double w = canvas.ActualWidth > 0 ? canvas.ActualWidth : 440;
        double h = canvas.ActualHeight > 0 ? canvas.ActualHeight : 240;

        // 1. Grade milimétrica refinada
        // Linhas verticais (Temperatura: 20°C a 100°C a cada 10°C)
        for (int temp = 20; temp <= 100; temp += 10)
        {
            double x = (temp - 20) / 80.0 * w;
            bool isMajor = (temp % 20 == 0);
            var line = new Line
            {
                X1 = x, Y1 = 0, X2 = x, Y2 = h,
                Stroke = new SolidColorBrush(Color.FromArgb((byte)(isMajor ? 45 : 20), 200, 200, 200)),
                StrokeThickness = 1,
                StrokeDashArray = isMajor ? null : new DoubleCollection { 2, 2 }
            };
            canvas.Children.Add(line);
        }

        // Linhas horizontais (PWM: 0% a 100% a cada 10%)
        double maxCurveRpm = isGpu ? GetGpuMaxRpm() : GetCpuMaxRpm();
        for (int pwm = 0; pwm <= 100; pwm += 10)
        {
            double y = h - (pwm / 100.0 * h);
            bool isMajor = (pwm % 20 == 0);
            var line = new Line
            {
                X1 = 0, Y1 = y, X2 = w, Y2 = y,
                Stroke = new SolidColorBrush(Color.FromArgb((byte)(isMajor ? 45 : 20), 200, 200, 200)),
                StrokeThickness = 1,
                StrokeDashArray = isMajor ? null : new DoubleCollection { 2, 2 }
            };
            canvas.Children.Add(line);

            if (isMajor && pwm > 0 && pwm < 100)
            {
                double lineRpm = maxCurveRpm * (pwm / 100.0);
                var rpmLabel = new TextBlock
                {
                    Text = $"{lineRpm:0} RPM",
                    FontSize = 9,
                    Foreground = new SolidColorBrush(Color.FromArgb(60, 200, 200, 200)),
                    FontFamily = (FontFamily)FindResource("Mono")
                };
                Canvas.SetRight(rpmLabel, 8);
                Canvas.SetTop(rpmLabel, y - 13);
                canvas.Children.Add(rpmLabel);
            }
        }

        var points = curve.Points.OrderBy(p => p.Temp).ToList();
        if (points.Count > 1)
        {
            // 2. Preenchimento sombreado da área sob a curva (Gradiente Translúcido)
            var areaPolygon = new Polygon();
            var accentBrush = (Brush)(TryFindResource("Accent") ?? Brushes.Cyan);
            Color accentColor = Colors.Cyan;
            if (accentBrush is SolidColorBrush scb)
            {
                accentColor = scb.Color;
            }

            var gradientBrush = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(0, 1)
            };
            gradientBrush.GradientStops.Add(new GradientStop(Color.FromArgb(50, accentColor.R, accentColor.G, accentColor.B), 0.0));
            gradientBrush.GradientStops.Add(new GradientStop(Color.FromArgb(5, accentColor.R, accentColor.G, accentColor.B), 1.0));
            areaPolygon.Fill = gradientBrush;

            // Inicia na base esquerda
            double firstX = Math.Clamp((points[0].Temp - 20) / 80.0 * w, 0, w);
            areaPolygon.Points.Add(new Point(firstX, h));

            for (int i = 0; i < points.Count; i++)
            {
                var pt = points[i];
                double x = Math.Clamp((pt.Temp - 20) / 80.0 * w, 0, w);
                double y = Math.Clamp(h - (pt.Pwm / 100.0 * h), 0, h);
                areaPolygon.Points.Add(new Point(x, y));
            }

            // Fecha na base direita
            double lastX = Math.Clamp((points[^1].Temp - 20) / 80.0 * w, 0, w);
            areaPolygon.Points.Add(new Point(lastX, h));

            canvas.Children.Add(areaPolygon);

            // 3. Traçado contínuo da curva
            var polyline = new Polyline
            {
                Stroke = accentBrush,
                StrokeThickness = 3,
                StrokeLineJoin = PenLineJoin.Round,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round
            };

            for (int i = 0; i < points.Count; i++)
            {
                var pt = points[i];
                double x = Math.Clamp((pt.Temp - 20) / 80.0 * w, 0, w);
                double y = Math.Clamp(h - (pt.Pwm / 100.0 * h), 0, h);
                polyline.Points.Add(new Point(x, y));
            }
            canvas.Children.Add(polyline);

            // 4. Bolinhas interativas arrastáveis + Badges com coordenadas exatas
            for (int i = 0; i < points.Count; i++)
            {
                var pt = points[i];
                double x = Math.Clamp((pt.Temp - 20) / 80.0 * w, 0, w);
                double y = Math.Clamp(h - (pt.Pwm / 100.0 * h), 0, h);

                // Badge de Coordenada Flutuante
                var badgeBorder = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(220, 15, 15, 18)),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(100, 255, 255, 255)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(3),
                    Padding = new Thickness(4, 1, 4, 1),
                    IsHitTestVisible = false
                };
                var badgeText = new TextBlock
                {
                    Text = $"P{i + 1}: {pt.Temp:0}°C·{pt.Pwm:0}%",
                    FontFamily = (FontFamily)(TryFindResource("Mono") ?? new FontFamily("Consolas")),
                    FontSize = 9,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = Brushes.White
                };
                badgeBorder.Child = badgeText;

                double badgeLeft = Math.Clamp(x - 28, 4, Math.Max(4, w - 68));
                double badgeTop = y > 26 ? y - 24 : y + 10;
                Canvas.SetLeft(badgeBorder, badgeLeft);
                Canvas.SetTop(badgeBorder, badgeTop);
                canvas.Children.Add(badgeBorder);

                // Bolinha arrastável
                var ellipse = new Ellipse
                {
                    Width = 14,
                    Height = 14,
                    Fill = accentBrush,
                    Stroke = Brushes.White,
                    StrokeThickness = 2,
                    Cursor = Cursors.Hand,
                    Tag = $"Point_{i}",
                    ToolTip = $"P{i + 1}: {pt.Temp:0}°C · {pt.Pwm:0}% PWM (Arraste com o mouse para ajustar)"
                };
                Canvas.SetLeft(ellipse, x - 7);
                Canvas.SetTop(ellipse, y - 7);
                canvas.Children.Add(ellipse);
            }
        }

        // 5. Linhas de Mira e Ponto Vermelho de Telemetria ao Vivo
        double livePwm = curve.Evaluate(currentTemp);
        double liveX = Math.Clamp((currentTemp - 20) / 80.0 * w, 0, w);
        double liveY = Math.Clamp(h - (livePwm / 100.0 * h), 0, h);

        // Mira vertical
        var crossV = new Line
        {
            X1 = liveX, Y1 = liveY, X2 = liveX, Y2 = h,
            Stroke = Brushes.Crimson, StrokeThickness = 1,
            StrokeDashArray = new DoubleCollection { 2, 2 },
            Opacity = 0.7,
            IsHitTestVisible = false
        };
        canvas.Children.Add(crossV);

        // Mira horizontal
        var crossH = new Line
        {
            X1 = 0, Y1 = liveY, X2 = liveX, Y2 = liveY,
            Stroke = Brushes.Crimson, StrokeThickness = 1,
            StrokeDashArray = new DoubleCollection { 2, 2 },
            Opacity = 0.7,
            IsHitTestVisible = false
        };
        canvas.Children.Add(crossH);

        // Bolinha Vermelha
        var liveDot = new Ellipse
        {
            Width = 16,
            Height = 16,
            Fill = Brushes.Crimson,
            Stroke = Brushes.White,
            StrokeThickness = 2,
            Tag = "LiveDot",
            ToolTip = $"Telemetria em Tempo Real:\nTemperatura: {currentTemp:0.1}°C\nRotação Curva: {livePwm:0}% PWM"
        };
        Canvas.SetLeft(liveDot, liveX - 8);
        Canvas.SetTop(liveDot, liveY - 8);
        canvas.Children.Add(liveDot);

        // Badge flutuante de telemetria ao vivo
        var liveBadge = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(230, 180, 20, 20)),
            BorderBrush = Brushes.White,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(5, 1, 5, 1),
            IsHitTestVisible = false
        };
        liveBadge.Child = new TextBlock
        {
            Text = $"🔴 {currentTemp:0.0}°C · {livePwm:0}%",
            FontFamily = (FontFamily)(TryFindResource("Mono") ?? new FontFamily("Consolas")),
            FontSize = 9.5,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White
        };
        double liveBadgeLeft = Math.Clamp(liveX + 10, 4, Math.Max(4, w - 85));
        double liveBadgeTop = Math.Clamp(liveY - 20, 4, Math.Max(4, h - 22));
        Canvas.SetLeft(liveBadge, liveBadgeLeft);
        Canvas.SetTop(liveBadge, liveBadgeTop);
        canvas.Children.Add(liveBadge);
    }

    private void UpdateCurveLiveDots()
    {
        if (_fanControlService == null) return;
        DrawCpuCurve();
        DrawGpuCurve();
    }

    // =========================================================================
    // EVENTOS DE MOUSE PARA ARRASTAR AS BOLINHAS DO GRÁFICO (CPU)
    // =========================================================================

    private void FanCurveCanvasCpu_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_fanControlService == null || FanCurveCanvasCpu == null) return;
        Point pos = e.GetPosition(FanCurveCanvasCpu);
        _draggedCpuPoint = GetHitPointIndex(FanCurveCanvasCpu, _fanControlService.CpuCurve, pos);
        if (_draggedCpuPoint >= 0)
        {
            FanCurveCanvasCpu.CaptureMouse();
        }
    }

    private void FanCurveCanvasCpu_MouseMove(object sender, MouseEventArgs e)
    {
        if (_draggedCpuPoint < 0 || _fanControlService == null || FanCurveCanvasCpu == null) return;
        Point pos = e.GetPosition(FanCurveCanvasCpu);
        ApplyDragToCurve(FanCurveCanvasCpu, _fanControlService.CpuCurve, _draggedCpuPoint, pos);
        SyncCpuCurveSliders();
        DrawCpuCurve();
    }

    private void FanCurveCanvasCpu_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (FanCurveCanvasCpu != null)
        {
            FanCurveCanvasCpu.ReleaseMouseCapture();
        }
        if (_draggedCpuPoint >= 0)
        {
            SaveFanControlSettings();
        }
        _draggedCpuPoint = -1;
    }

    private void FanCurveCanvasCpu_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        DrawCpuCurve();
    }

    // =========================================================================
    // EVENTOS DE MOUSE PARA ARRASTAR AS BOLINHAS DO GRÁFICO (GPU)
    // =========================================================================

    private void FanCurveCanvasGpu_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_fanControlService == null || FanCurveCanvasGpu == null) return;
        Point pos = e.GetPosition(FanCurveCanvasGpu);
        _draggedGpuPoint = GetHitPointIndex(FanCurveCanvasGpu, _fanControlService.GpuCurve, pos);
        if (_draggedGpuPoint >= 0)
        {
            FanCurveCanvasGpu.CaptureMouse();
        }
    }

    private void FanCurveCanvasGpu_MouseMove(object sender, MouseEventArgs e)
    {
        if (_draggedGpuPoint < 0 || _fanControlService == null || FanCurveCanvasGpu == null) return;
        Point pos = e.GetPosition(FanCurveCanvasGpu);
        ApplyDragToCurve(FanCurveCanvasGpu, _fanControlService.GpuCurve, _draggedGpuPoint, pos);
        SyncGpuCurveSliders();
        DrawGpuCurve();
    }

    private void FanCurveCanvasGpu_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (FanCurveCanvasGpu != null)
        {
            FanCurveCanvasGpu.ReleaseMouseCapture();
        }
        if (_draggedGpuPoint >= 0)
        {
            SaveFanControlSettings();
        }
        _draggedGpuPoint = -1;
    }

    private void FanCurveCanvasGpu_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        DrawGpuCurve();
    }

    private int GetHitPointIndex(Canvas canvas, FanCurve curve, Point mousePos)
    {
        double w = canvas.ActualWidth > 0 ? canvas.ActualWidth : 440;
        double h = canvas.ActualHeight > 0 ? canvas.ActualHeight : 240;

        for (int i = 0; i < curve.Points.Count; i++)
        {
            var pt = curve.Points[i];
            double x = (pt.Temp - 20) / 80.0 * w;
            double y = h - (pt.Pwm / 100.0 * h);

            double dist = Math.Sqrt(Math.Pow(mousePos.X - x, 2) + Math.Pow(mousePos.Y - y, 2));
            if (dist <= 22.0) // Raio de tolerância para clique
            {
                return i;
            }
        }
        return -1;
    }

    private void ApplyDragToCurve(Canvas canvas, FanCurve curve, int pointIndex, Point mousePos)
    {
        double w = canvas.ActualWidth > 0 ? canvas.ActualWidth : 440;
        double h = canvas.ActualHeight > 0 ? canvas.ActualHeight : 240;

        double newTemp = 20.0 + (mousePos.X / w) * 80.0;
        double newPwm = (1.0 - (mousePos.Y / h)) * 100.0;

        newPwm = Math.Clamp(Math.Round(newPwm), 0, 100);
        newTemp = Math.Clamp(Math.Round(newTemp), 20, 100);

        // Limites de temperatura relativos aos pontos adjacentes para manter a curva estritamente crescente
        double minTemp = pointIndex > 0 ? curve.Points[pointIndex - 1].Temp + 2 : 20;
        double maxTemp = pointIndex < curve.Points.Count - 1 ? curve.Points[pointIndex + 1].Temp - 2 : 100;

        newTemp = Math.Clamp(newTemp, minTemp, maxTemp);

        curve.Points[pointIndex].Temp = newTemp;
        curve.Points[pointIndex].Pwm = newPwm;
    }

    // =========================================================================
    // PRESETS E SINCRONIZAÇÃO DE SLIDERS (CPU)
    // =========================================================================

    private void SetActiveFanCpuPreset(Button? active)
    {
        Button?[] btns = { BtnFanCpuQuiet, BtnFanCpuBalanced, BtnFanCpuPerf, BtnFanCpuLock100 };
        foreach (var b in btns)
        {
            if (b == null) continue;
            b.Style = (Style)FindResource(b == active ? "PrimaryButton" : "OutlineButton");
        }
    }

    private void FanPresetQuiet_Click(object sender, RoutedEventArgs e)
    {
        if (_fanControlService == null) return;
        SetActiveFanCpuPreset(BtnFanCpuQuiet);
        var currentSource = _fanControlService.CpuCurve.TempSource;
        _fanControlService.CpuCurve = FanCurve.CreateQuiet(currentSource);
        SyncCpuCurveSliders();
        DrawCpuCurve();
        SaveFanControlSettings();
    }

    private void FanPresetBalanced_Click(object sender, RoutedEventArgs e)
    {
        if (_fanControlService == null) return;
        SetActiveFanCpuPreset(BtnFanCpuBalanced);
        var currentSource = _fanControlService.CpuCurve.TempSource;
        _fanControlService.CpuCurve = new FanCurve { TempSource = currentSource };
        SyncCpuCurveSliders();
        DrawCpuCurve();
        SaveFanControlSettings();
    }

    private void FanPresetPerf_Click(object sender, RoutedEventArgs e)
    {
        if (_fanControlService == null) return;
        SetActiveFanCpuPreset(BtnFanCpuPerf);
        var currentSource = _fanControlService.CpuCurve.TempSource;
        _fanControlService.CpuCurve = FanCurve.CreatePerformance(currentSource);
        SyncCpuCurveSliders();
        DrawCpuCurve();
        SaveFanControlSettings();
    }

    private void FanPresetCpuLock100_Click(object sender, RoutedEventArgs e)
    {
        if (_fanControlService == null) return;
        SetActiveFanCpuPreset(BtnFanCpuLock100);
        foreach (var ch in _fanControlService.Channels.Where(c => c.SourceType is FanSourceType.Motherboard or FanSourceType.Controller))
        {
            ch.IsLocked100 = true;
            ch.SetPwm(100f);
            ch.LastPulseTime = DateTime.UtcNow;
        }
        BuildFanCards();
        UpdateFanUi();
        SaveFanControlSettings();
    }

    private void SyncCpuCurveSliders()
    {
        if (_fanControlService == null || _fanControlService.CpuCurve.Points.Count < 6) return;
        var pts = _fanControlService.CpuCurve.Points;

        if (FanPoint1Temp != null) FanPoint1Temp.Value = pts[0].Temp;
        if (FanPoint1Pwm != null) FanPoint1Pwm.Value = pts[0].Pwm;
        if (FanPoint2Temp != null) FanPoint2Temp.Value = pts[1].Temp;
        if (FanPoint2Pwm != null) FanPoint2Pwm.Value = pts[1].Pwm;
        if (FanPoint3Temp != null) FanPoint3Temp.Value = pts[2].Temp;
        if (FanPoint3Pwm != null) FanPoint3Pwm.Value = pts[2].Pwm;
        if (FanPoint4Temp != null) FanPoint4Temp.Value = pts[3].Temp;
        if (FanPoint4Pwm != null) FanPoint4Pwm.Value = pts[3].Pwm;
        if (FanPoint5Temp != null) FanPoint5Temp.Value = pts[4].Temp;
        if (FanPoint5Pwm != null) FanPoint5Pwm.Value = pts[4].Pwm;
        if (FanPoint6Temp != null) FanPoint6Temp.Value = pts[5].Temp;
        if (FanPoint6Pwm != null) FanPoint6Pwm.Value = pts[5].Pwm;

        if (FanRampUpSlider != null) FanRampUpSlider.Value = _fanControlService.CpuCurve.RampUpSeconds;
        if (FanRampDownSlider != null) FanRampDownSlider.Value = _fanControlService.CpuCurve.RampDownSeconds;
    }

    private void FanPoint_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_fanControlInitialized || _fanControlService == null || _fanControlService.CpuCurve.Points.Count < 6) return;

        if (FanPoint1Temp != null && FanPoint1Pwm != null)
        {
            _fanControlService.CpuCurve.Points[0].Temp = FanPoint1Temp.Value;
            _fanControlService.CpuCurve.Points[0].Pwm = FanPoint1Pwm.Value;
        }
        if (FanPoint2Temp != null && FanPoint2Pwm != null)
        {
            _fanControlService.CpuCurve.Points[1].Temp = FanPoint2Temp.Value;
            _fanControlService.CpuCurve.Points[1].Pwm = FanPoint2Pwm.Value;
        }
        if (FanPoint3Temp != null && FanPoint3Pwm != null)
        {
            _fanControlService.CpuCurve.Points[2].Temp = FanPoint3Temp.Value;
            _fanControlService.CpuCurve.Points[2].Pwm = FanPoint3Pwm.Value;
        }
        if (FanPoint4Temp != null && FanPoint4Pwm != null)
        {
            _fanControlService.CpuCurve.Points[3].Temp = FanPoint4Temp.Value;
            _fanControlService.CpuCurve.Points[3].Pwm = FanPoint4Pwm.Value;
        }
        if (FanPoint5Temp != null && FanPoint5Pwm != null)
        {
            _fanControlService.CpuCurve.Points[4].Temp = FanPoint5Temp.Value;
            _fanControlService.CpuCurve.Points[4].Pwm = FanPoint5Pwm.Value;
        }
        if (FanPoint6Temp != null && FanPoint6Pwm != null)
        {
            _fanControlService.CpuCurve.Points[5].Temp = FanPoint6Temp.Value;
            _fanControlService.CpuCurve.Points[5].Pwm = FanPoint6Pwm.Value;
        }

        DrawCpuCurve();
        SaveFanControlSettings();
    }

    private void FanHysteresis_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_fanControlService == null) return;

        if (FanRampUpSlider != null && FanRampUpLabel != null)
        {
            _fanControlService.CpuCurve.RampUpSeconds = FanRampUpSlider.Value;
            FanRampUpLabel.Text = $"{FanRampUpSlider.Value:0} s";
        }
        if (FanRampDownSlider != null && FanRampDownLabel != null)
        {
            _fanControlService.CpuCurve.RampDownSeconds = FanRampDownSlider.Value;
            FanRampDownLabel.Text = $"{FanRampDownSlider.Value:0} s";
        }
        SaveFanControlSettings();
    }

    private void FanTempSource_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton rb && _fanControlService != null)
        {
            _fanControlService.CpuCurve.TempSource = ((string?)rb.Tag) switch
            {
                "Cpu" => CurveTempSource.Cpu,
                "Gpu" => CurveTempSource.Gpu,
                _ => CurveTempSource.MixedMax
            };
            DrawCpuCurve();
            SaveFanControlSettings();
        }
    }

    // =========================================================================
    // PRESETS E SINCRONIZAÇÃO DE SLIDERS (GPU)
    // =========================================================================

    private void SetActiveFanGpuPreset(Button? active)
    {
        Button?[] btns = { BtnFanGpuQuiet, BtnFanGpuBalanced, BtnFanGpuPerf, BtnFanGpuLock100 };
        foreach (var b in btns)
        {
            if (b == null) continue;
            b.Style = (Style)FindResource(b == active ? "PrimaryButton" : "OutlineButton");
        }
    }

    private void FanPresetGpuQuiet_Click(object sender, RoutedEventArgs e)
    {
        if (_fanControlService == null) return;
        SetActiveFanGpuPreset(BtnFanGpuQuiet);
        _fanControlService.GpuCurve = FanCurve.CreateQuiet(CurveTempSource.Gpu);
        SyncGpuCurveSliders();
        DrawGpuCurve();
        SaveFanControlSettings();
    }

    private void FanPresetGpuBalanced_Click(object sender, RoutedEventArgs e)
    {
        if (_fanControlService == null) return;
        SetActiveFanGpuPreset(BtnFanGpuBalanced);
        _fanControlService.GpuCurve = FanCurve.CreateGpuDefault();
        SyncGpuCurveSliders();
        DrawGpuCurve();
        SaveFanControlSettings();
    }

    private void FanPresetGpuPerf_Click(object sender, RoutedEventArgs e)
    {
        if (_fanControlService == null) return;
        SetActiveFanGpuPreset(BtnFanGpuPerf);
        _fanControlService.GpuCurve = FanCurve.CreatePerformance(CurveTempSource.Gpu);
        SyncGpuCurveSliders();
        DrawGpuCurve();
        SaveFanControlSettings();
    }

    private void FanPresetGpuLock100_Click(object sender, RoutedEventArgs e)
    {
        if (_fanControlService == null) return;
        SetActiveFanGpuPreset(BtnFanGpuLock100);
        foreach (var ch in _fanControlService.Channels.Where(c => c.SourceType is not (FanSourceType.Motherboard or FanSourceType.Controller)))
        {
            ch.IsLocked100 = true;
            ch.SetPwm(100f);
            ch.LastPulseTime = DateTime.UtcNow;
        }
        BuildFanCards();
        UpdateFanUi();
        SaveFanControlSettings();
    }

    private void SyncGpuCurveSliders()
    {
        if (_fanControlService == null || _fanControlService.GpuCurve.Points.Count < 6) return;
        var pts = _fanControlService.GpuCurve.Points;

        if (FanGpuPoint1Temp != null) FanGpuPoint1Temp.Value = pts[0].Temp;
        if (FanGpuPoint1Pwm != null) FanGpuPoint1Pwm.Value = pts[0].Pwm;
        if (FanGpuPoint2Temp != null) FanGpuPoint2Temp.Value = pts[1].Temp;
        if (FanGpuPoint2Pwm != null) FanGpuPoint2Pwm.Value = pts[1].Pwm;
        if (FanGpuPoint3Temp != null) FanGpuPoint3Temp.Value = pts[2].Temp;
        if (FanGpuPoint3Pwm != null) FanGpuPoint3Pwm.Value = pts[2].Pwm;
        if (FanGpuPoint4Temp != null) FanGpuPoint4Temp.Value = pts[3].Temp;
        if (FanGpuPoint4Pwm != null) FanGpuPoint4Pwm.Value = pts[3].Pwm;
        if (FanGpuPoint5Temp != null) FanGpuPoint5Temp.Value = pts[4].Temp;
        if (FanGpuPoint5Pwm != null) FanGpuPoint5Pwm.Value = pts[4].Pwm;
        if (FanGpuPoint6Temp != null) FanGpuPoint6Temp.Value = pts[5].Temp;
        if (FanGpuPoint6Pwm != null) FanGpuPoint6Pwm.Value = pts[5].Pwm;

        if (FanGpuRampUpSlider != null) FanGpuRampUpSlider.Value = _fanControlService.GpuCurve.RampUpSeconds;
        if (FanGpuRampDownSlider != null) FanGpuRampDownSlider.Value = _fanControlService.GpuCurve.RampDownSeconds;
    }

    private void FanGpuPoint_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_fanControlInitialized || _fanControlService == null || _fanControlService.GpuCurve.Points.Count < 6) return;

        if (FanGpuPoint1Temp != null && FanGpuPoint1Pwm != null)
        {
            _fanControlService.GpuCurve.Points[0].Temp = FanGpuPoint1Temp.Value;
            _fanControlService.GpuCurve.Points[0].Pwm = FanGpuPoint1Pwm.Value;
        }
        if (FanGpuPoint2Temp != null && FanGpuPoint2Pwm != null)
        {
            _fanControlService.GpuCurve.Points[1].Temp = FanGpuPoint2Temp.Value;
            _fanControlService.GpuCurve.Points[1].Pwm = FanGpuPoint2Pwm.Value;
        }
        if (FanGpuPoint3Temp != null && FanGpuPoint3Pwm != null)
        {
            _fanControlService.GpuCurve.Points[2].Temp = FanGpuPoint3Temp.Value;
            _fanControlService.GpuCurve.Points[2].Pwm = FanGpuPoint3Pwm.Value;
        }
        if (FanGpuPoint4Temp != null && FanGpuPoint4Pwm != null)
        {
            _fanControlService.GpuCurve.Points[3].Temp = FanGpuPoint4Temp.Value;
            _fanControlService.GpuCurve.Points[3].Pwm = FanGpuPoint4Pwm.Value;
        }
        if (FanGpuPoint5Temp != null && FanGpuPoint5Pwm != null)
        {
            _fanControlService.GpuCurve.Points[4].Temp = FanGpuPoint5Temp.Value;
            _fanControlService.GpuCurve.Points[4].Pwm = FanGpuPoint5Pwm.Value;
        }
        if (FanGpuPoint6Temp != null && FanGpuPoint6Pwm != null)
        {
            _fanControlService.GpuCurve.Points[5].Temp = FanGpuPoint6Temp.Value;
            _fanControlService.GpuCurve.Points[5].Pwm = FanGpuPoint6Pwm.Value;
        }

        DrawGpuCurve();
        SaveFanControlSettings();
    }

    private void FanGpuHysteresis_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_fanControlService == null) return;

        if (FanGpuRampUpSlider != null && FanGpuRampUpLabel != null)
        {
            _fanControlService.GpuCurve.RampUpSeconds = FanGpuRampUpSlider.Value;
            FanGpuRampUpLabel.Text = $"{FanGpuRampUpSlider.Value:0} s";
        }
        if (FanGpuRampDownSlider != null && FanGpuRampDownLabel != null)
        {
            _fanControlService.GpuCurve.RampDownSeconds = FanGpuRampDownSlider.Value;
            FanGpuRampDownLabel.Text = $"{FanGpuRampDownSlider.Value:0} s";
        }
        SaveFanControlSettings();
    }

    // =========================================================================
    // GERENCIADOR DE PRÉ-DEFINIÇÕES (PRESETS) SALVAS DE CURVAS DE FANS
    // =========================================================================

    private bool _isUpdatingPresetSelection;

    private void PopulateFanPresets()
    {
        _isUpdatingPresetSelection = true;
        try
        {
            // 1. Presets de CPU
            if (CmbFanCpuPresets != null)
            {
                CmbFanCpuPresets.Items.Clear();
                CmbFanCpuPresets.Items.Add("Equilibrado");
                CmbFanCpuPresets.Items.Add("Silencioso");
                CmbFanCpuPresets.Items.Add("Desempenho");

                if (_settings.FanControl?.SavedCpuPresets != null)
                {
                    foreach (var p in _settings.FanControl.SavedCpuPresets)
                    {
                        if (!string.IsNullOrWhiteSpace(p.Name) && !CmbFanCpuPresets.Items.Contains(p.Name))
                        {
                            CmbFanCpuPresets.Items.Add(p.Name);
                        }
                    }
                }

                string currentName = _fanControlService?.CpuCurve.Name ?? "Equilibrado";
                if (CmbFanCpuPresets.Items.Contains(currentName))
                    CmbFanCpuPresets.SelectedItem = currentName;
                else
                    CmbFanCpuPresets.SelectedIndex = 0;
            }

            // 2. Presets de GPU
            if (CmbFanGpuPresets != null)
            {
                CmbFanGpuPresets.Items.Clear();
                CmbFanGpuPresets.Items.Add("Equilibrado");
                CmbFanGpuPresets.Items.Add("Silencioso");
                CmbFanGpuPresets.Items.Add("Desempenho");

                if (_settings.FanControl?.SavedGpuPresets != null)
                {
                    foreach (var p in _settings.FanControl.SavedGpuPresets)
                    {
                        if (!string.IsNullOrWhiteSpace(p.Name) && !CmbFanGpuPresets.Items.Contains(p.Name))
                        {
                            CmbFanGpuPresets.Items.Add(p.Name);
                        }
                    }
                }

                string currentGpuName = _fanControlService?.GpuCurve.Name ?? "Equilibrado";
                if (CmbFanGpuPresets.Items.Contains(currentGpuName))
                    CmbFanGpuPresets.SelectedItem = currentGpuName;
                else
                    CmbFanGpuPresets.SelectedIndex = 0;
            }
        }
        finally
        {
            _isUpdatingPresetSelection = false;
        }
    }

    private void BtnSaveFanCpuPreset_Click(object sender, RoutedEventArgs e)
    {
        if (_fanControlService == null) return;
        string name = TxtFanCpuPresetName?.Text?.Trim() ?? "";

        // Se o usuário não digitou um nome novo, mas já está com uma pré-definição personalizada selecionada no combo, reutiliza o nome existente para edição direta
        if (string.IsNullOrWhiteSpace(name))
        {
            string currentSelected = CmbFanCpuPresets?.SelectedItem?.ToString() ?? "";
            if (!string.IsNullOrWhiteSpace(currentSelected) && currentSelected is not ("Silencioso" or "Equilibrado" or "Desempenho"))
            {
                name = currentSelected;
            }
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show("Por favor, selecione uma pré-definição existente para editar ou digite um novo nome na lacuna de texto.", "Lumini Tray", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var config = new FanCurveSaveConfig
        {
            Name = name,
            TempSource = _fanControlService.CpuCurve.TempSource.ToString(),
            RampUpSeconds = _fanControlService.CpuCurve.RampUpSeconds,
            RampDownSeconds = _fanControlService.CpuCurve.RampDownSeconds,
            Points = _fanControlService.CpuCurve.Points.Select(p => new FanCurvePointSave(p.Temp, p.Pwm)).ToList()
        };

        _settings.FanControl ??= new FanControlSaveSettings();
        _settings.FanControl.SavedCpuPresets.RemoveAll(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        _settings.FanControl.SavedCpuPresets.Add(config);
        _fanControlService.CpuCurve.Name = name;
        SaveFanControlSettings();

        PopulateFanPresets();
        if (CmbFanCpuPresets != null) CmbFanCpuPresets.SelectedItem = name;
        if (TxtFanCpuPresetName != null) TxtFanCpuPresetName.Text = name;
        Toast("Lumini Tray", $"Pré-definição '{name}' salva com sucesso!", "\uE718");
    }

    private void CmbFanCpuPresets_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingPresetSelection || _fanControlService == null || CmbFanCpuPresets?.SelectedItem == null) return;
        string selected = CmbFanCpuPresets.SelectedItem.ToString()!;

        if (selected == "Silencioso")
        {
            if (TxtFanCpuPresetName != null) TxtFanCpuPresetName.Text = "";
            FanPresetQuiet_Click(this, new RoutedEventArgs());
            return;
        }
        if (selected == "Equilibrado")
        {
            if (TxtFanCpuPresetName != null) TxtFanCpuPresetName.Text = "";
            FanPresetBalanced_Click(this, new RoutedEventArgs());
            return;
        }
        if (selected == "Desempenho")
        {
            if (TxtFanCpuPresetName != null) TxtFanCpuPresetName.Text = "";
            FanPresetPerf_Click(this, new RoutedEventArgs());
            return;
        }

        var custom = _settings.FanControl?.SavedCpuPresets.FirstOrDefault(p => p.Name.Equals(selected, StringComparison.OrdinalIgnoreCase));
        if (custom != null && custom.Points.Count >= 2)
        {
            if (TxtFanCpuPresetName != null) TxtFanCpuPresetName.Text = custom.Name;
            SetActiveFanCpuPreset(null);
            _fanControlService.CpuCurve.Name = custom.Name;
            _fanControlService.CpuCurve.RampUpSeconds = custom.RampUpSeconds;
            _fanControlService.CpuCurve.RampDownSeconds = custom.RampDownSeconds;
            if (Enum.TryParse<CurveTempSource>(custom.TempSource, out var src))
                _fanControlService.CpuCurve.TempSource = src;
            _fanControlService.CpuCurve.Points = custom.Points.Select(p => new FanCurvePoint(p.Temp, p.Pwm)).ToList();

            SyncCpuCurveSliders();
            DrawCpuCurve();
            SaveFanControlSettings();
        }
    }

    private void BtnDeleteFanCpuPreset_Click(object sender, RoutedEventArgs e)
    {
        if (CmbFanCpuPresets?.SelectedItem == null) return;
        string selected = CmbFanCpuPresets.SelectedItem.ToString()!;
        if (selected is "Silencioso" or "Equilibrado" or "Desempenho")
        {
            MessageBox.Show("As pré-definições padrão do sistema não podem ser excluídas.", "Lumini Tray", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (MessageBox.Show($"Deseja excluir a pré-definição '{selected}'?", "Confirmar Exclusão", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            _settings.FanControl?.SavedCpuPresets.RemoveAll(p => p.Name.Equals(selected, StringComparison.OrdinalIgnoreCase));
            SaveFanControlSettings();
            PopulateFanPresets();
        }
    }

    private void BtnSaveFanGpuPreset_Click(object sender, RoutedEventArgs e)
    {
        if (_fanControlService == null) return;
        string name = TxtFanGpuPresetName?.Text?.Trim() ?? "";

        // Se o usuário não digitou um nome novo, mas já está com uma pré-definição personalizada selecionada no combo, reutiliza o nome existente para edição direta
        if (string.IsNullOrWhiteSpace(name))
        {
            string currentSelected = CmbFanGpuPresets?.SelectedItem?.ToString() ?? "";
            if (!string.IsNullOrWhiteSpace(currentSelected) && currentSelected is not ("Silencioso" or "Equilibrado" or "Desempenho"))
            {
                name = currentSelected;
            }
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show("Por favor, selecione uma pré-definição de GPU existente para editar ou digite um novo nome na lacuna de texto.", "Lumini Tray", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var config = new FanCurveSaveConfig
        {
            Name = name,
            TempSource = _fanControlService.GpuCurve.TempSource.ToString(),
            RampUpSeconds = _fanControlService.GpuCurve.RampUpSeconds,
            RampDownSeconds = _fanControlService.GpuCurve.RampDownSeconds,
            Points = _fanControlService.GpuCurve.Points.Select(p => new FanCurvePointSave(p.Temp, p.Pwm)).ToList()
        };

        _settings.FanControl ??= new FanControlSaveSettings();
        _settings.FanControl.SavedGpuPresets.RemoveAll(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        _settings.FanControl.SavedGpuPresets.Add(config);
        _fanControlService.GpuCurve.Name = name;
        SaveFanControlSettings();

        PopulateFanPresets();
        if (CmbFanGpuPresets != null) CmbFanGpuPresets.SelectedItem = name;
        if (TxtFanGpuPresetName != null) TxtFanGpuPresetName.Text = name;
        Toast("Lumini Tray", $"Pré-definição de GPU '{name}' salva com sucesso!", "\uE718");
    }

    private void CmbFanGpuPresets_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingPresetSelection || _fanControlService == null || CmbFanGpuPresets?.SelectedItem == null) return;
        string selected = CmbFanGpuPresets.SelectedItem.ToString()!;

        if (selected == "Silencioso")
        {
            if (TxtFanGpuPresetName != null) TxtFanGpuPresetName.Text = "";
            FanPresetGpuQuiet_Click(this, new RoutedEventArgs());
            return;
        }
        if (selected == "Equilibrado")
        {
            if (TxtFanGpuPresetName != null) TxtFanGpuPresetName.Text = "";
            FanPresetGpuBalanced_Click(this, new RoutedEventArgs());
            return;
        }
        if (selected == "Desempenho")
        {
            if (TxtFanGpuPresetName != null) TxtFanGpuPresetName.Text = "";
            FanPresetGpuPerf_Click(this, new RoutedEventArgs());
            return;
        }

        var custom = _settings.FanControl?.SavedGpuPresets.FirstOrDefault(p => p.Name.Equals(selected, StringComparison.OrdinalIgnoreCase));
        if (custom != null && custom.Points.Count >= 2)
        {
            if (TxtFanGpuPresetName != null) TxtFanGpuPresetName.Text = custom.Name;
            SetActiveFanGpuPreset(null);
            _fanControlService.GpuCurve.Name = custom.Name;
            _fanControlService.GpuCurve.RampUpSeconds = custom.RampUpSeconds;
            _fanControlService.GpuCurve.RampDownSeconds = custom.RampDownSeconds;
            if (Enum.TryParse<CurveTempSource>(custom.TempSource, out var src))
                _fanControlService.GpuCurve.TempSource = src;
            _fanControlService.GpuCurve.Points = custom.Points.Select(p => new FanCurvePoint(p.Temp, p.Pwm)).ToList();

            SyncGpuCurveSliders();
            DrawGpuCurve();
            SaveFanControlSettings();
        }
    }

    private void BtnDeleteFanGpuPreset_Click(object sender, RoutedEventArgs e)
    {
        if (CmbFanGpuPresets?.SelectedItem == null) return;
        string selected = CmbFanGpuPresets.SelectedItem.ToString()!;
        if (selected is "Silencioso" or "Equilibrado" or "Desempenho")
        {
            MessageBox.Show("As pré-definições padrão do sistema não podem ser excluídas.", "Lumini Tray", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (MessageBox.Show($"Deseja excluir a pré-definição de GPU '{selected}'?", "Confirmar Exclusão", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            _settings.FanControl?.SavedGpuPresets.RemoveAll(p => p.Name.Equals(selected, StringComparison.OrdinalIgnoreCase));
            SaveFanControlSettings();
            PopulateFanPresets();
        }
    }
}
