using System.Diagnostics;
using System.IO;

namespace LuminiTray;

/// <summary>
/// Resultado da aplicação das funções automáticas do Lumini Tray.
/// </summary>
public sealed record AutoFeaturesResult(
    bool Success,
    int TweaksApplied,
    long MemoryFreedBytes,
    bool StandbyPurged,
    List<string> ClosedApps,
    string Summary
);

/// <summary>
/// Coordena a execução das funções automáticas do Lumini Tray:
/// limpeza de memória (RAM e Standby list), aplicação de ajustes de desempenho do Windows
/// e fechamento opcional de programas dispensáveis em segundo plano.
/// </summary>
public static class AutoFeaturesManager
{
    public static async Task<AutoFeaturesResult> ApplyAsync(AppSettings settings, Action<string>? logger = null)
    {
        int tweaksCount = 0;
        long freedBytes = 0;
        bool standbyPurged = false;
        var closedApps = new List<string>();
        var summaryParts = new List<string>();

        // 1. Ajustes do Windows para Jogos
        if (settings.AutoApplyTweaks)
        {
            try
            {
                logger?.Invoke("Aplicando ajustes de desempenho do Windows...");
                var tweaks = WindowsTweaks.All();
                foreach (var tweak in tweaks)
                {
                    try
                    {
                        tweak.Write(true, settings);
                        tweaksCount++;
                    }
                    catch (Exception ex)
                    {
                        logger?.Invoke($"Aviso no tweak '{tweak.Id}': {ex.Message}");
                    }
                }
                summaryParts.Add($"{tweaksCount} ajustes do Windows aplicados");
            }
            catch (Exception ex)
            {
                logger?.Invoke($"Erro nos ajustes do Windows: {ex.Message}");
            }
        }

        // 2. Limpeza de Memória (RAM + Lista em Espera do Windows)
        if (settings.AutoCleanRam)
        {
            try
            {
                logger?.Invoke("Limpando memória em espera e enxugando processos...");
                var mem = await Task.Run(Optimizer.CleanMemory);
                freedBytes = mem.FreedBytes;
                standbyPurged = mem.StandbyPurged;
                summaryParts.Add($"Memória: {freedBytes / 1048576.0:N0} MB liberados");
                if (standbyPurged) summaryParts.Add("memória em espera limpa");
            }
            catch (Exception ex)
            {
                logger?.Invoke($"Erro na limpeza de memória: {ex.Message}");
            }
        }

        // 3. Fechamento de Programas Dispensáveis em Segundo Plano
        if (settings.AutoCloseBackgroundApps)
        {
            try
            {
                logger?.Invoke("Fechando programas dispensáveis em segundo plano...");
                var apps = Optimizer.ListApps().Where(a => a.Suggested).ToList();
                if (apps.Count > 0)
                {
                    var closeRes = await Task.Run(() => Optimizer.CloseApps(apps));
                    closedApps = closeRes.Closed;
                    if (closedApps.Count > 0)
                    {
                        summaryParts.Add($"{closedApps.Count} programas fechados ({string.Join(", ", closedApps)})");
                    }
                }
            }
            catch (Exception ex)
            {
                logger?.Invoke($"Erro ao fechar programas: {ex.Message}");
            }
        }

        // 4. Troca Automática de Wallpaper ao Iniciar
        if (settings.AutoChangeWallpaperOnStartup)
        {
            try
            {
                string folder = settings.WallpaperFolder ?? WallpaperManager.DefaultLibraryFolder;
                if (Directory.Exists(folder))
                {
                    var items = WallpaperManager.ScanFolder(folder);
                    if (items.Count > 0)
                    {
                        var chosen = items[Random.Shared.Next(items.Count)];
                        WallpaperManager.SetWallpaper(chosen.FilePath, settings.WallpaperStyle, settings.SelectedWallpaperMonitorId);
                        settings.LastWallpaperPath = chosen.FilePath;
                        summaryParts.Add($"Wallpaper: {chosen.FileName}");
                    }
                }
            }
            catch (Exception ex)
            {
                logger?.Invoke($"Erro ao trocar wallpaper na inicialização: {ex.Message}");
            }
        }

        string fullSummary = summaryParts.Count > 0
            ? string.Join(" · ", summaryParts) + "."
            : "Nenhuma função automática ativada nas configurações.";

        settings.LastAutoFeaturesApplied = DateTime.Now;
        settings.LastAutoFeaturesSummary = fullSummary;
        try { settings.Save(); } catch { }

        logger?.Invoke($"Concluído: {fullSummary}");

        return new AutoFeaturesResult(
            Success: true,
            TweaksApplied: tweaksCount,
            MemoryFreedBytes: freedBytes,
            StandbyPurged: standbyPurged,
            ClosedApps: closedApps,
            Summary: fullSummary
        );
    }
}
