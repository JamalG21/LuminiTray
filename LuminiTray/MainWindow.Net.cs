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
using System.Windows.Shapes;
using System.Windows.Threading;

namespace LuminiTray;

/// <summary>
/// Módulos de Rede (Diagnóstico de Velocidade, Ping Contínuo, DNS Jumper WMI), Energia e Tema.
/// </summary>
public partial class MainWindow
{
    // ================= Rede (Diagnóstico Avançado & DNS Jumper) =================

    private NetworkDiagnosticsViewModel? _netDiagVm;

    public void InitNetworkDiagnostics()
    {
        if (_netDiagVm == null)
        {
            _netDiagVm = new NetworkDiagnosticsViewModel();

            _netDiagVm.PropertyChanged += (s, args) =>
            {
                Dispatcher.BeginInvoke(() =>
                {
                    switch (args.PropertyName)
                    {
                        case nameof(NetworkDiagnosticsViewModel.PingMs):
                            if (GaugePing != null) GaugePing.Value = _netDiagVm.PingMs;
                            break;
                        case nameof(NetworkDiagnosticsViewModel.DownloadMbps):
                            if (GaugeDownload != null) GaugeDownload.Value = _netDiagVm.DownloadMbps;
                            break;
                        case nameof(NetworkDiagnosticsViewModel.UploadMbps):
                            if (GaugeUpload != null) GaugeUpload.Value = _netDiagVm.UploadMbps;
                            break;
                        case nameof(NetworkDiagnosticsViewModel.SpeedTestStatus):
                            if (TxtSpeedStatus != null) TxtSpeedStatus.Text = _netDiagVm.SpeedTestStatus;
                            break;
                        case nameof(NetworkDiagnosticsViewModel.SpeedTestServer):
                            if (TxtSpeedServerInfo != null) TxtSpeedServerInfo.Text = $"Servidor: {_netDiagVm.SpeedTestServer}";
                            break;
                        case nameof(NetworkDiagnosticsViewModel.SpeedProgressFrac):
                            if (SpeedProgressBar != null) SpeedProgressBar.ScaleX = _netDiagVm.SpeedProgressFrac;
                            break;
                        case nameof(NetworkDiagnosticsViewModel.ActiveAdapterInfo):
                        case nameof(NetworkDiagnosticsViewModel.ActiveDnsInfo):
                            UpdateNetHeaderInfo();
                            break;
                        case nameof(NetworkDiagnosticsViewModel.DnsStatusMessage):
                            if (TxtDnsBenchmarkStatus != null) TxtDnsBenchmarkStatus.Text = _netDiagVm.DnsStatusMessage;
                            break;
                        case nameof(NetworkDiagnosticsViewModel.IsDnsBenchmarking):
                            if (BtnBenchmarkDns != null) BtnBenchmarkDns.IsEnabled = !_netDiagVm.IsDnsBenchmarking;
                            if (BtnApplyBestDns != null) BtnApplyBestDns.IsEnabled = !_netDiagVm.IsDnsBenchmarking;
                            if (BtnRestoreDhcp != null) BtnRestoreDhcp.IsEnabled = !_netDiagVm.IsDnsBenchmarking;
                            break;
                    }
                });
            };

            _netDiagVm.OnPingSampleReceived += () => Dispatcher.BeginInvoke(RenderContinuousPingChart);

            if (ListDnsPresets != null)
            {
                ListDnsPresets.ItemsSource = _netDiagVm.DnsPresets;
            }
        }

        _netDiagVm.RefreshActiveDnsInfo();
        UpdateNetHeaderInfo();
    }

    private void NetworkDiagnosticsCleanup()
    {
        try
        {
            _netDiagVm?.Dispose();
        }
        catch { }
    }

