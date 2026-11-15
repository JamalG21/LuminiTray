using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace LuminiTray;

public sealed class UpdateReleaseInfo
{
    public string TagName { get; set; } = "";
    public Version Version { get; set; } = new(0, 0, 0);
    public string Notes { get; set; } = "";
    public string? PortableZipUrl { get; set; }
    public string? PortableExeUrl { get; set; }
    public string? InstallerUrl { get; set; }
    public string HtmlUrl { get; set; } = "";
}

/// <summary>
/// Verificação e aplicação de atualizações via GitHub Releases.
/// Só consulta a rede sob demanda (início com janela visível, abertura do launcher ou clique manual):
/// nunca mantém timers ou polling em segundo plano.
/// </summary>
public sealed class AppUpdaterService
{
    public const string RepoOwner = "JamalG21";
    public const string RepoName = "LuminiTray";
    public const string PortableZipName = "LuminiTray-Portable.zip";
    public const string PortableExeName = "LuminiTray-Portable.exe";
    public const string InstallerName = "LuminiTray-Installer.exe";

    public static AppUpdaterService Instance { get; } = new();

    private static readonly TimeSpan MinAutoInterval = TimeSpan.FromMinutes(5);
    private DateTime _lastCheckUtc = DateTime.MinValue;
    private int _checking;

    public UpdateReleaseInfo? LatestRelease { get; private set; }
    public bool UpdateAvailable { get; private set; }

