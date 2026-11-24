using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;

namespace LuminiTray;

public partial class MainWindow
{
    // =========================================================================
    // REINICIALIZAÇÃO AVANÇADA PARA BIOS / UEFI (15s + CANCELAMENTO)
    // =========================================================================

    private DispatcherTimer? _biosCountdownTimer;
    private int _biosSecondsLeft;

    private void BtnBiosRebootStart_Click(object sender, RoutedEventArgs e)
    {
        if (_biosCountdownTimer != null && _biosCountdownTimer.IsEnabled)
            return;

        _biosSecondsLeft = 15;
        UpdateBiosCountdownUi(active: true);

        _biosCountdownTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _biosCountdownTimer.Tick -= OnBiosCountdownTick;
        _biosCountdownTimer.Tick += OnBiosCountdownTick;
        _biosCountdownTimer.Start();
    }

    private void OnBiosCountdownTick(object? sender, EventArgs e)
    {
        _biosSecondsLeft--;
        if (_biosSecondsLeft <= 0)
        {
            _biosCountdownTimer?.Stop();
            UpdateBiosCountdownUi(active: false);
            ExecuteFirmwareReboot();
            return;
        }

        UpdateBiosCountdownUi(active: true);
    }

    private void BtnBiosRebootCancel_Click(object sender, RoutedEventArgs e)
    {
        _biosCountdownTimer?.Stop();
        UpdateBiosCountdownUi(active: false);
        if (TxtBiosRebootStatus != null)
        {
            TxtBiosRebootStatus.Text = "Reinicialização para a BIOS/UEFI cancelada pelo usuário.";
            TxtBiosRebootStatus.Visibility = Visibility.Visible;
        }
    }

    private void BtnBiosRebootNow_Click(object sender, RoutedEventArgs e)
    {
        _biosCountdownTimer?.Stop();
        UpdateBiosCountdownUi(active: false);
        ExecuteFirmwareReboot();
    }

    private void UpdateBiosCountdownUi(bool active)
    {
        if (PanelBiosCountdown != null)
            PanelBiosCountdown.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
        if (BtnBiosRebootStart != null)
            BtnBiosRebootStart.IsEnabled = !active;
        if (TxtBiosCountdown != null && active)
            TxtBiosCountdown.Text = $"Reiniciando para a BIOS/UEFI em {_biosSecondsLeft} s...";
        if (PrgBiosCountdown != null && active)
            PrgBiosCountdown.Value = _biosSecondsLeft;
        if (TxtBiosRebootStatus != null && active)
            TxtBiosRebootStatus.Visibility = Visibility.Collapsed;
    }

