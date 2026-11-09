using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace LuminiTray;

/// <summary>
/// Categoria de origem do aplicativo para filtragem rápida na interface.
/// </summary>
public enum AppCategory
{
    DesktopWin32,
    MicrosoftStore,
    SteamGame,
    PortableDirectory
}

/// <summary>
/// Tipo de motor do instalador detectado para automação silenciosa e limpeza residual.
/// </summary>
public enum UninstallerEngineType
{
    Unknown,
    InnoSetup,
    Nsis,
    Msi,
    InstallShield,
    StoreApp,
    Steam,
    DirectoryDelete
}

/// <summary>
/// Modelo de um software instalado no sistema operacional.
/// </summary>
public sealed class InstalledSoftwareItem
{
    public string DisplayName { get; set; } = "";
    public string DisplayVersion { get; set; } = "";
    public string Publisher { get; set; } = "";
    public string InstallDate { get; set; } = "";
    public string InstallLocation { get; set; } = "";
    public string UninstallString { get; set; } = "";
    public string QuietUninstallString { get; set; } = "";
    public string GeneratedQuietCommand { get; set; } = "";
    public string RegistryKeyPath { get; set; } = "";
    public RegistryHive Hive { get; set; } = RegistryHive.LocalMachine;
    public RegistryView View { get; set; } = RegistryView.Default;
    public long EstimatedSizeKb { get; set; }
    public string DisplayIcon { get; set; } = "";
    public bool IsMsi { get; set; }

    public AppCategory Category { get; set; } = AppCategory.DesktopWin32;
    public UninstallerEngineType EngineType { get; set; } = UninstallerEngineType.Unknown;
    public bool CanSilentUninstall { get; set; }
    public System.Windows.Visibility SilentVisibility => CanSilentUninstall ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
    public string PackageFullName { get; set; } = "";
    public string SteamAppId { get; set; } = "";
    public bool IsSelected { get; set; }
    public bool IsHiddenOrComponent { get; set; }
    public string HiddenBadge => IsHiddenOrComponent ? "⚙️ Componente" : "";
    public System.Windows.Visibility HiddenBadgeVisibility => IsHiddenOrComponent ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

    public string CategoryBadge => Category switch
    {
        AppCategory.DesktopWin32 => "💻 Desktop",
        AppCategory.MicrosoftStore => "🛍️ Store",
        AppCategory.SteamGame => "🎮 Steam",
        AppCategory.PortableDirectory => "📁 Portátil",
        _ => "💻 Desktop"
    };

    public string CategoryBrush => Category switch
    {
        AppCategory.DesktopWin32 => "#3B82F6",
        AppCategory.MicrosoftStore => "#06B6D4",
        AppCategory.SteamGame => "#10B981",
        AppCategory.PortableDirectory => "#F59E0B",
        _ => "#3B82F6"
    };

    public string EngineBadge => EngineType switch
    {
        UninstallerEngineType.InnoSetup => "Inno Setup",
        UninstallerEngineType.Nsis => "NSIS",
        UninstallerEngineType.Msi => "MSI Installer",
        UninstallerEngineType.InstallShield => "InstallShield",
        UninstallerEngineType.StoreApp => "AppX / MSIX",
        UninstallerEngineType.Steam => "Steam Engine",
        UninstallerEngineType.DirectoryDelete => "Diretório / Portátil",
        _ => "Padrão"
    };

    public string FormattedSize => EstimatedSizeKb > 0
        ? DiskSpaceAnalyzer.FormatSize(EstimatedSizeKb * 1024L)
        : "—";

    public string DriveAllocation
    {
        get
        {
            string? candidate = null;
            if (!string.IsNullOrWhiteSpace(InstallLocation))
                candidate = InstallLocation;
            else if (!string.IsNullOrWhiteSpace(DisplayIcon))
                candidate = DisplayIcon;
            else if (!string.IsNullOrWhiteSpace(UninstallString))
                candidate = UninstallString;

            if (!string.IsNullOrWhiteSpace(candidate))
            {
                var match = Regex.Match(candidate, @"([a-zA-Z]:)", RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    return match.Groups[1].Value.ToUpperInvariant();
                }
            }
            return "C:";
        }
    }

    public string LocationOrDriveTip => !string.IsNullOrWhiteSpace(InstallLocation)
        ? $"Pasta: {InstallLocation}"
        : $"Instalado na unidade {DriveAllocation}";

    public override string ToString() => DisplayName;
}

/// <summary>
/// Resquício encontrado no sistema de arquivos (pasta, arquivo, atalho ou rastro).
/// </summary>
public sealed class LeftoverFileItem
{
    public string Path { get; set; } = "";
    public bool IsDirectory { get; set; }
    public long Size { get; set; }
    public bool Selected { get; set; } = true;
    public string Category { get; set; } = "Arquivos & Pastas"; // "Pastas/Arquivos", "Atalhos", "Rastros do Sistema"
    public string FormattedSize => DiskSpaceAnalyzer.FormatSize(Size);
}

/// <summary>
/// Resquício encontrado no Registro do Windows (chave, valor, regra de firewall ou inicialização).
/// </summary>
public sealed class LeftoverRegistryItem
{
    public RegistryHive Hive { get; set; }
    public string SubKeyPath { get; set; } = "";
    public string? ValueName { get; set; }
    public bool Selected { get; set; } = true;
    public string Category { get; set; } = "Registro"; // "Chaves de Software", "Regras de Firewall", "Inicialização"

    public string DisplayPath => !string.IsNullOrEmpty(ValueName)
        ? $"{(Hive == RegistryHive.CurrentUser ? "HKCU" : "HKLM")}\\{SubKeyPath} -> {ValueName}"
        : $"{(Hive == RegistryHive.CurrentUser ? "HKCU" : "HKLM")}\\{SubKeyPath}";
}

