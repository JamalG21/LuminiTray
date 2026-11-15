using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace LuminiTray;

public partial class MainWindow
{
    private bool _updateApplying;
    private bool _wasMinimizedForUpdate;
    private CancellationTokenSource? _updateCts;

    /// <summary>Pesquisa automática: só chamada ao iniciar com janela visível e ao abrir o launcher.</summary>
    private void TriggerAutoUpdateCheck()
    {
        if (!_settings.AutoCheckUpdates) return;
        _ = RunUpdateCheckAsync(force: false, showPopupIfAvailable: false);
    }

    private async Task RunUpdateCheckAsync(bool force, bool showPopupIfAvailable)
    {
        try
        {
            if (TxtUpdateStatus != null && PopupUpdateNotify?.IsOpen == true)
                TxtUpdateStatus.Text = "Verificando...";
            var info = await AppUpdaterService.Instance.CheckForUpdatesAsync(force);
            await Dispatcher.InvokeAsync(() =>
            {
                RefreshUpdateUi(info, checkedNow: force);
                if (showPopupIfAvailable && AppUpdaterService.Instance.UpdateAvailable && PopupUpdateNotify != null)
                    PopupUpdateNotify.IsOpen = true;
            });
        }
        catch { }
    }

    private void RefreshUpdateUi(UpdateReleaseInfo? info, bool checkedNow)
    {
        bool available = AppUpdaterService.Instance.UpdateAvailable && info != null;
        if (BadgeUpdateDot != null)
            BadgeUpdateDot.Visibility = available ? Visibility.Visible : Visibility.Collapsed;
        if (BtnApplyUpdateNow != null)
            BtnApplyUpdateNow.Visibility = available && !_updateApplying ? Visibility.Visible : Visibility.Collapsed;

        string cur = AppUpdaterService.CurrentVersion.ToString(3);
        if (TxtUpdateMode != null)
            TxtUpdateMode.Text = AppUpdaterService.IsInstalledDistribution() ? "Modo Instalado" : "Modo Portátil";

        if (info == null)
        {
            if (TxtUpdateStatus != null) TxtUpdateStatus.Text = $"Versão atual: v{cur}. Não foi possível consultar o GitHub.";
            if (TxtUpdateNotesPreview != null) TxtUpdateNotesPreview.Text = "";
            if (TxtSettingsUpdateInfo != null && checkedNow) TxtSettingsUpdateInfo.Text = "Falha ao consultar atualizações.";
            return;
        }

        if (available)
        {
            if (TxtUpdateStatus != null) TxtUpdateStatus.Text = $"Nova versão v{info.Version.ToString(3)} disponível! (atual v{cur})";
            if (TxtUpdateNotesPreview != null) TxtUpdateNotesPreview.Text = info.Notes;
            if (BtnUpdateNotify != null) BtnUpdateNotify.ToolTip = $"Atualização disponível: v{info.Version.ToString(3)}";
            if (TxtSettingsUpdateInfo != null) TxtSettingsUpdateInfo.Text = $"Nova versão disponível: v{info.Version.ToString(3)}";
        }
        else
        {
            if (TxtUpdateStatus != null) TxtUpdateStatus.Text = $"Você está na versão mais recente (v{cur}).";
            if (TxtUpdateNotesPreview != null) TxtUpdateNotesPreview.Text = "";
            if (BtnUpdateNotify != null) BtnUpdateNotify.ToolTip = "Atualizações do programa";
            if (TxtSettingsUpdateInfo != null && checkedNow) TxtSettingsUpdateInfo.Text = $"Já está na versão mais recente (v{cur}).";
        }
    }

    private void BtnUpdateNotify_Click(object sender, RoutedEventArgs e)
    {
        if (PopupUpdateNotify == null) return;
        PopupUpdateNotify.IsOpen = !PopupUpdateNotify.IsOpen;
        if (PopupUpdateNotify.IsOpen)
        {
            RefreshUpdateUi(AppUpdaterService.Instance.LatestRelease, checkedNow: false);
            if (AppUpdaterService.Instance.LatestRelease == null)
                _ = RunUpdateCheckAsync(force: true, showPopupIfAvailable: false);
        }
    }

    private void BtnCheckUpdateManual_Click(object sender, RoutedEventArgs e)
    {
        if (TxtSettingsUpdateInfo != null) TxtSettingsUpdateInfo.Text = "Verificando...";
        _ = RunUpdateCheckAsync(force: true, showPopupIfAvailable: sender == BtnCheckUpdateManual ? false : true);
    }

    private async void BtnApplyUpdateNow_Click(object sender, RoutedEventArgs e)
    {
        var info = AppUpdaterService.Instance.LatestRelease;
        if (info == null || _updateApplying) return;
        _updateApplying = true;
        BtnApplyUpdateNow.Visibility = Visibility.Collapsed;
        PbUpdateProgress.Visibility = Visibility.Visible;
        TxtUpdateProgress.Visibility = Visibility.Visible;
        TxtUpdateProgress.Text = "Baixando atualização...";
        _updateCts = new CancellationTokenSource();
        try
        {
            var progress = new Progress<double>(p =>
            {
                PbUpdateProgress.Value = p;
                TxtUpdateProgress.Text = $"Baixando atualização... {p:0}%";
            });
            bool ok = await AppUpdaterService.Instance.DownloadAndApplyUpdateAsync(info, progress, _updateCts.Token);
            if (!ok)
            {
                TxtUpdateProgress.Text = "Pacote de atualização não encontrado na release.";
                _updateApplying = false;
                BtnApplyUpdateNow.Visibility = Visibility.Visible;
                return;
            }
            TxtUpdateProgress.Text = "Aplicando e reiniciando...";
            await Task.Delay(400);
            PerformFullExit();
        }
        catch (Exception ex)
        {
            TxtUpdateProgress.Text = $"Falha na atualização: {ex.Message}";
            _updateApplying = false;
            BtnApplyUpdateNow.Visibility = Visibility.Visible;
        }
    }

    private void SettingsAutoCheckUpdates_Changed(object sender, RoutedEventArgs e)
    {
        if (_isSettingsLoading || ChkSettingsAutoCheckUpdates == null) return;
        _settings.AutoCheckUpdates = ChkSettingsAutoCheckUpdates.IsChecked == true;
        _settings.Save();
    }
}
