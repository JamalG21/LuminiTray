using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace LuminiTray;

public partial class StressReportWindow : Window
{
    private readonly StressReportData _data;

    public StressReportWindow(StressReportData data)
    {
        InitializeComponent();
        _data = data;
        LoadReportData();
    }

    private void LoadReportData()
    {
        // Veredito e Status
        if (_data.WasAborted)
        {
            BrdVerdictBadge.Background = (Brush)FindResource("BadBg");
            TxtVerdictBadge.Foreground = (Brush)FindResource("BadFg");
            TxtVerdictBadge.Text = "ABORTADO POR LIMITE";
            TxtMetaResult.Text = _data.AbortReason;
            TxtMetaResult.Foreground = (Brush)FindResource("BadBg");
        }
        else
        {
            BrdVerdictBadge.Background = (Brush)FindResource("Fg");
            TxtVerdictBadge.Foreground = (Brush)FindResource("Bg");
            TxtVerdictBadge.Text = "ESTÁVEL · APROVADO";
            TxtMetaResult.Text = "Concluído com Êxito";
        }

        TxtMetaDate.Text = _data.TestDate.ToString("dd/MM/yyyy HH:mm:ss");
        TxtMetaDuration.Text = $"{_data.Duration:hh\\:mm\\:ss}";
        TxtMetaIntegrity.Text = _data.RamErrors == 0 ? "0 erros (100% íntegro)" : $"{_data.RamErrors} erro(s) de memória";

        // CPU
        TxtCpuModel.Text = !string.IsNullOrWhiteSpace(_data.CpuName) ? _data.CpuName : "Processador do Sistema";
        TxtCpuCores.Text = !string.IsNullOrWhiteSpace(_data.CpuCoresThreads) ? _data.CpuCoresThreads : "Carga Multi-thread";
        TxtCpuMaxTemp.Text = _data.CpuMaxTemp > 0 ? $"{_data.CpuMaxTemp:0.#} °C" : "—";
        TxtCpuLimitTemp.Text = _data.CpuTempLimit > 0 ? $"{_data.CpuTempLimit:0} °C" : "—";
        TxtCpuMaxPower.Text = _data.CpuMaxPowerWatts > 0 ? $"{_data.CpuMaxPowerWatts:0.#} W" : "—";
        TxtCpuLoad.Text = _data.CpuAvgLoad > 0 ? $"{_data.CpuAvgLoad:0}%" : "100%";

        // GPU
        TxtGpuModel.Text = !string.IsNullOrWhiteSpace(_data.GpuName) ? _data.GpuName : "Placa de Vídeo";
        TxtGpuVram.Text = !string.IsNullOrWhiteSpace(_data.GpuVram) ? _data.GpuVram : "GPU Dedicada";
        TxtGpuMaxTemp.Text = _data.GpuMaxTemp > 0 ? $"{_data.GpuMaxTemp:0.#} °C" : "—";
        TxtGpuLimitTemp.Text = _data.GpuTempLimit > 0 ? $"{_data.GpuTempLimit:0} °C" : "—";
        TxtGpuMaxPower.Text = _data.GpuMaxPowerWatts > 0 ? $"{_data.GpuMaxPowerWatts:0.#} W" : "—";
        TxtGpuFps.Text = _data.GpuAvgFps > 0
            ? $"{_data.GpuAvgFps:0} FPS avg ({_data.GpuTotalFrames:N0} frames)"
            : "—";

        // RAM
        TxtRamTotal.Text = !string.IsNullOrWhiteSpace(_data.RamTotalText) ? _data.RamTotalText : "Memória RAM";
        TxtRamSpeed.Text = !string.IsNullOrWhiteSpace(_data.RamSpeedType) ? _data.RamSpeedType : "Memória de Sistema";
        TxtRamPasses.Text = _data.RamPasses > 0 ? $"{_data.RamPasses} passes completos" : "1 pass concluído";
        TxtRamErrors.Text = _data.RamErrors == 0 ? "0 erros (Aprovado)" : $"{_data.RamErrors} erros detectados";
        TxtRamErrors.Foreground = _data.RamErrors == 0
            ? (Brush)FindResource("Fg")
            : (Brush)FindResource("BadBg");
        TxtRamStatus.Text = _data.RamErrors == 0 ? "Estável" : "Instável";

        // Logs
        LstReportLogs.ItemsSource = _data.RecentLogs;
        if (_data.RecentLogs.Count > 0)
        {
            LstReportLogs.ScrollIntoView(_data.RecentLogs[^1]);
        }
    }

    private RenderTargetBitmap CaptureReportBitmap()
    {
        ReportCaptureArea.UpdateLayout();
        int width = (int)Math.Max(ReportCaptureArea.ActualWidth, 800);
        int height = (int)Math.Max(ReportCaptureArea.ActualHeight, 600);

        var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(ReportCaptureArea);
        return rtb;
    }

    private void BtnCopyScreenshot_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var bmp = CaptureReportBitmap();
            Clipboard.SetImage(bmp);
            MessageBox.Show(this, "Captura do relatório copiada com sucesso para a Área de Transferência!\nVocê pode colar diretamente no WhatsApp, Discord ou e-mail.",
                "Captura Copiada", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Erro ao copiar captura: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void BtnSaveScreenshot_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var sfd = new SaveFileDialog
            {
                Title = "Salvar Relatório do Teste de Stress como Imagem",
                Filter = "Imagem PNG (*.png)|*.png|Imagem JPEG (*.jpg)|*.jpg",
                FileName = $"LUMINI-TRAY-Stress-Report-{DateTime.Now:yyyyMMdd-HHmmss}.png"
            };

            if (sfd.ShowDialog(this) == true)
            {
                var bmp = CaptureReportBitmap();
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bmp));

                using var fs = File.OpenWrite(sfd.FileName);
                encoder.Save(fs);

                MessageBox.Show(this, $"Relatório salvo com sucesso em:\n{sfd.FileName}",
                    "Imagem Salva", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Erro ao salvar imagem: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
