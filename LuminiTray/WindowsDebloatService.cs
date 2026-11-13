using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace LuminiTray;

/// <summary>
/// Representa um aplicativo bloatware do catálogo com status de instalação e seleção.
/// </summary>
public sealed class DebloatAppItem : INotifyPropertyChanged
{
    public string AppId { get; set; } = "";
    public List<string> Aliases { get; set; } = new();
    public string FriendlyName { get; set; } = "";
    public string Description { get; set; } = "";
    public string Recommendation { get; set; } = "optional"; // safe, optional, unsafe
    public string Vendor { get; set; } = "Microsoft";
    public string RemovalMethod { get; set; } = "Appx"; // Appx, WinGet

    public bool IsInstalled { get; set; }
    public bool IsOem { get; set; }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                OnPropertyChanged(nameof(IsSelected));
            }
        }
    }

    public string RecommendationBadge => Recommendation.ToLowerInvariant() switch
    {
        "safe" => "SEGURO",
        "unsafe" => "AVANÇADO",
        _ => IsOem ? "BLOATWARE OEM" : "OPCIONAL"
    };

    public string BadgeColor => Recommendation.ToLowerInvariant() switch
    {
        "safe" => "#10B981",    // Verde
        "unsafe" => "#EF4444",  // Vermelho
        _ => IsOem ? "#3B82F6" : "#F59E0B" // Azul para OEM, Âmbar para opcional
    };

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// Item de otimização de CPU, GPU ou Sistema com estado em tempo real no registro e suporte a reversão.
/// </summary>
public sealed class WindowsTweakItem : INotifyPropertyChanged
{
    public string Id { get; init; } = "";
    public string Title { get; init; } = "";
    public string Description { get; init; } = "";
    public string Effect { get; init; } = "";
    public string Category { get; init; } = "";
    public bool NeedsRestart { get; init; }

    public Func<bool?> Read { get; init; } = () => null;
    public Action<bool, AppSettings> Write { get; init; } = (_, _) => { };
    public Action<AppSettings> Restore { get; init; } = _ => { };

    private bool _isOn;
    public bool IsOn
    {
        get => _isOn;
        set
        {
            if (_isOn != value)
            {
                _isOn = value;
                OnPropertyChanged(nameof(IsOn));
            }
        }
    }

    private string _status = "";
    public string Status
    {
        get => _status;
        set
        {
            if (_status != value)
            {
                _status = value;
                OnPropertyChanged(nameof(Status));
            }
        }
    }

    public string Badge => NeedsRestart ? "REQUER REINICIAR" : "";

    public void Refresh() => IsOn = Read() ?? false;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// Perfil inteligente calculado especificamente para a combinação de CPU, GPU e RAM da máquina.
/// </summary>
public sealed class HardwareProfileRecommendation
{
    public string CpuName { get; set; } = "";
    public int CoreCount { get; set; }
    public int ThreadCount { get; set; }
    public string GpuName { get; set; } = "";
    public double RamGigabytes { get; set; }

    public bool IsIntelHybrid { get; set; }
    public bool IsAmdRyzen { get; set; }
    public bool IsNvidiaGpu { get; set; }
    public bool IsAmdGpu { get; set; }
    public bool IsIntelGpu { get; set; }
    public bool HasHighRam { get; set; } // >= 16 GB

    public string HardwareSummary { get; set; } = "";
    public string ProfileTitle { get; set; } = "";
    public string ProfileDescription { get; set; } = "";

    public List<string> RecommendedTweakIds { get; set; } = new();
    public List<string> OptimizationHighlights { get; set; } = new();
}

public sealed class DebloatProgressReport
{
    public int CurrentIndex { get; set; }
    public int TotalCount { get; set; }
    public string CurrentAppId { get; set; } = "";
    public string CurrentAppName { get; set; } = "";
    public string StatusMessage { get; set; } = "";
}

public sealed class DebloatUninstallResult
{
    public int TotalSelected { get; set; }
    public int SuccessCount { get; set; }
    public int ErrorCount { get; set; }
    public List<string> Errors { get; set; } = new();
}

/// <summary>
/// Serviço central de Debloat e Otimizações de Hardware do Windows.
/// </summary>
public static class WindowsDebloatService
{
    private const string HighPerformanceGuid = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";
    private const string BalancedGuid = "381b4222-f694-41f0-9685-ff5bb260df2e";

    // ------------------------------------------------------------------ Catálogo de Bloatware

    public static List<DebloatAppItem> LoadCatalog()
    {
        string appsJson = GetCatalogJson("Apps.json");
        string ptBrJson = GetCatalogJson("Languages/pt-BR/Apps.json");

        return ParseCatalog(appsJson, ptBrJson);
    }

    public static List<DebloatAppItem> ParseCatalog(string appsJson, string ptBrJson)
    {
        var items = new List<DebloatAppItem>();
        if (string.IsNullOrWhiteSpace(appsJson)) return items;

        var translations = new Dictionary<string, (string FriendlyName, string Description)>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(ptBrJson))
        {
            try
            {
                using var docTrans = JsonDocument.Parse(ptBrJson);
                foreach (var prop in docTrans.RootElement.EnumerateObject())
                {
                    string appId = prop.Name;
                    string friendlyName = prop.Value.TryGetProperty("FriendlyName", out var fn) ? fn.GetString() ?? appId : appId;
                    string description = prop.Value.TryGetProperty("Description", out var d) ? d.GetString() ?? "" : "";
                    translations[appId] = (friendlyName, description);
                }
            }
            catch { }
        }

