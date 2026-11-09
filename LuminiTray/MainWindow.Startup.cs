using System.Threading.Tasks;
using System.Linq;
using System.Windows.Media;
using System.Windows;
using System.Windows.Controls;

namespace LuminiTray;

/// <summary>Aba RESET FERRAMENTAS · Inicialização: programas que abrem junto com o Windows.</summary>
public partial class MainWindow
{
    
    private bool _startupPageInitialized;

    private void InitStartupPage()
    {
        ScanStartup();
        if (!_startupPageInitialized)
        {
            _startupPageInitialized = true;
            _ = LoadStartupBootDiagnosticsAsync();
        }
    }

    private async Task LoadStartupBootDiagnosticsAsync()
    {
        try
        {
            var res = await BootDiagnosticsService.AnalyzeBootPerformanceAsync();

            Dispatcher.Invoke(() =>
            {
                if (TxtBootTypicalTime != null)
                {
                    TxtBootTypicalTime.Text = res.TypicalBootSeconds >= 60
                        ? $"{Math.Floor(res.TypicalBootSeconds / 60):F0}m {res.TypicalBootSeconds % 60:F0}s"
                        : $"{res.TypicalBootSeconds:F0} s";
                }

                if (TxtBootSummaryDetail != null)
                {
                    TxtBootSummaryDetail.Text = res.LastBoot != null
                        ? $"Último boot em {res.LastBoot.FormattedTotal} ({res.LastBoot.FormattedBreakdown})."
                        : "Tempo medido desde o acionamento do hardware até a área de trabalho pronta para uso.";
                }

                if (res.LastBoot != null)
                {
                    if (TxtBootMainPathTime != null) TxtBootMainPathTime.Text = $"{res.LastBoot.MainPathSeconds:F1} s";
                    if (TxtBootPostBootTime != null) TxtBootPostBootTime.Text = $"{res.LastBoot.PostBootSeconds:F1} s";
                    if (TxtBootLastDate != null) TxtBootLastDate.Text = res.LastBoot.Timestamp.ToString("dd/MM/yyyy HH:mm");
                }

                if (TxtBootLastUptime != null)
                {
                    var uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);
                    TxtBootLastUptime.Text = $"Ligado há {(int)uptime.TotalHours}h {uptime.Minutes}m";
                }

                if (TxtFastStartupBadge != null)
                {
                    TxtFastStartupBadge.Text = res.FastStartupEnabled ? "Início rápido do Windows ativo" : "Início rápido desativado";
                }
                if (BadgeFastStartup != null)
                {
                    BadgeFastStartup.ToolTip = res.FastStartupNote;
                }
                if (IcoFastStartup != null)
                {
                    IcoFastStartup.Text = res.FastStartupEnabled ? "" : "";
                    IcoFastStartup.Foreground = res.FastStartupEnabled
                        ? (Brush)new BrushConverter().ConvertFrom("#3B82F6")!
                        : (Brush)new BrushConverter().ConvertFrom("#94A3B8")!;
                }

                // Timeline
                if (BootTimelineList != null && res.Timeline.Count > 0)
                {
                    double maxVal = Math.Max(res.Timeline.Max(t => t.TotalSeconds), 1.0);
                    var timelineUi = res.Timeline.Select(t => new
                    {
                        t.FormattedDate,
                        t.FormattedTotal,
                        BarWidth = Math.Clamp((t.TotalSeconds / maxVal) * 260.0, 10.0, 260.0)
                    }).ToList();
                    BootTimelineList.ItemsSource = timelineUi;
                }

                // Degraders
                if (BootDegradersList != null)
                {
                    if (res.Degraders.Count > 0)
                    {
                        if (TxtDegradersEmpty != null) TxtDegradersEmpty.Visibility = Visibility.Collapsed;
                        BootDegradersList.ItemsSource = res.Degraders;
                    }
                    else
                    {
                        if (TxtDegradersEmpty != null) TxtDegradersEmpty.Visibility = Visibility.Visible;
                        BootDegradersList.ItemsSource = null;
                    }
                }
            });
        }
        catch { }
    }

