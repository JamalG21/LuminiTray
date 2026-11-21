using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using LibreHardwareMonitor.Hardware;

namespace LuminiTray;

/// <summary>
/// Sub-aba "Monitoramento" (dentro de LUMINI INFORMAÇÕES) + botão global de Modo Privacidade (ícone de olho).
/// </summary>
public partial class MainWindow
{
    private readonly MonitoringLogService _monitoringLogService = new();
    private ICollectionView? _monitoringView;
    private string? _monitoringCategoryFilter;
    private string _monitoringSearch = "";
    private bool _monitoringInitialized;
    private string? _rawSystemSubText;

    private List<(double X, double Y, MonitoringPoint Point)> _monChartPlottedPoints = new();
    private MonitoringSensorSummary? _monActiveChartSensor;

    private void InitMonitoringPage()
    {
        if (!_monitoringInitialized)
        {
            _monitoringInitialized = true;
            _monitoringView = new ListCollectionView(_monitor.Rows);
            _monitoringView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(SensorRow.Hardware)));
            _monitoringView.SortDescriptions.Add(new SortDescription(nameof(SensorRow.Hardware), ListSortDirection.Ascending));
            _monitoringView.SortDescriptions.Add(new SortDescription(nameof(SensorRow.Order), ListSortDirection.Ascending));
            _monitoringView.SortDescriptions.Add(new SortDescription(nameof(SensorRow.Name), ListSortDirection.Ascending));

            if (MonitoringGrid != null)
                MonitoringGrid.ItemsSource = _monitoringView;

            RebuildMonitoringCategoryChips();