/// <summary>
/// Relatório consolidado da varredura profunda de resíduos pós-desinstalação.
/// </summary>
public sealed class LeftoverScanReport
{
    public InstalledSoftwareItem App { get; set; } = new();
    public List<LeftoverFileItem> FilesAndFolders { get; set; } = new();
    public List<LeftoverFileItem> Shortcuts { get; set; } = new();
    public List<LeftoverFileItem> SystemTraces { get; set; } = new();
    public List<LeftoverRegistryItem> RegistryEntries { get; set; } = new();
    public List<LeftoverRegistryItem> FirewallRules { get; set; } = new();

    public int TotalItemsCount => FilesAndFolders.Count + Shortcuts.Count + SystemTraces.Count + RegistryEntries.Count + FirewallRules.Count;
    public long TotalLeftoverBytes => FilesAndFolders.Sum(f => f.Size) + Shortcuts.Sum(f => f.Size) + SystemTraces.Sum(f => f.Size);
    public string FormattedTotalSize => DiskSpaceAnalyzer.FormatSize(TotalLeftoverBytes);
}

/// <summary>
/// Gerenciador de desinstalação avançada e remoção profunda de resquícios (inspirado na arquitetura do Bulk Crap Uninstaller).
/// </summary>
public static class AppUninstaller
{
    private static readonly HashSet<string> ProhibitedDirectories = new(StringComparer.OrdinalIgnoreCase);

    static AppUninstaller()
    {
        InitProhibitedDirectories();
    }