    private void UpdateNetHeaderInfo()
    {
        if (_netDiagVm == null) return;
        if (TxtActiveAdapterHeader != null) TxtActiveAdapterHeader.Text = _netDiagVm.ActiveAdapterInfo;
        if (TxtActiveDnsHeader != null) TxtActiveDnsHeader.Text = _netDiagVm.ActiveDnsInfo;
    }

    // ---------------- TAREFA 1: SpeedTest Handlers ----------------

    private async void BtnStartSpeedTest_Click(object sender, RoutedEventArgs e)
    {
        if (_netDiagVm == null) InitNetworkDiagnostics();
        if (_netDiagVm!.IsTestingSpeed) return;

        BtnStartSpeedTest.IsEnabled = false;
        BtnCancelSpeedTest.Visibility = Visibility.Visible;
        TxtSpeedStatus.Text = "Iniciando diagnóstico de velocidade...";
        SpeedProgressBar.ScaleX = 0.05;

        GaugePing.Value = 0;
        GaugeDownload.Value = 0;
        GaugeUpload.Value = 0;

        try
        {
            await _netDiagVm.RunSpeedTestAsync();
        }
        finally
        {
            BtnStartSpeedTest.IsEnabled = true;
            BtnCancelSpeedTest.Visibility = Visibility.Collapsed;
        }
    }

    private void BtnCancelSpeedTest_Click(object sender, RoutedEventArgs e)
    {
        _netDiagVm?.CancelSpeedTest();
    }

    // ---------------- TAREFA 1 (Parte 2): Ping Contínuo Handlers & Gráfico ----------------

    private void BtnToggleContinuousPing_Click(object sender, RoutedEventArgs e)
    {
        if (_netDiagVm == null) InitNetworkDiagnostics();

        string host = "8.8.8.8";
        if (CmbPingTarget?.SelectedItem is ComboBoxItem item && item.Tag is string tag)
        {
            if (tag == "custom")
            {
                host = string.IsNullOrWhiteSpace(TxtCustomPingTarget?.Text) ? "8.8.8.8" : TxtCustomPingTarget.Text.Trim();
            }
            else
            {
                host = tag;
            }
        }

        if (_netDiagVm!.IsPingRunning)
        {
            _netDiagVm.StopContinuousPing();
            if (TxtContinuousPingBtn != null) TxtContinuousPingBtn.Text = "INICIAR MONITOR";
            if (IconContinuousPing != null) IconContinuousPing.Text = "\uE768";
            BtnToggleContinuousPing.Style = (Style)FindResource("PrimaryButton");
        }
        else
        {
            _netDiagVm.StartContinuousPing(host);
            if (TxtContinuousPingBtn != null) TxtContinuousPingBtn.Text = "PARAR MONITOR";
            if (IconContinuousPing != null) IconContinuousPing.Text = "\uE71A";
            BtnToggleContinuousPing.Style = (Style)FindResource("SecondaryButton");
            if (TxtPingGraphPlaceholder != null) TxtPingGraphPlaceholder.Visibility = Visibility.Collapsed;
        }
    }

