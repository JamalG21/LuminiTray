using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace LuminiTray;

public enum PostFormatCategory
{
    RestorePoint,
    Drivers,
    WiFi,
    Debloat,
    WingetApp,
    Tweaks
}

public enum QueueItemStatus
{
    Pending,
    InProgress,
    Completed,
    Failed,
    Skipped
}

public sealed class PostFormatQueueItem : System.ComponentModel.INotifyPropertyChanged
{
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));

    private bool _isSelected = true;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                if (_isSelected && _status == QueueItemStatus.Skipped)
                {
                    Status = QueueItemStatus.Pending;
                }
                OnPropertyChanged(nameof(IsSelected));
            }
        }
    }

    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Subtitle { get; set; } = "";
    public PostFormatCategory Category { get; set; }
    private QueueItemStatus _status = QueueItemStatus.Pending;
    public QueueItemStatus Status
    {
        get => _status;
        set
        {
            if (_status != value)
            {
                _status = value;
                OnPropertyChanged(nameof(Status));
                OnPropertyChanged(nameof(StatusBadgeText));
                OnPropertyChanged(nameof(StatusColor));
            }
        }
    }
    public string TargetPayload { get; set; } = ""; // Winget ID, driver path, etc.
    public string ErrorMessage { get; set; } = "";

    public string StatusBadgeText => Status switch
    {
        QueueItemStatus.Pending => "Pendente",
        QueueItemStatus.InProgress => "Instalando...",
        QueueItemStatus.Completed => "✓ Instalado",
        QueueItemStatus.Failed => "❌ Falhou",
        QueueItemStatus.Skipped => "Ignorado",
        _ => ""
    };

    public string StatusColor => Status switch
    {
        QueueItemStatus.InProgress => "#3B82F6",
        QueueItemStatus.Completed => "#10B981",
        QueueItemStatus.Failed => "#EF4444",
        QueueItemStatus.Skipped => "#888888",
        _ => "#A0AEC0"
    };

    public string Icon => Category switch
    {
        PostFormatCategory.RestorePoint => "\uE777",
        PostFormatCategory.Drivers => "\uE7F8",
        PostFormatCategory.WiFi => "\uE701",
        PostFormatCategory.Debloat => "\uE74D",
        PostFormatCategory.WingetApp => "\uE74C",
        PostFormatCategory.Tweaks => "\uE713",
        _ => "\uE8B7"
    };
}

public sealed class PostFormatSessionState
{
    public string SessionId { get; set; } = Guid.NewGuid().ToString("N");
    public string Status { get; set; } = "Idle"; // Idle, Running, Paused, Completed
    public int CurrentIndex { get; set; } = 0;
    public bool AutoResumeOnStartup { get; set; } = true;
    public DateTime LastUpdated { get; set; } = DateTime.Now;
    public List<PostFormatQueueItem> Items { get; set; } = new();

    public int TotalItems => Items.Count;
    public int SelectedItems => Items.Count(x => x.IsSelected);
    public int CompletedItems => Items.Count(x => x.Status == QueueItemStatus.Completed);
    public int PendingItems => Items.Count(x => x.IsSelected && (x.Status == QueueItemStatus.Pending || x.Status == QueueItemStatus.Skipped));
    public bool HasUnfinishedWork => Items.Any(x => x.IsSelected && (x.Status == QueueItemStatus.Pending || x.Status == QueueItemStatus.InProgress || x.Status == QueueItemStatus.Skipped));
}

public sealed class DiscoveredBackups
{
    public string? DriverFolder { get; set; }
    public int DriverCount { get; set; }
    public string? WifiFolder { get; set; }
    public int WifiCount { get; set; }
    public string? WingetScriptOrFile { get; set; }
    public List<string> WingetAppIds { get; set; } = new();

    public bool HasAny => !string.IsNullOrEmpty(DriverFolder) || !string.IsNullOrEmpty(WifiFolder) || WingetAppIds.Count > 0;
}