    public static Version CurrentVersion
    {
        get
        {
            var v = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0, 0);
            return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
        }
    }

    public static bool TryParseTag(string? tag, out Version version)
    {
        version = new Version(0, 0, 0);
        if (string.IsNullOrWhiteSpace(tag)) return false;
        string t = tag.Trim().TrimStart('v', 'V');
        int cut = t.IndexOfAny(new[] { '-', '+' });
        if (cut >= 0) t = t[..cut];
        if (!Version.TryParse(t, out var parsed)) return false;
        version = new Version(parsed.Major, parsed.Minor, Math.Max(0, parsed.Build));
        return true;
    }

    public static bool IsNewer(Version remote, Version current) =>
        remote.CompareTo(new Version(current.Major, current.Minor, Math.Max(0, current.Build))) > 0;

    public static UpdateReleaseInfo? ParseRelease(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        string tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";
        if (!TryParseTag(tag, out var ver)) return null;

        var info = new UpdateReleaseInfo
        {
            TagName = tag,
            Version = ver,
            Notes = root.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "",
            HtmlUrl = root.TryGetProperty("html_url", out var h) ? h.GetString() ?? "" : ""
        };

        if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
        {
            foreach (var a in assets.EnumerateArray())
            {
                string name = a.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                string url = a.TryGetProperty("browser_download_url", out var u) ? u.GetString() ?? "" : "";
                if (name.Equals(PortableZipName, StringComparison.OrdinalIgnoreCase)) info.PortableZipUrl = url;
                else if (name.Equals(PortableExeName, StringComparison.OrdinalIgnoreCase)) info.PortableExeUrl = url;
                else if (name.Equals(InstallerName, StringComparison.OrdinalIgnoreCase)) info.InstallerUrl = url;
            }
        }
        return info;
    }

    /// <summary>Entre as releases, ignora a release rolante "latest" e escolhe a de maior versão SemVer (não-rascunho/pré-lançamento).</summary>
    public static UpdateReleaseInfo? ParseReleaseList(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return ParseRelease(json);
        UpdateReleaseInfo? best = null;
        foreach (var el in doc.RootElement.EnumerateArray())
        {
            if (el.TryGetProperty("draft", out var d) && d.ValueKind == JsonValueKind.True) continue;
            if (el.TryGetProperty("prerelease", out var p) && p.ValueKind == JsonValueKind.True) continue;
            var info = ParseRelease(el.GetRawText());
            if (info == null) continue;
            if (best == null || info.Version > best.Version) best = info;
        }
        return best;
    }

    public static bool IsInstalledDistribution(string? baseDir = null)
    {
        baseDir ??= AppContext.BaseDirectory;
        try
        {
            if (File.Exists(Path.Combine(baseDir, ".installed"))) return true;
            string std = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "LuminiTray");
            return Path.GetFullPath(baseDir).TrimEnd('\\').Equals(Path.GetFullPath(std).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    /// <summary>Escolhe o asset: zip completo se houver pasta Assets/instalado, senão o exe portátil único.</summary>
    public static string? SelectAssetUrl(UpdateReleaseInfo info, bool hasAssetsFolder)
    {
        if (hasAssetsFolder || string.IsNullOrEmpty(info.PortableExeUrl))
            return info.PortableZipUrl ?? info.PortableExeUrl;
        return info.PortableExeUrl;
    }

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("LuminiTray-Updater");
        c.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return c;
    }

    public async Task<UpdateReleaseInfo?> CheckForUpdatesAsync(bool force = false, CancellationToken ct = default)
    {
        if (!force && DateTime.UtcNow - _lastCheckUtc < MinAutoInterval) return LatestRelease;
        if (Interlocked.Exchange(ref _checking, 1) == 1) return LatestRelease;
        try
        {
            using var http = CreateClient();
            string url = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases?per_page=15";
            string json = await http.GetStringAsync(url, ct).ConfigureAwait(false);
            var info = ParseReleaseList(json);
            _lastCheckUtc = DateTime.UtcNow;
            if (info == null) return LatestRelease;
            LatestRelease = info;
            UpdateAvailable = IsNewer(info.Version, CurrentVersion);
            return info;
        }
        catch
        {
            return LatestRelease;
        }
        finally
        {
            Interlocked.Exchange(ref _checking, 0);
        }
    }

    public async Task<bool> DownloadAndApplyUpdateAsync(UpdateReleaseInfo info, IProgress<double>? progress, CancellationToken ct = default)
    {
        string baseDir = AppContext.BaseDirectory.TrimEnd('\\');
        bool hasAssets = Directory.Exists(Path.Combine(baseDir, "Assets")) || IsInstalledDistribution(baseDir);
        string? url = SelectAssetUrl(info, hasAssets);
        if (string.IsNullOrEmpty(url)) return false;

        string tmp = Path.Combine(Path.GetTempPath(), "LuminiTray_Update");
        try { if (Directory.Exists(tmp)) Directory.Delete(tmp, true); } catch { }
        Directory.CreateDirectory(tmp);

        bool isZip = url.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
        string file = Path.Combine(tmp, isZip ? "update.zip" : "LuminiTray.exe");

        using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) })
        {
            http.DefaultRequestHeaders.UserAgent.ParseAdd("LuminiTray-Updater");
            using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            long total = resp.Content.Headers.ContentLength ?? -1;
            await using var src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            await using var dst = File.Create(file);
            var buf = new byte[81920];
            long read = 0;
            int n;
            while ((n = await src.ReadAsync(buf.AsMemory(0, buf.Length), ct).ConfigureAwait(false)) > 0)
            {
                await dst.WriteAsync(buf.AsMemory(0, n), ct).ConfigureAwait(false);
                read += n;
                if (total > 0) progress?.Report(read * 100.0 / total);
            }
        }
        progress?.Report(100);

        string stage = Path.Combine(tmp, "stage");
        Directory.CreateDirectory(stage);
        if (isZip) System.IO.Compression.ZipFile.ExtractToDirectory(file, stage, true);
        else File.Copy(file, Path.Combine(stage, "LuminiTray.exe"), true);

        string exeName = Path.GetFileName(Environment.ProcessPath ?? "LuminiTray.exe");
        string script = Path.Combine(tmp, "apply_update.ps1");
        var sb = new StringBuilder();
        sb.AppendLine($"$pidToWait = {Environment.ProcessId}");
        sb.AppendLine("try { Wait-Process -Id $pidToWait -Timeout 30 -ErrorAction SilentlyContinue } catch {}");
        sb.AppendLine("foreach ($n in 'LuminiTray.Wallpaper','LuminiTray.Fans','LuminiTray.Metrics','LuminiTray.Suppressor') { Get-Process -Name $n -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue }");
        sb.AppendLine("Start-Sleep -Milliseconds 500");
        sb.AppendLine($"$dest = '{baseDir.Replace("'", "''")}'");
        sb.AppendLine($"$stage = '{stage.Replace("'", "''")}'");
        sb.AppendLine("$ok = $false");
        sb.AppendLine("for ($i=0; $i -lt 20 -and -not $ok; $i++) { try { Copy-Item -Path (Join-Path $stage '*') -Destination $dest -Recurse -Force -ErrorAction Stop; $ok = $true } catch { Start-Sleep -Milliseconds 500 } }");
        sb.AppendLine($"if ($ok -and (Test-Path (Join-Path $dest '.installed'))) {{ try {{ Set-ItemProperty -Path 'HKCU:\\Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\LuminiTray' -Name DisplayVersion -Value '{info.Version}' }} catch {{}} }}");
        sb.AppendLine($"Start-Process -FilePath (Join-Path $dest '{exeName.Replace("'", "''")}') -WorkingDirectory $dest");
        sb.AppendLine($"Remove-Item -Path '{tmp.Replace("'", "''")}' -Recurse -Force -ErrorAction SilentlyContinue");
        File.WriteAllText(script, sb.ToString(), new UTF8Encoding(true));

        Process.Start(new ProcessStartInfo("powershell.exe",
            $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{script}\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true
        });
        return true;
    }
}