    private static void InitProhibitedDirectories()
    {
        void AddSafe(string path)
        {
            if (!string.IsNullOrWhiteSpace(path))
            {
                try
                {
                    string full = Path.GetFullPath(path).TrimEnd('\\');
                    ProhibitedDirectories.Add(full);
                }
                catch { }
            }
        }

        AddSafe(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
        AddSafe(Environment.GetFolderPath(Environment.SpecialFolder.System));
        AddSafe(Environment.GetFolderPath(Environment.SpecialFolder.SystemX86));
        AddSafe(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
        AddSafe(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));
        AddSafe(Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles));
        AddSafe(Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFilesX86));
        AddSafe(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        AddSafe(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));
        AddSafe(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
        AddSafe(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData));
        AddSafe(Environment.GetFolderPath(Environment.SpecialFolder.Desktop));
        AddSafe(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory));
        AddSafe(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
        AddSafe(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures));
        AddSafe(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic));
        AddSafe(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos));

        // Raízes de discos
        foreach (var drive in DriveInfo.GetDrives())
        {
            if (drive.IsReady)
                AddSafe(drive.RootDirectory.FullName);
        }
    }

    public static bool IsProhibitedDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return true;
        try
        {
            string full = Path.GetFullPath(path).TrimEnd('\\');
            if (ProhibitedDirectories.Contains(full)) return true;

            // Raiz de disco (ex: C:, C:\)
            var root = Path.GetPathRoot(full)?.TrimEnd('\\');
            if (string.Equals(full, root, StringComparison.OrdinalIgnoreCase)) return true;

            // Subpasta direta da raiz sem profundidade suficiente (ex: C:\Windows, C:\Program Files)
            string[] parts = full.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length <= 1) return true;

            return false;
        }
        catch
        {
            return true;
        }
    }

    /// <summary>
    /// Lista todos os softwares instalados no sistema utilizando o motor Multi-Source Factory:
    /// 1. Registro Win32 (HKLM 64-bit, HKLM 32-bit e HKCU);
    /// 2. Aplicativos Microsoft Store / UWP / MSIX;
    /// 3. Jogos e Aplicativos Steam em todas as bibliotecas;
    /// 4. Aplicativos Portáteis e Diretórios de Programas.
    /// </summary>
    public static async Task<List<InstalledSoftwareItem>> GetInstalledAppsAsync()
    {
        return await Task.Run(() =>
        {
            var apps = new Dictionary<string, InstalledSoftwareItem>(StringComparer.OrdinalIgnoreCase);

            // 1. Registro Win32
            ScanUninstallHive(RegistryHive.LocalMachine, RegistryView.Registry64, apps);
            ScanUninstallHive(RegistryHive.LocalMachine, RegistryView.Registry32, apps);
            ScanUninstallHive(RegistryHive.CurrentUser, RegistryView.Default, apps);

            // 2. Microsoft Store (UWP / MSIX)
            try { ScanStoreApps(apps); } catch { }

            // 3. Steam Games & Apps
            try { ScanSteamApps(apps); } catch { }

            // 4. Aplicativos em Diretórios / Portáteis
            try { ScanDirectoryApps(apps); } catch { }

            // Classifica o tipo de motor de desinstalação e detecta comandos silenciosos
            foreach (var item in apps.Values)
            {
                DetectEngineAndQuietCommand(item);
            }

            return apps.Values
                .OrderBy(a => a.DisplayName)
                .ToList();
        });
    }

    private static void ScanUninstallHive(RegistryHive hive, RegistryView view, Dictionary<string, InstalledSoftwareItem> dict)
    {
        const string uninstPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var subKey = baseKey.OpenSubKey(uninstPath);
            if (subKey == null) return;

            foreach (var subName in subKey.GetSubKeyNames())
            {
                try
                {
                    using var appKey = subKey.OpenSubKey(subName);
                    if (appKey == null) continue;

                    string name = appKey.GetValue("DisplayName")?.ToString()?.Trim() ?? "";
                    if (string.IsNullOrWhiteSpace(name)) continue;

                    // Ignora apenas hotfixes KB diretos do Windows Update
                    if (name.StartsWith("KB", StringComparison.OrdinalIgnoreCase) && name.Length <= 10) continue;

                    bool isSystem = (appKey.GetValue("SystemComponent") is int sys && sys == 1);
                    bool hasParent = appKey.GetValue("ParentKeyName") != null;
                    bool noDisplay = (appKey.GetValue("NoDisplay") is int nodisp && nodisp == 1);
                    bool isHidden = isSystem || hasParent || noDisplay;

                    string uninst = appKey.GetValue("UninstallString")?.ToString()?.Trim() ?? "";
                    string quiet = appKey.GetValue("QuietUninstallString")?.ToString()?.Trim() ?? "";
                    if (string.IsNullOrWhiteSpace(uninst) && string.IsNullOrWhiteSpace(quiet))
                    {
                        if (appKey.GetValue("WindowsInstaller") is int msiVal && msiVal == 1)
                        {
                            uninst = $"MsiExec.exe /X{subName}";
                            isHidden = true;
                        }
                        else
                        {
                            continue;
                        }
                    }

                    string version = appKey.GetValue("DisplayVersion")?.ToString()?.Trim() ?? "";
                    string publisher = appKey.GetValue("Publisher")?.ToString()?.Trim() ?? "";
                    string location = appKey.GetValue("InstallLocation")?.ToString()?.Trim() ?? "";
                    string date = appKey.GetValue("InstallDate")?.ToString()?.Trim() ?? "";
                    string icon = appKey.GetValue("DisplayIcon")?.ToString()?.Trim() ?? "";
                    long sizeKb = 0;
                    if (appKey.GetValue("EstimatedSize") is int sz) sizeKb = sz;
                    else if (appKey.GetValue("EstimatedSize") is long szl) sizeKb = szl;

                    bool isMsi = appKey.GetValue("WindowsInstaller") is int msi && msi == 1 || uninst.Contains("msiexec", StringComparison.OrdinalIgnoreCase);

                    var item = new InstalledSoftwareItem
                    {
                        DisplayName = name,
                        DisplayVersion = version,
                        Publisher = publisher,
                        InstallDate = date,
                        InstallLocation = location,
                        UninstallString = uninst,
                        QuietUninstallString = quiet,
                        RegistryKeyPath = $@"{uninstPath}\{subName}",
                        Hive = hive,
                        View = view,
                        EstimatedSizeKb = sizeKb,
                        DisplayIcon = icon,
                        IsMsi = isMsi,
                        Category = AppCategory.DesktopWin32,
                        IsHiddenOrComponent = isHidden
                    };

                    string key = $"{name} {version}".Trim();
                    if (!dict.ContainsKey(key))
                    {
                        dict[key] = item;
                    }
                }
                catch { }
            }
        }
        catch { }
    }

    /// <summary>
    /// Enumera aplicativos da Microsoft Store / UWP / MSIX via PowerShell JSON.
    /// </summary>
    private static void ScanStoreApps(Dictionary<string, InstalledSoftwareItem> dict)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -NonInteractive -Command \"Get-AppxPackage | Where-Object { -not $_.IsFramework -and -not $_.NonRemovable -and $_.SignatureKind -ne 'System' } | Select-Object Name, PackageFullName, InstallLocation, PublisherId | ConvertTo-Json -Compress\"",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi);
            if (proc == null) return;

            string json = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(6000);

            if (string.IsNullOrWhiteSpace(json)) return;

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            void ProcessElement(JsonElement el)
            {
                string name = el.TryGetProperty("Name", out var n) ? n.GetString() ?? "" : "";
                string pkgFull = el.TryGetProperty("PackageFullName", out var p) ? p.GetString() ?? "" : "";
                string loc = el.TryGetProperty("InstallLocation", out var l) ? l.GetString() ?? "" : "";
                string pub = el.TryGetProperty("PublisherId", out var pb) ? pb.GetString() ?? "" : "";

                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(pkgFull)) return;

                // Formata nome amigável removendo prefixos como Microsoft. ou Company.
                string displayName = CleanStoreAppName(name);

                // Evita duplicar se já foi lido pelo registro
                if (dict.Values.Any(v => v.DisplayName.Equals(displayName, StringComparison.OrdinalIgnoreCase) ||
                                         (!string.IsNullOrEmpty(loc) && v.InstallLocation.Equals(loc, StringComparison.OrdinalIgnoreCase))))
                {
                    return;
                }

                long sizeKb = 0;
                if (!string.IsNullOrEmpty(loc) && Directory.Exists(loc))
                {
                    try { sizeKb = GetDirSize(loc) / 1024L; } catch { }
                }

                var item = new InstalledSoftwareItem
                {
                    DisplayName = displayName,
                    Publisher = !string.IsNullOrEmpty(pub) ? $"Microsoft Store ({pub})" : "Microsoft Store",
                    InstallLocation = loc,
                    PackageFullName = pkgFull,
                    Category = AppCategory.MicrosoftStore,
                    EngineType = UninstallerEngineType.StoreApp,
                    CanSilentUninstall = true,
                    GeneratedQuietCommand = $"powershell.exe -NoProfile -NonInteractive -Command \"Remove-AppxPackage -Package '{pkgFull}'\"",
                    UninstallString = $"powershell.exe -NoProfile -NonInteractive -Command \"Remove-AppxPackage -Package '{pkgFull}'\"",
                    EstimatedSizeKb = sizeKb
                };

                dict[$"Store_{pkgFull}"] = item;
            }

            if (root.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in root.EnumerateArray())
                    ProcessElement(el);
            }
            else if (root.ValueKind == JsonValueKind.Object)
            {
                ProcessElement(root);
            }
        }
        catch { }
    }

    private static string CleanStoreAppName(string rawName)
    {
        // Se começar com Company.Name, quebra e limpa
        string[] parts = rawName.Split('.');
        if (parts.Length > 1)
        {
            return string.Join(" ", parts.Skip(1));
        }
        return rawName;
    }

    /// <summary>
    /// Detecta todos os jogos e aplicações do Steam instalados em qualquer unidade do computador.
    /// </summary>
    private static void ScanSteamApps(Dictionary<string, InstalledSoftwareItem> dict)
    {
        string? steamPath = null;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            steamPath = key?.GetValue("SteamPath")?.ToString()?.Replace('/', '\\');
        }
        catch { }

        if (string.IsNullOrWhiteSpace(steamPath) || !Directory.Exists(steamPath))
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam");
                steamPath = key?.GetValue("InstallPath")?.ToString();
            }
            catch { }
        }

        if (string.IsNullOrWhiteSpace(steamPath) || !Directory.Exists(steamPath)) return;

        var libraryFolders = new List<string> { steamPath };

        // Lê libraryfolders.vdf para encontrar bibliotecas em outros discos (D:\SteamLibrary, E:\, etc.)
        string vdfPath = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
        if (File.Exists(vdfPath))
        {
            try
            {
                string[] lines = File.ReadAllLines(vdfPath);
                foreach (var line in lines)
                {
                    var match = Regex.Match(line, @"""path""\s+""([^""]+)""", RegexOptions.IgnoreCase);
                    if (match.Success)
                    {
                        string p = match.Groups[1].Value.Replace(@"\\", @"\");
                        if (Directory.Exists(p) && !libraryFolders.Contains(p, StringComparer.OrdinalIgnoreCase))
                        {
                            libraryFolders.Add(p);
                        }
                    }
                }
            }
            catch { }
        }

        // Para cada biblioteca Steam, lê os arquivos appmanifest_*.acf
        foreach (var lib in libraryFolders)
        {
            string steamapps = Path.Combine(lib, "steamapps");
            if (!Directory.Exists(steamapps)) continue;

            try
            {
                foreach (var acf in Directory.GetFiles(steamapps, "appmanifest_*.acf"))
                {
                    try
                    {
                        string text = File.ReadAllText(acf);
                        string appId = Regex.Match(text, @"""appid""\s+""(\d+)""", RegexOptions.IgnoreCase).Groups[1].Value;
                        string name = Regex.Match(text, @"""name""\s+""([^""]+)""", RegexOptions.IgnoreCase).Groups[1].Value;
                        string installDir = Regex.Match(text, @"""installdir""\s+""([^""]+)""", RegexOptions.IgnoreCase).Groups[1].Value;
                        string sizeStr = Regex.Match(text, @"""SizeOnDisk""\s+""(\d+)""", RegexOptions.IgnoreCase).Groups[1].Value;

                        if (string.IsNullOrWhiteSpace(appId) || string.IsNullOrWhiteSpace(name)) continue;

                        // Ignora redistribuíveis comuns do Steam
                        if (name.Contains("Steamworks Common", StringComparison.OrdinalIgnoreCase)) continue;

                        // Evita duplicar se já foi adicionado pelo registro
                        if (dict.Values.Any(v => v.DisplayName.Equals(name, StringComparison.OrdinalIgnoreCase) ||
                                                 (!string.IsNullOrEmpty(v.SteamAppId) && v.SteamAppId == appId)))
                        {
                            continue;
                        }

                        long sizeBytes = 0;
                        long.TryParse(sizeStr, out sizeBytes);

                        string fullInstallLoc = Path.Combine(steamapps, "common", installDir);

                        var item = new InstalledSoftwareItem
                        {
                            DisplayName = name,
                            Publisher = "Steam (Valve)",
                            InstallLocation = Directory.Exists(fullInstallLoc) ? fullInstallLoc : steamapps,
                            SteamAppId = appId,
                            Category = AppCategory.SteamGame,
                            EngineType = UninstallerEngineType.Steam,
                            UninstallString = $"steam://uninstall/{appId}",
                            QuietUninstallString = $"steam://uninstall/{appId}",
                            CanSilentUninstall = false, // Steam solicita confirmação no cliente
                            EstimatedSizeKb = sizeBytes / 1024L,
                            DisplayIcon = Path.Combine(steamPath, "steam.exe")
                        };

                        dict[$"Steam_{appId}"] = item;
                    }
                    catch { }
                }
            }
            catch { }
        }
    }

    /// <summary>
    /// Escaneia pastas de Program Files e AppData\Programs para identificar aplicações portáteis ou não registradas.
    /// </summary>
    private static void ScanDirectoryApps(Dictionary<string, InstalledSoftwareItem> dict)
    {
        string[] searchRoots =
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs")
        };

        var knownLocations = dict.Values
            .Select(v => v.InstallLocation)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Select(l => l.TrimEnd('\\'))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var root in searchRoots.Where(Directory.Exists))
        {
            try
            {
                foreach (var dir in Directory.GetDirectories(root))
                {
                    if (IsProhibitedDirectory(dir)) continue;
                    if (knownLocations.Contains(dir)) continue;

                    string dirName = Path.GetFileName(dir);
                    if (string.Equals(dirName, "Common Files", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(dirName, "Windows Defender", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(dirName, "WindowsApps", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(dirName, "dotnet", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(dirName, "Uninstall Information", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    // Verifica se contém executáveis
                    var exes = Directory.GetFiles(dir, "*.exe", SearchOption.TopDirectoryOnly);
                    if (exes.Length == 0) continue;

                    // Procura desinstalador local
                    string uninstExe = exes.FirstOrDefault(e =>
                        Path.GetFileName(e).StartsWith("unins", StringComparison.OrdinalIgnoreCase) ||
                        Path.GetFileName(e).Equals("uninstall.exe", StringComparison.OrdinalIgnoreCase)) ?? "";

                    string mainExe = exes.FirstOrDefault(e => !e.Equals(uninstExe, StringComparison.OrdinalIgnoreCase)) ?? exes[0];
                    string appName = dirName;
                    try
                    {
                        var info = FileVersionInfo.GetVersionInfo(mainExe);
                        if (!string.IsNullOrWhiteSpace(info.FileDescription))
                            appName = info.FileDescription;
                    }
                    catch { }

                    long sizeKb = GetDirSize(dir) / 1024L;

                    var item = new InstalledSoftwareItem
                    {
                        DisplayName = appName,
                        Publisher = "Aplicativo em Diretório",
                        InstallLocation = dir,
                        Category = AppCategory.PortableDirectory,
                        EngineType = !string.IsNullOrEmpty(uninstExe) ? UninstallerEngineType.Unknown : UninstallerEngineType.DirectoryDelete,
                        UninstallString = !string.IsNullOrEmpty(uninstExe) ? $"\"{uninstExe}\"" : "",
                        DisplayIcon = mainExe,
                        EstimatedSizeKb = sizeKb
                    };

                    dict[$"Dir_{dir}"] = item;
                }
            }
            catch { }
        }
    }

    /// <summary>
    /// Detecta o motor de instalação subjacente e constrói flags para desinstalação silenciosa automatizada.
    /// </summary>
    public static void DetectEngineAndQuietCommand(InstalledSoftwareItem item)
    {
        if (item.Category == AppCategory.MicrosoftStore)
        {
            item.EngineType = UninstallerEngineType.StoreApp;
            item.CanSilentUninstall = true;
            item.GeneratedQuietCommand = $"powershell.exe -NoProfile -NonInteractive -Command \"Remove-AppxPackage -Package '{item.PackageFullName}'\"";
            return;
        }

        if (item.Category == AppCategory.SteamGame)
        {
            item.EngineType = UninstallerEngineType.Steam;
            item.CanSilentUninstall = false;
            return;
        }

        string cmd = !string.IsNullOrWhiteSpace(item.UninstallString) ? item.UninstallString : item.QuietUninstallString;
        if (string.IsNullOrWhiteSpace(cmd))
        {
            if (item.Category == AppCategory.PortableDirectory)
            {
                item.EngineType = UninstallerEngineType.DirectoryDelete;
                item.CanSilentUninstall = true;
            }
            return;
        }

        // 1. MSI / Windows Installer
        if (item.IsMsi || cmd.Contains("msiexec", StringComparison.OrdinalIgnoreCase))
        {
            item.EngineType = UninstallerEngineType.Msi;
            var guidMatch = Regex.Match(cmd, @"\{[0-9a-fA-F\-]{36}\}");
            if (guidMatch.Success)
            {
                item.GeneratedQuietCommand = $"msiexec.exe /x {guidMatch.Value} /qn /norestart";
                item.CanSilentUninstall = true;
                return;
            }
        }

        // 2. Inno Setup
        if (cmd.Contains("unins", StringComparison.OrdinalIgnoreCase))
        {
            string exe = ExtractExecutablePath(cmd);
            if (File.Exists(exe))
            {
                string dat = Path.ChangeExtension(exe, ".dat");
                if (File.Exists(dat) || Path.GetFileName(exe).StartsWith("unins0", StringComparison.OrdinalIgnoreCase))
                {
                    item.EngineType = UninstallerEngineType.InnoSetup;
                    item.GeneratedQuietCommand = $"\"{exe}\" /VERYSILENT /SUPPRESSMSGBOXES /NORESTART";
                    item.CanSilentUninstall = true;
                    return;
                }
            }
        }

        // 3. NSIS (Nullsoft)
        if (cmd.Contains("uninstall.exe", StringComparison.OrdinalIgnoreCase) || cmd.Contains("uninst.exe", StringComparison.OrdinalIgnoreCase))
        {
            string exe = ExtractExecutablePath(cmd);
            item.EngineType = UninstallerEngineType.Nsis;
            item.GeneratedQuietCommand = $"\"{exe}\" /S";
            item.CanSilentUninstall = true;
            return;
        }

        // 4. Se o registro já fornece QuietUninstallString oficial
        if (!string.IsNullOrWhiteSpace(item.QuietUninstallString))
        {
            item.GeneratedQuietCommand = item.QuietUninstallString;
            item.CanSilentUninstall = true;
            return;
        }
    }

    public static string ExtractExecutablePath(string cmd)
    {
        if (string.IsNullOrWhiteSpace(cmd)) return "";
        string trimmed = cmd.Trim();
        if (trimmed.StartsWith("\""))
        {
            int endQuote = trimmed.IndexOf('"', 1);
            if (endQuote > 1)
                return trimmed.Substring(1, endQuote - 1);
        }

        // Se contiver .exe, extrai até a extensão do executável
        int exeIdx = trimmed.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        if (exeIdx >= 0)
        {
            return trimmed.Substring(0, exeIdx + 4).Trim('"');
        }

        int space = trimmed.IndexOf(' ');
        return space > 0 ? trimmed.Substring(0, space) : trimmed;
    }

    /// <summary>
    /// Executa o desinstalador nativo do software.
    /// </summary>
    public static async Task<bool> RunNativeUninstallerAsync(InstalledSoftwareItem app)
    {
        return await RunUninstallerAsync(app, silent: false);
    }

    /// <summary>
    /// Executa o desinstalador do software (normal ou silencioso).
    /// </summary>
    public static async Task<bool> RunUninstallerAsync(InstalledSoftwareItem app, bool silent)
    {
        return await Task.Run(() =>
        {
            try
            {
                // Desinstalação de aplicativos da Store
                if (app.Category == AppCategory.MicrosoftStore && !string.IsNullOrEmpty(app.PackageFullName))
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        Arguments = $"-NoProfile -NonInteractive -Command \"Remove-AppxPackage -Package '{app.PackageFullName}'\"",
                        UseShellExecute = true,
                        Verb = "runas"
                    };
                    using var p = Process.Start(psi);
                    p?.WaitForExit(30000);
                    return true;
                }

                // Desinstalação de Jogos do Steam
                if (app.Category == AppCategory.SteamGame && !string.IsNullOrEmpty(app.SteamAppId))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = $"steam://uninstall/{app.SteamAppId}",
                        UseShellExecute = true
                    });
                    return true;
                }

                // Desinstalação de Diretório Puro (sem desinstalador)
                if (app.EngineType == UninstallerEngineType.DirectoryDelete)
                {
                    return true; // Será excluído na limpeza de resíduos
                }

                string cmd = (silent && !string.IsNullOrWhiteSpace(app.GeneratedQuietCommand))
                    ? app.GeneratedQuietCommand
                    : (!string.IsNullOrWhiteSpace(app.UninstallString) ? app.UninstallString : app.QuietUninstallString);

                if (string.IsNullOrWhiteSpace(cmd)) return false;

                string file = cmd, args = "";
                if (cmd.StartsWith("\""))
                {
                    int quote = cmd.IndexOf('"', 1);
                    if (quote > 1)
                    {
                        file = cmd.Substring(1, quote - 1);
                        args = cmd.Substring(quote + 1).Trim();
                    }
                }
                else
                {
                    int sp = cmd.IndexOf(' ');
                    if (sp > 0)
                    {
                        file = cmd.Substring(0, sp);
                        args = cmd.Substring(sp + 1).Trim();
                    }
                }

                var psiApp = new ProcessStartInfo
                {
                    FileName = file,
                    Arguments = args,
                    UseShellExecute = true,
                    Verb = "runas"
                };

                using var pApp = Process.Start(psiApp);
                pApp?.WaitForExit(180000); // Aguarda até 3 minutos pelo desinstalador nativo
                return true;
            }
            catch
            {
                return false;
            }
        });
    }

    /// <summary>
    /// Realiza a varredura profunda de resíduos em todas as 5 categorias do sistema operacional:
    /// 1. Pastas e Arquivos no Disco;
    /// 2. Atalhos no Desktop e Menu Iniciar;
    /// 3. Chaves no Registro do Windows;
    /// 4. Regras do Firewall do Windows;
    /// 5. Rastros de Sistema (Prefetch e Crash Dumps).
    /// </summary>
    public static async Task<LeftoverScanReport> ScanLeftoversAsync(InstalledSoftwareItem app, CancellationToken ct)
    {
        return await Task.Run(() =>
        {
            var report = new LeftoverScanReport { App = app };
            var keywords = ExtractKeywords(app.DisplayName, app.Publisher, app.InstallLocation);

            // 1. Pastas e Arquivos no Disco
            ScanFilesAndFolders(app, keywords, report.FilesAndFolders, ct);

            // 2. Atalhos no Desktop e Menu Iniciar
            ScanShortcuts(app, keywords, report.Shortcuts, ct);

            // 3. Chaves e Valores no Registro
            ScanRegistry(app, keywords, report.RegistryEntries, ct);

            // 4. Regras do Firewall do Windows
            ScanFirewallRules(app, keywords, report.FirewallRules, ct);

            // 5. Rastros do Sistema (Prefetch e Relatórios de Falha)
            ScanSystemTraces(app, keywords, report.SystemTraces, ct);

            return report;
        }, ct);
    }

    private static HashSet<string> ExtractKeywords(string appName, string publisher, string installLoc)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            var parts = text.Split(new[] { ' ', '-', '_', '.', '/', '\\', '(', ')' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var p in parts)
            {
                string clean = p.Trim();
                if (clean.Length >= 4 &&
                    !clean.Equals("Microsoft", StringComparison.OrdinalIgnoreCase) &&
                    !clean.Equals("Windows", StringComparison.OrdinalIgnoreCase) &&
                    !clean.Equals("Software", StringComparison.OrdinalIgnoreCase) &&
                    !clean.Equals("Version", StringComparison.OrdinalIgnoreCase) &&
                    !clean.Equals("Program", StringComparison.OrdinalIgnoreCase) &&
                    !clean.Equals("Programs", StringComparison.OrdinalIgnoreCase) &&
                    !clean.Equals("Steam", StringComparison.OrdinalIgnoreCase) &&
                    !clean.Equals("Desktop", StringComparison.OrdinalIgnoreCase))
                {
                    set.Add(clean);
                }
            }
        }

        Add(appName);
        Add(publisher);
        if (!string.IsNullOrWhiteSpace(installLoc))
        {
            try { Add(Path.GetFileName(installLoc.TrimEnd('\\', '/'))); } catch { }
        }

        return set;
    }

    private static void ScanFilesAndFolders(InstalledSoftwareItem app, HashSet<string> keywords, List<LeftoverFileItem> results, CancellationToken ct)
    {
        // Se o InstallLocation ainda existir
        if (!string.IsNullOrWhiteSpace(app.InstallLocation) && Directory.Exists(app.InstallLocation) && !IsProhibitedDirectory(app.InstallLocation))
        {
            results.Add(new LeftoverFileItem
            {
                Path = app.InstallLocation,
                IsDirectory = true,
                Size = GetDirSize(app.InstallLocation),
                Category = "Arquivos & Pastas"
            });
        }

        string[] scanRoots =
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), // ProgramData
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),       // AppData\Roaming
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),  // AppData\Local
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs")
        };

        foreach (var root in scanRoots.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var di = new DirectoryInfo(root);
                foreach (var dir in di.EnumerateDirectories())
                {
                    ct.ThrowIfCancellationRequested();
                    if (IsProhibitedDirectory(dir.FullName)) continue;

                    if (keywords.Any(kw => dir.Name.Contains(kw, StringComparison.OrdinalIgnoreCase)))
                    {
                        if (!results.Any(r => r.Path.Equals(dir.FullName, StringComparison.OrdinalIgnoreCase)))
                        {
                            results.Add(new LeftoverFileItem
                            {
                                Path = dir.FullName,
                                IsDirectory = true,
                                Size = GetDirSize(dir.FullName),
                                Category = "Arquivos & Pastas"
                            });
                        }
                    }
                }
            }
            catch { }
        }
    }

    private static void ScanShortcuts(InstalledSoftwareItem app, HashSet<string> keywords, List<LeftoverFileItem> results, CancellationToken ct)
    {
        string[] shortcutDirs =
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.Programs),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms)
        };

        foreach (var dir in shortcutDirs.Where(Directory.Exists))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                foreach (var file in Directory.GetFiles(dir, "*.lnk", SearchOption.AllDirectories))
                {
                    ct.ThrowIfCancellationRequested();
                    string fileName = Path.GetFileNameWithoutExtension(file);

                    bool match = keywords.Any(kw => fileName.Contains(kw, StringComparison.OrdinalIgnoreCase));
                    if (!match && !string.IsNullOrWhiteSpace(app.InstallLocation))
                    {
                        // Se o atalho estiver dentro de uma pasta da aplicação
                        match = file.Contains(app.InstallLocation, StringComparison.OrdinalIgnoreCase);
                    }

                    if (match && !results.Any(r => r.Path.Equals(file, StringComparison.OrdinalIgnoreCase)))
                    {
                        long sz = 0;
                        try { sz = new FileInfo(file).Length; } catch { }

                        results.Add(new LeftoverFileItem
                        {
                            Path = file,
                            IsDirectory = false,
                            Size = sz,
                            Category = "Atalhos"
                        });
                    }
                }
            }
            catch { }
        }
    }

    private static void ScanRegistry(InstalledSoftwareItem app, HashSet<string> keywords, List<LeftoverRegistryItem> results, CancellationToken ct)
    {
        ScanRegistryHiveForKeywords(RegistryHive.CurrentUser, keywords, results, ct);
        ScanRegistryHiveForKeywords(RegistryHive.LocalMachine, keywords, results, ct);

        // Chave de desinstalação residual no registro
        if (!string.IsNullOrWhiteSpace(app.RegistryKeyPath))
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(app.Hive, app.View);
                using var testKey = baseKey.OpenSubKey(app.RegistryKeyPath);
                if (testKey != null)
                {
                    results.Add(new LeftoverRegistryItem
                    {
                        Hive = app.Hive,
                        SubKeyPath = app.RegistryKeyPath,
                        Category = "Chaves de Desinstalação"
                    });
                }
            }
            catch { }
        }
    }

    private static void ScanRegistryHiveForKeywords(RegistryHive hive, HashSet<string> keywords, List<LeftoverRegistryItem> results, CancellationToken ct)
    {
        string[] searchPaths = { @"SOFTWARE", @"SOFTWARE\WOW6432Node" };
        using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Default);

        foreach (var p in searchPaths)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var target = baseKey.OpenSubKey(p);
                if (target == null) continue;

                foreach (var sub in target.GetSubKeyNames())
                {
                    ct.ThrowIfCancellationRequested();
                    if (keywords.Any(kw => sub.Contains(kw, StringComparison.OrdinalIgnoreCase)))
                    {
                        string path = $@"{p}\{sub}";
                        if (!results.Any(r => r.Hive == hive && r.SubKeyPath.Equals(path, StringComparison.OrdinalIgnoreCase)))
                        {
                            results.Add(new LeftoverRegistryItem
                            {
                                Hive = hive,
                                SubKeyPath = path,
                                Category = "Registro"
                            });
                        }
                    }
                }
            }
            catch { }
        }
    }

    private static void ScanFirewallRules(InstalledSoftwareItem app, HashSet<string> keywords, List<LeftoverRegistryItem> results, CancellationToken ct)
    {
        const string fwKeyPath = @"SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy\FirewallRules";
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Default);
            using var fwKey = baseKey.OpenSubKey(fwKeyPath);
            if (fwKey == null) return;

            foreach (var valName in fwKey.GetValueNames())
            {
                ct.ThrowIfCancellationRequested();
                string val = fwKey.GetValue(valName)?.ToString() ?? "";
                if (string.IsNullOrEmpty(val)) continue;

                bool match = false;
                if (!string.IsNullOrWhiteSpace(app.InstallLocation) && val.Contains(app.InstallLocation, StringComparison.OrdinalIgnoreCase))
                {
                    match = true;
                }
                else if (keywords.Any(kw => val.Contains(kw, StringComparison.OrdinalIgnoreCase)))
                {
                    match = true;
                }

                if (match)
                {
                    results.Add(new LeftoverRegistryItem
                    {
                        Hive = RegistryHive.LocalMachine,
                        SubKeyPath = fwKeyPath,
                        ValueName = valName,
                        Category = "Firewall"
                    });
                }
            }
        }
        catch { }
    }

    private static void ScanSystemTraces(InstalledSoftwareItem app, HashSet<string> keywords, List<LeftoverFileItem> results, CancellationToken ct)
    {
        // 1. Prefetch (*.pf)
        try
        {
            string prefetchDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Prefetch");
            if (Directory.Exists(prefetchDir))
            {
                foreach (var pf in Directory.GetFiles(prefetchDir, "*.pf"))
                {
                    ct.ThrowIfCancellationRequested();
                    string name = Path.GetFileName(pf);
                    if (keywords.Any(kw => name.Contains(kw, StringComparison.OrdinalIgnoreCase)))
                    {
                        long sz = 0;
                        try { sz = new FileInfo(pf).Length; } catch { }
                        results.Add(new LeftoverFileItem
                        {
                            Path = pf,
                            IsDirectory = false,
                            Size = sz,
                            Category = "Rastros do Sistema"
                        });
                    }
                }
            }
        }
        catch { }

        // 2. Crash Dumps
        try
        {
            string crashDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CrashDumps");
            if (Directory.Exists(crashDir))
            {
                foreach (var dmp in Directory.GetFiles(crashDir, "*.dmp"))
                {
                    ct.ThrowIfCancellationRequested();
                    string name = Path.GetFileName(dmp);
                    if (keywords.Any(kw => name.Contains(kw, StringComparison.OrdinalIgnoreCase)))
                    {
                        long sz = 0;
                        try { sz = new FileInfo(dmp).Length; } catch { }
                        results.Add(new LeftoverFileItem
                        {
                            Path = dmp,
                            IsDirectory = false,
                            Size = sz,
                            Category = "Rastros do Sistema"
                        });
                    }
                }
            }
        }
        catch { }
    }

    private static long GetDirSize(string path)
    {
        try
        {
            long s = 0;
            var di = new DirectoryInfo(path);
            foreach (var fi in di.EnumerateFiles("*", SearchOption.AllDirectories))
            {
                try { s += fi.Length; } catch { }
            }
            return s;
        }
        catch { return 0; }
    }

    /// <summary>
    /// Exclui os resquícios selecionados de arquivos, pastas, atalhos, chaves do Registro, regras de firewall e rastros.
    /// </summary>
    public static async Task<(int FilesDeleted, int RegDeleted)> CleanLeftoversAsync(LeftoverScanReport report)
    {
        return await Task.Run(() =>
        {
            int filesDel = 0;
            int regDel = 0;

            // Todos os itens de arquivos selecionados (pastas, atalhos, rastros)
            var filesToClean = report.FilesAndFolders.Concat(report.Shortcuts).Concat(report.SystemTraces)
                .Where(f => f.Selected);

            foreach (var f in filesToClean)
            {
                if (IsProhibitedDirectory(f.Path)) continue;

                try
                {
                    if (f.IsDirectory && Directory.Exists(f.Path))
                    {
                        Directory.Delete(f.Path, true);
                        filesDel++;

                        // Se a pasta pai for da desenvolvedora e ficar vazia, tenta limpar
                        try
                        {
                            string? parent = Path.GetDirectoryName(f.Path);
                            if (!string.IsNullOrEmpty(parent) && !IsProhibitedDirectory(parent) && Directory.Exists(parent))
                            {
                                if (!Directory.EnumerateFileSystemEntries(parent).Any())
                                {
                                    Directory.Delete(parent, false);
                                    filesDel++;
                                }
                            }
                        }
                        catch { }
                    }
                    else if (!f.IsDirectory && File.Exists(f.Path))
                    {
                        File.Delete(f.Path);
                        filesDel++;
                    }
                }
                catch { }
            }

            // Todos os itens do registro selecionados (chaves, valores, firewall)
            var regToClean = report.RegistryEntries.Concat(report.FirewallRules)
                .Where(r => r.Selected);

            foreach (var r in regToClean)
            {
                try
                {
                    using var baseKey = RegistryKey.OpenBaseKey(r.Hive, RegistryView.Default);
                    if (!string.IsNullOrEmpty(r.ValueName))
                    {
                        using var sub = baseKey.OpenSubKey(r.SubKeyPath, true);
                        if (sub != null)
                        {
                            sub.DeleteValue(r.ValueName, false);
                            regDel++;
                        }
                    }
                    else
                    {
                        baseKey.DeleteSubKeyTree(r.SubKeyPath, false);
                        regDel++;
                    }
                }
                catch { }
            }

            return (filesDel, regDel);
        });
    }

    /// <summary>
    /// Localiza qual aplicativo instalado corresponde a um caminho de executável ou janela selecionada.
    /// </summary>
    public static async Task<InstalledSoftwareItem?> FindAppByExecutableAsync(string exePath, List<InstalledSoftwareItem>? cachedApps = null)
    {
        var apps = cachedApps ?? await GetInstalledAppsAsync();
        string filename = Path.GetFileNameWithoutExtension(exePath);

        // 1. Tenta correspondência por InstallLocation
        var match = apps.FirstOrDefault(a => !string.IsNullOrWhiteSpace(a.InstallLocation) &&
                                              exePath.StartsWith(a.InstallLocation.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase));
        if (match != null) return match;

        // 2. Tenta por nome do arquivo (direto ou com normalização alfanumérica)
        string normFilename = new string(filename.Where(char.IsLetterOrDigit).ToArray());
        match = apps.FirstOrDefault(a =>
        {
            if (a.DisplayName.Contains(filename, StringComparison.OrdinalIgnoreCase) ||
                filename.Contains(a.DisplayName, StringComparison.OrdinalIgnoreCase))
                return true;

            string normName = new string(a.DisplayName.Where(char.IsLetterOrDigit).ToArray());
            if (!string.IsNullOrEmpty(normName) && !string.IsNullOrEmpty(normFilename))
            {
                if (normName.Contains(normFilename, StringComparison.OrdinalIgnoreCase) ||
                    normFilename.Contains(normName, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        });

        return match;
    }
}
