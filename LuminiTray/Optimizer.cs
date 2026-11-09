using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace LuminiTray;

/// <summary>Programa (agrupado pelo nome do executável) que pode ser fechado para liberar recursos.</summary>
public sealed class BackgroundApp : INotifyPropertyChanged
{
    public string ProcessName { get; init; } = "";
    public string Title { get; init; } = "";
    public string Detail { get; init; } = "";
    public long MemoryBytes { get; init; }
    public int Count { get; init; }
    public bool HasWindow { get; init; }
    public bool Suggested { get; init; }
    public string MemoryText => $"{MemoryBytes / 1048576.0:N0} MB";

    private bool _selected;
    public bool Selected
    {
        get => _selected;
        set { _selected = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Selected))); }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed record CleanupResult(long FreedBytes, int Trimmed, bool StandbyPurged, string? StandbyError);
public sealed record CloseResult(List<string> Closed, List<string> NotClosed);

/// <summary>Limpeza de memória e fechamento de programas em segundo plano.</summary>
public static class Optimizer
{
    /// <summary>Processos que nunca aparecem na lista (sistema, segurança, drivers, o próprio app).</summary>
    private static readonly HashSet<string> Protected = new(StringComparer.OrdinalIgnoreCase)
    {
        "System", "Idle", "Registry", "smss", "csrss", "wininit", "winlogon", "services", "lsass", "lsaiso", "svchost",
        "dwm", "explorer", "fontdrvhost", "sihost", "ctfmon", "taskhostw", "RuntimeBroker", "StartMenuExperienceHost",
        "ShellExperienceHost", "SearchHost", "SearchIndexer", "TextInputHost", "SecurityHealthService", "SecurityHealthSystray",
        "MsMpEng", "NisSrv", "SgrmBroker", "audiodg", "conhost", "dllhost", "spoolsv", "WmiPrvSE", "LockApp", "ApplicationFrameHost",
        "SystemSettings", "smartscreen", "MemCompression", "Secure System", "WUDFHost", "dasHost", "nvcontainer", "NVDisplay.Container",
        "atiesrxx", "atieclxx", "amdfendrsr", "RadeonSoftware", "igfxEM", "PawnIO", "LuminiTray", "cmd", "powershell", "WindowsTerminal",
        "OpenConsole", "devenv", "Code", "dotnet",
    };

    /// <summary>Processos de segundo plano seguros de fechar durante o jogo (pré-marcados).</summary>
    private static readonly Dictionary<string, string> SuggestedApps = new(StringComparer.OrdinalIgnoreCase)
    {
        ["OneDrive"] = "Sincronização do OneDrive (pode usar disco e rede)",
        ["ms-teams"] = "Microsoft Teams",
        ["Teams"] = "Microsoft Teams (clássico)",
        ["Skype"] = "Skype",
        ["PhoneExperienceHost"] = "Vincular ao Celular",
        ["YourPhone"] = "Vincular ao Celular",
        ["Widgets"] = "Widgets do Windows",
        ["WidgetService"] = "Serviço de Widgets",
        ["AdobeARM"] = "Atualizador da Adobe",
        ["CCXProcess"] = "Adobe Creative Cloud",
        ["Creative Cloud"] = "Adobe Creative Cloud",
        ["AdobeIPCBroker"] = "Adobe (comunicação em segundo plano)",
        ["GoogleDriveFS"] = "Google Drive",
        ["Dropbox"] = "Dropbox",
        ["Cortana"] = "Cortana",
        ["OfficeClickToRun"] = "Atualizador do Office",
        ["QuickShare"] = "Quick Share em segundo plano",
        ["CrossDeviceService"] = "Serviço Cross Device / Celular",
        ["GameBarPresenceWriter"] = "Gravador de presença Xbox Game Bar",
        ["MicrosoftEdgeUpdate"] = "Atualizador do Edge em segundo plano",
        ["GoogleUpdate"] = "Atualizador do Google em segundo plano",
        ["BraveUpdate"] = "Atualizador do Brave em segundo plano",
        ["SpotifyMigrator"] = "Migrador do Spotify",
        ["OriginWebHelperService"] = "Origin Helper Service",
    };

