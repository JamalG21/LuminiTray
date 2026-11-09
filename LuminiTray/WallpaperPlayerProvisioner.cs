using System;
using System.Diagnostics;
using System.IO;

namespace LuminiTray;

/// <summary>
/// Garante que o player MPV nativo seja executado diretamente sob a identidade oficial
/// "LuminiTray.Wallpaper.exe" (com FileDescription: "Lumini Tray - Papel de Parede Animado"),
/// eliminando launchers intermediários e subprocessos anônimos ("mpv-core.exe" ou "mpv.exe").
/// </summary>
public static class WallpaperPlayerProvisioner
{
    private static readonly object _lock = new();
    private static string? _resolvedPath;

    /// <summary>
    /// Retorna o caminho absoluto do executável definitivo do player.
    /// Prioriza o binário unificado LuminiTray.Wallpaper.exe com metadados PE dedicados.
    /// </summary>
    public static string? GetOrProvisionPlayer()
    {
        lock (_lock)
        {
            if (_resolvedPath != null && File.Exists(_resolvedPath))
            {
                return _resolvedPath;
            }

            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string appDataMpvDir = Path.Combine(appData, "LuminiTray", "plugins", "mpv");

            // Candidato 1: LuminiTray.Wallpaper.exe direto na pasta do app
            string appAssetsWallpaper = Path.Combine(baseDir, "Assets", "Plugins", "mpv", "LuminiTray.Wallpaper.exe");
            if (IsValidNativePlayer(appAssetsWallpaper))
            {
                _resolvedPath = appAssetsWallpaper;
                return _resolvedPath;
            }

            // Candidato 2: LuminiTray.Wallpaper.exe provisionado no AppData
            string appDataWallpaper = Path.Combine(appDataMpvDir, "LuminiTray.Wallpaper.exe");
            if (IsValidNativePlayer(appDataWallpaper))
            {
                _resolvedPath = appDataWallpaper;
                return _resolvedPath;
            }

            // Provisionamento: localiza o mpv.exe nativo e copia como LuminiTray.Wallpaper.exe no AppData
            string? sourceMpv = FindSourceMpv(baseDir, appDataMpvDir);
            if (!string.IsNullOrEmpty(sourceMpv))
            {
                try
                {
                    Directory.CreateDirectory(appDataMpvDir);

                    // Copia dependências (vulkan-1.dll, etc.)
                    string srcDir = Path.GetDirectoryName(sourceMpv)!;
                    foreach (var dll in Directory.GetFiles(srcDir, "*.dll"))
                    {
                        string destDll = Path.Combine(appDataMpvDir, Path.GetFileName(dll));
                        if (!File.Exists(destDll) || new FileInfo(destDll).Length != new FileInfo(dll).Length)
                        {
                            File.Copy(dll, destDll, overwrite: true);
                        }
                    }

                    // Copia o player se não existir ou se o tamanho diferir
                    if (!File.Exists(appDataWallpaper) || new FileInfo(appDataWallpaper).Length != new FileInfo(sourceMpv).Length)
                    {
                        File.Copy(sourceMpv, appDataWallpaper, overwrite: true);
                    }

                    if (File.Exists(appDataWallpaper))
                    {
                        _resolvedPath = appDataWallpaper;
                        return _resolvedPath;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[WallpaperPlayerProvisioner] Falha ao provisionar no AppData: {ex.Message}");
                }

                // Fallback: executa o próprio sourceMpv diretamente
                _resolvedPath = sourceMpv;
                return _resolvedPath;
            }

            return null;
        }
    }

    /// <summary>
    /// Valida se o binário é o player nativo MPV (> 50 MB) e não um launcher .NET intermediário (< 40 MB).
    /// </summary>
    private static bool IsValidNativePlayer(string path)
    {
        if (!File.Exists(path)) return false;
        try
        {
            var info = new FileInfo(path);
            // O binário nativo do MPV possui ~57 MB
            return info.Length > 50_000_000;
        }
        catch
        {
            return false;
        }
    }

    private static string? FindSourceMpv(string baseDir, string appDataMpvDir)
    {
        string[] candidates = new[]
        {
            Path.Combine(baseDir, "Assets", "Plugins", "mpv", "mpv.exe"),
            Path.Combine(baseDir, "Assets", "Plugins", "mpv", "mpv-core.exe"),
            Path.Combine(baseDir, "plugins", "mpv", "mpv.exe"),
            Path.Combine(baseDir, "mpv.exe"),
            Path.Combine(appDataMpvDir, "mpv.exe")
        };

        foreach (var c in candidates)
        {
            if (File.Exists(c) && new FileInfo(c).Length > 50_000_000)
            {
                return c;
            }
        }

        return null;
    }
}