    private void ExecuteFirmwareReboot()
    {
        try
        {
            bool elevated = SpecialShortcutService.IsProcessElevated();
            var psi = new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "shutdown.exe"),
                Arguments = "/r /fw /t 0",
                UseShellExecute = !elevated,
                CreateNoWindow = elevated,
                Verb = elevated ? "" : "runas"
            };
            Process.Start(psi);
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            if (TxtBiosRebootStatus != null)
            {
                TxtBiosRebootStatus.Text = "Permissão de administrador (UAC) cancelada. O reinício para a BIOS/UEFI não foi executado.";
                TxtBiosRebootStatus.Visibility = Visibility.Visible;
            }
        }
        catch (Exception ex)
        {
            if (TxtBiosRebootStatus != null)
            {
                TxtBiosRebootStatus.Text = $"Não foi possível reiniciar para a BIOS/UEFI: {ex.Message}";
                TxtBiosRebootStatus.Visibility = Visibility.Visible;
            }
        }
    }

    // =========================================================================
    // ATALHO ESPECIAL (ADMIN SEM UAC)
    // =========================================================================

    private List<SpecialShortcutExeCandidate> _specialShortcutExeCandidates = new();
    private bool _specialShortcutExesLoaded;

    private async Task LoadSpecialShortcutExeCandidatesAsync(bool force = false)
    {
        if (_specialShortcutExesLoaded && !force) return;
        _specialShortcutExesLoaded = true;

        if (BtnSpecialShortcutScanExes != null) BtnSpecialShortcutScanExes.IsEnabled = false;
        try
        {
            _specialShortcutExeCandidates = await SpecialShortcutService.DiscoverExeCandidatesAsync();
            ApplySpecialShortcutExeFilter();
        }
        finally
        {
            if (BtnSpecialShortcutScanExes != null) BtnSpecialShortcutScanExes.IsEnabled = true;
        }
    }

    private void ApplySpecialShortcutExeFilter()
    {
        if (LstSpecialShortcutExes == null) return;
        string q = TxtSpecialShortcutExeSearch?.Text?.Trim() ?? "";
        var filtered = string.IsNullOrEmpty(q)
            ? _specialShortcutExeCandidates
            : _specialShortcutExeCandidates.Where(c =>
                c.Name.Contains(q, StringComparison.CurrentCultureIgnoreCase) ||
                c.ExePath.Contains(q, StringComparison.CurrentCultureIgnoreCase)).ToList();

        LstSpecialShortcutExes.ItemsSource = filtered;
    }

    private void TxtSpecialShortcutExeSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        ApplySpecialShortcutExeFilter();
    }

    private async void BtnSpecialShortcutScanExes_Click(object sender, RoutedEventArgs e)
    {
        await LoadSpecialShortcutExeCandidatesAsync(force: true);
    }

    private void LstSpecialShortcutExes_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LstSpecialShortcutExes?.SelectedItem is not SpecialShortcutExeCandidate selected) return;
        SelectSpecialShortcutExe(selected.ExePath, selected.Name);
    }

    private void SelectSpecialShortcutExe(string exePath, string? suggestedName = null)
    {
        if (TxtSpecialShortcutExe != null)
            TxtSpecialShortcutExe.Text = exePath;

        if (TxtSpecialShortcutName != null)
            TxtSpecialShortcutName.Text = string.IsNullOrWhiteSpace(suggestedName)
                ? Path.GetFileNameWithoutExtension(exePath)
                : suggestedName;

        UpdateSpecialShortcutTaskPreview();
    }

    private void RefreshSpecialShortcutsList()
    {
        if (LstSpecialShortcuts == null || TxtSpecialShortcutEmpty == null) return;
        var items = SpecialShortcutService.ListDesktopSpecialShortcuts();
        LstSpecialShortcuts.ItemsSource = items;
        bool hasItems = items.Count > 0;
        LstSpecialShortcuts.Visibility = hasItems ? Visibility.Visible : Visibility.Collapsed;
        TxtSpecialShortcutEmpty.Visibility = hasItems ? Visibility.Collapsed : Visibility.Visible;
    }

    private void BtnSpecialShortcutRefresh_Click(object sender, RoutedEventArgs e)
    {
        RefreshSpecialShortcutsList();
    }

    private void BtnSpecialShortcutBrowse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Selecionar executável para Atalho Especial",
            Filter = "Executáveis (*.exe)|*.exe",
            CheckFileExists = true
        };

        if (dlg.ShowDialog(this) != true) return;

        string name = Path.GetFileNameWithoutExtension(dlg.FileName);
        if (!_specialShortcutExeCandidates.Any(c => c.ExePath.Equals(dlg.FileName, StringComparison.OrdinalIgnoreCase)))
        {
            _specialShortcutExeCandidates.Insert(0, new SpecialShortcutExeCandidate(name, dlg.FileName, "Manual"));
            ApplySpecialShortcutExeFilter();
        }

        SelectSpecialShortcutExe(dlg.FileName, name);
    }

    private void TxtSpecialShortcutName_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateSpecialShortcutTaskPreview();
    }

    private void UpdateSpecialShortcutTaskPreview()
    {
        if (TxtSpecialShortcutTaskPreview == null) return;
        string name = TxtSpecialShortcutName?.Text ?? "";
        TxtSpecialShortcutTaskPreview.Text = $"Tarefa: {SpecialShortcutService.BuildTaskName(name)}";
    }

    private void BtnSpecialShortcutCreate_Click(object sender, RoutedEventArgs e)
    {
        string exePath = TxtSpecialShortcutExe?.Text?.Trim() ?? "";
        string shortcutName = TxtSpecialShortcutName?.Text?.Trim() ?? "";

        var (success, message, _) = SpecialShortcutService.CreateSpecialShortcut(exePath, shortcutName);
        if (TxtSpecialShortcutStatus != null)
        {
            TxtSpecialShortcutStatus.Text = message;
            TxtSpecialShortcutStatus.Visibility = Visibility.Visible;
        }

        if (success)
            RefreshSpecialShortcutsList();
    }

    private void BtnSpecialShortcutRemove_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: SpecialShortcutItem item }) return;

        var (success, message) = SpecialShortcutService.RemoveSpecialShortcut(item);
        if (TxtSpecialShortcutStatus != null)
        {
            TxtSpecialShortcutStatus.Text = message;
            TxtSpecialShortcutStatus.Visibility = Visibility.Visible;
        }

        if (success)
            RefreshSpecialShortcutsList();
    }
}