private void ScanStartup()
    {
        UpdateAutoStartUi();

        List<StartupEntry> list;
        try { list = StartupManager.List(); }
        catch (Exception ex) { StartupResult.Text = $"Não foi possível listar: {ex.Message}"; return; }

        StartupList.ItemsSource = list;
        int on = list.Count(e => e.Enabled);
        StartupSummary.Text = list.Count == 0 ? "NENHUM PROGRAMA NA INICIALIZAÇÃO"
                            : $"{list.Count} PROGRAMAS · {on} ATIVADOS · {list.Count - on} DESATIVADOS";
        BtnStartupRestore.IsEnabled = _settings.StartupRemoved.Count > 0;
        BtnStartupRestore.Content = _settings.StartupRemoved.Count > 0
            ? $"RESTAURAR REMOVIDOS ({_settings.StartupRemoved.Count})" : "RESTAURAR REMOVIDOS";
    }

    private void BtnStartupScan_Click(object sender, RoutedEventArgs e)
    {
        StartupResult.Text = "";
        ScanStartup();
        _ = LoadStartupBootDiagnosticsAsync();
    }

    private void StartupToggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox { Tag: StartupEntry item } box) return;
        bool turnOn = box.IsChecked == true;
        if (item.Machine && !RequireAdmin()) { box.IsChecked = item.Enabled; return; }
        try
        {
            StartupManager.SetEnabled(item, turnOn);
            StartupResult.Text = $"{item.Name}: {(turnOn ? "ativado" : "desativado")}. Vale na próxima inicialização do Windows.";
        }
        catch (Exception ex) { StartupResult.Text = $"{item.Name}: falhou ({ex.Message})."; }
        ScanStartup();
    }

    private void StartupRemove_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: StartupEntry item }) return;
        if (item.Machine && !RequireAdmin()) return;
        var r = MessageBox.Show($"Remover \"{item.Name}\" da inicialização do Windows?\n\n{item.Command}\n\n" +
                                "O programa continua instalado, só deixa de abrir sozinho. " +
                                "Dá para desfazer em \"Restaurar removidos\".\n\n" +
                                "Se só quiser testar, prefira desligar o interruptor.",
                                "Inicialização", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (r != MessageBoxResult.Yes) return;
        try
        {
            _settings.StartupRemoved.Add(StartupManager.Remove(item));
            _settings.Save();
            StartupResult.Text = $"{item.Name} removido da inicialização.";
        }
        catch (Exception ex) { StartupResult.Text = $"{item.Name}: não foi possível remover ({ex.Message})."; }
        ScanStartup();
    }

    private void BtnStartupRestore_Click(object sender, RoutedEventArgs e)
    {
        var removed = _settings.StartupRemoved;
        if (removed.Count == 0) return;
        if (removed.Any(b => b.Machine) && !RequireAdmin()) return;
        string names = string.Join("\n", removed.Take(12).Select(b => $"  • {b.Name}"))
                       + (removed.Count > 12 ? $"\n  … e mais {removed.Count - 12}" : "");
        var r = MessageBox.Show($"Devolver {removed.Count} programas à inicialização do Windows?\n\n{names}",
                                "Inicialização", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (r != MessageBoxResult.Yes) return;

        var failed = new List<string>();
        foreach (var b in removed.ToList())
        {
            try { StartupManager.Restore(b); removed.Remove(b); }
            catch (Exception ex) { failed.Add($"{b.Name} ({ex.Message})"); }
        }
        _settings.Save();
        StartupResult.Text = failed.Count == 0 ? "Programas restaurados." : $"Não restaurados: {string.Join("; ", failed)}";
        ScanStartup();
    }

    private void UpdateAutoStartUi()
    {
        if (ChkAutoStartWithWindows == null || TxtAutoStartStatus == null || TxtAutoStartLastRun == null)
            return;

        bool taskEnabled = AutoStartManager.IsAutoStartEnabled();
        ChkAutoStartWithWindows.IsChecked = _settings.AutoStartWithWindows || taskEnabled;
        ChkAutoCleanRam.IsChecked = _settings.AutoCleanRam;
        ChkAutoApplyTweaks.IsChecked = _settings.AutoApplyTweaks;
        ChkAutoCloseApps.IsChecked = _settings.AutoCloseBackgroundApps;
        ChkAutoStartMinimized.IsChecked = _settings.AutoStartMinimized;

        string statusDesc = taskEnabled
            ? "ATIVADO (Inicia com o Windows como Administrador · Agendador de Tarefas)"
            : "DESATIVADO (Não inicia automaticamente)";
        TxtAutoStartStatus.Text = statusDesc;

        if (_settings.LastAutoFeaturesApplied is { } dt)
        {
            TxtAutoStartLastRun.Text = $"Última execução: {dt:dd/MM/yyyy HH:mm:ss} · {_settings.LastAutoFeaturesSummary}";
        }
        else
        {
            TxtAutoStartLastRun.Text = "Última execução: Nenhuma registrada ainda.";
        }
    }

    private void ChkAutoStartWithWindows_Click(object sender, RoutedEventArgs e)
    {
        bool enable = ChkAutoStartWithWindows.IsChecked == true;
        try
        {
            AutoStartManager.SetAutoStart(enable, _settings.AutoStartMinimized);
            _settings.AutoStartWithWindows = enable;
            _settings.Save();
            Toast("Inicialização Automática",
                  enable ? "Lumini Tray configurado para iniciar junto com o Windows."
                         : "Inicialização automática com o Windows desativada.",
                  "\uE88E");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Não foi possível alterar a inicialização: {ex.Message}", "Lumini Tray", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        UpdateAutoStartUi();
    }

    private void AutoFeatureOption_Click(object sender, RoutedEventArgs e)
    {
        _settings.AutoCleanRam = ChkAutoCleanRam.IsChecked == true;
        _settings.AutoApplyTweaks = ChkAutoApplyTweaks.IsChecked == true;
        _settings.AutoCloseBackgroundApps = ChkAutoCloseApps.IsChecked == true;
        _settings.AutoStartMinimized = ChkAutoStartMinimized.IsChecked == true;
        _settings.Save();

        if (_settings.AutoStartWithWindows && AutoStartManager.IsAutoStartEnabled())
        {
            try { AutoStartManager.SetAutoStart(true, _settings.AutoStartMinimized); } catch { }
        }
        UpdateAutoStartUi();
    }

    private async void BtnApplyAutoNow_Click(object sender, RoutedEventArgs e)
    {
        await ApplyAutoFeaturesAsync(showToast: true);
    }
}
