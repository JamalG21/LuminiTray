using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace LuminiTray;

/// <summary>
/// Aba RESET OC: módulo completo de Overclock e Undervolt para CPU (AMD Ryzen e Intel)
/// e GPU (NVIDIA, AMD Radeon, Intel Arc), além do verificador de estabilidade e guia da BIOS.
/// </summary>
public partial class MainWindow
{
    public sealed record GuideStep(string Number, string Title, string Where, string Text);

    private ICpuController? _cpuController;
    private IGpuController? _gpuController;

    private uint? _ocClockCap;
    private uint? _ocPendingCap;
    private CancellationTokenSource? _ocCts;
    private bool _ocRunning;

    private nint OcGpu
    {
        get
        {
            if (_gpus.Count <= 0) return IntPtr.Zero;
            return _gpus[0].NvmlHandle;
        }
    }

    private void InitOc()
    {
        // 1. Inicializa controladores especializados via Factory
        _cpuController = HardwareControllerFactory.GetCpuController();
        _gpuController = HardwareControllerFactory.GetGpuController();

        // Configuração da UI de CPU (AMD ou Intel)
        if (OcCpuTitle != null)
            OcCpuTitle.Text = $"CPU · {_cpuController.Name.ToUpperInvariant()}";

        if (_cpuController.Vendor == CpuVendor.Amd)
        {
            if (PanelAmdCpu != null) PanelAmdCpu.Visibility = Visibility.Visible;
            if (PanelIntelCpu != null) PanelIntelCpu.Visibility = Visibility.Collapsed;
            if (OcCpuVendorBadge != null) OcCpuVendorBadge.Text = "AMD RYZEN (SMU / Curve Optimizer)";

            // Valores iniciais dinâmicos de acordo com a CPU AMD detectada na máquina
            int stockPpt = _cpuController.StockPptW;
            int stockTdc = _cpuController.StockTdcA;
            int stockEdc = _cpuController.StockEdcA;
            int stockTemp = _cpuController.StockTempC;

            if (SliderAmdCurve != null) SliderAmdCurve.Value = 0;
            if (SliderAmdPpt != null)
            {
                SliderAmdPpt.Maximum = Math.Max(250, (int)Math.Round(stockPpt * 1.6));
                SliderAmdPpt.Value = stockPpt;
            }
            if (SliderAmdTdc != null)
            {
                SliderAmdTdc.Maximum = Math.Max(180, (int)Math.Round(stockTdc * 1.6));
                SliderAmdTdc.Value = stockTdc;
            }
            if (SliderAmdEdc != null)
            {
                SliderAmdEdc.Maximum = Math.Max(260, (int)Math.Round(stockEdc * 1.6));
                SliderAmdEdc.Value = stockEdc;
            }
            if (SliderAmdTemp != null) SliderAmdTemp.Value = stockTemp;

            // Rótulos dinâmicos dos botões de acordo com o hardware
            int ecoPpt = (int)Math.Max(35, Math.Round(stockPpt * 0.65));
            if (_cpuController.IsAm4X3d)
            {
                if (BannerAmdX3d != null) BannerAmdX3d.Visibility = Visibility.Visible;
                if (PanelAmdPboSliders != null) PanelAmdPboSliders.Visibility = Visibility.Collapsed;
                if (BtnAmdPresetEco != null) BtnAmdPresetEco.Content = "Eco (CO -15)";
                if (BtnAmdPresetBalanced != null) BtnAmdPresetBalanced.Content = "Equilibrado (Stock · CO 0)";
                if (BtnAmdPresetPerf != null) BtnAmdPresetPerf.Content = "Performance / Undervolt (CO -30)";
                if (BtnAmdResetDefault != null) BtnAmdResetDefault.Content = "Restaurar Padrão de Fábrica (CO 0)";
            }
            else
            {
                if (BannerAmdX3d != null) BannerAmdX3d.Visibility = Visibility.Collapsed;
                if (PanelAmdPboSliders != null) PanelAmdPboSliders.Visibility = Visibility.Visible;
                if (BtnAmdPresetEco != null) BtnAmdPresetEco.Content = $"Eco ({ecoPpt}W · CO -15)";
                if (BtnAmdPresetBalanced != null) BtnAmdPresetBalanced.Content = $"Equilibrado (Stock {stockPpt}W · CO 0)";
                if (BtnAmdPresetPerf != null) BtnAmdPresetPerf.Content = $"Performance / Undervolt ({stockPpt}W · CO -30)";
                if (BtnAmdResetDefault != null) BtnAmdResetDefault.Content = $"Restaurar Padrão de Fábrica ({stockPpt}W · CO 0)";
            }

            SetActiveAmdPreset(BtnAmdPresetBalanced);
        }
        else if (_cpuController.Vendor == CpuVendor.Intel)
        {
            if (PanelAmdCpu != null) PanelAmdCpu.Visibility = Visibility.Collapsed;
            if (PanelIntelCpu != null) PanelIntelCpu.Visibility = Visibility.Visible;
            if (OcCpuVendorBadge != null) OcCpuVendorBadge.Text = "INTEL CORE (FIVR / MSR)";

            // Valores iniciais dinâmicos de acordo com a CPU Intel detectada na máquina
            int stockPl1 = _cpuController.StockPl1Watts;
            int stockPl2 = _cpuController.StockPl2Watts;

            if (SliderIntelPl1 != null)
            {
                SliderIntelPl1.Maximum = Math.Max(300, (int)Math.Round(stockPl2 * 1.3));
                SliderIntelPl1.Value = stockPl1;
            }
            if (SliderIntelPl2 != null)
            {
                SliderIntelPl2.Maximum = Math.Max(350, (int)Math.Round(stockPl2 * 1.4));
                SliderIntelPl2.Value = stockPl2;
            }
            if (SliderIntelCoreUv != null) SliderIntelCoreUv.Value = 0;
            if (SliderIntelCacheUv != null) SliderIntelCacheUv.Value = 0;

            // Rótulos dinâmicos dos botões de acordo com o hardware
            int ecoPl1 = (int)Math.Max(15, Math.Round(stockPl1 * 0.6));
            if (BtnIntelPresetEco != null) BtnIntelPresetEco.Content = $"Eco (PL1 {ecoPl1}W · UV -50mV)";
            if (BtnIntelPresetBalanced != null) BtnIntelPresetBalanced.Content = $"Equilibrado (Stock {stockPl1}W/{stockPl2}W)";
            if (BtnIntelPresetPerf != null) BtnIntelPresetPerf.Content = $"Performance (PL1 {stockPl2}W · UV -75mV)";
            if (BtnIntelResetDefault != null) BtnIntelResetDefault.Content = $"Restaurar Padrão de Fábrica ({stockPl1}W/{stockPl2}W)";

            SetActiveIntelPreset(BtnIntelPresetBalanced);
        }

        // Configuração da UI de GPU
        if (OcGpuHeaderTitle != null)
            OcGpuHeaderTitle.Text = $"GPU · {_gpuController.Name.ToUpperInvariant()}";

        if (OcGpuVendorBadge != null)
        {
            OcGpuVendorBadge.Text = _gpuController.Vendor switch
            {
                LuminiTray.GpuVendor.Nvidia => "NVIDIA GEFORCE / RTX (NvAPI + NVML)",
                LuminiTray.GpuVendor.Amd => "AMD RADEON (ADL + SPPT)",
                LuminiTray.GpuVendor.Intel => "INTEL ARC / XE (IGCL)",
                _ => "GPU PADRÃO"
            };
        }

        if (PanelAmdSppt != null)
            PanelAmdSppt.Visibility = _gpuController.Vendor == LuminiTray.GpuVendor.Amd ? Visibility.Visible : Visibility.Collapsed;

        // Configuração legada / NVML
        nint ocGpu = OcGpu;
        if (OcGpuTitle != null)
            OcGpuTitle.Text = ((_gpus.Count > 0) ? ("GPU · " + GameCatalog.ShortGpuName(_gpus[0].Name).ToUpperInvariant()) : "GPU");

        if (ocGpu != IntPtr.Zero)
        {
            var tuple = Nvml.PowerConstraintsMw(ocGpu);
            if (tuple.HasValue && OcPower != null)
            {
                OcPower.Minimum = (double)tuple.Value.Item1 / 1000.0;
                OcPower.Maximum = (double)tuple.Value.Item2 / 1000.0;
            }
            uint? num = Nvml.PowerLimitMw(ocGpu);
            if (num.HasValue && OcPower != null)
            {
                OcPower.Value = (double)num.Value / 1000.0;
            }
            uint maxClk = Nvml.MaxClock(ocGpu, 0).GetValueOrDefault(3000u);
            if (OcClockPanel != null && OcClockPanel.Children.Count == 0)
            {
                Chip("Sem teto", null, on: true);
                Chip($"Leve · {maxClk - 300} MHz", maxClk - 300, on: false);
                Chip($"Equilibrado · {maxClk - 500} MHz", maxClk - 500, on: false);
                Chip($"Econômico · {maxClk - 800} MHz", maxClk - 800, on: false);
            }
            UpdateOcGpuState();
        }

        BuildOcGuide();
        UpdateOcHistory();
        UpdateRecentErrors();

        void Chip(string label, uint? cap, bool on)
        {
            RadioButton radioButton = new RadioButton
            {
                Content = label,
                GroupName = "OcClock",
                Style = (Style)FindResource("Chip"),
                IsChecked = on
            };
            radioButton.Checked += delegate { _ocPendingCap = cap; };
            OcClockPanel?.Children.Add(radioButton);
        }
    }

