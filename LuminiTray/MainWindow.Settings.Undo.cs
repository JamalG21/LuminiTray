using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;

namespace LuminiTray;

/// <summary>
/// Central Desfazer do Lumini Tray: visualização e reversão de otimizações aplicadas,
/// restauração do sistema e pontos de restauração.
/// </summary>
public partial class MainWindow
{
    private void RefreshUndoCenter()
    {
        if (LstUndoActiveTweaks == null || TxtUndoStatusTitle == null) return;

        var allTweaks = WindowsDebloatService.GetAllTweaks();
        var activeTweaks = new List<WindowsTweakItem>();

        foreach (var t in allTweaks)
        {
            try
            {
                bool isOn = t.Read() == true;
                bool hasBackup = _settings.TweakBackup.Keys.Any(k => k.StartsWith(t.Id));
                if (isOn || hasBackup)
                {
                    activeTweaks.Add(t);
                }
            }
            catch { }
        }

        LstUndoActiveTweaks.ItemsSource = null;
        LstUndoActiveTweaks.ItemsSource = activeTweaks;

        int count = activeTweaks.Count;
        TxtUndoCountBadge.Text = $"({count} ativos)";

        if (count > 0)
        {
            TxtUndoStatusIcon.Text = "⚠️";
            TxtUndoStatusTitle.Text = $"Você tem {count} ajustes aplicados no sistema";
            TxtUndoStatusSubtitle.Text = "Estes ajustes alteraram configurações ou registros do Windows. Você pode desfazê-los individualmente na lista abaixo ou restaurar tudo de fábrica com um clique.";
            TxtUndoEmptyState.Visibility = Visibility.Collapsed;
            BtnUndoRestoreAll.IsEnabled = true;
            BtnUndoRestoreAll.Opacity = 1.0;
        }
        else
        {
            TxtUndoStatusIcon.Text = "✅";
            TxtUndoStatusTitle.Text = "Seu PC está no estado original";
            TxtUndoStatusSubtitle.Text = "Nenhum ajuste ou modificação está ativo no momento. Todos os parâmetros utilizam as configurações padrão do Windows.";
            TxtUndoEmptyState.Visibility = Visibility.Visible;
            BtnUndoRestoreAll.IsEnabled = false;
            BtnUndoRestoreAll.Opacity = 0.6;
        }
    }

    private void BtnUndoSingleTweak_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not WindowsTweakItem tweak) return;

        try
        {
            tweak.Restore(_settings);
            tweak.Write(false, _settings);

            // Limpa chaves de backup deste tweak
            var keysToRemove = _settings.TweakBackup.Keys.Where(k => k.StartsWith(tweak.Id)).ToList();
            foreach (var k in keysToRemove)
            {
                _settings.TweakBackup.Remove(k);
            }
            _settings.Save();
            tweak.Refresh();

            RefreshUndoCenter();
            UpdateRevertButtonState();

            MessageBox.Show($"O ajuste \"{tweak.Title}\" foi revertido para o padrão original do Windows.",
                "Central Desfazer", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Não foi possível reverter o ajuste: {ex.Message}", "Erro ao Desfazer", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void BtnUndoRestoreAll_Click(object sender, RoutedEventArgs e)
    {
        var r = MessageBox.Show(
            "Deseja realmente restaurar todos os ajustes para as configurações originais de fábrica do Windows?",
            "Restaurar Tudo ao Padrão",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (r == MessageBoxResult.Yes)
        {
            try
            {
                var allTweaks = WindowsDebloatService.GetAllTweaks();
                WindowsDebloatService.RevertAllTweaks(_settings, allTweaks);

                RefreshUndoCenter();
                UpdateRevertButtonState();

                MessageBox.Show("Todos os ajustes foram revertidos com sucesso para o padrão de fábrica do Windows.",
                    "Central Desfazer Concluída", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro durante a restauração: {ex.Message}", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }

    private void BtnOpenSystemRestore_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("rstrui.exe") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Não foi possível abrir a Restauração do Sistema: {ex.Message}", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void BtnUndoCreateRestorePoint_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var (success, msg) = await SystemRestoreManager.CreateRestorePointAsync("Lumini Tray - Central Desfazer");
            if (success)
            {
                Toast("Ponto de Restauração", "Ponto de restauração criado com sucesso!", "\uE777");
            }
            else
            {
                MessageBox.Show(msg, "Criar Ponto de Restauração", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Falha ao solicitar ponto de restauração: {ex.Message}", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void BtnUndoRefresh_Click(object sender, RoutedEventArgs e)
    {
        RefreshUndoCenter();
    }
}
