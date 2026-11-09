using System.Windows;
using System.Windows.Controls;

namespace LuminiTray;

/// <summary>
/// Módulo Display Driver Uninstaller (DDU) para desinstalação profunda e limpa de drivers de vídeo.
/// </summary>
public partial class MainWindow
{
    private DduDetectedHardware? _dduHardware;
    private bool _dduBusy;
    private CancellationTokenSource? _dduCts;

    private void InitDdu()
    {
        _dduHardware = DisplayDriverUninstaller.DetectHardware();

        TxtDduHardwareSummary.Text = _dduHardware.GpuSummary;
        TxtDduDriverVersion.Text = !string.IsNullOrEmpty(_dduHardware.DriverVersion) ? $"Versão instalada: {_dduHardware.DriverVersion}" : "Driver padrão do sistema";

        if (_dduHardware.IsInSafeMode)
        {
            BrdDduSafeModeBadge.Visibility = Visibility.Visible;
            TxtDduSafeModeHint.Text = "Você está em Modo de Segurança (Ambiente ideal para DDU).";
        }
        else
        {
            BrdDduSafeModeBadge.Visibility = Visibility.Collapsed;
            TxtDduSafeModeHint.Text = "Recomendação: Para máxima eficácia e evitar arquivos travados, execute em Modo de Segurança.";
        }

        // Seleciona o fabricante detectado por padrão
        if (CmbDduVendor != null && CmbDduVendor.Items.Count > 0)
        {
            if (_dduHardware.HasNvidia) CmbDduVendor.SelectedIndex = 0; // NVIDIA
            else if (_dduHardware.HasAmd) CmbDduVendor.SelectedIndex = 1; // AMD
            else if (_dduHardware.HasIntel) CmbDduVendor.SelectedIndex = 2; // Intel
        }
    }

    private async void BtnDduClean_Click(object sender, RoutedEventArgs e)
    {
        if (_dduBusy) return;

        bool restart = (sender as Button)?.Name == "BtnDduCleanRestart";
        string vendor = (CmbDduVendor.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "NVIDIA";

        var warn = MessageBox.Show(
            $"ATENÇÃO: A limpeza DDU de driver ({vendor}) irá:\n" +
            "• Fechar todos os programas que usam a GPU\n" +
            "• Desinstalar todos os pacotes do DriverStore com PnPUtil\n" +
            "• Apagar diretórios e caches em Program Files, ProgramData e AppData\n" +
            "• Limpar chaves do Registro\n\n" +
            (restart ? "O computador será REINICIADO ao final." : "O computador NÃO será reiniciado automaticamente.") +
            "\n\nDeseja continuar?",
            "Confirmação DDU",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (warn != MessageBoxResult.Yes) return;

        _dduBusy = true;
        _dduCts = new CancellationTokenSource();
        BtnDduCleanRestart.IsEnabled = false;
        BtnDduCleanNoRestart.IsEnabled = false;
        PgbDduProgress.Visibility = Visibility.Visible;
        TxtDduLog.Text = "";

        try
        {
            var (success, logText) = await DisplayDriverUninstaller.CleanGpuDriverAsync(
                vendor,
                (msg, progress) =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        TxtDduStatus.Text = msg;
                        PgbDduProgress.Value = progress * 100.0;
                        TxtDduLog.AppendText(msg + "\n");
                        TxtDduLog.ScrollToEnd();
                    });
                },
                _dduCts.Token);

            TxtDduStatus.Text = success ? "Limpeza DDU concluída com êxito!" : "Limpeza finalizada com avisos.";

            if (success && restart)
            {
                var r = MessageBox.Show("Limpeza concluída! Deseja reiniciar o computador agora?", "DDU Concluído",
                    MessageBoxButton.YesNo, MessageBoxImage.Information);
                if (r == MessageBoxResult.Yes)
                {
                    DisplayDriverUninstaller.RebootComputer();
                }
            }
        }
        catch (Exception ex)
        {
            TxtDduStatus.Text = $"Erro na execução do DDU: {ex.Message}";
        }
        finally
        {
            _dduBusy = false;
            BtnDduCleanRestart.IsEnabled = true;
            BtnDduCleanNoRestart.IsEnabled = true;
            PgbDduProgress.Visibility = Visibility.Collapsed;
        }
    }

    private void BtnDduSafeMode_Click(object sender, RoutedEventArgs e)
    {
        var r = MessageBox.Show(
            "O Lumini Tray irá configurar o Windows para reiniciar no Modo de Segurança mínimo.\n" +
            "Ao reiniciar no Modo de Segurança, abra o programa novamente e execute a limpeza DDU.\n\n" +
            "Reiniciar no Modo de Segurança agora?",
            "Reiniciar em Modo de Segurança",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (r == MessageBoxResult.Yes)
        {
            bool ok = DisplayDriverUninstaller.EnableSafeBootMinimal();
            if (ok)
            {
                DisplayDriverUninstaller.RebootComputer();
            }
            else
            {
                MessageBox.Show("Não foi possível configurar a inicialização segura via BCDEdit (requer privilégios de Administrador).", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }

    private void BtnDduRestoreNormalBoot_Click(object sender, RoutedEventArgs e)
    {
        bool ok = DisplayDriverUninstaller.RestoreNormalBoot();
        MessageBox.Show(ok ? "Inicialização normal do Windows restaurada com sucesso." : "A inicialização normal já está ativa ou não pôde ser alterada.",
            "Modo de Inicialização", MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