public static class PostFormatManager
{
    private static readonly string StateFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LuminiTray",
        "post_format_state.json");

    private const string RunOnceKeyPath = @"Software\Microsoft\Windows\CurrentVersion\RunOnce";
    private const string RunOnceValueName = "LuminiTrayPostFormatResume";

    public static async Task SaveSessionAsync(PostFormatSessionState state)
    {
        try
        {
            state.LastUpdated = DateTime.Now;
            string dir = Path.GetDirectoryName(StateFilePath)!;
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            string json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(StateFilePath, json);

            SetRunOnceResume(state.AutoResumeOnStartup && state.HasUnfinishedWork);
        }
        catch { }
    }

    public static async Task<PostFormatSessionState?> LoadSessionAsync()
    {
        try
        {
            if (File.Exists(StateFilePath))
            {
                string json = await File.ReadAllTextAsync(StateFilePath);
                var state = JsonSerializer.Deserialize<PostFormatSessionState>(json);
                return state;
            }
        }
        catch { }
        return null;
    }

    public static void ClearSession()
    {
        try
        {
            if (File.Exists(StateFilePath)) File.Delete(StateFilePath);
            SetRunOnceResume(false);
        }
        catch { }
    }

    public static void SetRunOnceResume(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunOnceKeyPath, true);
            if (key == null) return;

            if (enable)
            {
                string exePath = Process.GetCurrentProcess().MainModule?.FileName ?? "";
                if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
                {
                    key.SetValue(RunOnceValueName, $"\"{exePath}\" --resume-post-format");
                }
            }
            else
            {
                key.DeleteValue(RunOnceValueName, false);
            }
        }
        catch { }
    }

    public static async Task<DiscoveredBackups> ScanForBackupsAsync(string? specificFolder = null)
    {
        var discovered = new DiscoveredBackups();

        var searchRoots = new List<string>();
        if (!string.IsNullOrEmpty(specificFolder) && Directory.Exists(specificFolder))
        {
            searchRoots.Add(specificFolder);
        }
        else
        {
            // Busca em unidades removíveis USB e discos secundários
            foreach (var d in DriveInfo.GetDrives().Where(d => d.IsReady))
            {
                if (d.DriveType is DriveType.Removable or DriveType.Fixed)
                {
                    searchRoots.Add(d.RootDirectory.FullName);
                }
            }

            // Busca também no Desktop, Downloads e Documentos
            searchRoots.Add(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
            searchRoots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"));
            searchRoots.Add(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
        }

        await Task.Run(() =>
        {
            foreach (var root in searchRoots.Distinct())
            {
                if (!Directory.Exists(root)) continue;

                try
                {
                    // 1. Procura Backups de Drivers
                    if (string.IsNullOrEmpty(discovered.DriverFolder))
                    {
                        var driverDirs = Directory.GetDirectories(root, "*Driver*", SearchOption.TopDirectoryOnly)
                            .Concat(Directory.GetDirectories(root, "*Backup*", SearchOption.TopDirectoryOnly))
                            .ToList();

                        foreach (var dir in driverDirs)
                        {
                            var infs = Directory.GetFiles(dir, "*.inf", SearchOption.AllDirectories);
                            if (infs.Length > 0)
                            {
                                discovered.DriverFolder = dir;
                                discovered.DriverCount = infs.Length;
                                break;
                            }
                        }
                    }

                    // 2. Procura Backups de Wi-Fi
                    if (string.IsNullOrEmpty(discovered.WifiFolder))
                    {
                        string jsonWifi = Path.Combine(root, "wifi_backup.json");
                        if (File.Exists(jsonWifi))
                        {
                            discovered.WifiFolder = root;
                            discovered.WifiCount = 1;
                        }
                        else
                        {
                            var wifiXmls = Directory.GetFiles(root, "Wi-Fi-*.xml", SearchOption.TopDirectoryOnly);
                            if (wifiXmls.Length > 0)
                            {
                                discovered.WifiFolder = root;
                                discovered.WifiCount = wifiXmls.Length;
                            }
                        }
                    }

                    // 3. Procura Listas de Programas Winget
                    if (discovered.WingetAppIds.Count == 0)
                    {
                        string scriptBat = Path.Combine(root, "InstalarProgramas.bat");
                        string jsonWinget = Path.Combine(root, "winget_packages.json");
                        string txtWinget = Path.Combine(root, "winget_apps.txt");

                        if (File.Exists(jsonWinget))
                        {
                            discovered.WingetScriptOrFile = jsonWinget;
                            ParseWingetFile(jsonWinget, discovered.WingetAppIds);
                        }
                        else if (File.Exists(txtWinget))
                        {
                            discovered.WingetScriptOrFile = txtWinget;
                            ParseWingetFile(txtWinget, discovered.WingetAppIds);
                        }
                        else if (File.Exists(scriptBat))
                        {
                            discovered.WingetScriptOrFile = scriptBat;
                            ParseWingetFile(scriptBat, discovered.WingetAppIds);
                        }
                    }
                }
                catch { }
            }
        });

        return discovered;
    }

    private static void ParseWingetFile(string path, List<string> targetList)
    {
        try
        {
            var lines = File.ReadAllLines(path);
            foreach (var l in lines)
            {
                var line = l.Trim();
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("::") || line.StartsWith("#") || line.StartsWith("REM"))
                    continue;

                if (line.Contains("winget install", StringComparison.OrdinalIgnoreCase))
                {
                    var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    for (int i = 0; i < parts.Length; i++)
                    {
                        if ((parts[i] == "--id" || parts[i] == "-e") && i + 1 < parts.Length)
                        {
                            targetList.Add(parts[i + 1].Trim('"'));
                            break;
                        }
                    }
                }
                else if (line.Contains('.'))
                {
                    targetList.Add(line);
                }
            }
        }
        catch { }
    }

    public static async Task ExecuteQueueAsync(
        PostFormatSessionState state,
        Action<PostFormatSessionState, string> onProgress,
        CancellationToken ct)
    {
        state.Status = "Running";

        for (int i = 0; i < state.Items.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            var item = state.Items[i];
            if (!item.IsSelected)
            {
                continue;
            }

            if (item.Status == QueueItemStatus.Completed)
                continue;

            state.CurrentIndex = i;
            item.Status = QueueItemStatus.InProgress;
            await SaveSessionAsync(state);

            onProgress(state, $"Iniciando: {item.Title}...");

            try
            {
                switch (item.Category)
                {
                    case PostFormatCategory.RestorePoint:
                        var (okRp, msgRp) = await SystemRestoreManager.CreateRestorePointAsync("Pós-Formatação Lumini Tray");
                        item.Status = okRp ? QueueItemStatus.Completed : QueueItemStatus.Failed;
                        item.ErrorMessage = msgRp;
                        break;

                    case PostFormatCategory.Drivers:
                        if (!string.IsNullOrEmpty(item.TargetPayload) && Directory.Exists(item.TargetPayload))
                        {
                            var psi = new ProcessStartInfo
                            {
                                FileName = "pnputil.exe",
                                Arguments = $"/add-driver \"{Path.Combine(item.TargetPayload, "*.inf")}\" /subdirs /install",
                                CreateNoWindow = true,
                                UseShellExecute = false
                            };
                            using var proc = Process.Start(psi);
                            if (proc != null) await proc.WaitForExitAsync(ct);
                            item.Status = QueueItemStatus.Completed;
                        }
                        else
                        {
                            item.Status = QueueItemStatus.Completed;
                        }
                        break;

                    case PostFormatCategory.WiFi:
                        if (!string.IsNullOrEmpty(item.TargetPayload) && Directory.Exists(item.TargetPayload))
                        {
                            var (okWifi, msgWifi, count) = await BackupService.RestoreWiFiProfilesAsync(item.TargetPayload);
                            item.Status = okWifi ? QueueItemStatus.Completed : QueueItemStatus.Failed;
                            item.ErrorMessage = msgWifi;
                        }
                        else
                        {
                            item.Status = QueueItemStatus.Completed;
                        }
                        break;

                    case PostFormatCategory.Debloat:
                        await Task.CompletedTask;
                        item.Status = QueueItemStatus.Completed;
                        break;

                    case PostFormatCategory.WingetApp:
                        if (!string.IsNullOrEmpty(item.TargetPayload))
                        {
                            var psi = new ProcessStartInfo
                            {
                                FileName = "winget.exe",
                                Arguments = $"install --id \"{item.TargetPayload}\" --silent --accept-package-agreements --accept-source-agreements",
                                CreateNoWindow = true,
                                UseShellExecute = false
                            };
                            using var proc = Process.Start(psi);
                            if (proc != null) await proc.WaitForExitAsync(ct);
                            item.Status = QueueItemStatus.Completed;
                        }
                        break;

                    case PostFormatCategory.Tweaks:
                        var appSettings = AppSettings.Load();
                        var tweaks = WindowsTweaks.All();
                        foreach (var tw in tweaks)
                        {
                            try { tw.Write(true, appSettings); } catch { }
                        }
                        item.Status = QueueItemStatus.Completed;
                        break;
                }
            }
            catch (OperationCanceledException)
            {
                item.Status = QueueItemStatus.Pending;
                state.Status = "Paused";
                await SaveSessionAsync(state);
                onProgress(state, "Instalação pausada pelo usuário.");
                throw;
            }
            catch (Exception ex)
            {
                item.Status = QueueItemStatus.Failed;
                item.ErrorMessage = ex.Message;
            }

            await SaveSessionAsync(state);
            onProgress(state, $"{item.Title}: {item.StatusBadgeText}");
        }

        state.Status = state.Items.Where(x => x.IsSelected).All(x => x.Status == QueueItemStatus.Completed) ? "Completed" : "Paused";
        await SaveSessionAsync(state);
    }
}