    private void CmbPingTarget_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TxtCustomPingTarget == null) return;
        if (CmbPingTarget?.SelectedItem is ComboBoxItem item && item.Tag is string tag)
        {
            TxtCustomPingTarget.Visibility = tag == "custom" ? Visibility.Visible : Visibility.Collapsed;
            if (_netDiagVm != null && _netDiagVm.IsPingRunning && tag != "custom")
            {
                _netDiagVm.StartContinuousPing(tag);
            }
        }
    }

    private void NetPingCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        RenderContinuousPingChart();
    }

    private void RenderContinuousPingChart()
    {
        if (_netDiagVm == null || NetPingCanvas == null) return;

        // Atualiza Badges de Métricas Numéricas
        if (TxtPingMin != null) TxtPingMin.Text = $"{_netDiagVm.MinPingMs:0.0} ms";
        if (TxtPingAvg != null) TxtPingAvg.Text = $"{_netDiagVm.AvgPingMs:0.0} ms";
        if (TxtPingMax != null) TxtPingMax.Text = $"{_netDiagVm.MaxPingMs:0.0} ms";
        if (TxtPingJitter != null) TxtPingJitter.Text = $"{_netDiagVm.JitterMs:0.0} ms";

        if (TxtPingLoss != null)
        {
            TxtPingLoss.Text = $"{_netDiagVm.PacketLossPercent:0.0}%";
            TxtPingLoss.Foreground = _netDiagVm.PacketLossPercent > 0
                ? new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44)) // Vermelho
                : new SolidColorBrush(Color.FromRgb(0x10, 0xB9, 0x81)); // Verde
        }

        double w = NetPingCanvas.ActualWidth;
        double h = NetPingCanvas.ActualHeight;
        if (w <= 40 || h <= 30) return;

        NetPingCanvas.Children.Clear();

        var samples = _netDiagVm.PingHistory.ToList();
        if (samples.Count == 0)
        {
            if (TxtPingGraphPlaceholder != null) TxtPingGraphPlaceholder.Visibility = Visibility.Visible;
            return;
        }

        if (TxtPingGraphPlaceholder != null) TxtPingGraphPlaceholder.Visibility = Visibility.Collapsed;

        const double left = 46.0;
        const double right = 14.0;
        const double top = 14.0;
        const double bottom = 24.0;
        double plotW = w - left - right;
        double plotH = h - top - bottom;

        double maxSample = samples.Where(s => s.Success).Select(s => s.LatencyMs).DefaultIfEmpty(40.0).Max();
        double yMax = Math.Max(30.0, Math.Ceiling(maxSample * 1.3 / 10.0) * 10.0);
        double yMin = 0.0;

        double X(int index, int totalCount)
        {
            const int maxPoints = 60;
            double step = plotW / Math.Max(1, maxPoints - 1);
            return left + plotW - (totalCount - 1 - index) * step;
        }

        double Y(double val) =>
            top + plotH * (1.0 - (Math.Clamp(val, yMin, yMax) - yMin) / (yMax - yMin));

        var faint = (Brush)FindResource("Faint");
        var mono = (FontFamily)FindResource("Mono");
        var lineBrush = (Brush)FindResource("Line");

        // 1. Grade horizontal e rótulos
        for (int k = 0; k <= 3; k++)
        {
            double v = yMin + (yMax - yMin) * k / 3.0;
            double yPos = Y(v);

            NetPingCanvas.Children.Add(new Line
            {
                X1 = left,
                X2 = left + plotW,
                Y1 = yPos,
                Y2 = yPos,
                Stroke = lineBrush,
                StrokeDashArray = new DoubleCollection { 4, 4 },
                StrokeThickness = 1
            });

            var lbl = new TextBlock
            {
                Text = $"{v:0} ms",
                FontSize = 10,
                Foreground = faint,
                FontFamily = mono
            };
            Canvas.SetLeft(lbl, 2);
            Canvas.SetTop(lbl, yPos - 7);
            NetPingCanvas.Children.Add(lbl);
        }

        // 2. Pontos e Linha de Latência
        var pts = new List<Point>();
        var failedIndices = new List<(double X, double Y)>();

        for (int i = 0; i < samples.Count; i++)
        {
            var s = samples[i];
            double x = X(i, samples.Count);
            if (s.Success)
            {
                pts.Add(new Point(x, Y(s.LatencyMs)));
            }
            else
            {
                failedIndices.Add((x, Y(0)));
            }
        }

        // Desenha marcações de Packet Loss (linhas verticais vermelhas)
        foreach (var (fx, _) in failedIndices)
        {
            var lossLine = new Line
            {
                X1 = fx,
                X2 = fx,
                Y1 = top,
                Y2 = top + plotH,
                Stroke = new SolidColorBrush(Color.FromArgb(180, 239, 68, 68)),
                StrokeThickness = 2
            };
            NetPingCanvas.Children.Add(lossLine);

            var lossTag = new Ellipse
            {
                Width = 6,
                Height = 6,
                Fill = new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44))
            };
            Canvas.SetLeft(lossTag, fx - 3);
            Canvas.SetTop(lossTag, top + 2);
            NetPingCanvas.Children.Add(lossTag);
        }

        if (pts.Count >= 2)
        {
            // Área sob a curva com degradê Neon Cyan
            var area = new Polygon
            {
                Fill = new LinearGradientBrush(
                    Color.FromArgb(0x44, 0x06, 0xB6, 0xD4),
                    Color.FromArgb(0x02, 0x06, 0xB6, 0xD4),
                    90)
            };
            foreach (var p in pts) area.Points.Add(p);
            area.Points.Add(new Point(pts[^1].X, top + plotH));
            area.Points.Add(new Point(pts[0].X, top + plotH));
            NetPingCanvas.Children.Add(area);

            // Linha contínua
            var polyline = new Polyline
            {
                Stroke = new SolidColorBrush(Color.FromRgb(0x06, 0xB6, 0xD4)),
                StrokeThickness = 2.2,
                StrokeLineJoin = PenLineJoin.Round
            };
            foreach (var p in pts) polyline.Points.Add(p);
            NetPingCanvas.Children.Add(polyline);

            // Marcador e balão no ponto mais recente
            var lastPt = pts[^1];
            var dot = new Ellipse
            {
                Width = 9,
                Height = 9,
                Fill = new SolidColorBrush(Color.FromRgb(0x06, 0xB6, 0xD4)),
                Stroke = (Brush)FindResource("Card"),
                StrokeThickness = 2
            };
            Canvas.SetLeft(dot, lastPt.X - 4.5);
            Canvas.SetTop(dot, lastPt.Y - 4.5);
            NetPingCanvas.Children.Add(dot);

            var bubble = new Border
            {
                Background = (Brush)FindResource("Card"),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x06, 0xB6, 0xD4)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(6, 2, 6, 2),
                Child = new TextBlock
                {
                    Text = $"{_netDiagVm.CurrentPingMs:0.#} ms",
                    FontFamily = mono,
                    FontSize = 10,
                    FontWeight = FontWeights.Bold,
                    Foreground = (Brush)FindResource("Fg")
                }
            };
            bubble.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(bubble, Math.Max(left, lastPt.X - bubble.DesiredSize.Width - 6));
            Canvas.SetTop(bubble, Math.Clamp(lastPt.Y - bubble.DesiredSize.Height - 6, 0, h - bubble.DesiredSize.Height));
            NetPingCanvas.Children.Add(bubble);
        }
    }

    // ---------------- TAREFAS 2 & 3: DNS Jumper & Injeção WMI Handlers ----------------

    private async void BtnBenchmarkDns_Click(object sender, RoutedEventArgs e)
    {
        if (_netDiagVm == null) InitNetworkDiagnostics();
        if (_netDiagVm!.IsDnsBenchmarking) return;

        try
        {
            await _netDiagVm.RunDnsBenchmarkAsync();
        }
        catch (Exception ex)
        {
            Toast("DNS Jumper", $"Erro no benchmark: {ex.Message}", "\uE783");
        }
    }

    private void BtnApplyBestDns_Click(object sender, RoutedEventArgs e)
    {
        if (_netDiagVm == null) return;
        var (ok, msg) = _netDiagVm.ApplyBestDns();
        Toast("DNS Jumper", msg, ok ? "\uE73E" : "\uE783");
        UpdateNetHeaderInfo();
    }

    private void BtnRestoreDhcp_Click(object sender, RoutedEventArgs e)
    {
        if (_netDiagVm == null) return;
        var (ok, msg) = _netDiagVm.RestoreDhcpDns();
        Toast("DNS Jumper", msg, ok ? "\uE777" : "\uE783");
        UpdateNetHeaderInfo();
    }

    private void BtnApplySpecificDns_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is DnsBenchmarkItem item)
        {
            if (_netDiagVm == null) return;
            var (ok, msg) = _netDiagVm.ApplySelectedDns(item);
            Toast("DNS Jumper", msg, ok ? "\uE73E" : "\uE783");
            UpdateNetHeaderInfo();
        }
    }

    private void BtnRefreshNetInfo_Click(object sender, RoutedEventArgs e)
    {
        _netDiagVm?.RefreshActiveDnsInfo();
        UpdateNetHeaderInfo();
        Toast("Rede", "Informações do adaptador de rede e DNS atualizadas.", "\uE72C");
    }

    // ================= Diagnóstico Estruturado (Relatório de Exportação) =================

    private CancellationTokenSource? _netCts;
    private bool _netRunning;
    private List<DiskHealthCard>? _lastNet;

    private async void BtnNetTest_Click(object sender, RoutedEventArgs e)
    {
        if (_netRunning) { _netCts?.Cancel(); return; }
        if (AnyBenchRunning || TestRunning) { NetStatus.Text = "Termine o outro teste antes."; return; }

        _netRunning = true;
        _netCts = new CancellationTokenSource();
        var ct = _netCts.Token;
        BtnNetTest.Content = "CANCELAR";
        NetCards.ItemsSource = null;
        NetProgress.ScaleX = 0;
        try
        {
            var cards = await NetworkTest.Run((text, frac) => Dispatcher.BeginInvoke(new Action(() =>
            {
                NetStatus.Text = text;
                NetProgress.ScaleX = Math.Clamp(frac, 0, 1);
            })), ct);
            _lastNet = cards;
            NetCards.ItemsSource = cards;
            NetStatus.Text = $"Concluído às {DateTime.Now:HH:mm:ss}. O resultado entra no relatório exportado.";
            NetProgress.ScaleX = 1;
        }
        catch (OperationCanceledException) { NetStatus.Text = "Teste cancelado."; NetProgress.ScaleX = 0; }
        catch (Exception ex) { NetStatus.Text = "Falhou: " + ex.Message; NetProgress.ScaleX = 0; }
        finally
        {
            _netRunning = false;
            _netCts?.Dispose();
            _netCts = null;
            BtnNetTest.Content = "GERAR DIAGNÓSTICO DO RELATÓRIO";
        }
    }

    // ================= Energia =================

    private bool _energyBusy;

    private async void RefreshEnergy()
    {
        if (_energyBusy) return;
        _energyBusy = true;
        BtnEnergyRefresh.IsEnabled = false;
        EnergyStatus.Text = "Lendo bateria e energia…";
        try
        {
            var sensors = _monitor.Rows.ToList();
            var cards = await Task.Run(() => BatteryInfo.Read(sensors));
            EnergyCards.ItemsSource = cards;

            // Extrai dados detalhados via powercfg /batteryreport
            var report = await BatteryReportService.GenerateReportAsync();

            if (report.HasBattery && report.PrimaryBattery != null)
            {
                var b = report.PrimaryBattery;
                if (BrdBatteryDetailedCard != null) BrdBatteryDetailedCard.Visibility = Visibility.Visible;
                if (BrdDesktopPowerNotice != null) BrdDesktopPowerNotice.Visibility = Visibility.Collapsed;

                if (TxtBatteryHealthPercentage != null) TxtBatteryHealthPercentage.Text = $"{b.HealthPercentage:0}%";
                if (TxtBatteryHealthVerdict != null)
                {
                    TxtBatteryHealthVerdict.Text = b.HealthVerdict;
                    TxtBatteryHealthVerdict.Foreground = new System.Windows.Media.SolidColorBrush(
                        (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(b.HealthBadgeColor));
                }
                if (TxtBatteryHealthExplanation != null) TxtBatteryHealthExplanation.Text = b.HealthExplanation;

                if (TxtBatteryDesignCap != null) TxtBatteryDesignCap.Text = $"{b.DesignCapacityMwh:N0} mWh ({b.DesignCapacityWh:0.1} Wh)";
                if (TxtBatteryFullCap != null) TxtBatteryFullCap.Text = $"{b.FullChargeCapacityMwh:N0} mWh ({b.FullChargeCapacityWh:0.1} Wh)";
                if (TxtBatteryWear != null) TxtBatteryWear.Text = $"{b.DegradationPercentage:0.1}%";
                if (TxtBatteryCycles != null) TxtBatteryCycles.Text = b.CycleCount?.ToString() ?? "Não informado pelo firmware";
                if (TxtBatteryChemistry != null) TxtBatteryChemistry.Text = b.Chemistry;
                if (TxtBatteryManufacturer != null) TxtBatteryManufacturer.Text = b.Manufacturer;
                if (TxtBatteryModel != null) TxtBatteryModel.Text = b.ModelName;
                if (TxtBatteryRuntimeNew != null) TxtBatteryRuntimeNew.Text = b.RuntimeEstimatedNew;
                if (TxtBatteryRuntimeCurrent != null) TxtBatteryRuntimeCurrent.Text = b.RuntimeEstimatedCurrent;

                if (TxtBatteryHistorySummary != null) TxtBatteryHistorySummary.Text = report.HistorySummary;
                if (LstBatteryHistory != null) LstBatteryHistory.ItemsSource = report.History.TakeLast(10).Reverse().ToList();

                EnergyStatus.Text = $"Bateria detectada · Saúde: {b.HealthPercentage:0}% ({b.HealthVerdict}) · lido às {DateTime.Now:HH:mm:ss}";
            }
            else
            {
                if (BrdBatteryDetailedCard != null) BrdBatteryDetailedCard.Visibility = Visibility.Collapsed;
                if (BrdDesktopPowerNotice != null) BrdDesktopPowerNotice.Visibility = Visibility.Visible;
                EnergyStatus.Text = $"Nenhuma bateria interna (Computador de mesa conectado à tomada) · lido às {DateTime.Now:HH:mm:ss}";
            }
        }
        catch (Exception ex) { EnergyStatus.Text = "Falhou: " + ex.Message; }
        finally
        {
            _energyBusy = false;
            BtnEnergyRefresh.IsEnabled = true;
        }
    }

    private void BtnEnergyRefresh_Click(object sender, RoutedEventArgs e) => RefreshEnergy();

    private async void BtnOpenBatteryReportHtml_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string? htmlPath = await BatteryReportService.GenerateHtmlReportAsync();
            if (!string.IsNullOrEmpty(htmlPath) && File.Exists(htmlPath))
            {
                Process.Start(new ProcessStartInfo { FileName = htmlPath, UseShellExecute = true });
            }
            else
            {
                MessageBox.Show("Não foi possível gerar o relatório HTML de bateria.", "Relatório de Bateria", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show("Erro ao abrir relatório: " + ex.Message, "Relatório de Bateria", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ================= Tema (escuro / claro) =================

    private void Theme_Click(object sender, RoutedEventArgs e)
    {
        ThemeManager.Apply(!ThemeManager.IsLight);
        _settings.LightTheme = ThemeManager.IsLight;
        try { _settings.Save(); } catch { }
        UpdateThemeButton();
    }

    /// <summary>Mostra o ícone do modo para onde o clique leva (sol = ir para o claro, lua = ir para o escuro) e troca o logo.</summary>
    private void UpdateThemeButton()
    {
        bool light = ThemeManager.IsLight;
        BtnTheme.Content = light ? "\uE708" : "\uE706";
        BtnTheme.ToolTip = light ? "Modo escuro" : "Modo claro";
        TitleLogo.Source = new System.Windows.Media.Imaging.BitmapImage(
            new Uri(light ? "pack://application:,,,/Assets/logo-dark.png" : "pack://application:,,,/Assets/logo.png"));
    }
}