        try
        {
            using var doc = JsonDocument.Parse(appsJson);
            if (doc.RootElement.TryGetProperty("Apps", out var appsArray) && appsArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in appsArray.EnumerateArray())
                {
                    var appIds = new List<string>();
                    if (el.TryGetProperty("AppId", out var idProp))
                    {
                        if (idProp.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var sub in idProp.EnumerateArray())
                            {
                                string? s = sub.GetString();
                                if (!string.IsNullOrWhiteSpace(s)) appIds.Add(s.Trim());
                            }
                        }
                        else if (idProp.ValueKind == JsonValueKind.String)
                        {
                            string? s = idProp.GetString();
                            if (!string.IsNullOrWhiteSpace(s)) appIds.Add(s.Trim());
                        }
                    }

                    if (appIds.Count == 0) continue;
                    string primaryId = appIds[0];

                    bool selectedDefault = el.TryGetProperty("SelectedByDefault", out var sel) && sel.GetBoolean();
                    string rec = el.TryGetProperty("Recommendation", out var r) ? r.GetString() ?? "optional" : "optional";
                    string method = el.TryGetProperty("RemovalMethod", out var m) ? m.GetString() ?? "Appx" : "Appx";

                    string friendly = primaryId;
                    string desc = "";
                    if (translations.TryGetValue(primaryId, out var trans))
                    {
                        friendly = trans.FriendlyName;
                        desc = trans.Description;
                    }
                    else
                    {
                        // Procura em aliases
                        foreach (var alias in appIds)
                        {
                            if (translations.TryGetValue(alias, out var aliasTrans))
                            {
                                friendly = aliasTrans.FriendlyName;
                                desc = aliasTrans.Description;
                                break;
                            }
                        }
                    }

                    bool isOem = DetectIsOem(primaryId, appIds);
                    string vendor = DetectVendor(primaryId, isOem);

                    var item = new DebloatAppItem
                    {
                        AppId = primaryId,
                        Aliases = appIds.Skip(1).ToList(),
                        FriendlyName = friendly,
                        Description = desc,
                        Recommendation = rec,
                        RemovalMethod = method,
                        IsOem = isOem,
                        Vendor = vendor,
                        IsSelected = rec.Equals("safe", StringComparison.OrdinalIgnoreCase)
                    };

                    items.Add(item);
                }
            }
        }
        catch { }

        return items;
    }

    public static bool DetectIsOem(string primaryId, IEnumerable<string>? aliases = null)
    {
        var all = new List<string> { primaryId };
        if (aliases != null) all.AddRange(aliases);

        foreach (var id in all)
        {
            if (id.StartsWith("DellInc.", StringComparison.OrdinalIgnoreCase) ||
                id.StartsWith("AD2F1837.", StringComparison.OrdinalIgnoreCase) || // HP
                id.StartsWith("E046963F.", StringComparison.OrdinalIgnoreCase) || // Lenovo
                id.StartsWith("Lenovo", StringComparison.OrdinalIgnoreCase) ||
                id.StartsWith("B9ECED6F.", StringComparison.OrdinalIgnoreCase) || // Asus
                id.StartsWith("Acer", StringComparison.OrdinalIgnoreCase) ||
                id.StartsWith("SAMSUNGELECTRONICS", StringComparison.OrdinalIgnoreCase) ||
                id.StartsWith("9426MICRO-STAR", StringComparison.OrdinalIgnoreCase) || // MSI
                id.Contains("Alienware", StringComparison.OrdinalIgnoreCase) ||
                id.Contains("ASUS", StringComparison.OrdinalIgnoreCase) ||
                id.Contains("MSICenter", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    public static string DetectVendor(string appId, bool isOem)
    {
        if (appId.StartsWith("DellInc.", StringComparison.OrdinalIgnoreCase) || appId.Contains("Alienware", StringComparison.OrdinalIgnoreCase))
            return "Dell";
        if (appId.StartsWith("AD2F1837.", StringComparison.OrdinalIgnoreCase) || appId.Contains(".HP", StringComparison.OrdinalIgnoreCase))
            return "HP";
        if (appId.StartsWith("Lenovo", StringComparison.OrdinalIgnoreCase) || appId.StartsWith("E046963F.", StringComparison.OrdinalIgnoreCase))
            return "Lenovo";
        if (appId.StartsWith("B9ECED6F.", StringComparison.OrdinalIgnoreCase) || appId.Contains("ASUS", StringComparison.OrdinalIgnoreCase))
            return "ASUS";
        if (appId.StartsWith("Acer", StringComparison.OrdinalIgnoreCase))
            return "Acer";
        if (appId.StartsWith("SAMSUNGELECTRONICS", StringComparison.OrdinalIgnoreCase))
            return "Samsung";
        if (appId.StartsWith("9426MICRO-STAR", StringComparison.OrdinalIgnoreCase) || appId.Contains("MSI", StringComparison.OrdinalIgnoreCase))
            return "MSI";
        if (appId.StartsWith("Microsoft.", StringComparison.OrdinalIgnoreCase) || appId.StartsWith("Clipchamp", StringComparison.OrdinalIgnoreCase) || appId.StartsWith("Xbox", StringComparison.OrdinalIgnoreCase))
            return "Microsoft";

        return isOem ? "Fabricante OEM" : "Terceiros";
    }

    private static string GetCatalogJson(string relativeFile)
    {
        // 1. Tenta carregar do disco (desenvolvimento / diretório local)
        string[] searchPaths = {
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Debloat", relativeFile),
            Path.Combine(Directory.GetCurrentDirectory(), "Assets", "Debloat", relativeFile),
            Path.Combine(Directory.GetCurrentDirectory(), "LuminiTray", "Assets", "Debloat", relativeFile),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config", relativeFile),
            Path.Combine(Directory.GetCurrentDirectory(), "Config", relativeFile)
        };

        foreach (var p in searchPaths)
        {
            if (File.Exists(p))
            {
                try { return File.ReadAllText(p); } catch { }
            }
        }

        // 2. Fallback: Recurso embutido
        string resName = relativeFile.Contains("pt-BR") ? "Debloat/pt-BR/Apps.json" : "Debloat/Apps.json";
        try
        {
            var asm = Assembly.GetExecutingAssembly();
            using var stream = asm.GetManifestResourceStream(resName);
            if (stream != null)
            {
                using var reader = new StreamReader(stream);
                return reader.ReadToEnd();
            }
        }
        catch { }

        return "";
    }

    // ------------------------------------------------------------------ Varredura de Apps Instalados

    public static async Task<List<DebloatAppItem>> ScanInstalledBloatwareAsync(IProgress<string>? progress = null)
    {
        progress?.Report("Carregando catálogo de bloatwares...");
        var catalog = await Task.Run(() => LoadCatalog());

        progress?.Report("Consultando pacotes AppX e provisionados instalados...");
        var installedPackages = await Task.Run(() => GetInstalledPackageNames());

        progress?.Report("Filtrando softwares detectados no sistema...");
        var installedBloatware = new List<DebloatAppItem>();

        foreach (var app in catalog)
        {
            bool isInstalled = false;
            var identifiers = new List<string> { app.AppId };
            identifiers.AddRange(app.Aliases);

            foreach (var id in identifiers)
            {
                if (installedPackages.Any(pkg => pkg.Contains(id, StringComparison.OrdinalIgnoreCase)))
                {
                    isInstalled = true;
                    break;
                }
            }

            if (isInstalled)
            {
                app.IsInstalled = true;
                installedBloatware.Add(app);
            }
        }

        return installedBloatware;
    }

    public static HashSet<string> GetInstalledPackageNames()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        string script = @"
$list = @()
try { $list += @(Get-AppxPackage -AllUsers -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Name) } catch {}
if ($list.Count -eq 0) {
    try { $list += @(Get-AppxPackage -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Name) } catch {}
}
try { 
    $prov = @(Get-AppxProvisionedPackage -Online -ErrorAction SilentlyContinue)
    $list += @($prov | Select-Object -ExpandProperty DisplayName -ErrorAction SilentlyContinue)
    $list += @($prov | Select-Object -ExpandProperty PackageName -ErrorAction SilentlyContinue)
} catch {}
$list | Where-Object { $_ } | Select-Object -Unique
";

        try
        {
            string encoded = Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(script));
            var psi = new ProcessStartInfo("powershell.exe", $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encoded}")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi);
            if (proc != null)
            {
                string output = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit(20000);

                foreach (var line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string trimmed = line.Trim();
                    if (!string.IsNullOrWhiteSpace(trimmed)) names.Add(trimmed);
                }
            }
        }
        catch { }

        return names;
    }

    // ------------------------------------------------------------------ Remoção de Aplicativos

    public static async Task<DebloatUninstallResult> UninstallAppsAsync(
        IEnumerable<DebloatAppItem> apps,
        IProgress<DebloatProgressReport>? progress = null,
        CancellationToken ct = default)
    {
        var list = apps.Where(a => a.IsSelected).ToList();
        var result = new DebloatUninstallResult { TotalSelected = list.Count };

        int index = 0;
        foreach (var app in list)
        {
            if (ct.IsCancellationRequested) break;
            index++;

            progress?.Report(new DebloatProgressReport
            {
                CurrentIndex = index,
                TotalCount = list.Count,
                CurrentAppId = app.AppId,
                CurrentAppName = app.FriendlyName,
                StatusMessage = $"Removendo ({index}/{list.Count}): {app.FriendlyName}..."
            });

            var (success, errorMsg) = await Task.Run(() => RemoveAppPackage(app), ct);
            if (success)
            {
                result.SuccessCount++;
                app.IsInstalled = false;
                app.IsSelected = false;
            }
            else
            {
                result.ErrorCount++;
                result.Errors.Add($"Falha ao desinstalar {app.FriendlyName} ({app.AppId})" + (string.IsNullOrWhiteSpace(errorMsg) ? "" : $": {errorMsg}"));
            }
        }

        return result;
    }

    private static (bool Success, string? ErrorMessage) RemoveAppPackage(DebloatAppItem app)
    {
        var ids = new List<string> { app.AppId };
        ids.AddRange(app.Aliases);

        // Se o método declarado for WinGet, tenta desinstalação via winget cli
        if (app.RemovalMethod.Equals("WinGet", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var psiWinget = new ProcessStartInfo("winget.exe", $"uninstall --id \"{app.AppId}\" --silent --accept-source-agreements")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var pWinget = Process.Start(psiWinget);
                if (pWinget != null)
                {
                    pWinget.WaitForExit(30000);
                    if (pWinget.ExitCode == 0) return (true, null);
                }
            }
            catch { }
        }

        string idList = string.Join(",", ids.Select(i => $"'{i.Replace("'", "''")}'"));
        string psScript = $@"
$ids = @({idList})
$errLog = @()
foreach ($id in $ids) {{
    try {{
        $pkgs = @(Get-AppxPackage -Name ""*$id*"" -AllUsers -ErrorAction SilentlyContinue)
        if ($pkgs.Count -eq 0) {{
            $pkgs = @(Get-AppxPackage -Name ""*$id*"" -ErrorAction SilentlyContinue)
        }}
        foreach ($p in $pkgs) {{
            try {{ Remove-AppxPackage -Package $p.PackageFullName -AllUsers -ErrorAction Stop }} catch {{
                try {{ Remove-AppxPackage -Package $p.PackageFullName -ErrorAction Stop }} catch {{ $errLog += $_.Exception.Message }}
            }}
        }}
    }} catch {{ $errLog += $_.Exception.Message }}

    try {{
        $provs = @(Get-AppxProvisionedPackage -Online -ErrorAction SilentlyContinue | Where-Object {{ $_.DisplayName -like ""*$id*"" -or $_.PackageName -like ""*$id*"" }})
        foreach ($pr in $provs) {{
            try {{ Remove-AppxProvisionedPackage -Online -AllUsers -PackageName $pr.PackageName -ErrorAction Stop }} catch {{
                try {{ Remove-AppxProvisionedPackage -Online -PackageName $pr.PackageName -ErrorAction Stop }} catch {{ $errLog += $_.Exception.Message }}
            }}
        }}
    }} catch {{ $errLog += $_.Exception.Message }}
}}

$stillInstalled = $false
foreach ($id in $ids) {{
    $chk = @(Get-AppxPackage -Name ""*$id*"" -ErrorAction SilentlyContinue)
    if ($chk.Count -gt 0) {{ $stillInstalled = $true; break }}
}}

if ($stillInstalled) {{
    Write-Output (""FAILED:"" + ($errLog -join ""; ""))
}} else {{
    Write-Output ""SUCCESS""
}}
";

        string encoded = Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(psScript));
        var psi = new ProcessStartInfo("powershell.exe", $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encoded}")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        try
        {
            using var proc = Process.Start(psi);
            if (proc != null)
            {
                string output = proc.StandardOutput.ReadToEnd().Trim();
                proc.WaitForExit(35000);

                if (output.Contains("SUCCESS"))
                {
                    return (true, null);
                }
                else if (output.Contains("FAILED:"))
                {
                    int idx = output.IndexOf("FAILED:");
                    string err = output.Substring(idx + 7).Trim();
                    return (false, string.IsNullOrWhiteSpace(err) ? "Pacote ainda detectado após tentativa de remoção." : err);
                }
            }
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }

        return (true, null);
    }

    /// <summary>
    /// Reinstala e re-registra todos os aplicativos nativos padrão e pacotes provisionados do Windows.
    /// </summary>
    public static async Task<(bool Success, string Message, int RestoredCount)> ReinstallAllDefaultWindowsAppsAsync(
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            try
            {
                progress?.Report("Restaurando e registrando todos os pacotes nativos do Windows...");

                string script = @"
$count = 0
try {
    Get-AppxPackage -AllUsers -ErrorAction SilentlyContinue | Foreach-Object {
        if ($_.InstallLocation -and (Test-Path ""$($_.InstallLocation)\AppXManifest.xml"")) {
            Add-AppxPackage -DisableDevelopmentMode -Register ""$($_.InstallLocation)\AppXManifest.xml"" -ErrorAction SilentlyContinue
            $count++
        }
    }
} catch {}
$count
";
                string encoded = Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(script));
                var psi = new ProcessStartInfo("powershell.exe", $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encoded}")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var proc = Process.Start(psi);
                if (proc == null) return (false, "Não foi possível iniciar o PowerShell.", 0);

                string output = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit(60000);

                int restored = 0;
                int.TryParse(output.Trim(), out restored);

                return (true, $"Aplicativos nativos do Windows re-registrados com sucesso ({restored} pacotes verificados).", restored);
            }
            catch (Exception ex)
            {
                return (false, $"Erro ao reinstalar aplicativos nativos: {ex.Message}", 0);
            }
        }, ct);
    }

    // ------------------------------------------------------------------ Perfil Adaptativo de Hardware

    public static HardwareProfileRecommendation DetectHardwareProfile()
    {
        string cpuName = "Processador";
        int cores = Environment.ProcessorCount;
        int threads = Environment.ProcessorCount;
        double ramGb = 16.0;
        string gpuName = "Placa de Vídeo";

        try
        {
            using var searcherCpu = new ManagementObjectSearcher("SELECT Name, NumberOfCores, NumberOfLogicalProcessors FROM Win32_Processor");
            foreach (ManagementObject mo in searcherCpu.Get())
            {
                cpuName = mo["Name"]?.ToString()?.Trim() ?? cpuName;
                if (int.TryParse(mo["NumberOfCores"]?.ToString(), out int c) && c > 0) cores = c;
                if (int.TryParse(mo["NumberOfLogicalProcessors"]?.ToString(), out int t) && t > 0) threads = t;
                break;
            }
        }
        catch { }

        try
        {
            using var searcherRam = new ManagementObjectSearcher("SELECT Capacity FROM Win32_PhysicalMemory");
            ulong totalBytes = 0;
            foreach (ManagementObject mo in searcherRam.Get())
            {
                if (ulong.TryParse(mo["Capacity"]?.ToString(), out ulong b)) totalBytes += b;
            }
            if (totalBytes > 0)
            {
                ramGb = Math.Round(totalBytes / (1024.0 * 1024.0 * 1024.0), 1);
            }
            else
            {
                // Fallback para máquinas virtuais ou sistemas onde Win32_PhysicalMemory é restrito
                using var searcherOs = new ManagementObjectSearcher("SELECT TotalVisibleMemorySize FROM Win32_OperatingSystem");
                foreach (ManagementObject mo in searcherOs.Get())
                {
                    if (ulong.TryParse(mo["TotalVisibleMemorySize"]?.ToString(), out ulong kb))
                        ramGb = Math.Round(kb / (1024.0 * 1024.0), 1);
                    break;
                }
            }
        }
        catch { }

        try
        {
            using var searcherGpu = new ManagementObjectSearcher("SELECT Name FROM Win32_VideoController");
            foreach (ManagementObject mo in searcherGpu.Get())
            {
                string name = mo["Name"]?.ToString()?.Trim() ?? "";
                if (!string.IsNullOrWhiteSpace(name))
                {
                    gpuName = name;
                    // Se for dedicada (NVIDIA, AMD ou Intel Arc), dá prioridade na exibição
                    if (name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("GeForce", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("Radeon", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("Arc", StringComparison.OrdinalIgnoreCase))
                    {
                        break;
                    }
                }
            }
        }
        catch { }

        return CalculateProfile(cpuName, cores, threads, gpuName, ramGb);
    }

    public static HardwareProfileRecommendation CalculateProfile(string cpuName, int cores, int threads, string gpuName, double ramGb)
    {
        var profile = new HardwareProfileRecommendation
        {
            CpuName = cpuName,
            CoreCount = cores,
            ThreadCount = threads,
            GpuName = gpuName,
            RamGigabytes = ramGb,
            HardwareSummary = $"{cpuName} ({cores}C/{threads}T) · {gpuName} · {ramGb:0} GB RAM"
        };

        // Identificação de CPU
        bool isIntel = cpuName.Contains("Intel", StringComparison.OrdinalIgnoreCase);
        bool isAmd = cpuName.Contains("AMD", StringComparison.OrdinalIgnoreCase) || cpuName.Contains("Ryzen", StringComparison.OrdinalIgnoreCase);

        // Intel Híbrida: 12ª, 13ª, 14ª gerações, Core Ultra ou HT assimétrico por P+E cores (threads != 2*cores)
        bool isHybrid = isIntel && (
            (cores >= 6 && threads > cores && threads < cores * 2) ||
            cpuName.Contains("Ultra", StringComparison.OrdinalIgnoreCase) ||
            System.Text.RegularExpressions.Regex.IsMatch(cpuName, @"\b(1[2-4]th\s+Gen|i[3579]-1[2-4]\d{3}|Ultra)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
        );

        profile.IsIntelHybrid = isHybrid;
        profile.IsAmdRyzen = isAmd;

        // Identificação de GPU
        profile.IsNvidiaGpu = gpuName.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) || gpuName.Contains("GeForce", StringComparison.OrdinalIgnoreCase);
        profile.IsAmdGpu = gpuName.Contains("AMD", StringComparison.OrdinalIgnoreCase) || gpuName.Contains("Radeon", StringComparison.OrdinalIgnoreCase);
        profile.IsIntelGpu = gpuName.Contains("Intel", StringComparison.OrdinalIgnoreCase) || gpuName.Contains("Arc", StringComparison.OrdinalIgnoreCase);

        // Identificação de Memória: >= 14.5 GB cobre com precisão sistemas de 16 GB mesmo com VRAM compartilhada
        profile.HasHighRam = ramGb >= 14.5;

        // Montagem das Recomendações
        var recTweaks = new List<string>();
        var highlights = new List<string>();

        // 1. CPU
        recTweaks.Add("cpu_system_responsiveness");
        recTweaks.Add("cpu_game_priority");
        recTweaks.Add("cpu_power_throttling");
        recTweaks.Add("cpu_high_performance");

        if (isHybrid)
        {
            highlights.Add("Arquitetura Intel Híbrida: priorização de P-cores para jogos e remoção de restrições de Power Throttling.");
        }
        else if (isAmd)
        {
            highlights.Add("Processador AMD Ryzen: agendamento otimizado de núcleos de alta frequência e latência ultrabaixa.");
        }
        else
        {
            highlights.Add("CPU Multi-core: SystemResponsiveness = 0 e prioridade de agendamento multimídia máxima.");
        }

        // 2. GPU
        recTweaks.Add("gpu_hags");
        recTweaks.Add("gpu_frame_latency");
        recTweaks.Add("gpu_windowed_optimizations");
        recTweaks.Add("gpu_game_dvr");

        if (profile.IsNvidiaGpu)
        {
            highlights.Add("NVIDIA GeForce: HAGS ativado (essencial para Frame Generation / DLSS 3) e DirectX Flip Model.");
        }
        else if (profile.IsAmdGpu)
        {
            highlights.Add("AMD Radeon: Agendamento acelerado por hardware e modelo Flip habilitados.");
        }
        else
        {
            highlights.Add("GPU: Otimizações de latência de quadros e desativação de gravação contínua em segundo plano.");
        }

        // 3. RAM
        if (profile.HasHighRam)
        {
            recTweaks.Add("sys_disable_paging_executive");
            highlights.Add($"{ramGb:0} GB de RAM: Modo de alto desempenho de memória (núcleo e drivers mantidos 100% na RAM).");
        }
        else
        {
            highlights.Add($"{ramGb:0} GB de RAM: Paginação dinâmica preservada para segurança contra estouro de memória.");
        }

        // 4. Latência & Sistema
        recTweaks.Add("input_mouse_precision");
        recTweaks.Add("net_throttling");
        recTweaks.Add("net_nagle");
        recTweaks.Add("sys_telemetry");
        recTweaks.Add("sys_copilot");
        recTweaks.Add("sys_recall");
        recTweaks.Add("sys_classic_menu");
        recTweaks.Add("sys_suggestions");

        highlights.Add("Periféricos & Rede: Mira 1:1 linear do mouse sem aceleração e estabilidade de ping em jogos.");
        highlights.Add("Debloat de Sistema: Bloqueio de Copilot, Recall, telemetria invasiva e retorno do menu clássico.");

        profile.RecommendedTweakIds = recTweaks;
        profile.OptimizationHighlights = highlights;
        profile.ProfileTitle = $"Perfil Adaptativo para {cpuName.Split('@')[0].Trim()} + {(profile.IsNvidiaGpu ? "NVIDIA" : profile.IsAmdGpu ? "AMD" : "Gráficos Integrados")}";
        profile.ProfileDescription = $"Customizado para {cores} núcleos, {gpuName} e {ramGb:0} GB de RAM.";

        return profile;
    }

    // ------------------------------------------------------------------ Lista de Ajustes & Reversão

    public static List<WindowsTweakItem> GetAllTweaks()
    {
        return new List<WindowsTweakItem>
        {
            // ===== 1. CPU & PRIORIDADE =====
            new WindowsTweakItem
            {
                Id = "cpu_system_responsiveness",
                Title = "SystemResponsiveness = 0 (100% de CPU para Jogos)",
                Description = "O Windows reserva 20% do uso da CPU para tarefas de segundo plano. Define o valor para 0, liberando 100% dos recursos do processador para o jogo ativo.",
                Effect = "Elimina microstuttering e oscilações bruscas de frametime.",
                Category = "⚡ CPU & Prioridade de Processos",
                NeedsRestart = false,
                Read = () => (ReadDword(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", "SystemResponsiveness") ?? 20) == 0,
                Write = (on, s) => SetDword(s, "cpu_system_responsiveness", Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", "SystemResponsiveness", on ? 0 : 20),
                Restore = s => RestoreDword(s, "cpu_system_responsiveness", Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", "SystemResponsiveness")
            },
            new WindowsTweakItem
            {
                Id = "cpu_game_priority",
                Title = "Prioridade de Agendamento Multimedia & Jogos",
                Description = "Eleva a prioridade de agendamento de processos e threads de jogos para categoria 'High' no kernel de multimídia do Windows.",
                Effect = "Melhora a consistência dos 1% lows de FPS em jogos pesados.",
                Category = "⚡ CPU & Prioridade de Processos",
                NeedsRestart = false,
                Read = () => ReadDword(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games", "Priority") == 6 &&
                             ReadString(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games", "Scheduling Category") == "High",
                Write = (on, s) =>
                {
                    const string path = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games";
                    SetDword(s, "cpu_game_priority.Priority", Registry.LocalMachine, path, "Priority", on ? 6 : 2);
                    SetDword(s, "cpu_game_priority.GpuPriority", Registry.LocalMachine, path, "GPU Priority", on ? 8 : 8);
                    SetString(s, "cpu_game_priority.Scheduling", Registry.LocalMachine, path, "Scheduling Category", on ? "High" : "Medium");
                    SetString(s, "cpu_game_priority.Sfio", Registry.LocalMachine, path, "SFIO Priority", on ? "High" : "Normal");
                },
                Restore = s =>
                {
                    const string path = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games";
                    RestoreDword(s, "cpu_game_priority.Priority", Registry.LocalMachine, path, "Priority");
                    RestoreDword(s, "cpu_game_priority.GpuPriority", Registry.LocalMachine, path, "GPU Priority");
                    RestoreString(s, "cpu_game_priority.Scheduling", Registry.LocalMachine, path, "Scheduling Category");
                    RestoreString(s, "cpu_game_priority.Sfio", Registry.LocalMachine, path, "SFIO Priority");
                }
            },
            new WindowsTweakItem
            {
                Id = "cpu_power_throttling",
                Title = "Desativar Power Throttling de Processos",
                Description = "Impede que o Windows reduza o clock ou mande threads ativas de jogos para núcleos de eficiência (E-cores) em momentos críticos.",
                Effect = "Maximiza a entrega de clocks da CPU sem restrições de eficiência energética.",
                Category = "⚡ CPU & Prioridade de Processos",
                NeedsRestart = false,
                Read = () => (ReadDword(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Power\PowerThrottling", "PowerThrottlingOff") ?? 0) == 1,
                Write = (on, s) => SetDword(s, "cpu_power_throttling", Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Power\PowerThrottling", "PowerThrottlingOff", on ? 1 : 0),
                Restore = s => RestoreDword(s, "cpu_power_throttling", Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Power\PowerThrottling", "PowerThrottlingOff")
            },
            new WindowsTweakItem
            {
                Id = "cpu_high_performance",
                Title = "Plano de Energia: Alto Desempenho",
                Description = "Mantém o processador no clock base mais alto, evitando transições lentas de estado C-State durante a partida.",
                Effect = "Garante resposta instantânea da CPU em tiroteios e cenas intensas.",
                Category = "⚡ CPU & Prioridade de Processos",
                NeedsRestart = false,
                Read = () => ActiveScheme()?.Equals(HighPerformanceGuid, StringComparison.OrdinalIgnoreCase) ?? false,
                Write = (on, s) =>
                {
                    if (!s.TweakBackup.ContainsKey("cpu_high_performance")) s.TweakBackup["cpu_high_performance"] = ActiveScheme();
                    if (on)
                    {
                        if (!PowerCfg("/list").Contains(HighPerformanceGuid, StringComparison.OrdinalIgnoreCase))
                            PowerCfg($"-duplicatescheme {HighPerformanceGuid} {HighPerformanceGuid}");
                        PowerCfg($"/setactive {HighPerformanceGuid}");
                    }
                    else if (s.TweakBackup["cpu_high_performance"] is string prev && !prev.Equals(HighPerformanceGuid, StringComparison.OrdinalIgnoreCase))
                        PowerCfg($"/setactive {prev}");
                    else PowerCfg($"/setactive {BalancedGuid}");
                },
                Restore = s =>
                {
                    if (s.TweakBackup.GetValueOrDefault("cpu_high_performance") is string prev)
                        PowerCfg($"/setactive {prev}");
                }
            },

            // ===== 2. GPU & GRÁFICOS =====
            new WindowsTweakItem
            {
                Id = "gpu_hags",
                Title = "Agendamento de GPU Acelerado por Hardware (HAGS)",
                Description = "Permite que a GPU gerencie diretamente sua própria memória e fila de renderização. Requisito obrigatório para DLSS 3 Frame Generation.",
                Effect = "Reduz overhead de CPU e latência de entrada.",
                Category = "🎮 GPU & Gráficos",
                NeedsRestart = true,
                Read = () => (ReadDword(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode") ?? 1) == 2,
                Write = (on, s) => SetDword(s, "gpu_hags", Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode", on ? 2 : 1),
                Restore = s => RestoreDword(s, "gpu_hags", Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode")
            },
            new WindowsTweakItem
            {
                Id = "gpu_frame_latency",
                Title = "Otimização de Latência de Quadros (Low Frame Latency)",
                Description = "Reduz o limite de pré-renderização de quadros DirectX, evitando fila desnecessária de imagens na memória da GPU.",
                Effect = "Mira mais imediata e sincronismo visual aprimorado.",
                Category = "🎮 GPU & Gráficos",
                NeedsRestart = false,
                Read = () => (ReadDword(Registry.CurrentUser, @"Software\Microsoft\Direct3D", "DisableDXMaxFrameLatency") ?? 0) == 1,
                Write = (on, s) => SetDword(s, "gpu_frame_latency", Registry.CurrentUser, @"Software\Microsoft\Direct3D", "DisableDXMaxFrameLatency", on ? 1 : 0),
                Restore = s => RestoreDword(s, "gpu_frame_latency", Registry.CurrentUser, @"Software\Microsoft\Direct3D", "DisableDXMaxFrameLatency")
            },
            new WindowsTweakItem
            {
                Id = "gpu_windowed_optimizations",
                Title = "Otimizações para Jogos em Janela (Flip Model Moderno)",
                Description = "Força jogos DirectX 10/11 sem bordas a utilizarem o pipeline Flip Model moderno do Windows 11 com suporte a VRR/G-Sync e latência de tela cheia.",
                Effect = "Permite Alt+Tab instantâneo com a mesma taxa de quadros de tela inteira.",
                Category = "🎮 GPU & Gráficos",
                NeedsRestart = false,
                Read = () => DxSettings().GetValueOrDefault("SwapEffectUpgradeEnable") == "1",
                Write = (on, s) =>
                {
                    const string dxPath = @"Software\Microsoft\DirectX\UserGpuPreferences";
                    if (!s.TweakBackup.ContainsKey("gpu_windowed_optimizations"))
                        s.TweakBackup["gpu_windowed_optimizations"] = Registry.CurrentUser.OpenSubKey(dxPath)?.GetValue("DirectXUserGlobalSettings") as string ?? "<ausente>";
                    var d = DxSettings();
                    d["SwapEffectUpgradeEnable"] = on ? "1" : "0";
                    using var k = Registry.CurrentUser.CreateSubKey(dxPath);
                    k.SetValue("DirectXUserGlobalSettings", string.Concat(d.Select(kv => $"{kv.Key}={kv.Value};")));
                },
                Restore = s =>
                {
                    const string dxPath = @"Software\Microsoft\DirectX\UserGpuPreferences";
                    if (s.TweakBackup.GetValueOrDefault("gpu_windowed_optimizations") is not string prev) return;
                    using var k = Registry.CurrentUser.CreateSubKey(dxPath);
                    if (prev == "<ausente>") k.DeleteValue("DirectXUserGlobalSettings", false);
                    else k.SetValue("DirectXUserGlobalSettings", prev);
                }
            },
            new WindowsTweakItem
            {
                Id = "gpu_game_dvr",
                Title = "Desativar Xbox Game DVR e Captura em Segundo Plano",
                Description = "Desliga a gravação contínua do Xbox Game Bar, liberando os codificadores de vídeo NVENC/AMF e uso residual de GPU e SSD.",
                Effect = "Recupera FPS e previne travamentos durante replays.",
                Category = "🎮 GPU & Gráficos",
                NeedsRestart = false,
                Read = () => (ReadDword(Registry.CurrentUser, @"System\GameConfigStore", "GameDVR_Enabled") ?? 1) == 0 &&
                             (ReadDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled") ?? 1) == 0,
                Write = (on, s) =>
                {
                    SetDword(s, "gpu_game_dvr.store", Registry.CurrentUser, @"System\GameConfigStore", "GameDVR_Enabled", on ? 0 : 1);
                    SetDword(s, "gpu_game_dvr.capture", Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled", on ? 0 : 1);
                    SetDword(s, "gpu_game_dvr.policy", Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\GameDVR", "AllowGameDVR", on ? 0 : 1);
                },
                Restore = s =>
                {
                    RestoreDword(s, "gpu_game_dvr.store", Registry.CurrentUser, @"System\GameConfigStore", "GameDVR_Enabled");
                    RestoreDword(s, "gpu_game_dvr.capture", Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled");
                    RestoreDword(s, "gpu_game_dvr.policy", Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\GameDVR", "AllowGameDVR");
                }
            },

            // ===== 3. LATÊNCIA DE INPUT & PERIFÉRICOS =====
            new WindowsTweakItem
            {
                Id = "input_mouse_precision",
                Title = "Precisão Linear do Mouse (1:1 Sem Aceleração)",
                Description = "Desativa o 'Aprimorar precisão do ponteiro'. O cursor percorre sempre a distância matemática exata correspondente ao movimento físico no mousepad.",
                Effect = "Garante consistência absoluta de memória muscular em jogos competitivos.",
                Category = "🎯 Latência de Input & Periféricos",
                NeedsRestart = false,
                Read = () => ReadString(Registry.CurrentUser, @"Control Panel\Mouse", "MouseSpeed") == "0",
                Write = (on, s) =>
                {
                    foreach (var n in new[] { "MouseSpeed", "MouseThreshold1", "MouseThreshold2" })
                        if (!s.TweakBackup.ContainsKey($"input_mouse.{n}"))
                            s.TweakBackup[$"input_mouse.{n}"] = ReadString(Registry.CurrentUser, @"Control Panel\Mouse", n);

                    var v = on ? new[] { "0", "0", "0" } : new[] { "1", "6", "10" };
                    ApplyMouseSpeed(v);
                },
                Restore = s =>
                {
                    if (!s.TweakBackup.ContainsKey("input_mouse.MouseSpeed")) return;
                    ApplyMouseSpeed(new[] { "MouseSpeed", "MouseThreshold1", "MouseThreshold2" }
                        .Select(n => s.TweakBackup.GetValueOrDefault($"input_mouse.{n}") ?? "0").ToArray());
                }
            },
            new WindowsTweakItem
            {
                Id = "net_throttling",
                Title = "Desativar Network Throttling Index (Estabilidade de Ping)",
                Description = "Remove a limitação de tráfego de pacotes não multimídia que o Windows impõe quando um aplicativo ou jogo está em execução.",
                Effect = "Elimina picos de jitter e variações súbitas de ping em conexões de internet rápidas.",
                Category = "🎯 Latência de Input & Periféricos",
                NeedsRestart = false,
                Read = () => ReadDword(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", "NetworkThrottlingIndex") == -1,
                Write = (on, s) => SetDword(s, "net_throttling", Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", "NetworkThrottlingIndex", on ? -1 : 10),
                Restore = s => RestoreDword(s, "net_throttling", Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", "NetworkThrottlingIndex")
            },
            new WindowsTweakItem
            {
                Id = "net_nagle",
                Title = "Desativar Algoritmo de Nagle (TcpAckFrequency / TCPNoDelay)",
                Description = "Força o Windows a enviar pacotes de rede imediatamente, sem esperar acumular pequenos blocos de dados no buffer TCP.",
                Effect = "Reduz o tempo de resposta em jogos multiplayer online que dependem de pacotes pequenos.",
                Category = "🎯 Latência de Input & Periféricos",
                NeedsRestart = false,
                Read = () => IsNagleDisabled(),
                Write = (on, s) => SetNagleState(on, s),
                Restore = s => RestoreNagle(s)
            },
            new WindowsTweakItem
            {
                Id = "input_disable_office_hotkeys",
                Title = "Desativar Office Hotkeys (Ctrl + Shift + Alt + Win)",
                Description = "Desativa os atalhos globais do Microsoft Office / Copilot (Ctrl+Shift+Alt+Win e combinações W/X/P/O/N/T/Y/L/D) no Windows.",
                Effect = "Evita abertura acidental do navegador ou app Office ao usar combinações de modificadores em jogos e atalhos.",
                Category = "🎯 Latência de Input & Periféricos",
                NeedsRestart = false,
                Read = () => AreOfficeHotkeysDisabled(),
                Write = (on, _) => SetOfficeHotkeysDisabled(on),
                Restore = _ => SetOfficeHotkeysDisabled(false)
            },

            // ===== 4. TELEMETRIA & SISTEMA =====
            new WindowsTweakItem
            {
                Id = "sys_telemetry",
                Title = "Desativar Telemetria Diagnóstica do Windows",
                Description = "Bloqueia o envio em segundo plano de relatórios de telemetria, rastreamento de uso e diagnósticos pesados para os servidores da Microsoft.",
                Effect = "Economiza largura de banda e ciclos de CPU em segundo plano.",
                Category = "🛡️ Telemetria & Privacidade",
                NeedsRestart = false,
                Read = () => (ReadDword(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry") ?? 1) == 0,
                Write = (on, s) => SetDword(s, "sys_telemetry", Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry", on ? 0 : 1),
                Restore = s => RestoreDword(s, "sys_telemetry", Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry")
            },
            new WindowsTweakItem
            {
                Id = "sys_copilot",
                Title = "Desativar Microsoft Copilot",
                Description = "Desativa o assistente de IA Copilot da barra de tarefas e desliga seus serviços em segundo plano no sistema.",
                Effect = "Remove o consumo de memória e processos associados ao Copilot.",
                Category = "🛡️ Telemetria & Privacidade",
                NeedsRestart = false,
                Read = () => (ReadDword(Registry.CurrentUser, @"Software\Policies\Microsoft\Windows\WindowsCopilot", "TurnOffWindowsCopilot") ?? 0) == 1,
                Write = (on, s) =>
                {
                    SetDword(s, "sys_copilot.user", Registry.CurrentUser, @"Software\Policies\Microsoft\Windows\WindowsCopilot", "TurnOffWindowsCopilot", on ? 1 : 0);
                    SetDword(s, "sys_copilot.machine", Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\WindowsCopilot", "TurnOffWindowsCopilot", on ? 1 : 0);
                    SetDword(s, "sys_copilot.button", Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "ShowCopilotButton", on ? 0 : 1);
                },
                Restore = s =>
                {
                    RestoreDword(s, "sys_copilot.user", Registry.CurrentUser, @"Software\Policies\Microsoft\Windows\WindowsCopilot", "TurnOffWindowsCopilot");
                    RestoreDword(s, "sys_copilot.machine", Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\WindowsCopilot", "TurnOffWindowsCopilot");
                    RestoreDword(s, "sys_copilot.button", Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "ShowCopilotButton");
                }
            },
            new WindowsTweakItem
            {
                Id = "sys_recall",
                Title = "Desativar Windows Recall & Análise de IA",
                Description = "Bloqueia a captura periódica de telas e o processamento de instantâneos pelo recurso de inteligência artificial Recall.",
                Effect = "Garante privacidade total e previne gravações de disco contínuas.",
                Category = "🛡️ Telemetria & Privacidade",
                NeedsRestart = false,
                Read = () => (ReadDword(Registry.CurrentUser, @"Software\Policies\Microsoft\Windows\WindowsAI", "DisableAIDataAnalysis") ?? 0) == 1,
                Write = (on, s) =>
                {
                    SetDword(s, "sys_recall.cu", Registry.CurrentUser, @"Software\Policies\Microsoft\Windows\WindowsAI", "DisableAIDataAnalysis", on ? 1 : 0);
                    SetDword(s, "sys_recall.lm", Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\WindowsAI", "DisableAIDataAnalysis", on ? 1 : 0);
                    SetDword(s, "sys_recall.allow", Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\WindowsAI", "AllowRecallEnablement", on ? 0 : 1);
                    SetDword(s, "sys_recall.snap", Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\WindowsAI", "TurnOffSavingSnapshots", on ? 1 : 0);
                },
                Restore = s =>
                {
                    RestoreDword(s, "sys_recall.cu", Registry.CurrentUser, @"Software\Policies\Microsoft\Windows\WindowsAI", "DisableAIDataAnalysis");
                    RestoreDword(s, "sys_recall.lm", Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\WindowsAI", "DisableAIDataAnalysis");
                    RestoreDword(s, "sys_recall.allow", Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\WindowsAI", "AllowRecallEnablement");
                    RestoreDword(s, "sys_recall.snap", Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\WindowsAI", "TurnOffSavingSnapshots");
                }
            },
            new WindowsTweakItem
            {
                Id = "sys_classic_menu",
                Title = "Menu de Contexto Clássico (Windows 10 Style)",
                Description = "Restaura o menu de contexto completo tradicional ao clicar com o botão direito, eliminando o clique extra em 'Mostrar mais opções'.",
                Effect = "Navegação no Explorer e na Área de Trabalho muito mais rápida.",
                Category = "🛡️ Telemetria & Privacidade",
                NeedsRestart = false,
                Read = () =>
                {
                    using var k = Registry.CurrentUser.OpenSubKey(@"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32");
                    return k != null;
                },
                Write = (on, s) =>
                {
                    const string path = @"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32";
                    if (!s.TweakBackup.ContainsKey("sys_classic_menu"))
                    {
                        using var existing = Registry.CurrentUser.OpenSubKey(path);
                        s.TweakBackup["sys_classic_menu"] = existing != null ? "1" : "<ausente>";
                    }

                    if (on)
                    {
                        using var k = Registry.CurrentUser.CreateSubKey(path);
                        k.SetValue("", "");
                    }
                    else
                    {
                        try { Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}", false); } catch { }
                    }
                },
                Restore = s =>
                {
                    var prev = s.TweakBackup.GetValueOrDefault("sys_classic_menu");
                    if (prev == "<ausente>")
                    {
                        try { Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}", false); } catch { }
                    }
                    else if (prev == "1")
                    {
                        using var k = Registry.CurrentUser.CreateSubKey(@"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32");
                        k.SetValue("", "");
                    }
                }
            },
            new WindowsTweakItem
            {
                Id = "sys_suggestions",
                Title = "Desativar Sugestões e Anúncios na Interface",
                Description = "Desliga recomendações de aplicativos promovidos, dicas de tela de bloqueio e anúncios contextuais embutidos no Windows.",
                Effect = "Interface limpa e livre de requisições de marketing.",
                Category = "🛡️ Telemetria & Privacidade",
                NeedsRestart = false,
                Read = () => (ReadDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SystemPaneSuggestionsEnabled") ?? 1) == 0,
                Write = (on, s) =>
                {
                    const string path = @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager";
                    int v = on ? 0 : 1;
                    SetDword(s, "sys_sug.pane", Registry.CurrentUser, path, "SystemPaneSuggestionsEnabled", v);
                    SetDword(s, "sys_sug.sub338", Registry.CurrentUser, path, "SubscribedContent-338388Enabled", v);
                    SetDword(s, "sys_sug.sub339", Registry.CurrentUser, path, "SubscribedContent-338389Enabled", v);
                    SetDword(s, "sys_sug.sub353", Registry.CurrentUser, path, "SubscribedContent-353698Enabled", v);
                    SetDword(s, "sys_sug.sub310", Registry.CurrentUser, path, "SubscribedContent-310093Enabled", v);
                },
                Restore = s =>
                {
                    const string path = @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager";
                    RestoreDword(s, "sys_sug.pane", Registry.CurrentUser, path, "SystemPaneSuggestionsEnabled");
                    RestoreDword(s, "sys_sug.sub338", Registry.CurrentUser, path, "SubscribedContent-338388Enabled");
                    RestoreDword(s, "sys_sug.sub339", Registry.CurrentUser, path, "SubscribedContent-338389Enabled");
                    RestoreDword(s, "sys_sug.sub353", Registry.CurrentUser, path, "SubscribedContent-353698Enabled");
                    RestoreDword(s, "sys_sug.sub310", Registry.CurrentUser, path, "SubscribedContent-310093Enabled");
                }
            },
            new WindowsTweakItem
            {
                Id = "sys_disable_paging_executive",
                Title = "Manter Kernel e Drivers na Memória RAM (Sistemas >= 16 GB)",
                Description = "Impede o Windows de mover páginas do kernel e drivers para o arquivo de paginação (pagefile) no SSD, mantendo-os sempre na RAM ultrarrápida.",
                Effect = "Resposta do sistema instantânea e fim de travamentos por leitura de paginação.",
                Category = "🛡️ Telemetria & Privacidade",
                NeedsRestart = true,
                Read = () => (ReadDword(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management", "DisablePagingExecutive") ?? 0) == 1,
                Write = (on, s) => SetDword(s, "sys_disable_paging_executive", Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management", "DisablePagingExecutive", on ? 1 : 0),
                Restore = s => RestoreDword(s, "sys_disable_paging_executive", Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management", "DisablePagingExecutive")
            },

            // ===== 5. BARRA DE TAREFAS & INICIAR (HUB PRO) =====
            new WindowsTweakItem
            {
                Id = "taskbar_task_view",
                Title = "Ocultar Visão de Tarefas (Task View)",
                Description = "Remove o botão de alternância de áreas de trabalho da barra de tarefas.",
                Effect = "Menos elementos na barra e menor consumo de DWM.",
                Category = "📌 Barra de Tarefas & Iniciar",
                NeedsRestart = false,
                Read = () => (ReadDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "ShowTaskViewButton") ?? 1) == 0,
                Write = (on, s) => SetDword(s, "taskbar_task_view", Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "ShowTaskViewButton", on ? 0 : 1),
                Restore = s => RestoreDword(s, "taskbar_task_view", Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "ShowTaskViewButton")
            },
            new WindowsTweakItem
            {
                Id = "taskbar_seconds_clock",
                Title = "Exibir Segundos no Relógio da Barra de Tarefas",
                Description = "Adiciona a contagem de segundos ao relógio do sistema no canto inferior direito.",
                Effect = "Visualização precisa de tempo sem abrir relógio auxiliar.",
                Category = "📌 Barra de Tarefas & Iniciar",
                NeedsRestart = false,
                Read = () => (ReadDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "ShowSecondsInSystemClock") ?? 0) == 1,
                Write = (on, s) => SetDword(s, "taskbar_seconds_clock", Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "ShowSecondsInSystemClock", on ? 1 : 0),
                Restore = s => RestoreDword(s, "taskbar_seconds_clock", Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "ShowSecondsInSystemClock")
            },
            new WindowsTweakItem
            {
                Id = "taskbar_align_left",
                Title = "Alinhar Menu Iniciar e Ícones à Esquerda",
                Description = "Alinha os botões da barra de tarefas e menu Iniciar à esquerda da tela, reproduzindo a ergonomia clássica do Windows 10.",
                Effect = "Acesso mais rápido e ergonômico no canto da tela.",
                Category = "📌 Barra de Tarefas & Iniciar",
                NeedsRestart = false,
                Read = () => (ReadDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "TaskbarAl") ?? 1) == 0,
                Write = (on, s) => SetDword(s, "taskbar_align_left", Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "TaskbarAl", on ? 0 : 1),
                Restore = s => RestoreDword(s, "taskbar_align_left", Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "TaskbarAl")
            },
            new WindowsTweakItem
            {
                Id = "taskbar_hide_widgets",
                Title = "Ocultar Widgets e Notícias da Barra de Tarefas",
                Description = "Desativa o botão de Clima e Widgets no Windows 11, evitando carregamento do webview do Bing.",
                Effect = "Economiza centenas de megabytes de RAM do processo Widgets.exe.",
                Category = "📌 Barra de Tarefas & Iniciar",
                NeedsRestart = false,
                Read = () => (ReadDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "TaskbarDa") ?? 1) == 0,
                Write = (on, s) => SetDword(s, "taskbar_hide_widgets", Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "TaskbarDa", on ? 0 : 1),
                Restore = s => RestoreDword(s, "taskbar_hide_widgets", Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "TaskbarDa")
            },
            new WindowsTweakItem
            {
                Id = "taskbar_hide_chat",
                Title = "Ocultar Chat / Microsoft Teams da Barra de Tarefas",
                Description = "Remove o botão integrado de bate-papo do Teams da barra de tarefas.",
                Effect = "Libera espaço e previne inicialização silenciosa do cliente Teams pessoal.",
                Category = "📌 Barra de Tarefas & Iniciar",
                NeedsRestart = false,
                Read = () => (ReadDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "TaskbarMn") ?? 1) == 0,
                Write = (on, s) => SetDword(s, "taskbar_hide_chat", Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "TaskbarMn", on ? 0 : 1),
                Restore = s => RestoreDword(s, "taskbar_hide_chat", Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "TaskbarMn")
            },
            new WindowsTweakItem
            {
                Id = "taskbar_search_icon",
                Title = "Reduzir Pesquisa da Barra para Apenas Ícone",
                Description = "Substitui a caixa de texto larga da pesquisa do Windows por um ícone compacto.",
                Effect = "Mais espaço na barra de tarefas para janelas e atalhos.",
                Category = "📌 Barra de Tarefas & Iniciar",
                NeedsRestart = false,
                Read = () => (ReadDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Search", "SearchboxTaskbarMode") ?? 2) == 1,
                Write = (on, s) => SetDword(s, "taskbar_search_icon", Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Search", "SearchboxTaskbarMode", on ? 1 : 2),
                Restore = s => RestoreDword(s, "taskbar_search_icon", Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Search", "SearchboxTaskbarMode")
            },
            new WindowsTweakItem
            {
                Id = "sys_silent_app_install",
                Title = "Desativar Instalação Silenciosa de Jogos e Apps Patrocinados",
                Description = "Bloqueia o download automático em segundo plano de jogos como Candy Crush, Spotify promocional e bloatwares da Microsoft Store.",
                Effect = "Impede poluição da biblioteca de aplicativos sem consentimento.",
                Category = "📌 Barra de Tarefas & Iniciar",
                NeedsRestart = false,
                Read = () => (ReadDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SilentInstalledAppsEnabled") ?? 1) == 0,
                Write = (on, s) => SetDword(s, "sys_silent_app_install", Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SilentInstalledAppsEnabled", on ? 0 : 1),
                Restore = s => RestoreDword(s, "sys_silent_app_install", Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SilentInstalledAppsEnabled")
            },

            // ===== 6. WINDOWS EXPLORER & INTERFACE (HUB PRO) =====
            new WindowsTweakItem
            {
                Id = "explorer_show_extensions",
                Title = "Mostrar Extensões de Arquivos (.exe, .zip, .txt)",
                Description = "Força o Windows Explorer a sempre exibir a extensão de todos os tipos de arquivos.",
                Effect = "Segurança crítica contra malwares disfarçados com duplo ponto.",
                Category = "📁 Windows Explorer & Interface",
                NeedsRestart = false,
                Read = () => (ReadDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "HideFileExt") ?? 1) == 0,
                Write = (on, s) => SetDword(s, "explorer_show_extensions", Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "HideFileExt", on ? 0 : 1),
                Restore = s => RestoreDword(s, "explorer_show_extensions", Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "HideFileExt")
            },
            new WindowsTweakItem
            {
                Id = "explorer_show_hidden",
                Title = "Mostrar Pastas e Arquivos Ocultos",
                Description = "Torna visíveis arquivos e diretórios ocultos no Explorer (AppData, pastas de sistema etc).",
                Effect = "Facilita manutenção técnica e localização de pastas de configurações.",
                Category = "📁 Windows Explorer & Interface",
                NeedsRestart = false,
                Read = () => (ReadDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "Hidden") ?? 2) == 1,
                Write = (on, s) => SetDword(s, "explorer_show_hidden", Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "Hidden", on ? 1 : 2),
                Restore = s => RestoreDword(s, "explorer_show_hidden", Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "Hidden")
            },
            new WindowsTweakItem
            {
                Id = "explorer_open_this_pc",
                Title = "Abrir o Explorador em 'Este Computador' em vez de 'Início'",
                Description = "Abre direto as unidades de disco (C:, D:) ao abrir o Explorador, em vez da aba de arquivos recentes.",
                Effect = "Abertura instantânea do Explorer sem delay de busca de nuvem.",
                Category = "📁 Windows Explorer & Interface",
                NeedsRestart = false,
                Read = () => (ReadDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "LaunchTo") ?? 2) == 1,
                Write = (on, s) => SetDword(s, "explorer_open_this_pc", Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "LaunchTo", on ? 1 : 2),
                Restore = s => RestoreDword(s, "explorer_open_this_pc", Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "LaunchTo")
            },
            new WindowsTweakItem
            {
                Id = "explorer_disable_recent",
                Title = "Desativar Arquivos e Pastas Recentes no Acesso Rápido",
                Description = "Impede o Windows de gravar e listar arquivos recentes no painel de navegação do Explorer.",
                Effect = "Privacidade absoluta no Explorador e menos gravações de metadados no SSD.",
                Category = "📁 Windows Explorer & Interface",
                NeedsRestart = false,
                Read = () => (ReadDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer", "ShowRecent") ?? 1) == 0,
                Write = (on, s) =>
                {
                    const string path = @"Software\Microsoft\Windows\CurrentVersion\Explorer";
                    SetDword(s, "explorer_disable_recent.recent", Registry.CurrentUser, path, "ShowRecent", on ? 0 : 1);
                    SetDword(s, "explorer_disable_recent.frequent", Registry.CurrentUser, path, "ShowFrequent", on ? 0 : 1);
                },
                Restore = s =>
                {
                    const string path = @"Software\Microsoft\Windows\CurrentVersion\Explorer";
                    RestoreDword(s, "explorer_disable_recent.recent", Registry.CurrentUser, path, "ShowRecent");
                    RestoreDword(s, "explorer_disable_recent.frequent", Registry.CurrentUser, path, "ShowFrequent");
                }
            },
            new WindowsTweakItem
            {
                Id = "search_disable_bing",
                Title = "Desativar Pesquisa na Web do Bing no Menu Iniciar",
                Description = "Impede que a busca do Iniciar envie termos digitados para o Bing na internet, limitando a busca estritamente aos arquivos locais.",
                Effect = "Busca do Menu Iniciar instantânea, sem atraso de rede e com privacidade total.",
                Category = "📁 Windows Explorer & Interface",
                NeedsRestart = false,
                Read = () => (ReadDword(Registry.CurrentUser, @"Software\Policies\Microsoft\Windows\Explorer", "DisableSearchBoxSuggestions") ?? 0) == 1,
                Write = (on, s) =>
                {
                    SetDword(s, "search_disable_bing.policy", Registry.CurrentUser, @"Software\Policies\Microsoft\Windows\Explorer", "DisableSearchBoxSuggestions", on ? 1 : 0);
                    SetDword(s, "search_disable_bing.bing", Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Search", "BingSearchEnabled", on ? 0 : 1);
                },
                Restore = s =>
                {
                    RestoreDword(s, "search_disable_bing.policy", Registry.CurrentUser, @"Software\Policies\Microsoft\Windows\Explorer", "DisableSearchBoxSuggestions");
                    RestoreDword(s, "search_disable_bing.bing", Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Search", "BingSearchEnabled");
                }
            },

            // ===== 7. PRIVACIDADE AVANÇADA (HUB PRO) =====
            new WindowsTweakItem
            {
                Id = "privacy_disable_ad_id",
                Title = "Desativar ID de Anúncios e Perfil de Publicidade",
                Description = "Desliga o identificador exclusivo de publicidade usado por aplicativos e pela Microsoft para direcionar anúncios.",
                Effect = "Impede cruzamento de dados de hábitos e perfil de navegação.",
                Category = "🛡️ Telemetria & Privacidade",
                NeedsRestart = false,
                Read = () => (ReadDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo", "Enabled") ?? 1) == 0,
                Write = (on, s) => SetDword(s, "privacy_disable_ad_id", Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo", "Enabled", on ? 0 : 1),
                Restore = s => RestoreDword(s, "privacy_disable_ad_id", Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo", "Enabled")
            },
            new WindowsTweakItem
            {
                Id = "privacy_disable_app_tracking",
                Title = "Desativar Rastreamento de Inicialização de Apps",
                Description = "Impede o Windows de monitorar quais programas você abre e com que frequência para alimentar sugestões.",
                Effect = "Menos telemetria local e integridade de privacidade.",
                Category = "🛡️ Telemetria & Privacidade",
                NeedsRestart = false,
                Read = () => (ReadDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "Start_TrackProgs") ?? 1) == 0,
                Write = (on, s) => SetDword(s, "privacy_disable_app_tracking", Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "Start_TrackProgs", on ? 0 : 1),
                Restore = s => RestoreDword(s, "privacy_disable_app_tracking", Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "Start_TrackProgs")
            },
            new WindowsTweakItem
            {
                Id = "privacy_disable_activity_feed",
                Title = "Desativar Feed de Atividades e Histórico de Uso",
                Description = "Bloqueia a coleta e sincronização contínua do histórico de atividades do Windows.",
                Effect = "Reduz tráfego de rede e gravações de histórico no registro.",
                Category = "🛡️ Telemetria & Privacidade",
                NeedsRestart = false,
                Read = () => (ReadDword(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\System", "EnableActivityFeed") ?? 1) == 0,
                Write = (on, s) => SetDword(s, "privacy_disable_activity_feed", Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\System", "EnableActivityFeed", on ? 0 : 1),
                Restore = s => RestoreDword(s, "privacy_disable_activity_feed", Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\System", "EnableActivityFeed")
            },
            new WindowsTweakItem
            {
                Id = "privacy_disable_typing_telemetry",
                Title = "Desativar Coleta de Dados de Digitação e Escrita",
                Description = "Bloqueia o envio de amostras de texto digitado, pontuação e escrita à mão para os servidores de nuvem da Microsoft.",
                Effect = "Garante sigilo de termos digitados e teclas pressionadas.",
                Category = "🛡️ Telemetria & Privacidade",
                NeedsRestart = false,
                Read = () => (ReadDword(Registry.CurrentUser, @"Software\Microsoft\InputPersonalization", "RestrictImplicitTextCollection") ?? 0) == 1,
                Write = (on, s) =>
                {
                    const string path = @"Software\Microsoft\InputPersonalization";
                    SetDword(s, "privacy_disable_typing.text", Registry.CurrentUser, path, "RestrictImplicitTextCollection", on ? 1 : 0);
                    SetDword(s, "privacy_disable_typing.ink", Registry.CurrentUser, path, "RestrictImplicitInkCollection", on ? 1 : 0);
                },
                Restore = s =>
                {
                    const string path = @"Software\Microsoft\InputPersonalization";
                    RestoreDword(s, "privacy_disable_typing.text", Registry.CurrentUser, path, "RestrictImplicitTextCollection");
                    RestoreDword(s, "privacy_disable_typing.ink", Registry.CurrentUser, path, "RestrictImplicitInkCollection");
                }
            },
            new WindowsTweakItem
            {
                Id = "privacy_disable_feedback",
                Title = "Desativar Pedidos de Feedback e Avaliações do Windows",
                Description = "Configura a frequência de questionários e pop-ups de feedback do Windows para 'Nunca'.",
                Effect = "Zero interrupções enquanto joga ou trabalha.",
                Category = "🛡️ Telemetria & Privacidade",
                NeedsRestart = false,
                Read = () => (ReadDword(Registry.CurrentUser, @"Software\Microsoft\Siuf\Rules", "NumberOfSIUFInPeriod") ?? 1) == 0,
                Write = (on, s) => SetDword(s, "privacy_disable_feedback", Registry.CurrentUser, @"Software\Microsoft\Siuf\Rules", "NumberOfSIUFInPeriod", on ? 0 : 1),
                Restore = s => RestoreDword(s, "privacy_disable_feedback", Registry.CurrentUser, @"Software\Microsoft\Siuf\Rules", "NumberOfSIUFInPeriod")
            },

            // ===== 8. DESEMPENHO & FLUIDEZ DO SISTEMA (HUB PRO) =====
            new WindowsTweakItem
            {
                Id = "perf_disable_hibernation",
                Title = "Desativar Hibernação do Windows (Libera arquivo hiberfil.sys)",
                Description = "Desativa o recurso de hibernação, excluindo imediatamente o arquivo hiberfil.sys da raiz do C: (economiza de 8 a 32 GB de espaço em SSD).",
                Effect = "Libera grande volume de armazenamento no disco do sistema.",
                Category = "⚡ CPU & Prioridade de Processos",
                NeedsRestart = false,
                Read = () => (ReadDword(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Power", "HibernateEnabled") ?? 1) == 0,
                Write = (on, s) =>
                {
                    SetDword(s, "perf_disable_hibernation", Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Power", "HibernateEnabled", on ? 0 : 1);
                    try
                    {
                        using var p = Process.Start(new ProcessStartInfo("powercfg.exe", on ? "-h off" : "-h on") { CreateNoWindow = true, UseShellExecute = false });
                        p?.WaitForExit(3000);
                    }
                    catch { }
                },
                Restore = s =>
                {
                    RestoreDword(s, "perf_disable_hibernation", Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Power", "HibernateEnabled");
                    try
                    {
                        using var p = Process.Start(new ProcessStartInfo("powercfg.exe", "-h on") { CreateNoWindow = true, UseShellExecute = false });
                        p?.WaitForExit(3000);
                    }
                    catch { }
                }
            },
            new WindowsTweakItem
            {
                Id = "perf_app_kill_timeout",
                Title = "Encerramento Acelerado de Aplicativos Travados",
                Description = "Reduz o tempo de espera do Windows para encerrar processos congelados durante o desligamento de 20 segundos para 2 segundos.",
                Effect = "Desligamento e reinicialização do Windows muito mais rápidos.",
                Category = "⚡ CPU & Prioridade de Processos",
                NeedsRestart = false,
                Read = () => ReadString(Registry.CurrentUser, @"Control Panel\Desktop", "WaitToKillAppTimeout") == "2000",
                Write = (on, s) =>
                {
                    const string path = @"Control Panel\Desktop";
                    SetString(s, "perf_app_kill_timeout.wait", Registry.CurrentUser, path, "WaitToKillAppTimeout", on ? "2000" : "10000");
                    SetString(s, "perf_app_kill_timeout.hung", Registry.CurrentUser, path, "HungAppTimeout", on ? "1000" : "5000");
                },
                Restore = s =>
                {
                    const string path = @"Control Panel\Desktop";
                    RestoreString(s, "perf_app_kill_timeout.wait", Registry.CurrentUser, path, "WaitToKillAppTimeout");
                    RestoreString(s, "perf_app_kill_timeout.hung", Registry.CurrentUser, path, "HungAppTimeout");
                }
            },
            new WindowsTweakItem
            {
                Id = "perf_disable_transparency",
                Title = "Desativar Efeitos de Transparência (Fluidez de GPU)",
                Description = "Desliga a transparência acrílica nas janelas e barra de tarefas do Windows.",
                Effect = "Alivia a carga do Desktop Window Manager (DWM) e reduz input lag da GPU em jogos.",
                Category = "🎮 GPU & Gráficos",
                NeedsRestart = false,
                Read = () => (ReadDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "EnableTransparency") ?? 1) == 0,
                Write = (on, s) => SetDword(s, "perf_disable_transparency", Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "EnableTransparency", on ? 0 : 1),
                Restore = s => RestoreDword(s, "perf_disable_transparency", Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "EnableTransparency")
            }
        };
    }

    public static void ApplyProfile(HardwareProfileRecommendation profile, AppSettings s, IEnumerable<WindowsTweakItem> allTweaks)
    {
        var targetIds = new HashSet<string>(profile.RecommendedTweakIds, StringComparer.OrdinalIgnoreCase);
        foreach (var t in allTweaks)
        {
            if (targetIds.Contains(t.Id))
            {
                try
                {
                    t.Write(true, s);
                }
                catch { }
            }
        }
        s.Save();
        foreach (var t in allTweaks) t.Refresh();
    }

    public static Task ApplyProfileAsync(HardwareProfileRecommendation profile, AppSettings s, IEnumerable<WindowsTweakItem> allTweaks) =>
        Task.Run(() => ApplyProfile(profile, s, allTweaks));

    public static void RevertAllTweaks(AppSettings s, IEnumerable<WindowsTweakItem> allTweaks)
    {
        foreach (var t in allTweaks)
        {
            try
            {
                t.Restore(s);
            }
            catch { }
        }
        // Remove apenas as chaves registradas por WindowsDebloatService, preservando backups de outras telas
        var debloatKeys = s.TweakBackup.Keys
            .Where(k => k.StartsWith("cpu_") || k.StartsWith("gpu_") || k.StartsWith("input_") || k.StartsWith("net_") || k.StartsWith("sys_")
                     || k.StartsWith("taskbar_") || k.StartsWith("explorer_") || k.StartsWith("search_") || k.StartsWith("privacy_") || k.StartsWith("perf_"))
            .ToList();
        foreach (var k in debloatKeys)
        {
            s.TweakBackup.Remove(k);
        }
        s.Save();
        foreach (var t in allTweaks) t.Refresh();
    }

    public static Task RevertAllTweaksAsync(AppSettings s, IEnumerable<WindowsTweakItem> allTweaks) =>
        Task.Run(() => RevertAllTweaks(s, allTweaks));

    // ------------------------------------------------------------------ Auxiliares de Registro

    private static int? ReadDword(RegistryKey hive, string path, string name)
    {
        try
        {
            using var k = hive.OpenSubKey(path);
            var val = k?.GetValue(name);
            if (val == null) return null;
            if (val is int i) return i;
            if (val is uint u) return unchecked((int)u);
            if (val is long l) return unchecked((int)l);
            if (int.TryParse(val.ToString(), out int parsed)) return parsed;
            if (uint.TryParse(val.ToString(), out uint parsedU)) return unchecked((int)parsedU);
            return null;
        }
        catch { return null; }
    }

    private static string? ReadString(RegistryKey hive, string path, string name)
    {
        try
        {
            using var k = hive.OpenSubKey(path);
            return k?.GetValue(name)?.ToString();
        }
        catch { return null; }
    }

    private static void SetDword(AppSettings s, string backupKey, RegistryKey hive, string path, string name, int value)
    {
        bool hadKey = s.TweakBackup.ContainsKey(backupKey);
        string? original = hadKey ? null : (ReadDword(hive, path, name)?.ToString() ?? "<ausente>");
        if (!hadKey) s.TweakBackup[backupKey] = original;

        try
        {
            using var k = hive.CreateSubKey(path);
            k.SetValue(name, value, RegistryValueKind.DWord);
        }
        catch
        {
            if (!hadKey) s.TweakBackup.Remove(backupKey);
            throw;
        }
    }

    private static void RestoreDword(AppSettings s, string backupKey, RegistryKey hive, string path, string name)
    {
        try
        {
            if (s.TweakBackup.GetValueOrDefault(backupKey) is not string prev) return;
            using var k = hive.CreateSubKey(path);
            if (prev == "<ausente>") k.DeleteValue(name, false);
            else if (int.TryParse(prev, out int v)) k.SetValue(name, v, RegistryValueKind.DWord);
            else if (uint.TryParse(prev, out uint uv)) k.SetValue(name, unchecked((int)uv), RegistryValueKind.DWord);
        }
        catch { }
    }

    private static void SetString(AppSettings s, string backupKey, RegistryKey hive, string path, string name, string value)
    {
        bool hadKey = s.TweakBackup.ContainsKey(backupKey);
        string? original = hadKey ? null : (ReadString(hive, path, name) ?? "<ausente>");
        if (!hadKey) s.TweakBackup[backupKey] = original;

        try
        {
            using var k = hive.CreateSubKey(path);
            k.SetValue(name, value, RegistryValueKind.String);
        }
        catch
        {
            if (!hadKey) s.TweakBackup.Remove(backupKey);
            throw;
        }
    }

    private static void RestoreString(AppSettings s, string backupKey, RegistryKey hive, string path, string name)
    {
        try
        {
            if (s.TweakBackup.GetValueOrDefault(backupKey) is not string prev) return;
            using var k = hive.CreateSubKey(path);
            if (prev == "<ausente>") k.DeleteValue(name, false);
            else k.SetValue(name, prev, RegistryValueKind.String);
        }
        catch { }
    }

    private static Dictionary<string, string> DxSettings()
    {
        var d = new Dictionary<string, string>();
        try
        {
            string raw = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\DirectX\UserGpuPreferences")?.GetValue("DirectXUserGlobalSettings") as string ?? "";
            foreach (var part in raw.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var kv = part.Split('=', 2);
                if (kv.Length == 2) d[kv[0].Trim()] = kv[1].Trim();
            }
        }
        catch { }
        return d;
    }

    private static void ApplyMouseSpeed(string[] v)
    {
        try
        {
            using (var k = Registry.CurrentUser.CreateSubKey(@"Control Panel\Mouse"))
            {
                k.SetValue("MouseSpeed", v.Length > 0 ? v[0] : "0");
                k.SetValue("MouseThreshold1", v.Length > 1 ? v[1] : "0");
                k.SetValue("MouseThreshold2", v.Length > 2 ? v[2] : "0");
            }
            int speed = v.Length > 0 && int.TryParse(v[0], out int sVal) ? sVal : 0;
            int thresh1 = v.Length > 1 && int.TryParse(v[1], out int t1Val) ? t1Val : 6;
            int thresh2 = v.Length > 2 && int.TryParse(v[2], out int t2Val) ? t2Val : 10;
            var p = new[] { speed, thresh1, thresh2 };
            SystemParametersInfo(0x0004 /* SPI_SETMOUSE */, 0, p, 0x01 | 0x02);
        }
        catch { }
    }

    private static bool IsNagleDisabled()
    {
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces");
            if (k == null) return false;
            foreach (var sub in k.GetSubKeyNames())
            {
                using var iface = k.OpenSubKey(sub);
                if (iface?.GetValue("TcpAckFrequency") is int f && f == 1 &&
                    iface?.GetValue("TCPNoDelay") is int d && d == 1)
                {
                    return true;
                }
            }
        }
        catch { }
        return false;
    }

    private static void SetNagleState(bool on, AppSettings s)
    {
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces");
            if (k == null) return;
            foreach (var sub in k.GetSubKeyNames())
            {
                string path = $@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\{sub}";
                using var ifaceRead = Registry.LocalMachine.OpenSubKey(path);
                // Aplica nas interfaces configuradas com IP
                if (ifaceRead?.GetValue("IPAddress") != null || ifaceRead?.GetValue("DhcpIPAddress") != null)
                {
                    SetDword(s, $"net_nagle.ack.{sub}", Registry.LocalMachine, path, "TcpAckFrequency", on ? 1 : 2);
                    SetDword(s, $"net_nagle.nodelay.{sub}", Registry.LocalMachine, path, "TCPNoDelay", on ? 1 : 0);
                }
            }
        }
        catch { }
    }

    private static void RestoreNagle(AppSettings s)
    {
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces");
            if (k == null) return;
            foreach (var sub in k.GetSubKeyNames())
            {
                string path = $@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\{sub}";
                RestoreDword(s, $"net_nagle.ack.{sub}", Registry.LocalMachine, path, "TcpAckFrequency");
                RestoreDword(s, $"net_nagle.nodelay.{sub}", Registry.LocalMachine, path, "TCPNoDelay");
            }
        }
        catch { }
    }

    private static string? ActiveScheme()
    {
        try
        {
            var m = System.Text.RegularExpressions.Regex.Match(PowerCfg("/getactivescheme"), @"[0-9a-fA-F\-]{36}");
            return m.Success ? m.Value.ToLowerInvariant() : null;
        }
        catch { return null; }
    }

    private static string PowerCfg(string args)
    {
        try
        {
            var psi = new ProcessStartInfo("powercfg.exe", args)
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi)!;
            string output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(5000);
            return output;
        }
        catch { return ""; }
    }

    public static bool AreOfficeHotkeysDisabled()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Classes\ms-officeapp\Shell\Open\Command");
            var val = k?.GetValue("")?.ToString();
            return string.Equals(val, "rundll32", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    public static void SetOfficeHotkeysDisabled(bool disable)
    {
        try
        {
            if (disable)
            {
                using var k = Registry.CurrentUser.CreateSubKey(@"Software\Classes\ms-officeapp\Shell\Open\Command");
                k?.SetValue("", "rundll32", RegistryValueKind.String);
            }
            else
            {
                Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\ms-officeapp", throwOnMissingSubKey: false);
            }
        }
        catch { }
    }

    [DllImport("user32.dll")] private static extern bool SystemParametersInfo(uint action, uint param, int[] data, uint winIni);
}