            if (!_monitor.Rows.Any(r => r.IsSelectedForMonitoring))
                SelectEssentialMonitoringSensors();
        }
        else
        {
            RebuildMonitoringCategoryChips();
        }

        UpdateMonitoringUiState();
        RefreshMonitoringSummaryAndChart();
    }

    private void RebuildMonitoringCategoryChips()
    {
        if (MonitoringCategoryChips == null) return;
        MonitoringCategoryChips.Children.Clear();

        AddMonitoringChip("Todos", null, isChecked: _monitoringCategoryFilter == null);
        foreach (string cat in _monitor.Rows.Select(r => r.Category).Distinct().OrderBy(c => c))
            AddMonitoringChip(cat, cat, isChecked: string.Equals(_monitoringCategoryFilter, cat, StringComparison.Ordinal));
    }

    private void AddMonitoringChip(string label, string? category, bool isChecked)
    {
        var chip = new RadioButton
        {
            Content = label,
            GroupName = "MonitoringCategory",
            Style = (Style)FindResource("Chip"),
            IsChecked = isChecked
        };
        chip.Checked += (_, _) =>
        {
            _monitoringCategoryFilter = category;
            ApplyMonitoringFilter();
        };
        MonitoringCategoryChips.Children.Add(chip);
    }

    private void ApplyMonitoringFilter()
    {
        if (_monitoringView == null) return;
        if (_monitoringCategoryFilter == null && string.IsNullOrEmpty(_monitoringSearch))
        {
            _monitoringView.Filter = null;
            return;
        }

        _monitoringView.Filter = o =>
        {
            if (o is not SensorRow r) return false;
            bool catOk = _monitoringCategoryFilter == null || string.Equals(r.Category, _monitoringCategoryFilter, StringComparison.Ordinal);
            bool searchOk = string.IsNullOrEmpty(_monitoringSearch)
                || r.Name.Contains(_monitoringSearch, StringComparison.OrdinalIgnoreCase)
                || r.Hardware.Contains(_monitoringSearch, StringComparison.OrdinalIgnoreCase)
                || r.Category.Contains(_monitoringSearch, StringComparison.OrdinalIgnoreCase);
            return catOk && searchOk;
        };
    }

    private void SelectEssentialMonitoringSensors()
    {
        foreach (var r in _monitor.Rows)
        {
            bool isCpuOrGpu = r.HardwareType is HardwareType.Cpu or HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel;
            bool isUsefulType = r.Type is SensorType.Temperature or SensorType.Load or SensorType.Power;
            bool isMainName = r.Name.Contains("Tctl", StringComparison.OrdinalIgnoreCase)
                || r.Name.Contains("Package", StringComparison.OrdinalIgnoreCase)
                || r.Name.Contains("GPU Core", StringComparison.OrdinalIgnoreCase)
                || r.Name.Equals("CPU Total", StringComparison.OrdinalIgnoreCase);

            r.IsSelectedForMonitoring = isCpuOrGpu && isUsefulType && isMainName;
        }
    }

    private void OnMonitoringOneSecondTick()
    {
        if (!_monitoringLogService.IsRunning) return;

        bool completed = _monitoringLogService.Tick(_monitor.Rows.ToList(), nowOverride: null, out bool sampledNow);
        if (PageMonitoring != null && PageMonitoring.Visibility == Visibility.Visible)
        {
            UpdateMonitoringUiState();
            if (sampledNow || completed)
                RefreshMonitoringSummaryAndChart();
        }

        if (completed)
        {
            UpdateSensorPollingState();
            Toast("Monitoramento Concluído", "Resumo e gráfico gerados. Arquivo temporário de gravação foi limpo do disco.", "\uE9D9");
        }
    }

    private void UpdateMonitoringUiState()
    {
        int selectedCount = _monitor.Rows.Count(r => r.IsSelectedForMonitoring);

        if (_monitoringLogService.IsRunning)
        {
            var rem = _monitoringLogService.GetRemainingTime();
            string remText = rem.TotalHours >= 1
                ? $"{(int)rem.TotalHours}h {rem.Minutes:D2}m {rem.Seconds:D2}s"
                : $"{rem.Minutes:D2}m {rem.Seconds:D2}s";

            if (TxtMonitoringStatus != null)
                TxtMonitoringStatus.Text = $"● Gravando a cada {_monitoringLogService.IntervalSeconds}s · {selectedCount} sensor(es) · {_monitoringLogService.RecordedIntervalsCount} leitura(s) · Restante: {remText}";
            if (BtnMonStartStop != null)
            {
                BtnMonStartStop.Content = "⏹ PARAR E GERAR RESUMO";
                if (TryFindResource("DangerButton") is Style danger) BtnMonStartStop.Style = danger;
            }
            if (PbMonSession != null)
            {
                PbMonSession.Visibility = Visibility.Visible;
                PbMonSession.Value = _monitoringLogService.GetProgressFraction() * 100.0;
            }
            SetMonitoringConfigInputsEnabled(false);
        }
        else
        {
            if (TxtMonitoringStatus != null)
                TxtMonitoringStatus.Text = $"Visualização estilo HWMonitor com horários de Mín/Máx · {_monitor.Rows.Count} sensores · {selectedCount} selecionado(s) para registro.";
            if (BtnMonStartStop != null)
            {
                BtnMonStartStop.Content = "▶ INICIAR MONITORAMENTO";
                if (TryFindResource("PrimaryButton") is Style primary) BtnMonStartStop.Style = primary;
            }
            if (PbMonSession != null)
            {
                PbMonSession.Visibility = Visibility.Collapsed;
                PbMonSession.Value = 0;
            }
            SetMonitoringConfigInputsEnabled(true);
        }
    }

    private void SetMonitoringConfigInputsEnabled(bool enabled)
    {
        if (TxtMonIntervalValue != null) TxtMonIntervalValue.IsEnabled = enabled;
        if (CmbMonIntervalUnit != null) CmbMonIntervalUnit.IsEnabled = enabled;
        if (TxtMonDurationValue != null) TxtMonDurationValue.IsEnabled = enabled;
        if (CmbMonDurationUnit != null) CmbMonDurationUnit.IsEnabled = enabled;
    }

    private int ReadConfiguredIntervalSeconds()
    {
        int val = int.TryParse(TxtMonIntervalValue?.Text.Trim(), out int parsed) ? Math.Clamp(parsed, 1, 3600) : 1;
        bool isMinutes = CmbMonIntervalUnit?.SelectedIndex == 1;
        return isMinutes ? val * 60 : val;
    }

    private int ReadConfiguredDurationSeconds()
    {
        int val = int.TryParse(TxtMonDurationValue?.Text.Trim(), out int parsed) ? Math.Clamp(parsed, 1, 1440) : 30;
        bool isHours = CmbMonDurationUnit?.SelectedIndex == 1;
        return isHours ? val * 3600 : val * 60;
    }

    private void RefreshMonitoringSummaryAndChart()
    {
        if (CardMonChartSummary == null) return;

        var session = _monitoringLogService.GetLiveSnapshot() ?? _monitoringLogService.LastResult;
        if (session == null || session.Sensors.Count == 0)
        {
            CardMonChartSummary.Visibility = Visibility.Collapsed;
            _monActiveChartSensor = null;
            return;
        }

        CardMonChartSummary.Visibility = Visibility.Visible;

        string statusWord = _monitoringLogService.IsRunning
            ? "Em andamento"
            : session.CompletedAutomatically ? "Concluído (arquivo bruto limpo)" : "Finalizado pelo usuário";
        if (TxtMonSummaryHeader != null)
        {
            TxtMonSummaryHeader.Text = $"{statusWord} · {session.StartedAt:HH:mm:ss} → {session.EndedAt:HH:mm:ss} · Intervalo: {session.IntervalSeconds}s · {session.Sensors.Count} sensor(es)";
        }

        string? prevSelectedId = (CmbMonChartSensor?.SelectedItem as MonitoringSensorSummary)?.SensorId;
        if (CmbMonChartSensor != null)
        {
            CmbMonChartSensor.DisplayMemberPath = nameof(MonitoringSensorSummary.DisplayLabel);
            CmbMonChartSensor.ItemsSource = session.Sensors;
            var match = session.Sensors.FirstOrDefault(s => string.Equals(s.SensorId, prevSelectedId, StringComparison.OrdinalIgnoreCase))
                        ?? session.Sensors[0];
            CmbMonChartSensor.SelectedItem = match;
        }

        UpdateSelectedMonitoringSensorChart();
    }

    private void UpdateSelectedMonitoringSensorChart()
    {
        var selected = CmbMonChartSensor?.SelectedItem as MonitoringSensorSummary;
        _monActiveChartSensor = selected;
        if (selected == null) return;

        if (TxtMonStatMin != null) TxtMonStatMin.Text = $"{selected.FormatStat(selected.Min)} ({selected.MinTime:HH:mm:ss})";
        if (TxtMonStatAvg != null) TxtMonStatAvg.Text = selected.FormatStat(selected.Average);
        if (TxtMonStatMax != null) TxtMonStatMax.Text = $"{selected.FormatStat(selected.Max)} ({selected.MaxTime:HH:mm:ss})";
        if (TxtMonStatSamples != null) TxtMonStatSamples.Text = selected.SampleCount.ToString();

        RenderMonitoringChart();
    }

    private void RenderMonitoringChart()
    {
        if (CanvasMonChart == null) return;
        CanvasMonChart.Children.Clear();
        _monChartPlottedPoints.Clear();
        if (BorderMonChartTooltip != null) BorderMonChartTooltip.Visibility = Visibility.Collapsed;

        var s = _monActiveChartSensor;
        if (s == null || s.Points.Count == 0) return;

        double w = CanvasMonChart.ActualWidth;
        double h = CanvasMonChart.ActualHeight;
        if (w <= 60 || h <= 40) return;

        const double padL = 68, padR = 16, padT = 14, padB = 24;
        double plotW = w - padL - padR;
        double plotH = h - padT - padB;

        double minVal = s.Points.Min(p => (double)p.Value);
        double maxVal = s.Points.Max(p => (double)p.Value);
        double span = Math.Max(0.1, maxVal - minVal);
        double yMin = minVal - span * 0.12;
        double yMax = maxVal + span * 0.12;
        if (Math.Abs(yMax - yMin) < 0.01) { yMin -= 1; yMax += 1; }

        var mutedBrush = TryFindResource("Muted") as Brush ?? Brushes.Gray;
        var lineBrush = TryFindResource("Line") as Brush ?? Brushes.DimGray;
        var accentBrush = TryFindResource("Accent") as Brush ?? new SolidColorBrush(Color.FromRgb(0, 210, 255));
        var monoFont = TryFindResource("Mono") as FontFamily ?? new FontFamily("Consolas");

        // Grade horizontal + rótulos de eixo Y
        for (int i = 0; i <= 3; i++)
        {
            double frac = i / 3.0;
            double v = yMin + (yMax - yMin) * frac;
            double y = padT + plotH * (1.0 - frac);

            CanvasMonChart.Children.Add(new Line
            {
                X1 = padL,
                X2 = padL + plotW,
                Y1 = y,
                Y2 = y,
                Stroke = lineBrush,
                StrokeDashArray = i == 0 ? null : new DoubleCollection { 4, 4 },
                StrokeThickness = 1
            });

            var lbl = new TextBlock
            {
                Text = s.FormatStat((float)v),
                Foreground = mutedBrush,
                FontFamily = monoFont,
                FontSize = 10,
                Width = padL - 8,
                TextAlignment = TextAlignment.Right
            };
            Canvas.SetLeft(lbl, 0);
            Canvas.SetTop(lbl, y - 7);
            CanvasMonChart.Children.Add(lbl);
        }

        // Pontos plotados
        var polyPts = new PointCollection();
        var areaPts = new PointCollection { new Point(padL, padT + plotH) };

        for (int i = 0; i < s.Points.Count; i++)
        {
            double x = s.Points.Count == 1
                ? padL + plotW * 0.5
                : padL + (plotW * i / (s.Points.Count - 1));
            double y = padT + plotH * (1.0 - (s.Points[i].Value - yMin) / (yMax - yMin));
            var pt = new Point(x, y);
            polyPts.Add(pt);
            areaPts.Add(pt);
            _monChartPlottedPoints.Add((x, y, s.Points[i]));
        }
        areaPts.Add(new Point(_monChartPlottedPoints[^1].X, padT + plotH));

        Color accentColor = accentBrush is SolidColorBrush scb ? scb.Color : Color.FromRgb(0, 210, 255);
        CanvasMonChart.Children.Add(new Polygon
        {
            Points = areaPts,
            Fill = new LinearGradientBrush(Color.FromArgb(75, accentColor.R, accentColor.G, accentColor.B),
                                           Color.FromArgb(4, accentColor.R, accentColor.G, accentColor.B), 90)
        });

        CanvasMonChart.Children.Add(new Polyline
        {
            Points = polyPts,
            Stroke = accentBrush,
            StrokeThickness = 2,
            StrokeLineJoin = PenLineJoin.Round
        });

        if (s.Points.Count <= 60)
        {
            foreach (var (x, y, _) in _monChartPlottedPoints)
            {
                var dot = new Ellipse { Width = 5, Height = 5, Fill = accentBrush };
                Canvas.SetLeft(dot, x - 2.5);
                Canvas.SetTop(dot, y - 2.5);
                CanvasMonChart.Children.Add(dot);
            }
        }

        // Rótulos de horário no eixo X
        int[] labelIndices = s.Points.Count == 1
            ? new[] { 0 }
            : s.Points.Count == 2
                ? new[] { 0, 1 }
                : new[] { 0, s.Points.Count / 2, s.Points.Count - 1 };

        foreach (int idx in labelIndices.Distinct())
        {
            var (x, _, p) = _monChartPlottedPoints[idx];
            var tb = new TextBlock
            {
                Text = p.Timestamp.ToString("HH:mm:ss"),
                Foreground = mutedBrush,
                FontFamily = monoFont,
                FontSize = 10
            };
            double left = idx == 0 ? padL : (idx == s.Points.Count - 1 ? Math.Max(padL, padL + plotW - 56) : x - 26);
            Canvas.SetLeft(tb, left);
            Canvas.SetTop(tb, padT + plotH + 4);
            CanvasMonChart.Children.Add(tb);
        }
    }

    // ---------------- Handlers vinculados no MainWindow.xaml ----------------

    private void BtnMonitoringResetMinMax_Click(object sender, RoutedEventArgs e)
    {
        _monitor.ResetAllMinMax();
        Toast("Monitoramento", "Valores Mín/Máx e horários foram zerados.", "\uE72C");
    }

    private void MonitoringSearch_Changed(object sender, TextChangedEventArgs e)
    {
        _monitoringSearch = MonitoringSearch?.Text.Trim() ?? "";
        ApplyMonitoringFilter();
    }

    private void BtnMonPreset10s_Click(object sender, RoutedEventArgs e)
    {
        if (TxtMonIntervalValue != null) TxtMonIntervalValue.Text = "10";
        if (CmbMonIntervalUnit != null) CmbMonIntervalUnit.SelectedIndex = 0;
    }

    private void BtnMonPreset1m_Click(object sender, RoutedEventArgs e)
    {
        if (TxtMonIntervalValue != null) TxtMonIntervalValue.Text = "1";
        if (CmbMonIntervalUnit != null) CmbMonIntervalUnit.SelectedIndex = 1;
    }

    private void BtnMonPreset5m_Click(object sender, RoutedEventArgs e)
    {
        if (TxtMonIntervalValue != null) TxtMonIntervalValue.Text = "5";
        if (CmbMonIntervalUnit != null) CmbMonIntervalUnit.SelectedIndex = 1;
    }

    private void BtnMonDur15m_Click(object sender, RoutedEventArgs e)
    {
        if (TxtMonDurationValue != null) TxtMonDurationValue.Text = "15";
        if (CmbMonDurationUnit != null) CmbMonDurationUnit.SelectedIndex = 0;
    }

    private void BtnMonDur30m_Click(object sender, RoutedEventArgs e)
    {
        if (TxtMonDurationValue != null) TxtMonDurationValue.Text = "30";
        if (CmbMonDurationUnit != null) CmbMonDurationUnit.SelectedIndex = 0;
    }

    private void BtnMonDur1h_Click(object sender, RoutedEventArgs e)
    {
        if (TxtMonDurationValue != null) TxtMonDurationValue.Text = "1";
        if (CmbMonDurationUnit != null) CmbMonDurationUnit.SelectedIndex = 1;
    }

    private void BtnMonSelectEssentials_Click(object sender, RoutedEventArgs e)
    {
        SelectEssentialMonitoringSensors();
        UpdateMonitoringUiState();
    }

    private void BtnMonSelectAll_Click(object sender, RoutedEventArgs e)
    {
        IEnumerable<SensorRow> scope = _monitoringView != null ? _monitoringView.Cast<SensorRow>() : _monitor.Rows;
        foreach (var r in scope) r.IsSelectedForMonitoring = true;
        UpdateMonitoringUiState();
    }

    private void BtnMonSelectNone_Click(object sender, RoutedEventArgs e)
    {
        foreach (var r in _monitor.Rows) r.IsSelectedForMonitoring = false;
        UpdateMonitoringUiState();
    }

    private void BtnMonToggleHardwareGroup_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string hwName || string.IsNullOrWhiteSpace(hwName))
            return;

        var rows = _monitor.Rows.Where(r => string.Equals(r.Hardware, hwName, StringComparison.Ordinal)).ToList();
        if (rows.Count == 0) return;

        bool target = !rows.All(r => r.IsSelectedForMonitoring);
        foreach (var r in rows) r.IsSelectedForMonitoring = target;
        UpdateMonitoringUiState();
    }

    private async void BtnMonStartStop_Click(object sender, RoutedEventArgs e)
    {
        if (_monitoringLogService.IsRunning)
        {
            _monitoringLogService.StopAndFinalize(completedAutomatically: false);
            UpdateMonitoringUiState();
            RefreshMonitoringSummaryAndChart();
            UpdateSensorPollingState();
            Toast("Monitoramento Finalizado", "Resumo breve e gráfico salvos; arquivo bruto removido do disco.", "\uE71A");
            return;
        }

        if (_monitor.IsOpen)
            await _monitor.RefreshAsync(updateUi: true);

        var selected = _monitor.Rows.Where(r => r.IsSelectedForMonitoring).ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show(this, "Selecione ao menos um sensor ou hardware na tabela abaixo para iniciar o monitoramento registrado.", "Monitoramento", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        int intervalSec = ReadConfiguredIntervalSeconds();
        int durationSec = ReadConfiguredDurationSeconds();
        if (durationSec < intervalSec)
        {
            MessageBox.Show(this, "A duração total deve ser maior ou igual ao intervalo entre leituras.", "Monitoramento", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (_monitoringLogService.Start(selected, intervalSec, durationSec))
        {
            if (!_timer.IsEnabled) _timer.Start();
            UpdateSensorPollingState();
            UpdateMonitoringUiState();
            RefreshMonitoringSummaryAndChart();
            Toast("Monitoramento Iniciado", $"Registrando {selected.Count} sensor(es) a cada {intervalSec}s por {durationSec / 60.0:0.#} min.", "\uE9D9");
        }
    }

    private void BtnMonClearSummary_Click(object sender, RoutedEventArgs e)
    {
        if (_monitoringLogService.IsRunning)
            _monitoringLogService.StopAndFinalize(completedAutomatically: false);
        _monitoringLogService.ClearLastResult();
        UpdateMonitoringUiState();
        RefreshMonitoringSummaryAndChart();
        Toast("Monitoramento", "Resumo e gráfico limpos.", "\uE74D");
    }

    private void CmbMonChartSensor_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateSelectedMonitoringSensorChart();
    }

    private void CanvasMonChart_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        RenderMonitoringChart();
    }

    private void CanvasMonChart_MouseMove(object sender, MouseEventArgs e)
    {
        if (_monChartPlottedPoints.Count == 0 || _monActiveChartSensor == null || BorderMonChartTooltip == null || TxtMonChartTooltip == null)
            return;

        Point pos = e.GetPosition(CanvasMonChart);
        var nearest = _monChartPlottedPoints.MinBy(p => Math.Abs(p.X - pos.X));
        TxtMonChartTooltip.Text = $"{nearest.Point.Timestamp:HH:mm:ss}  ·  {_monActiveChartSensor.FormatStat(nearest.Point.Value)}";
        BorderMonChartTooltip.Visibility = Visibility.Visible;

        double left = Math.Clamp(nearest.X + 10, 4, Math.Max(4, CanvasMonChart.ActualWidth - 170));
        double top = Math.Clamp(nearest.Y - 28, 4, Math.Max(4, CanvasMonChart.ActualHeight - 36));
        BorderMonChartTooltip.Margin = new Thickness(left, top, 0, 0);
    }

    private void CanvasMonChart_MouseLeave(object sender, MouseEventArgs e)
    {
        if (BorderMonChartTooltip != null)
            BorderMonChartTooltip.Visibility = Visibility.Collapsed;
    }

    // ---------------- Modo Privacidade Global (Ícone de Olho) ----------------

    private void BtnPrivacyMode_Click(object sender, RoutedEventArgs e)
    {
        bool active = PrivacyModeService.Toggle();
        UpdatePrivacyModeVisuals(active);
        Toast("Modo Privacidade",
            active
                ? "Dados sensíveis (IP, MAC, Serial, Nome do PC/Usuário) ocultos nas abas e no relatório."
                : "Dados sensíveis exibidos normalmente.",
            active ? "\uED1A" : "\uE7B3");
    }

    private void UpdatePrivacyModeVisuals(bool active)
    {
        if (BtnPrivacyMode != null)
        {
            BtnPrivacyMode.Content = active ? "\uED1A" : "\uE7B3";
            BtnPrivacyMode.Foreground = active
                ? (TryFindResource("Accent") as Brush ?? Brushes.DeepSkyBlue)
                : (TryFindResource("Muted") as Brush ?? Brushes.Gray);
            BtnPrivacyMode.ToolTip = active
                ? "Modo Privacidade ATIVO (dados sensíveis ocultos). Clique para mostrar."
                : "Modo Privacidade: ocultar dados sensíveis (IP, MAC, Serial, Nome do PC/Usuário)";
        }

        // 1. Aba Sistema (InfoNode + título com nome da máquina)
        if (InfoCategories?.ItemsSource is IEnumerable<InfoNode> roots)
        {
            foreach (var r in roots)
                r.NotifyPrivacyChanged();
        }
        if (!string.IsNullOrWhiteSpace(_rawSystemSubText) && SystemSub != null)
            SystemSub.Text = PrivacyModeService.MaskSensitivePatterns(_rawSystemSubText);

        // 2. Aba Rede & DNS
        UpdateNetHeaderInfo();
        RefreshNetworkPrivacyDisplay();

        // 3. Aba Saúde dos Discos
        RefreshDiskCardsPrivacy();

        // 4. Aba Periféricos / Bluetooth
        RefreshPeripheralsPrivacyDisplay();
    }
}
