using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace LuminiTray;

public partial class MainWindow
{
    private List<RestorePointItem> _restorePoints = new();
    private bool _isRestoreBusy = false;

    private async void InitRestorePage()
    {
        await RefreshRestorePointsAsync();
    }

    private async Task RefreshRestorePointsAsync()
    {
        if (_isRestoreBusy) return;
        _isRestoreBusy = true;

        if (BtnRestoreRefresh != null) BtnRestoreRefresh.IsEnabled = false;
        if (TxtRestoreStatus != null) TxtRestoreStatus.Text = "Carregando pontos de restauração do sistema...";
        if (RestoreLoadingBar != null) RestoreLoadingBar.Visibility = Visibility.Visible;

        try
        {
            var points = await Task.Run(() => SystemRestoreManager.GetRestorePoints());
            _restorePoints = points;

            if (LstRestorePoints != null)
            {
                LstRestorePoints.ItemsSource = null;
                LstRestorePoints.ItemsSource = _restorePoints;
            }

            if (TxtRestoreCount != null)
            {
                TxtRestoreCount.Text = $"{_restorePoints.Count} ponto{(_restorePoints.Count == 1 ? "" : "s")}";
            }

            if (TxtRestoreEmptyState != null)
            {
                TxtRestoreEmptyState.Visibility = _restorePoints.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            }

            string storage = await SystemRestoreManager.GetStorageUsageAsync();
            if (TxtRestoreStorageInfo != null)
            {
                TxtRestoreStorageInfo.Text = storage;
            }

            if (TxtRestoreStatus != null)
            {
                TxtRestoreStatus.Text = _restorePoints.Count > 0
                    ? $"Última atualização às {DateTime.Now:HH:mm:ss}."
                    : "Nenhum ponto de restauração encontrado (a Proteção do Sistema pode estar desativada no Windows).";
            }
        }
        catch (Exception ex)
        {
            if (TxtRestoreStatus != null)
                TxtRestoreStatus.Text = $"Erro ao listar pontos: {ex.Message}";
        }
        finally
        {
            _isRestoreBusy = false;
            if (BtnRestoreRefresh != null) BtnRestoreRefresh.IsEnabled = true;
            if (RestoreLoadingBar != null) RestoreLoadingBar.Visibility = Visibility.Collapsed;
        }
    }

    private async void BtnRestoreRefresh_Click(object sender, RoutedEventArgs e)
    {
        await RefreshRestorePointsAsync();
    }

    private async void BtnCreateRestorePoint_Click(object sender, RoutedEventArgs e)
    {
        string desc = TxtNewRestoreDesc?.Text?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(desc))
        {
            desc = $"Lumini Tray - Backup {DateTime.Now:dd/MM/yyyy HH:mm}";
        }

        if (BtnCreateRestore != null) BtnCreateRestore.IsEnabled = false;
        if (TxtRestoreStatus != null) TxtRestoreStatus.Text = "Criando ponto de restauração... Isso pode levar alguns segundos.";
        if (RestoreLoadingBar != null) RestoreLoadingBar.Visibility = Visibility.Visible;

        try
        {
            var (success, msg) = await SystemRestoreManager.CreateRestorePointAsync(desc);
            if (success)
            {
                Toast("Ponto de Restauração", "Ponto de restauração criado com sucesso!", "\uE777");
                if (TxtNewRestoreDesc != null) TxtNewRestoreDesc.Text = "";
            }
            else
            {
                MessageBox.Show(msg, "Criar Ponto de Restauração", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            await RefreshRestorePointsAsync();
        }
        finally
        {
            if (BtnCreateRestore != null) BtnCreateRestore.IsEnabled = true;
            if (RestoreLoadingBar != null) RestoreLoadingBar.Visibility = Visibility.Collapsed;
        }
    }

    private async void BtnDeleteRestorePoint_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not RestorePointItem item) return;

        var r = MessageBox.Show(
            $"Deseja realmente excluir o ponto de restauração?\n\n" +
            $"• Descrição: {item.Description}\n" +
            $"• Data: {item.DateText}\n" +
            $"• Sequência: #{item.SequenceNumber}\n\n" +
            "Esta ação liberará o espaço em disco ocupado por esta cópia de sombra e não pode ser desfeita.",
            "Excluir Ponto de Restauração",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (r != MessageBoxResult.Yes) return;

        if (TxtRestoreStatus != null) TxtRestoreStatus.Text = "Excluindo ponto de restauração...";
        if (RestoreLoadingBar != null) RestoreLoadingBar.Visibility = Visibility.Visible;

        var (success, msg) = await SystemRestoreManager.DeleteRestorePointAsync(item);
        if (success)
        {
            Toast("Ponto Excluído", "Ponto de restauração excluído com sucesso!", "\uE74D");
        }
        else
        {
            MessageBox.Show(msg, "Excluir Ponto de Restauração", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        await RefreshRestorePointsAsync();
    }

    private async void BtnDeleteOldestPoints_Click(object sender, RoutedEventArgs e)
    {
        var r = MessageBox.Show(
            "Deseja excluir todos os pontos de restauração antigos, mantendo apenas o mais recente?\n\n" +
            "Isso liberará vários gigabytes de espaço no disco sem deixar o sistema desprotegido.",
            "Limpar Pontos Antigos",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (r != MessageBoxResult.Yes) return;

        if (TxtRestoreStatus != null) TxtRestoreStatus.Text = "Excluindo pontos de restauração antigos...";
        if (RestoreLoadingBar != null) RestoreLoadingBar.Visibility = Visibility.Visible;

        var (success, msg) = await SystemRestoreManager.DeleteOldestPointsAsync();
        if (success)
        {
            Toast("Limpeza Concluída", msg, "\uE74D");
        }
        else
        {
            MessageBox.Show(msg, "Limpar Pontos Antigos", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        await RefreshRestorePointsAsync();
    }

    private async void BtnExecuteRestorePoint_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not RestorePointItem item) return;

        var r = MessageBox.Show(
            $"ATENÇÃO: Deseja restaurar o computador para este ponto?\n\n" +
            $"• Ponto: {item.Description}\n" +
            $"• Criado em: {item.DateText} ({item.TimeAgoText})\n" +
            $"• Tipo: {item.TypeText}\n\n" +
            "O Windows precisará reiniciar para aplicar a restauração e reverter alterações de sistema e drivers.\n" +
            "Deseja iniciar a restauração agora?",
            "Restaurar Sistema",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (r != MessageBoxResult.Yes) return;

        var (success, msg) = await SystemRestoreManager.ExecuteRestoreAsync(item.SequenceNumber);
        if (success)
        {
            MessageBox.Show(
                "A restauração foi programada com sucesso! O computador será reiniciado para concluir o processo.",
                "Restauração do Sistema",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        else
        {
            var fallback = MessageBox.Show(
                $"{msg}\n\nDeseja abrir o Assistente Oficial do Windows (rstrui.exe) para executar a restauração com interface gráfica interativa?",
                "Assistente do Windows",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (fallback == MessageBoxResult.Yes)
            {
                SystemRestoreManager.OpenWindowsRestoreWizard();
            }
        }
    }

    private void BtnOpenRstrui_Click(object sender, RoutedEventArgs e)
    {
        SystemRestoreManager.OpenWindowsRestoreWizard();
    }
}