    public static List<BackgroundApp> ListApps()
    {
        int session = Process.GetCurrentProcess().SessionId;
        var groups = new Dictionary<string, List<Process>>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                if (p.SessionId != session || Protected.Contains(p.ProcessName)) continue;
                if (!groups.TryGetValue(p.ProcessName, out var list)) groups[p.ProcessName] = list = new List<Process>();
                list.Add(p);
            }
            catch { }
        }

        var result = new List<BackgroundApp>();
        foreach (var (name, procs) in groups)
        {
            bool hasWindow = procs.Any(p => SafeWindow(p) != IntPtr.Zero);
            bool suggested = SuggestedApps.ContainsKey(name);
            if (!hasWindow && !suggested) continue; // processos auxiliares sem janela ficam de fora

            string title = name, detail;
            try { title = procs[0].MainModule?.FileVersionInfo.FileDescription is { Length: > 0 } d ? d : name; } catch { }
            detail = suggested ? SuggestedApps[name] : hasWindow ? "Programa aberto" : "";
            if (procs.Count > 1) detail += $" · {procs.Count} processos";
            long mem = procs.Sum(p => { try { return p.WorkingSet64; } catch { return 0L; } });

            result.Add(new BackgroundApp
            {
                ProcessName = name, Title = title, Detail = detail, MemoryBytes = mem, Count = procs.Count,
                HasWindow = hasWindow, Suggested = suggested, Selected = suggested
            });
        }
        return result.OrderByDescending(a => a.Suggested).ThenByDescending(a => a.MemoryBytes).ToList();
    }

    private static IntPtr SafeWindow(Process p) { try { return p.MainWindowHandle; } catch { return IntPtr.Zero; } }

    /// <summary>
    /// Fecha os programas: com janela → pede para fechar (como clicar no X), sem forçar, para não perder nada
    /// não salvo; sem janela (segundo plano) → encerra.
    /// </summary>
    public static CloseResult CloseApps(IEnumerable<BackgroundApp> apps)
    {
        var closed = new List<string>();
        var notClosed = new List<string>();
        foreach (var app in apps)
        {
            var procs = Process.GetProcessesByName(app.ProcessName);
            foreach (var p in procs)
            {
                try
                {
                    if (SafeWindow(p) != IntPtr.Zero) p.CloseMainWindow();
                    else if (app.Suggested) p.Kill();
                }
                catch { }
            }
            // Dá até 4 s para fecharem sozinhos.
            var sw = Stopwatch.StartNew();
            while (sw.Elapsed.TotalSeconds < 4 && Process.GetProcessesByName(app.ProcessName).Length > 0) Thread.Sleep(200);
            if (Process.GetProcessesByName(app.ProcessName).Length == 0) closed.Add(app.Title);
            else notClosed.Add(app.Title);
        }
        return new CloseResult(closed, notClosed);
    }

    /// <summary>
    /// Encerra processos em segundo plano considerados desnecessários / bloatware,
    /// garantindo que nenhum processo crítico do sistema nem processos na lista de exclusão
    /// do usuário sejam fechados.
    /// </summary>
    public static CloseResult CloseUselessProcesses(IEnumerable<string>? userExclusions = null)
    {
        var exclusionSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (userExclusions != null)
        {
            foreach (var ex in userExclusions)
            {
                if (string.IsNullOrWhiteSpace(ex)) continue;
                string clean = ex.Trim();
                if (clean.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    clean = clean[..^4];
                exclusionSet.Add(clean);
            }
        }

        var toClose = new List<BackgroundApp>();
        var apps = ListApps();

        foreach (var app in apps)
        {
            // Ignora se estiver na lista protegida do sistema ou na lista de exclusão do usuário
            if (Protected.Contains(app.ProcessName) || exclusionSet.Contains(app.ProcessName))
                continue;

            // Se for um processo sugerido para fechar (bloatware/updater/serviço inútil)
            if (app.Suggested)
            {
                toClose.Add(app);
            }
        }

        if (toClose.Count == 0)
            return new CloseResult(new List<string>(), new List<string>());

        return CloseApps(toClose);
    }

    /// <summary>
    /// Limpa arquivos temporários antigos da pasta %TEMP% do usuário.
    /// </summary>
    public static (long FreedBytes, int FilesDeleted) CleanTempFiles()
    {
        long freed = 0;
        int count = 0;
        try
        {
            string tempDir = Path.GetTempPath();
            var dir = new DirectoryInfo(tempDir);
            if (!dir.Exists) return (0, 0);

            var now = DateTime.Now;
            foreach (var file in dir.GetFiles())
            {
                try
                {
                    // Apenas arquivos com mais de 6 horas para evitar conflito com apps em execução
                    if ((now - file.LastWriteTime).TotalHours >= 6)
                    {
                        long len = file.Length;
                        file.Delete();
                        freed += len;
                        count++;
                    }
                }
                catch { }
            }

            foreach (var sub in dir.GetDirectories())
            {
                try
                {
                    if ((now - sub.LastWriteTime).TotalHours >= 12 && sub.GetFiles().Length == 0 && sub.GetDirectories().Length == 0)
                    {
                        sub.Delete(true);
                    }
                }
                catch { }
            }
        }
        catch { }

        return (freed, count);
    }

    /// <summary>
    /// Esvazia a lista de espera (standby) da RAM, como o ISLC, e enxuga o working set dos processos.
    /// A lista de espera é cache que o Windows libera sozinho quando precisa; esvaziá-la antes de jogar
    /// ajuda quando a RAM está quase cheia e o jogo engasga ao carregar áreas novas.
    /// </summary>
    public static CleanupResult CleanMemory()
    {
        long before = AvailableBytes();
        int trimmed = 0;
        int self = Environment.ProcessId;
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                if (p.Id == self || p.Id <= 4) continue;
                if (EmptyWorkingSet(p.Handle)) trimmed++;
            }
            catch { }
        }

        bool purged = false;
        string? error = null;
        try
        {
            EnablePrivilege("SeProfileSingleProcessPrivilege");
            int command = 4; // MemoryPurgeStandbyList
            int status = NtSetSystemInformation(80 /* SystemMemoryListInformation */, ref command, sizeof(int));
            purged = status == 0;
            if (!purged) error = $"o Windows recusou (0x{status:X8}); é preciso rodar como administrador";
        }
        catch (Exception ex) { error = ex.Message; }

        Thread.Sleep(300);
        return new CleanupResult(Math.Max(0, AvailableBytes() - before), trimmed, purged, error);
    }

    public static (long Total, long Available) MemoryStatus()
    {
        var m = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        return GlobalMemoryStatusEx(ref m) ? ((long)m.ullTotalPhys, (long)m.ullAvailPhys) : (0, 0);
    }

    /// <summary>
    /// Retorna o uso atual de memória Swap (arquivo de paginação em uso e tamanho total) via NTDLL.
    /// Executa em nanossegundos com 0% de overhead para uso em overlays em tempo real.
    /// </summary>
    public static (long SwapUsedBytes, long SwapTotalBytes) GetSwapUsage()
    {
        int bufSize = 4096;
        IntPtr pBuf = Marshal.AllocHGlobal(bufSize);
        try
        {
            // SystemPagefileInformation = 18
            int status = NtQuerySystemInformation(18, pBuf, bufSize, out int retLen);
            if (status == 0)
            {
                long totalInUsePages = 0;
                long totalSizePages = 0;
                int offset = 0;

                while (true)
                {
                    IntPtr pEntry = IntPtr.Add(pBuf, offset);
                    var entry = Marshal.PtrToStructure<SYSTEM_PAGEFILE_INFORMATION>(pEntry);
                    totalInUsePages += entry.TotalInUse;
                    totalSizePages += entry.TotalSize;

                    if (entry.NextEntryOffset == 0)
                        break;
                    offset += (int)entry.NextEntryOffset;
                }

                return (totalInUsePages * 4096L, totalSizePages * 4096L);
            }
        }
        catch { }
        finally
        {
            Marshal.FreeHGlobal(pBuf);
        }

        // Fallback rápido via GlobalMemoryStatusEx se NtQuerySystemInformation falhar
        var m = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (GlobalMemoryStatusEx(ref m))
        {
            long commitInUse = (long)(m.ullTotalPageFile - m.ullAvailPageFile);
            long physInUse = (long)(m.ullTotalPhys - m.ullAvailPhys);
            long swapFallback = Math.Max(0, commitInUse - physInUse);
            long totalSwap = (long)Math.Max(0, m.ullTotalPageFile - m.ullTotalPhys);
            return (swapFallback, totalSwap);
        }

        return (0, 0);
    }

    private static long AvailableBytes() => MemoryStatus().Available;

    private static void EnablePrivilege(string name)
    {
        if (!OpenProcessToken(Process.GetCurrentProcess().Handle, 0x0020 | 0x0008, out var token)) // ADJUST | QUERY
            throw new Win32Exception();
        try
        {
            if (!LookupPrivilegeValue(null, name, out long luid)) throw new Win32Exception();
            var tp = new TOKEN_PRIVILEGES { PrivilegeCount = 1, Luid = luid, Attributes = 2 }; // SE_PRIVILEGE_ENABLED
            AdjustTokenPrivileges(token, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero);
        }
        finally { CloseHandle(token); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct UNICODE_STRING
    {
        public ushort Length;
        public ushort MaximumLength;
        public IntPtr Buffer;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_PAGEFILE_INFORMATION
    {
        public uint NextEntryOffset;
        public uint TotalSize;
        public uint TotalInUse;
        public uint PeakUsage;
        public UNICODE_STRING PageFileName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength, dwMemoryLoad;
        public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct TOKEN_PRIVILEGES { public uint PrivilegeCount; public long Luid; public uint Attributes; }

    [DllImport("psapi.dll")] private static extern bool EmptyWorkingSet(IntPtr hProcess);
    [DllImport("ntdll.dll")] private static extern int NtSetSystemInformation(int infoClass, ref int info, int length);
    [DllImport("ntdll.dll")] private static extern int NtQuerySystemInformation(int infoClass, IntPtr info, int length, out int returnLength);
    [DllImport("kernel32.dll")] private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX m);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr h);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool LookupPrivilegeValue(string? system, string name, out long luid);
    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool AdjustTokenPrivileges(IntPtr token, bool disableAll, ref TOKEN_PRIVILEGES state, uint len, IntPtr prev, IntPtr retLen);
}
