using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace LuminiTray;

public partial class MainWindow
{
    private readonly ObservableCollection<RepairTaskItem> _repairTasksList = new();
    private CancellationTokenSource? _windowsRepairCts;
    private readonly StringBuilder _repairLogBuffer = new();

    private void InitWindowsRepairPage()
    {
        if (LstWindowsRepairTasks == null) return;

        if (_repairTasksList.Count == 0)
        {
            var defaultTasks = WindowsRepairService.GetDefaultRepairTasks();
            foreach (var t in defaultTasks) _repairTasksList.Add(t);
        }

        LstWindowsRepairTasks.ItemsSource = _repairTasksList;
        UpdateRepairTasksSummary();
    }

    private void UpdateRepairTasksSummary()
    {
        int checkedCount = _repairTasksList.Count(x => x.IsChecked);
        if (TxtWindowsRepairSummary != null)
        {
            TxtWindowsRepairSummary.Text = $"{checkedCount} reparo(s) marcado(s) • cerca de 15 a 45 min";
        }

        if (BtnWindowsRepairStart != null)
        {
            bool isRunning = _windowsRepairCts != null && !_windowsRepairCts.IsCancellationRequested;
            BtnWindowsRepairStart.IsEnabled = !isRunning && checkedCount > 0;
            BtnWindowsRepairStart.Content = isRunning ? "REPARANDO O WINDOWS..." : "REPARAR AGORA";
        }

        if (BtnWindowsRepairCancel != null)
        {
            BtnWindowsRepairCancel.IsEnabled = _windowsRepairCts != null && !_windowsRepairCts.IsCancellationRequested;
        }
    }

    private void ChkRepairTask_Changed(object sender, RoutedEventArgs e)
    {
        UpdateRepairTasksSummary();
    }

    private void BtnWindowsRepairSelectRecommended_Click(object sender, RoutedEventArgs e)
    {
        foreach (var t in _repairTasksList)
        {
            t.IsChecked = t.IsRecommended;
        }
        LstWindowsRepairTasks?.Items.Refresh();
        UpdateRepairTasksSummary();
    }

    private async void BtnWindowsRepairStart_Click(object sender, RoutedEventArgs e)
    {
        var active = _repairTasksList.Where(x => x.IsChecked).ToList();
        if (active.Count == 0) return;

        _windowsRepairCts?.Cancel();
        _windowsRepairCts = new CancellationTokenSource();

        _repairLogBuffer.Clear();
        if (TxtWindowsRepairLogs != null) TxtWindowsRepairLogs.Text = "Iniciando processo oficial de reparo do Windows...\n";
        if (BrdWindowsRepairResult != null) BrdWindowsRepairResult.Visibility = Visibility.Collapsed;

        UpdateRepairTasksSummary();

        try
        {
            await WindowsRepairService.ExecuteRepairsAsync(_repairTasksList, (update) =>
            {
                Dispatcher.Invoke(() =>
                {
                    if (!string.IsNullOrEmpty(update.LogLine))
                    {
                        _repairLogBuffer.AppendLine(update.LogLine);
                        if (TxtWindowsRepairLogs != null)
                        {
                            TxtWindowsRepairLogs.Text = _repairLogBuffer.ToString();
                            ScrWindowsRepairLogs?.ScrollToEnd();
                        }
                    }

                    if (PgbWindowsRepair != null)
                    {
                        PgbWindowsRepair.Value = update.OverallPercent;
                    }

                    LstWindowsRepairTasks?.Items.Refresh();
                });
            }, _windowsRepairCts.Token);

            // Resumo de conclusão
            if (BrdWindowsRepairResult != null)
            {
                BrdWindowsRepairResult.Visibility = Visibility.Visible;
                bool needsReboot = _repairTasksList.Any(x => x.IsChecked && x.RequiresReboot && x.Status == RepairStepStatus.Completed);
                if (TxtWindowsRepairResultSummary != null)
                {
                    TxtWindowsRepairResultSummary.Text = needsReboot
                        ? $"Reparo concluído às {DateTime.Now:HH:mm} • Tudo certo: reinicie o computador para efetivar as alterações."
                        : $"Reparo concluído às {DateTime.Now:HH:mm} • Todas as ferramentas foram executadas com sucesso.";
                }

                if (BtnWindowsRepairReboot != null)
                {
                    BtnWindowsRepairReboot.Visibility = needsReboot ? Visibility.Visible : Visibility.Collapsed;
                }
            }
        }
        catch (OperationCanceledException)
        {
            _repairLogBuffer.AppendLine("\n[AVISO] Operação de reparo cancelada pelo usuário.");
            if (TxtWindowsRepairLogs != null) TxtWindowsRepairLogs.Text = _repairLogBuffer.ToString();
        }
        catch (Exception ex)
        {
            MessageBox.Show("Erro durante a execução dos reparos: " + ex.Message, "Reparar o Windows", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _windowsRepairCts = null;
            UpdateRepairTasksSummary();
            LstWindowsRepairTasks?.Items.Refresh();
        }
    }

    private void BtnWindowsRepairCancel_Click(object sender, RoutedEventArgs e)
    {
        if (_windowsRepairCts != null && !_windowsRepairCts.IsCancellationRequested)
        {
            _windowsRepairCts.Cancel();
            BtnWindowsRepairCancel.IsEnabled = false;
        }
    }

    private void BtnWindowsRepairReboot_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show("Deseja reiniciar o computador agora para concluir as correções?",
            "Reiniciar Windows", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "shutdown.exe",
                    Arguments = "/r /t 5 /c \"Reiniciando para concluir o reparo do Lumini Tray\"",
                    CreateNoWindow = true,
                    UseShellExecute = false
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show("Não foi possível agendar o reinício: " + ex.Message, "Reiniciar", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
}
