using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace LuminiTray;

/// <summary>
/// Modelo de um programa ou processo elegível para seleção na lista de exclusão.
/// </summary>
public sealed class SelectableAppItem : INotifyPropertyChanged
{
    public string ExeName { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string Detail { get; init; } = "";
    public string ExecutablePath { get; set; } = "";
    public string Category { get; init; } = "Programas Instalados"; // "Em Execução", "Jogos", "Programas Instalados"
    public bool IsRunning { get; init; }
    public bool IsGame { get; init; }
    public bool IsAlreadyExcluded { get; init; }

    public ImageSource? IconSource { get; set; }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }
    }

    public string CategoryBadge => Category switch
    {
        "Em Execução" => "⚡ Em Execução",
        "Jogos" => "🎮 Jogo",
        _ => "💻 Instalado"
    };

    public string CategoryColor => Category switch
    {
        "Em Execução" => "#10B981", // Verde esmeralda
        "Jogos" => "#8B5CF6",       // Roxo gamer
        _ => "#0EA5E9"              // Cyan / Azul
    };

    public string StatusBadge => IsAlreadyExcluded ? "✔ Já Blindado" : "";

    public System.Windows.Visibility AlreadyExcludedVisibility => IsAlreadyExcluded ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
    public System.Windows.Visibility IconVisibility => IconSource != null ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
    public System.Windows.Visibility FallbackIconVisibility => IconSource == null ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
    public bool CanSelect => !IsAlreadyExcluded;

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>
/// Motor de varredura sob demanda de programas instalados e processos em execução.
/// NUNCA executa em segundo plano por conta própria; dispara exclusivamente quando o usuário solicita.
/// </summary>
public static class AppScannerService
{
    private static readonly ConcurrentDictionary<string, ImageSource?> IconCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Executa uma varredura pontual sob demanda de processos ativos e programas instalados.
    /// </summary>
    public static async Task<List<SelectableAppItem>> ScanAllAppsAsync(IEnumerable<string>? existingExclusions = null, CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            var exclusionSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (existingExclusions != null)
            {
                foreach (var ex in existingExclusions)
                {
                    if (string.IsNullOrWhiteSpace(ex)) continue;
                    exclusionSet.Add(NormalizeExe(ex));
                }
            }

            // Dicionário consolidado por nome do executável (ex: "discord.exe")
            var map = new Dictionary<string, AppCandidate>(StringComparer.OrdinalIgnoreCase);

            // 1. Processos em execução na sessão do usuário
            ScanRunningProcesses(map, ct);

            // 2. Jogos instalados (Steam, Epic, GOG, etc.)
            ScanGames(map, ct);

            // 3. Atalhos do Menu Iniciar e Desktop
            ScanShortcuts(map, ct);

            // 4. Softwares registrados em App Paths do Windows
            ScanAppPaths(map, ct);

            // 5. Softwares instalados no Registro (Uninstall)
            ScanUninstallRegistry(map, ct);

            // Converte e ordena os resultados (Em execução primeiro, depois Jogos, depois Instalados em ordem alfabética)
            var result = new List<SelectableAppItem>();
            foreach (var kvp in map.Values)
            {
                if (ct.IsCancellationRequested) break;

                bool isExcluded = exclusionSet.Contains(kvp.ExeName);
                ImageSource? icon = null;
                if (!string.IsNullOrEmpty(kvp.FullPath) && File.Exists(kvp.FullPath))
                {
                    icon = GetOrExtractIcon(kvp.FullPath);
                }

                result.Add(new SelectableAppItem
                {
                    ExeName = kvp.ExeName,
                    DisplayName = kvp.DisplayName,
                    Detail = kvp.Detail,
                    ExecutablePath = kvp.FullPath ?? "",
                    Category = kvp.Category,
                    IsRunning = kvp.IsRunning,
                    IsGame = kvp.IsGame,
                    IsAlreadyExcluded = isExcluded,
                    IsSelected = false,
                    IconSource = icon
                });
            }

            return result
                .OrderByDescending(x => x.IsRunning)
                .ThenByDescending(x => x.IsGame)
                .ThenBy(x => x.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }, ct);
    }

    private static void ScanRunningProcesses(Dictionary<string, AppCandidate> map, CancellationToken ct)
    {
        try
        {
            int session = Process.GetCurrentProcess().SessionId;
            foreach (var p in Process.GetProcesses())
            {
                if (ct.IsCancellationRequested) return;
                try
                {
                    if (p.SessionId != session) continue;

                    string name = p.ProcessName;
                    if (string.IsNullOrWhiteSpace(name)) continue;

                    // Ignora processos essenciais do sistema e kernel
                    if (IsProtectedSystemProcess(name)) continue;

                    string exeName = NormalizeExe(name);
                    string? fullPath = null;
                    try { fullPath = p.MainModule?.FileName; } catch { }

                    string title = name;
                    try
                    {
                        if (!string.IsNullOrWhiteSpace(p.MainWindowTitle))
                            title = p.MainWindowTitle;
                        else if (p.MainModule?.FileVersionInfo.FileDescription is { Length: > 0 } d)
                            title = d;
                    }
                    catch { }

                    long memBytes = 0;
                    try { memBytes = p.WorkingSet64; } catch { }
                    string memStr = memBytes > 0 ? $"{memBytes / 1048576.0:N0} MB de RAM" : "Em execução";

                    if (!map.TryGetValue(exeName, out var cand))
                    {
                        map[exeName] = new AppCandidate
                        {
                            ExeName = exeName,
                            DisplayName = CleanTitle(title, name),
                            Detail = $"Em execução · {memStr}",
                            FullPath = fullPath,
                            Category = "Em Execução",
                            IsRunning = true
                        };
                    }
                    else
                    {
                        cand.IsRunning = true;
                        cand.Category = "Em Execução";
                        cand.Detail = $"Em execução · {memStr}";
                        if (string.IsNullOrEmpty(cand.FullPath) && !string.IsNullOrEmpty(fullPath))
                            cand.FullPath = fullPath;
                    }
                }
                catch { }
            }
        }
        catch { }
    }

    private static void ScanGames(Dictionary<string, AppCandidate> map, CancellationToken ct)
    {
        try
        {
            var games = GameLibrary.Scan(new List<CustomGame>());
            foreach (var g in games)
            {
                if (ct.IsCancellationRequested) return;
                if (string.IsNullOrWhiteSpace(g.ExePath) || !File.Exists(g.ExePath)) continue;

                string exeName = NormalizeExe(Path.GetFileName(g.ExePath));
                if (string.IsNullOrWhiteSpace(exeName)) continue;

                if (!map.TryGetValue(exeName, out var cand))
                {
                    map[exeName] = new AppCandidate
                    {
                        ExeName = exeName,
                        DisplayName = !string.IsNullOrWhiteSpace(g.Name) ? g.Name : Path.GetFileNameWithoutExtension(exeName),
                        Detail = $"Jogo · {g.Store}",
                        FullPath = g.ExePath,
                        Category = "Jogos",
                        IsGame = true
                    };
                }
                else
                {
                    cand.IsGame = true;
                    if (cand.Category != "Em Execução") cand.Category = "Jogos";
                    if (string.IsNullOrEmpty(cand.FullPath)) cand.FullPath = g.ExePath;
                }
            }
        }
        catch { }
    }

    private static void ScanShortcuts(Dictionary<string, AppCandidate> map, CancellationToken ct)
    {
        try
        {
            var shortcutDirs = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
                Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
            };

            foreach (var baseDir in shortcutDirs.Where(Directory.Exists))
            {
                if (ct.IsCancellationRequested) return;
                foreach (var lnk in Directory.EnumerateFiles(baseDir, "*.lnk", SearchOption.AllDirectories))
                {
                    if (ct.IsCancellationRequested) return;
                    try
                    {
                        string? target = ResolveShortcut(lnk);
                        if (string.IsNullOrWhiteSpace(target) || !target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                            continue;

                        // Remove argumentos se presentes
                        target = target.Trim('\"');
                        int spaceIdx = target.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
                        if (spaceIdx > 0 && spaceIdx + 4 < target.Length)
                            target = target.Substring(0, spaceIdx + 4);

                        if (!File.Exists(target)) continue;

                        string exeName = NormalizeExe(Path.GetFileName(target));
                        if (IsProtectedSystemProcess(Path.GetFileNameWithoutExtension(exeName))) continue;

                        string displayName = Path.GetFileNameWithoutExtension(lnk);

                        if (!map.TryGetValue(exeName, out var cand))
                        {
                            map[exeName] = new AppCandidate
                            {
                                ExeName = exeName,
                                DisplayName = displayName,
                                Detail = "Instalado (Menu Iniciar)",
                                FullPath = target,
                                Category = "Programas Instalados"
                            };
                        }
                        else
                        {
                            if (string.IsNullOrEmpty(cand.FullPath)) cand.FullPath = target;
                        }
                    }
                    catch { }
                }
            }
        }
        catch { }
    }

    private static void ScanAppPaths(Dictionary<string, AppCandidate> map, CancellationToken ct)
    {
        var roots = new[]
        {
            (RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths"),
            (RegistryHive.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths")
        };

        foreach (var (hive, path) in roots)
        {
            if (ct.IsCancellationRequested) return;
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Default);
                using var appPathsKey = baseKey.OpenSubKey(path);
                if (appPathsKey == null) continue;

                foreach (var subName in appPathsKey.GetSubKeyNames())
                {
                    if (ct.IsCancellationRequested) return;
                    try
                    {
                        if (!subName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;
                        using var key = appPathsKey.OpenSubKey(subName);
                        if (key == null) continue;

                        string? target = key.GetValue(null) as string;
                        if (string.IsNullOrWhiteSpace(target)) continue;
                        target = target.Trim('\"');
                        if (!File.Exists(target)) continue;

                        string exeName = NormalizeExe(subName);
                        if (IsProtectedSystemProcess(Path.GetFileNameWithoutExtension(exeName))) continue;

                        string displayName = Path.GetFileNameWithoutExtension(subName);
                        try
                        {
                            var vi = FileVersionInfo.GetVersionInfo(target);
                            if (!string.IsNullOrWhiteSpace(vi.FileDescription))
                                displayName = vi.FileDescription;
                        }
                        catch { }

                        if (!map.TryGetValue(exeName, out var cand))
                        {
                            map[exeName] = new AppCandidate
                            {
                                ExeName = exeName,
                                DisplayName = displayName,
                                Detail = "Instalado (App Paths)",
                                FullPath = target,
                                Category = "Programas Instalados"
                            };
                        }
                        else
                        {
                            if (string.IsNullOrEmpty(cand.FullPath)) cand.FullPath = target;
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }
    }

    private static void ScanUninstallRegistry(Dictionary<string, AppCandidate> map, CancellationToken ct)
    {
        var hives = new[]
        {
            (RegistryHive.LocalMachine, RegistryView.Registry64),
            (RegistryHive.LocalMachine, RegistryView.Registry32),
            (RegistryHive.CurrentUser, RegistryView.Default)
        };

        foreach (var (hive, view) in hives)
        {
            if (ct.IsCancellationRequested) return;
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                using var uninstallKey = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                if (uninstallKey == null) continue;

                foreach (var subKeyName in uninstallKey.GetSubKeyNames())
                {
                    if (ct.IsCancellationRequested) return;
                    try
                    {
                        using var sub = uninstallKey.OpenSubKey(subKeyName);
                        if (sub == null) continue;

                        // Ignora componentes internos do sistema e updates
                        if (sub.GetValue("SystemComponent") is int sysComp && sysComp == 1) continue;
                        if (sub.GetValue("ParentKeyName") != null) continue;

                        string? displayName = sub.GetValue("DisplayName") as string;
                        if (string.IsNullOrWhiteSpace(displayName)) continue;

                        // Tenta extrair o executável a partir do DisplayIcon ou InstallLocation
                        string? exePath = ExtractExeFromDisplayIcon(sub.GetValue("DisplayIcon") as string);
                        if (string.IsNullOrEmpty(exePath))
                        {
                            string? installLoc = sub.GetValue("InstallLocation") as string;
                            if (!string.IsNullOrEmpty(installLoc) && Directory.Exists(installLoc))
                            {
                                exePath = FindPrimaryExeInFolder(installLoc);
                            }
                        }

                        if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath)) continue;

                        string exeName = NormalizeExe(Path.GetFileName(exePath));
                        if (IsProtectedSystemProcess(Path.GetFileNameWithoutExtension(exeName))) continue;

                        if (!map.TryGetValue(exeName, out var cand))
                        {
                            map[exeName] = new AppCandidate
                            {
                                ExeName = exeName,
                                DisplayName = displayName,
                                Detail = "Instalado (Win32)",
                                FullPath = exePath,
                                Category = "Programas Instalados"
                            };
                        }
                        else
                        {
                            if (string.IsNullOrEmpty(cand.FullPath)) cand.FullPath = exePath;
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }
    }

    private static string? ExtractExeFromDisplayIcon(string? rawIcon)
    {
        if (string.IsNullOrWhiteSpace(rawIcon)) return null;
        rawIcon = rawIcon.Trim().Trim('\"');

        int commaIdx = rawIcon.IndexOf(',');
        if (commaIdx > 0)
            rawIcon = rawIcon.Substring(0, commaIdx).Trim('\"');

        if (rawIcon.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(rawIcon))
            return rawIcon;

        return null;
    }

    private static string? FindPrimaryExeInFolder(string folderPath)
    {
        try
        {
            var exes = Directory.EnumerateFiles(folderPath, "*.exe", SearchOption.TopDirectoryOnly).ToList();
            if (exes.Count == 0) return null;

            // Evita instaladores, uninstallers e helpers
            var filtered = exes.Where(x =>
            {
                string fn = Path.GetFileName(x).ToLowerInvariant();
                return !fn.Contains("unins") && !fn.Contains("setup") && !fn.Contains("update") && !fn.Contains("crash");
            }).ToList();

            return filtered.FirstOrDefault() ?? exes.FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    private static string? ResolveShortcut(string lnkPath)
    {
        try
        {
            var type = Type.GetTypeFromProgID("WScript.Shell");
            if (type == null) return null;
            dynamic shell = Activator.CreateInstance(type)!;
            dynamic sc = shell.CreateShortcut(lnkPath);
            return sc.TargetPath as string;
        }
        catch
        {
            return null;
        }
    }

    public static ImageSource? GetOrExtractIcon(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) return null;

        if (IconCache.TryGetValue(filePath, out var cached))
            return cached;

        try
        {
            using var sysIcon = System.Drawing.Icon.ExtractAssociatedIcon(filePath);
            if (sysIcon != null)
            {
                var bmpSource = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                    sysIcon.Handle,
                    System.Windows.Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
                bmpSource.Freeze();
                IconCache[filePath] = bmpSource;
                return bmpSource;
            }
        }
        catch { }

        IconCache[filePath] = null;
        return null;
    }

    public static string NormalizeExe(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return "";
        string trimmed = input.Trim().ToLowerInvariant();
        if (!trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            trimmed += ".exe";
        return trimmed;
    }

    private static string CleanTitle(string title, string fallback)
    {
        if (string.IsNullOrWhiteSpace(title)) return fallback;
        if (title.Length > 45) return title.Substring(0, 42) + "...";
        return title;
    }

    public static bool IsProtectedSystemProcess(string procName)
    {
        string p = procName.ToLowerInvariant();
        if (p.EndsWith(".exe")) p = p[..^4];

        return p is "system" or "idle" or "registry" or "smss" or "csrss" or "wininit" or "winlogon"
            or "services" or "lsass" or "svchost" or "dwm" or "explorer" or "sihost" or "taskhostw"
            or "runtimebroker" or "shellexperiencehost" or "searchhost" or "searchindexer" or "conhost"
            or "dllhost" or "spoolsv" or "audiodg" or "luminitray" or "dotnet";
    }

    private sealed class AppCandidate
    {
        public string ExeName { get; init; } = "";
        public string DisplayName { get; set; } = "";
        public string Detail { get; set; } = "";
        public string? FullPath { get; set; }
        public string Category { get; set; } = "Programas Instalados";
        public bool IsRunning { get; set; }
        public bool IsGame { get; set; }
    }
}