    // =========================================================================
    // Handlers CPU AMD (SMU & Curve Optimizer)
    // =========================================================================

    private void SliderAmdCurve_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (LabelAmdCurveValue == null) return;
        int val = (int)e.NewValue;
        LabelAmdCurveValue.Text = val == 0 ? "0 (Padrão)" : val < 0 ? $"{val} (Undervolt)" : $"+{val} (Overvolt)";
    }

    private void SliderAmdPpt_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (LabelAmdPptValue != null) LabelAmdPptValue.Text = $"{e.NewValue:0} W";
    }

    private void SliderAmdTdc_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (LabelAmdTdcValue != null) LabelAmdTdcValue.Text = $"{e.NewValue:0} A";
    }

    private void SliderAmdEdc_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (LabelAmdEdcValue != null) LabelAmdEdcValue.Text = $"{e.NewValue:0} A";
    }

    private void SliderAmdTemp_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (LabelAmdTempValue != null) LabelAmdTempValue.Text = $"{e.NewValue:0} °C";
    }

    private void SetActiveAmdPreset(Button? active)
    {
        Button?[] btns = { BtnAmdPresetEco, BtnAmdPresetBalanced, BtnAmdPresetPerf, BtnAmdResetDefault };
        foreach (var b in btns)
        {
            if (b == null) continue;
            b.Style = (Style)FindResource(b == active ? "PrimaryButton" : "OutlineButton");
        }
    }

    private void SetActiveIntelPreset(Button? active)
    {
        Button?[] btns = { BtnIntelPresetEco, BtnIntelPresetBalanced, BtnIntelPresetPerf, BtnIntelResetDefault };
        foreach (var b in btns)
        {
            if (b == null) continue;
            b.Style = (Style)FindResource(b == active ? "PrimaryButton" : "OutlineButton");
        }
    }

    private void BtnAmdPresetEco_Click(object sender, RoutedEventArgs e)
    {
        if (_cpuController == null) return;
        SetActiveAmdPreset(BtnAmdPresetEco);
        int ecoPpt = (int)Math.Max(35, Math.Round(_cpuController.StockPptW * 0.65));
        int ecoTdc = (int)Math.Max(25, Math.Round(_cpuController.StockTdcA * 0.65));
        int ecoEdc = (int)Math.Max(40, Math.Round(_cpuController.StockEdcA * 0.65));
        int ecoTemp = Math.Min(_cpuController.StockTempC, 80);

        if (SliderAmdCurve != null) SliderAmdCurve.Value = -15;
        if (SliderAmdPpt != null) SliderAmdPpt.Value = ecoPpt;
        if (SliderAmdTdc != null) SliderAmdTdc.Value = ecoTdc;
        if (SliderAmdEdc != null) SliderAmdEdc.Value = ecoEdc;
        if (SliderAmdTemp != null) SliderAmdTemp.Value = ecoTemp;
        BtnAmdApply_Click(sender, e);
    }

    private void BtnAmdPresetBalanced_Click(object sender, RoutedEventArgs e)
    {
        if (_cpuController == null) return;
        SetActiveAmdPreset(BtnAmdPresetBalanced);
        if (SliderAmdCurve != null) SliderAmdCurve.Value = 0;
        if (SliderAmdPpt != null) SliderAmdPpt.Value = _cpuController.StockPptW;
        if (SliderAmdTdc != null) SliderAmdTdc.Value = _cpuController.StockTdcA;
        if (SliderAmdEdc != null) SliderAmdEdc.Value = _cpuController.StockEdcA;
        if (SliderAmdTemp != null) SliderAmdTemp.Value = _cpuController.StockTempC;
        BtnAmdApply_Click(sender, e);
    }

    private void BtnAmdPresetPerf_Click(object sender, RoutedEventArgs e)
    {
        if (_cpuController == null) return;
        SetActiveAmdPreset(BtnAmdPresetPerf);
        if (SliderAmdCurve != null) SliderAmdCurve.Value = -30;
        if (SliderAmdPpt != null) SliderAmdPpt.Value = _cpuController.StockPptW;
        if (SliderAmdTdc != null) SliderAmdTdc.Value = _cpuController.StockTdcA;
        if (SliderAmdEdc != null) SliderAmdEdc.Value = _cpuController.StockEdcA;
        if (SliderAmdTemp != null) SliderAmdTemp.Value = _cpuController.StockTempC;
        BtnAmdApply_Click(sender, e);
    }

    private void BtnAmdResetDefault_Click(object sender, RoutedEventArgs e)
    {
        if (_cpuController == null) return;
        SetActiveAmdPreset(BtnAmdResetDefault);
        if (SliderAmdCurve != null) SliderAmdCurve.Value = 0;
        if (SliderAmdPpt != null) SliderAmdPpt.Value = _cpuController.StockPptW;
        if (SliderAmdTdc != null) SliderAmdTdc.Value = _cpuController.StockTdcA;
        if (SliderAmdEdc != null) SliderAmdEdc.Value = _cpuController.StockEdcA;
        if (SliderAmdTemp != null) SliderAmdTemp.Value = _cpuController.StockTempC;
        _cpuController.ResetToDefaults();
        if (TextAmdCpuResult != null)
        {
            TextAmdCpuResult.Text = $"Restaurado para o padrão de fábrica da CPU ({_cpuController.Name}: {_cpuController.StockPptW}W PPT · {_cpuController.StockTdcA}A TDC · {_cpuController.StockEdcA}A EDC · CO 0).";
            TextAmdCpuResult.Foreground = (Brush)FindResource("Accent");
        }
    }

    private void BtnAmdApply_Click(object sender, RoutedEventArgs e)
    {
        if (_cpuController == null) return;
        int co = (int)(SliderAmdCurve?.Value ?? 0);
        int ppt = (int)(SliderAmdPpt?.Value ?? 105);
        int tdc = (int)(SliderAmdTdc?.Value ?? 75);
        int edc = (int)(SliderAmdEdc?.Value ?? 115);
        int temp = (int)(SliderAmdTemp?.Value ?? 90);

        bool okLimits = _cpuController.ApplyAmdLimits(ppt, tdc, edc, temp);
        bool okCo = _cpuController.ApplyCurveOptimizer(co, null);

        if (TextAmdCpuResult != null)
        {
            if (okLimits || okCo)
            {
                TextAmdCpuResult.Text = $"Aplicado com sucesso: Curve Optimizer {co} · PPT {ppt}W · TDC {tdc}A · EDC {edc}A · Temp {temp}°C.";
                TextAmdCpuResult.Foreground = (Brush)FindResource("Accent");
            }
            else
            {
                TextAmdCpuResult.Text = "Comandos SMU enviados. Se o driver PawnIO não estiver aberto em modo administrador, reinicie o app como admin.";
                TextAmdCpuResult.Foreground = (Brush)FindResource("Muted");
            }
        }
    }

    // =========================================================================
    // Handlers CPU Intel (MSR & FIVR Undervolt)
    // =========================================================================

    private void SliderIntelPl1_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (LabelIntelPl1Value != null) LabelIntelPl1Value.Text = $"{e.NewValue:0} W";
    }

    private void SliderIntelPl2_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (LabelIntelPl2Value != null) LabelIntelPl2Value.Text = $"{e.NewValue:0} W";
    }

    private void SliderIntelCoreUv_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (LabelIntelCoreUvValue != null) LabelIntelCoreUvValue.Text = $"{e.NewValue:0} mV";
    }

    private void SliderIntelCacheUv_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (LabelIntelCacheUvValue != null) LabelIntelCacheUvValue.Text = $"{e.NewValue:0} mV";
    }

    private void BtnIntelPresetEco_Click(object sender, RoutedEventArgs e)
    {
        if (_cpuController == null) return;
        SetActiveIntelPreset(BtnIntelPresetEco);
        int ecoPl1 = (int)Math.Max(15, Math.Round(_cpuController.StockPl1Watts * 0.6));
        int ecoPl2 = (int)Math.Max(25, Math.Round(_cpuController.StockPl2Watts * 0.6));

        if (SliderIntelPl1 != null) SliderIntelPl1.Value = ecoPl1;
        if (SliderIntelPl2 != null) SliderIntelPl2.Value = ecoPl2;
        if (SliderIntelCoreUv != null) SliderIntelCoreUv.Value = -50;
        if (SliderIntelCacheUv != null) SliderIntelCacheUv.Value = -50;
        BtnIntelApply_Click(sender, e);
    }

    private void BtnIntelPresetBalanced_Click(object sender, RoutedEventArgs e)
    {
        if (_cpuController == null) return;
        SetActiveIntelPreset(BtnIntelPresetBalanced);
        if (SliderIntelPl1 != null) SliderIntelPl1.Value = _cpuController.StockPl1Watts;
        if (SliderIntelPl2 != null) SliderIntelPl2.Value = _cpuController.StockPl2Watts;
        if (SliderIntelCoreUv != null) SliderIntelCoreUv.Value = 0;
        if (SliderIntelCacheUv != null) SliderIntelCacheUv.Value = 0;
        BtnIntelApply_Click(sender, e);
    }

    private void BtnIntelPresetPerf_Click(object sender, RoutedEventArgs e)
    {
        if (_cpuController == null) return;
        SetActiveIntelPreset(BtnIntelPresetPerf);
        if (SliderIntelPl1 != null) SliderIntelPl1.Value = _cpuController.StockPl2Watts;
        if (SliderIntelPl2 != null) SliderIntelPl2.Value = _cpuController.StockPl2Watts;
        if (SliderIntelCoreUv != null) SliderIntelCoreUv.Value = -75;
        if (SliderIntelCacheUv != null) SliderIntelCacheUv.Value = -75;
        BtnIntelApply_Click(sender, e);
    }

    private void BtnIntelResetDefault_Click(object sender, RoutedEventArgs e)
    {
        if (_cpuController == null) return;
        SetActiveIntelPreset(BtnIntelResetDefault);
        if (SliderIntelPl1 != null) SliderIntelPl1.Value = _cpuController.StockPl1Watts;
        if (SliderIntelPl2 != null) SliderIntelPl2.Value = _cpuController.StockPl2Watts;
        if (SliderIntelCoreUv != null) SliderIntelCoreUv.Value = 0;
        if (SliderIntelCacheUv != null) SliderIntelCacheUv.Value = 0;
        _cpuController.ResetToDefaults();
        if (TextIntelCpuResult != null)
        {
            TextIntelCpuResult.Text = $"Restaurado para o padrão de fábrica da CPU ({_cpuController.Name}: PL1 {_cpuController.StockPl1Watts}W · PL2 {_cpuController.StockPl2Watts}W · Offset 0mV).";
            TextIntelCpuResult.Foreground = (Brush)FindResource("Accent");
        }
    }

    private void BtnIntelApply_Click(object sender, RoutedEventArgs e)
    {
        if (_cpuController == null) return;
        int pl1 = (int)(SliderIntelPl1?.Value ?? 125);
        int pl2 = (int)(SliderIntelPl2?.Value ?? 180);
        int coreMv = (int)(SliderIntelCoreUv?.Value ?? 0);
        int cacheMv = (int)(SliderIntelCacheUv?.Value ?? 0);

        bool okTdp = _cpuController.ApplyIntelLimits(pl1, pl2, 28);
        bool okVolt = _cpuController.ApplyIntelVoltageOffset(coreMv, cacheMv, 0);

        if (TextIntelCpuResult != null)
        {
            if (okTdp || okVolt)
            {
                TextIntelCpuResult.Text = $"Aplicado via MSR Ring 0: PL1 {pl1}W · PL2 {pl2}W · Core {coreMv}mV · Cache {cacheMv}mV.";
                TextIntelCpuResult.Foreground = (Brush)FindResource("Accent");
            }
            else
            {
                TextIntelCpuResult.Text = "Comandos MSR enviados. Verifique se o recurso de Undervolt Protection está desabilitado na BIOS.";
                TextIntelCpuResult.Foreground = (Brush)FindResource("Muted");
            }
        }
    }

    // =========================================================================
    // Handlers GPU (Overclock, Power Limit, VFP Undervolt Lock)
    // =========================================================================

    private void SliderGpuCoreOffset_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (LabelGpuCoreOffset != null) LabelGpuCoreOffset.Text = $"{e.NewValue:+0;-0;0} MHz";
    }

    private void SliderGpuMemOffset_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (LabelGpuMemOffset != null) LabelGpuMemOffset.Text = $"{e.NewValue:+0;-0;0} MHz";
    }

    private void SliderGpuPowerPercent_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (LabelGpuPowerPercent != null) LabelGpuPowerPercent.Text = $"{e.NewValue:0}%";
    }

    private void SliderGpuTempLimit_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (LabelGpuTempLimit != null) LabelGpuTempLimit.Text = $"{e.NewValue:0} °C";
    }

    private void SliderGpuVfpLock_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (LabelGpuVfpLock != null) LabelGpuVfpLock.Text = $"{e.NewValue:0} MHz";
    }

    private void BtnGpuApplyOc_Click(object sender, RoutedEventArgs e)
    {
        if (_gpuController == null) return;
        int core = (int)(SliderGpuCoreOffset?.Value ?? 0);
        int mem = (int)(SliderGpuMemOffset?.Value ?? 0);
        int power = (int)(SliderGpuPowerPercent?.Value ?? 100);
        int temp = (int)(SliderGpuTempLimit?.Value ?? 83);

        bool okClocks = _gpuController.SetClocks(core, mem);
        bool okPower = _gpuController.SetPowerAndTempLimits(power, temp);

        if (TextGpuOcResult != null)
        {
            TextGpuOcResult.Text = $"GPU configurada: Core {core:+0;-0;0} MHz · VRAM {mem:+0;-0;0} MHz · Power {power}% · Temp {temp}°C.";
            TextGpuOcResult.Foreground = (Brush)FindResource("Accent");
        }
    }

    private void BtnGpuLockVfp_Click(object sender, RoutedEventArgs e)
    {
        if (_gpuController == null) return;
        int clock = (int)(SliderGpuVfpLock?.Value ?? 2500);
        bool ok = _gpuController.SetVfpLock(clock, 0);

        if (TextGpuOcResult != null)
        {
            TextGpuOcResult.Text = ok
                ? $"P-State travado em {clock} MHz para undervolt estável. Tensão e frequência estabilizadas."
                : $"Comando de trava de clock enviado ({clock} MHz).";
            TextGpuOcResult.Foreground = (Brush)FindResource("Accent");
        }
    }

    private void BtnGpuUnlockVfp_Click(object sender, RoutedEventArgs e)
    {
        if (_gpuController == null) return;
        _gpuController.ResetVfpLock();
        if (TextGpuOcResult != null)
        {
            TextGpuOcResult.Text = "Trava de clock removida. GPU retornou ao gerenciamento dinâmico de frequências.";
            TextGpuOcResult.Foreground = (Brush)FindResource("Muted");
        }
    }

    private void BtnGpuResetDefaults_Click(object sender, RoutedEventArgs e)
    {
        if (_gpuController == null) return;
        _gpuController.ResetToDefaults();

        if (SliderGpuCoreOffset != null) SliderGpuCoreOffset.Value = 0;
        if (SliderGpuMemOffset != null) SliderGpuMemOffset.Value = 0;
        if (SliderGpuPowerPercent != null) SliderGpuPowerPercent.Value = 100;
        if (SliderGpuTempLimit != null) SliderGpuTempLimit.Value = 83;

        if (TextGpuOcResult != null)
        {
            TextGpuOcResult.Text = "Configurações da placa de vídeo restauradas para os padrões de fábrica.";
            TextGpuOcResult.Foreground = (Brush)FindResource("Muted");
        }
    }

    private void BtnAmdSpptBackup_Click(object sender, RoutedEventArgs e)
    {
        byte[]? sppt = AdlNative.ReadSpptTable("0000");
        if (TextAmdSpptStatus != null)
        {
            TextAmdSpptStatus.Text = sppt != null && sppt.Length > 0
                ? $"SPPT detectada no Registro: {sppt.Length} bytes gravados."
                : "Nenhuma Soft Power Play Table personalizada ativa (VBIOS original em uso).";
        }
    }

    private void BtnAmdSpptReset_Click(object sender, RoutedEventArgs e)
    {
        bool ok = AdlNative.DeleteSpptTable("0000");
        if (TextAmdSpptStatus != null)
        {
            TextAmdSpptStatus.Text = ok
                ? "SPPT removida com sucesso do Registro. Reinicie o computador para aplicar os padrões originais da VBIOS."
                : "Não foi possível remover a SPPT (execute como administrador).";
        }
    }

    // =========================================================================
    // Métodos legados de GPU / NVML
    // =========================================================================

    private void UpdateOcGpuState()
    {
        nint ocGpu = OcGpu;
        if (ocGpu != IntPtr.Zero && OcGpuState != null)
        {
            uint? num = Nvml.PowerLimitMw(ocGpu);
            string value = num.HasValue ? $"{(double)num.Value / 1000.0:0} W" : "—";
            num = Nvml.PowerDefaultMw(ocGpu);
            string value2 = num.HasValue ? $"{(double)num.Value / 1000.0:0} W" : "—";
            string obj3 = $"Limite de energia atual {value} (padrão {value2}) · teto de clock: ";
            num = _ocClockCap;
            string obj4 = num.HasValue ? $"{num.Value} MHz" : "nenhum";
            num = Nvml.Clock(ocGpu, 0);
            string obj5 = num.HasValue ? $"{num.Value} MHz" : "—";
            OcGpuState.Text = obj3 + obj4 + " · clock máximo atual: " + obj5;
        }
    }

    private void OcPower_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (OcPowerValue != null && OcPower != null)
            OcPowerValue.Text = $"{OcPower.Value:0} W";
    }

    private void BtnOcApply_Click(object sender, RoutedEventArgs e)
    {
        nint ocGpu = OcGpu;
        if (ocGpu == IntPtr.Zero || !RequireAdmin() || OcPower == null) return;

        List<string> list = new();
        int code = Nvml.SetPowerLimitMw(ocGpu, (uint)(OcPower.Value * 1000.0));
        string? text = Nvml.Explain(code);
        list.Add((text != null) ? ("Energia: não aplicado (" + text + ").") : $"Energia: {OcPower.Value:0} W aplicado.");

        uint? ocPendingCap = _ocPendingCap;
        int code2;
        if (ocPendingCap.HasValue)
        {
            code2 = Nvml.LockGpuClocks(ocGpu, 210u, ocPendingCap.Value);
        }
        else
        {
            code2 = Nvml.ResetGpuClocks(ocGpu);
        }
        string? text2 = Nvml.Explain(code2);
        if (text2 != null)
        {
            list.Add("Teto de clock: não aplicado (" + text2 + ").");
        }
        else
        {
            _ocClockCap = _ocPendingCap;
            list.Add(_ocClockCap.HasValue ? $"Teto de clock: {_ocClockCap.Value} MHz aplicado." : "Teto de clock: removido.");
        }
        if (OcGpuResult != null)
            OcGpuResult.Text = string.Join(" ", list) + " Rode o verificador para confirmar a estabilidade.";
        UpdateOcGpuState();
    }

    private void BtnOcReset_Click(object sender, RoutedEventArgs e)
    {
        nint ocGpu = OcGpu;
        if (ocGpu == IntPtr.Zero || !RequireAdmin()) return;

        uint? num = Nvml.PowerDefaultMw(ocGpu);
        int code = num.HasValue ? Nvml.SetPowerLimitMw(ocGpu, num.Value) : 3;
        Nvml.ResetGpuClocks(ocGpu);
        if (num.HasValue && OcPower != null)
            OcPower.Value = (double)num.Value / 1000.0;
        _ocClockCap = null;
        _ocPendingCap = null;
        if (OcClockPanel != null)
        {
            foreach (RadioButton item in OcClockPanel.Children.OfType<RadioButton>())
                item.IsChecked = item.Content.ToString() == "Sem teto";
        }

        if (OcGpuResult != null)
            OcGpuResult.Text = "Configurações restauradas aos padrões do driver.";
        UpdateOcGpuState();
    }

    private void UpdateOcHistory()
    {
        if (OcHistory == null) return;
        var list = _settings.BenchRecords.Where(r => r.Test == "oc").Take(5).ToList();
        OcHistory.ItemsSource = list;
    }

    private void UpdateRecentErrors()
    {
        if (OcRecentErrors == null) return;
        var err = OcCheck.ErrorsSince(DateTime.Now.AddHours(-24));
        if (err.Total == 0)
        {
            OcRecentErrors.Text = "Nenhum erro de hardware registrado nas últimas 24h (WHEA e crashes de driver).";
            OcRecentErrors.Foreground = (Brush)FindResource("Muted");
            return;
        }
        OcRecentErrors.Text = $"{err.Total} erro(s) nas últimas 24h ({err.Whea} WHEA, {err.GpuResets} resets de GPU)." +
            (string.IsNullOrEmpty(err.LastDetail) ? "" : $" Último: {err.LastDetail}");
        OcRecentErrors.Foreground = (Brush)(TryFindResource("Danger") ?? Brushes.Red);
    }

    private async void BtnOcCheck_Click(object sender, RoutedEventArgs e)
    {
        if (_ocRunning)
        {
            _ocCts?.Cancel();
            return;
        }
        if (OcDurPanel == null) return;
        int minutes = int.TryParse(CheckedTag(OcDurPanel), out int m) ? m : 15;
        TimeSpan duration = TimeSpan.FromMinutes(minutes);

        _ocRunning = true;
        _ocCts = new CancellationTokenSource();
        CancellationToken ct = _ocCts.Token;

        BtnOcCheck.Content = "PARAR TESTE";
        BtnOcCheck.Style = (Style)(TryFindResource("DangerButton") ?? FindResource("PrimaryButton"));
        if (OcVerdictCard != null) OcVerdictCard.Visibility = Visibility.Collapsed;

        Stopwatch sw = Stopwatch.StartNew();
        StressTester stress = new StressTester();
        string fail = "";
        var startErrors = OcCheck.ErrorsSince(DateTime.Now);

        try
        {
            stress.Start(cpu: true, ram: true);
            while (sw.Elapsed < duration && !ct.IsCancellationRequested)
            {
                if (OcProgress != null) OcProgress.ScaleX = sw.Elapsed / duration;
                if (OcStatus != null)
                {
                    OcStatus.Text = $"Verificando estabilidade · {sw.Elapsed:mm\\:ss} de {duration:mm\\:ss}";
                }
                if (stress.MemoryErrors > 0)
                {
                    fail = $"{stress.MemoryErrors} erros de memória (RAM/EXPO instável)";
                    break;
                }
                var curErrors = OcCheck.ErrorsSince(DateTime.Now.AddMinutes(-minutes));
                if (curErrors.Total > startErrors.Total)
                {
                    fail = $"{curErrors.Total - startErrors.Total} novos erros de hardware detectados (WHEA / Driver)";
                    break;
                }
                await Task.Delay(1000, ct);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            fail = $"Falha no verificador: {ex.Message}";
        }
        finally
        {
            try { stress.Stop(); } catch { }
            _ocRunning = false;
            BtnOcCheck.Content = "INICIAR VERIFICAÇÃO";
            BtnOcCheck.Style = (Style)(TryFindResource("PrimaryButton") ?? FindResource("PrimaryButton"));
            if (OcProgress != null) OcProgress.ScaleX = 0;
        }

        bool passed = string.IsNullOrEmpty(fail) && !ct.IsCancellationRequested;
        if (OcVerdictCard != null)
        {
            OcVerdictCard.Visibility = Visibility.Visible;
            if (OcVerdict != null)
            {
                OcVerdict.Text = passed ? "ESTÁVEL" : "INSTÁVEL";
                OcVerdict.Foreground = passed ? (Brush)(TryFindResource("Accent") ?? Brushes.Cyan) : (Brush)(TryFindResource("Danger") ?? Brushes.Red);
            }
            if (OcVerdictWhy != null)
            {
                OcVerdictWhy.Text = passed
                    ? $"Nenhum erro em {duration.TotalMinutes:0} min de stress. Para Curve Optimizer, use o PC normalmente por um dia e confira os erros recentes."
                    : $"Motivo: {fail}. Volte um passo no ajuste e teste de novo.";
            }
        }

        _settings.BenchRecords.Insert(0, new BenchRecord(DateTime.Now, "oc", passed ? 1.0 : 0.15, passed ? "Estável" : fail));
        _settings.Save();
        UpdateOcHistory();
        UpdateRecentErrors();
    }

    private void BuildOcGuide()
    {
        var tuple = OcCheck.Motherboard();
        string item = tuple.Maker;
        string item2 = tuple.Model;
        bool flag = item.Contains("Gigabyte", StringComparison.OrdinalIgnoreCase);
        bool flag2 = item.Contains("ASUS", StringComparison.OrdinalIgnoreCase);
        bool flag3 = item.Contains("Micro-Star", StringComparison.OrdinalIgnoreCase) || item.Contains("MSI", StringComparison.OrdinalIgnoreCase);
        string text = GameCatalog.ShortCpuName(_cpuName);
        bool isRyzen = _cpuName.Contains("Ryzen", StringComparison.OrdinalIgnoreCase);
        string text2 = ((flag ? "Gigabyte" : (item.Split(' ').FirstOrDefault() ?? "")) + " " + item2).Trim();
        if (OcGuideTitle != null)
            OcGuideTitle.Text = "GUIA: OVERCLOCK NA BIOS · " + text2.ToUpperInvariant() + " · " + text.ToUpperInvariant();
        if (OcGuideIntro != null)
            OcGuideIntro.Text = "Faça uma mudança por vez e rode o verificador depois de cada uma. Os menus podem mudar um pouco com a versão da BIOS. Valores são pontos de partida seguros, não garantia: cada chip é diferente.";
        string text3 = (flag ? "Tweaker → Advanced CPU Settings → Precision Boost Overdrive  (ou Settings → AMD Overclocking)" : (flag2 ? "Advanced → AMD Overclocking → Precision Boost Overdrive  (ou AI Tweaker → PBO)" : (flag3 ? "OC → Advanced CPU Configuration → AMD Overclocking → Precision Boost Overdrive" : "Advanced → AMD Overclocking → Precision Boost Overdrive")));
        string where = (flag ? "Tweaker → Extreme Memory Profile (X.M.P.) → EXPO1" : (flag2 ? "AI Tweaker → Ai Overclock Tuner → EXPO I" : (flag3 ? "OC → A-XMP/EXPO → Profile 1" : "procure EXPO / XMP no menu de memória")));
        List<GuideStep> list = new List<GuideStep>
        {
            new GuideStep("0", "Antes de começar", flag ? "Save & Exit → Save Profiles" : "Save & Exit → Save Profile", "Salve o perfil atual da BIOS. Se o PC não ligar depois de um ajuste, espere 3 tentativas (muitas placas voltam sozinhas) ou limpe a CMOS (jumper CLR_CMOS na placa ou tirando a bateria por 1 minuto)."),
            new GuideStep("1", "Ative o perfil da memória (EXPO)", where, "É o maior ganho em jogos para a maioria dos PCs: a RAM sai do padrão lento e vai para a velocidade da etiqueta. Teste: verificador \"CPU + RAM\" por 15 min (o teste de RAM acusa erro se o perfil não for estável).")
        };
        if (isRyzen)
        {
            list.Add(new GuideStep("2", "Precision Boost Overdrive (PBO)", text3, "Precision Boost Overdrive → Advanced. PBO Limits → Motherboard. Isso libera os limites de energia para o boost durar mais. Teste com o verificador."));
            list.Add(new GuideStep("3", "Curve Optimizer (undervolt da CPU)", text3 + " → Curve Optimizer", "All Cores → Negative → 10. Teste. Se passar, tente 15, 20, 25… até falhar; volte 5 a partir de onde falhou. No " + text + " costuma ficar entre -15 e -30. Instabilidade de Curve Optimizer aparece muito em uso leve: use o PC normalmente por um dia e veja \"Erros de hardware recentes\" aqui — erro WHEA = volte um passo."));
            list.Add(new GuideStep("4", "Max CPU Boost Clock Override (opcional)", text3, "Depois do Curve Optimizer estável: +100 MHz, teste; depois +200 MHz. Ganho pequeno; pode exigir reduzir o Curve Optimizer."));
            list.Add(new GuideStep("5", "Alternativa fria: Eco Mode", text3 + " → Eco Mode", "Em vez de mais desempenho: limita a CPU a 65 W (ou 105 W). Perde ~5–10% em tarefas pesadas multi-core, quase nada em jogos, e roda bem mais frio e silencioso."));
        }
        else
        {
            list.Add(new GuideStep("2", "Processador Intel", "Tweaker / Ai Tweaker / OC → CPU", "Para Intel, prefira undervolt (offset negativo de tensão) e limites de energia (PL1/PL2) em vez de aumentar multiplicadores. Teste cada passo com o verificador."));
        }
        if (OcGuide != null)
            OcGuide.ItemsSource = list;
    }
}
