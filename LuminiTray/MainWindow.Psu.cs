using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace LuminiTray;

/// <summary>
/// Módulo da Calculadora de Fonte (PSU) e Simulador de Upgrades de Hardware na aba Sistema.
/// Permite estimar a potência de pico necessária, recomendar a capacidade ideal de fonte
/// e simular trocas de processador, placa de vídeo, refrigeração e discos.
/// </summary>
public partial class MainWindow
{
    private bool _psuInitialized = false;
    private bool _isFilteringPsu = false;
    private int _psuNvmeCount = 1;
    private int _psuSataSsdCount = 0;
    private int _psuHddCount = 0;
    private int _psuFansCount = 4;
    private int _detectedSystemPeakWatts = 0;

    /// <summary>
    /// Botão de atalho rápido no cabeçalho de Sistema para abrir a Calculadora de Fonte.
    /// </summary>
    private void BtnPsuCalc_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (InfoCategories.ItemsSource is IEnumerable<InfoNode> cats)
            {
                var psuNode = cats.FirstOrDefault(c => c.Header.StartsWith("Calculadora de Fonte"));
                if (psuNode != null)
                {
                    if (InfoCategories.SelectedItem == psuNode)
                    {
                        OpenPsuCalculatorView();
                    }
                    else
                    {
                        InfoCategories.SelectedItem = psuNode;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[PSU] Erro ao abrir atalho da Calculadora de Fonte: {ex}");
        }
    }

    /// <summary>
    /// Exibe a visualização da Calculadora de Fonte e inicializa os controles.
    /// </summary>
    private void OpenPsuCalculatorView()
    {
        try
        {
            InfoTitle.Text = "Calculadora de Fonte (PSU) & Simulador de Upgrades";
            if (DrivesPanel != null) DrivesPanel.Visibility = Visibility.Collapsed;
            if (InfoDetails != null) InfoDetails.Visibility = Visibility.Collapsed;
            if (PsuCalculatorPanel != null)
            {
                PsuCalculatorPanel.Visibility = Visibility.Visible;
                InitPsuCalculator();
            }
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[PSU] Erro em OpenPsuCalculatorView: {ex}");
        }
    }

    /// <summary>
    /// Popula os ComboBoxes e carrega os componentes detectados no PC atual.
    /// </summary>
    private void InitPsuCalculator()
    {
        try
        {
            if (_psuInitialized)
            {
                RecalculatePsu();
                return;
            }

            _psuInitialized = true;

            if (CmbPsuMobo != null)
            {
                CmbPsuMobo.ItemsSource = null;
                if (CmbPsuMobo.Items.Count > 0)
                {
                    try { CmbPsuMobo.Items.Clear(); } catch { }
                }
                CmbPsuMobo.ItemsSource = PsuCalculator.MotherboardList;
            }

            if (CmbPsuGpu != null)
            {
                CmbPsuGpu.ItemsSource = null;
                if (CmbPsuGpu.Items.Count > 0)
                {
                    try { CmbPsuGpu.Items.Clear(); } catch { }
                }
                CmbPsuGpu.ItemsSource = PsuCalculator.GpuList;
            }

            ResetPsuToDetectedComponents();
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[PSU] Erro ao inicializar Calculadora de Fonte: {ex}");
        }
    }

    /// <summary>
    /// Filtra a lista de CPUs e opções de Memória RAM compatíveis com a placa-mãe / plataforma selecionada.
    /// </summary>
    private void ApplyMotherboardFilter(PsuComponentItem? selectedMobo, bool preserveSelection = true)
    {
        if (selectedMobo == null) return;

        try
        {
            // Filtra CPUs compatíveis
            var compCpus = PsuCalculator.GetCompatibleCpus(selectedMobo);
            string? prevCpuName = (CmbPsuCpu?.SelectedItem as PsuComponentItem)?.Name;

            if (CmbPsuCpu != null)
            {
                CmbPsuCpu.ItemsSource = null;
                if (CmbPsuCpu.Items.Count > 0)
                {
                    try { CmbPsuCpu.Items.Clear(); } catch { }
                }
                CmbPsuCpu.ItemsSource = compCpus;
                if (preserveSelection && !string.IsNullOrEmpty(prevCpuName))
                {
                    var match = compCpus.FirstOrDefault(c => c.Name == prevCpuName);
                    CmbPsuCpu.SelectedItem = match ?? compCpus.FirstOrDefault();
                }
                else
                {
                    CmbPsuCpu.SelectedItem = compCpus.FirstOrDefault();
                }
            }

            // Filtra Memória RAM compatível
            var compRam = PsuCalculator.GetCompatibleRam(selectedMobo);
            string? prevRamTag = null;
            if (CmbPsuRam?.SelectedItem is PsuRamOption ramOpt)
            {
                prevRamTag = ramOpt.Tag;
            }
            else if (CmbPsuRam?.SelectedItem is ComboBoxItem cbi)
            {
                prevRamTag = cbi.Tag?.ToString();
            }

            if (CmbPsuRam != null)
            {
                CmbPsuRam.ItemsSource = null;
                if (CmbPsuRam.Items.Count > 0)
                {
                    try { CmbPsuRam.Items.Clear(); } catch { }
                }
                CmbPsuRam.ItemsSource = compRam;
                if (preserveSelection && !string.IsNullOrEmpty(prevRamTag))
                {
                    var match = compRam.FirstOrDefault(r => r.Tag == prevRamTag);
                    CmbPsuRam.SelectedItem = match ?? compRam.FirstOrDefault(r => r.IsDefault) ?? compRam.FirstOrDefault();
                }
                else
                {
                    CmbPsuRam.SelectedItem = compRam.FirstOrDefault(r => r.IsDefault) ?? compRam.FirstOrDefault();
                }
            }
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[PSU] Erro em ApplyMotherboardFilter: {ex}");
        }
    }

    /// <summary>
    /// Redefine as seleções para os componentes detectados no computador do usuário.
    /// </summary>
    private void ResetPsuToDetectedComponents()
    {
        if (CmbPsuMobo == null || CmbPsuCpu == null || CmbPsuGpu == null) return;

        _isFilteringPsu = true;
        try
        {
            // 1. Detecta Placa-Mãe atual (identifica chipset desktop ou plataforma de notebook)
            string boardText = "";
            try
            {
                var (maker, model) = OcCheck.Motherboard();
                boardText = $"{maker} {model}".Trim();
            }
            catch { }
            var detectedMobo = PsuCalculator.DetectCurrentMotherboard(boardText, _cpuName);
            var moboMatch = PsuCalculator.MotherboardList.FirstOrDefault(m => m.Name == detectedMobo.Name) ?? detectedMobo;
            CmbPsuMobo.SelectedItem = moboMatch;

            // 2. Aplica filtro inteligente de compatibilidade para CPU e RAM baseado na placa-mãe
            ApplyMotherboardFilter(moboMatch, preserveSelection: false);

            // 3. Detecta CPU atual e seleciona na lista compatível
            var detectedCpu = PsuCalculator.DetectCurrentCpu(_cpuName);
            if (CmbPsuCpu.ItemsSource is List<PsuComponentItem> compCpus)
            {
                var cpuMatch = compCpus.FirstOrDefault(c => c.Name == detectedCpu.Name)
                               ?? compCpus.FirstOrDefault(c => c.Category == detectedCpu.Category)
                               ?? compCpus.FirstOrDefault()
                               ?? detectedCpu;
                CmbPsuCpu.SelectedItem = cpuMatch;
            }
            else
            {
                CmbPsuCpu.SelectedItem = detectedCpu;
            }

            // 4. Detecta GPU atual
            var detectedGpu = PsuCalculator.DetectCurrentGpu(_gpuName);
            var gpuMatch = PsuCalculator.GpuList.FirstOrDefault(g => g.Name == detectedGpu.Name) ?? detectedGpu;
            CmbPsuGpu.SelectedItem = gpuMatch;

            // 5. Cooler padrão
            if (CmbPsuCooler != null && CmbPsuCooler.Items.Count > 0)
            {
                CmbPsuCooler.SelectedIndex = 0;
            }

            // Se for plataforma de Notebook, ajusta ventoinhas e discos para perfil de notebook
            bool isLaptop = (moboMatch != null && moboMatch.Category == "Notebook") ||
                            (detectedCpu != null && (detectedCpu.Category == "Notebook" || detectedCpu.Category.Contains("Mobile")));
            if (isLaptop)
            {
                _psuFansCount = 0; // Ventoinhas internas já incluídas no envelope de refrigeração OEM
                _psuNvmeCount = 1;
                _psuSataSsdCount = 0;
                _psuHddCount = 0;
            }
            else
            {
                _psuFansCount = 4;
                _psuNvmeCount = 1;
                _psuSataSsdCount = 0;
                _psuHddCount = 0;
            }

            if (TxtPsuNvmeCount != null) TxtPsuNvmeCount.Text = _psuNvmeCount.ToString();
            if (TxtPsuSataSsdCount != null) TxtPsuSataSsdCount.Text = _psuSataSsdCount.ToString();
            if (TxtPsuHddCount != null) TxtPsuHddCount.Text = _psuHddCount.ToString();
            if (TxtPsuFansCount != null) TxtPsuFansCount.Text = _psuFansCount.ToString();

            if (ChkPsuOc != null) ChkPsuOc.IsChecked = false;
            if (ChkPsuRgb != null) ChkPsuRgb.IsChecked = !isLaptop;
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[PSU] Erro em ResetPsuToDetectedComponents: {ex}");
        }
        finally
        {
            _isFilteringPsu = false;
        }

        RecalculatePsu();

        // Salva o consumo base do sistema detectado para calcular deltas de upgrade
        if (int.TryParse(TxtPsuPeakWatts?.Text, out int peak))
        {
            _detectedSystemPeakWatts = peak;
        }
    }

    private void BtnPsuResetDetected_Click(object sender, RoutedEventArgs e)
    {
        ResetPsuToDetectedComponents();
    }

    private void CmbPsuComponent_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_psuInitialized || _isFilteringPsu) return;

        // Quando o usuário troca a placa-mãe, atualiza inteligentemente as opções de CPU e RAM compatíveis
        if (ReferenceEquals(sender, CmbPsuMobo))
        {
            _isFilteringPsu = true;
            try
            {
                var selectedMobo = CmbPsuMobo.SelectedItem as PsuComponentItem;
                ApplyMotherboardFilter(selectedMobo, preserveSelection: true);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[PSU] Erro ao trocar placa-mãe: {ex}");
            }
            finally
            {
                _isFilteringPsu = false;
            }
        }

        RecalculatePsu();
    }

    private void PsuOption_Click(object sender, RoutedEventArgs e)
    {
        if (!_psuInitialized) return;
        RecalculatePsu();
    }

    private void BtnPsuNvmeMinus_Click(object sender, RoutedEventArgs e)
    {
        if (_psuNvmeCount > 0)
        {
            _psuNvmeCount--;
            if (TxtPsuNvmeCount != null) TxtPsuNvmeCount.Text = _psuNvmeCount.ToString();
            RecalculatePsu();
        }
    }

    private void BtnPsuNvmePlus_Click(object sender, RoutedEventArgs e)
    {
        if (_psuNvmeCount < 8)
        {
            _psuNvmeCount++;
            if (TxtPsuNvmeCount != null) TxtPsuNvmeCount.Text = _psuNvmeCount.ToString();
            RecalculatePsu();
        }
    }

    private void BtnPsuSataSsdMinus_Click(object sender, RoutedEventArgs e)
    {
        if (_psuSataSsdCount > 0)
        {
            _psuSataSsdCount--;
            if (TxtPsuSataSsdCount != null) TxtPsuSataSsdCount.Text = _psuSataSsdCount.ToString();
            RecalculatePsu();
        }
    }

    private void BtnPsuSataSsdPlus_Click(object sender, RoutedEventArgs e)
    {
        if (_psuSataSsdCount < 8)
        {
            _psuSataSsdCount++;
            if (TxtPsuSataSsdCount != null) TxtPsuSataSsdCount.Text = _psuSataSsdCount.ToString();
            RecalculatePsu();
        }
    }

    private void BtnPsuHddMinus_Click(object sender, RoutedEventArgs e)
    {
        if (_psuHddCount > 0)
        {
            _psuHddCount--;
            if (TxtPsuHddCount != null) TxtPsuHddCount.Text = _psuHddCount.ToString();
            RecalculatePsu();
        }
    }

    private void BtnPsuHddPlus_Click(object sender, RoutedEventArgs e)
    {
        if (_psuHddCount < 8)
        {
            _psuHddCount++;
            if (TxtPsuHddCount != null) TxtPsuHddCount.Text = _psuHddCount.ToString();
            RecalculatePsu();
        }
    }

    private void BtnPsuFansMinus_Click(object sender, RoutedEventArgs e)
    {
        if (_psuFansCount > 0)
        {
            _psuFansCount--;
            if (TxtPsuFansCount != null) TxtPsuFansCount.Text = _psuFansCount.ToString();
            RecalculatePsu();
        }
    }

    private void BtnPsuFansPlus_Click(object sender, RoutedEventArgs e)
    {
        if (_psuFansCount < 16)
        {
            _psuFansCount++;
            if (TxtPsuFansCount != null) TxtPsuFansCount.Text = _psuFansCount.ToString();
            RecalculatePsu();
        }
    }

    /// <summary>
    /// Recalcula o consumo de pico, recomendação de fonte e orientações com base nas seleções.
    /// </summary>
    private void RecalculatePsu()
    {
        try
        {
            if (CmbPsuCpu?.SelectedItem == null || CmbPsuGpu?.SelectedItem == null) return;

            var moboItem = CmbPsuMobo?.SelectedItem as PsuComponentItem ?? PsuCalculator.MotherboardList[0];
            var cpuItem = CmbPsuCpu.SelectedItem as PsuComponentItem ?? PsuCalculator.CpuList[0];
            var gpuItem = CmbPsuGpu.SelectedItem as PsuComponentItem ?? PsuCalculator.GpuList[0];

            string ramTag = "DDR4;2;8";
            if (CmbPsuRam?.SelectedItem is PsuRamOption ramOpt)
            {
                ramTag = ramOpt.Tag;
            }
            else if (CmbPsuRam?.SelectedItem is ComboBoxItem cbi)
            {
                ramTag = cbi.Tag?.ToString() ?? "DDR4;2;8";
            }

            var ramParts = ramTag.Split(';');
            string ramType = ramParts.Length > 0 ? ramParts[0] : "DDR4";
            int ramSticks = ramParts.Length > 1 && int.TryParse(ramParts[1], out int rs) ? rs : 2;
            int ramWattsOverride = ramParts.Length > 2 && int.TryParse(ramParts[2], out int rw) ? rw : 0;

            string coolerText = (CmbPsuCooler?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Air Cooler";

            var config = new PsuConfig
            {
                CpuName = cpuItem.Name ?? "",
                CpuWatts = cpuItem.Watts,
                MotherboardName = moboItem.Name ?? "",
                MotherboardWatts = moboItem.Watts,
                GpuName = gpuItem.Name ?? "",
                GpuWatts = gpuItem.Watts,
                RamType = ramType,
                RamSticks = ramSticks,
                RamWattsOverride = ramWattsOverride,
                NvmeCount = _psuNvmeCount,
                SataSsdCount = _psuSataSsdCount,
                HddCount = _psuHddCount,
                CoolerType = coolerText,
                FanCount = _psuFansCount,
                Overclock = ChkPsuOc?.IsChecked == true,
                RgbAndPeripherals = ChkPsuRgb?.IsChecked == true
            };

            var result = PsuCalculator.Calculate(config);

            if (TxtPsuPeakWatts != null) TxtPsuPeakWatts.Text = result.TotalPeakWatts.ToString();
            if (TxtPsuRecommendedWatts != null) TxtPsuRecommendedWatts.Text = result.RecommendedPsuWatts.ToString();
            if (TxtPsuHeadroom != null) TxtPsuHeadroom.Text = $"+{result.HeadroomWatts}";
            if (TxtPsuEfficiency != null) TxtPsuEfficiency.Text = result.RecommendedEfficiency;
            if (TxtPsuLoadInfo != null) TxtPsuLoadInfo.Text = $"Carga estimada de {result.LoadPercentageAtRecommended:F0}% em pico (faixa de alta eficiência)";

            // Delta em relação ao hardware detectado
            if (TxtPsuDeltaInfo != null)
            {
                if (_detectedSystemPeakWatts > 0)
                {
                    int delta = result.TotalPeakWatts - _detectedSystemPeakWatts;
                    if (delta != 0)
                    {
                        string sign = delta > 0 ? "+" : "";
                        TxtPsuDeltaInfo.Text = $"Impacto do upgrade: {sign}{delta} W em relação ao hardware detectado";
                    }
                    else
                    {
                        TxtPsuDeltaInfo.Text = "Folga para absorção de transientes e futuros upgrades";
                    }
                }
                else
                {
                    TxtPsuDeltaInfo.Text = "Folga para absorção de transientes e futuros upgrades";
                }
            }

            // Tabela de detalhamento de carga
            if (TxtBreakdownCpu != null) TxtBreakdownCpu.Text = $"{result.CpuWatts} W";
            if (TxtBreakdownGpu != null) TxtBreakdownGpu.Text = $"{result.GpuWatts} W";
            if (TxtBreakdownMobo != null) TxtBreakdownMobo.Text = $"{result.MotherboardWatts} W";
            if (TxtBreakdownRam != null) TxtBreakdownRam.Text = $"{result.RamWatts} W";
            if (TxtBreakdownStorage != null) TxtBreakdownStorage.Text = $"{result.StorageWatts} W";
            if (TxtBreakdownCooling != null) TxtBreakdownCooling.Text = $"{result.CoolingWatts} W";
            if (TxtBreakdownMisc != null) TxtBreakdownMisc.Text = $"{result.UsbAndMiscWatts} W";
            if (TxtBreakdownTotal != null) TxtBreakdownTotal.Text = $"{result.TotalPeakWatts} W";

            if (TxtPsuCables != null) TxtPsuCables.Text = result.PcieCablesRecommendation;
            if (TxtPsuAdvice != null) TxtPsuAdvice.Text = result.UpgradeAdvice;
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[PSU] Erro ao recalcular consumo: {ex}");
        }
    }
}
