using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Windows;

namespace LuminiTray;

/// <summary>
/// Serviço de alto desempenho do Mini Gerenciador de Tarefas (adaptado do mod MicroManager/Windhawk).
/// Realiza leitura em nível de kernel de CPU, GPU, RAM e Disco, identifica os maiores consumidores (top consumers),
/// mantém histórico de 16 amostras para cada métrica e provê ações seguras de gerenciamento de processos.
/// </summary>
public sealed class MiniTaskManagerService : IDisposable
{
    private static readonly Lazy<MiniTaskManagerService> _lazyInstance =
        new(() => new MiniTaskManagerService());
    public static MiniTaskManagerService Instance => _lazyInstance.Value;

    public const int HistorySamples = 16;
    public const int MetricCpu = 0;
    public const int MetricGpu = 1;
    public const int MetricRam = 2;
    public const int MetricDisk = 3;
    public const int MetricCount = 4;

    public sealed class ProcessTargetInfo
    {
        public int Pid { get; set; }
        public long CreatedTime { get; set; }
        public string Name { get; set; } = string.Empty;
        public string ExecutablePath { get; set; } = string.Empty;
    }

    public sealed class MetricSnapshot
    {
        public int TotalCpu { get; set; } = -1;
        public int TopCpuPct { get; set; }
        public string TopCpuName { get; set; } = string.Empty;
        public ProcessTargetInfo TopCpuTarget { get; set; } = new();

        public int TotalGpu { get; set; } = -1;
        public int TopGpuPct { get; set; }
        public string TopGpuName { get; set; } = string.Empty;
        public ProcessTargetInfo TopGpuTarget { get; set; } = new();

        public int TotalRam { get; set; } = -1;
        public int TopRamPct { get; set; }
        public string TopRamName { get; set; } = string.Empty;
        public ProcessTargetInfo TopRamTarget { get; set; } = new();

        public double TotalDiskBps { get; set; }
        public string TotalDiskFormatted { get; set; } = "0 B/s";
        public double TopDiskBps { get; set; }
        public string TopDiskFormatted { get; set; } = string.Empty;
        public string TopDiskName { get; set; } = string.Empty;
        public ProcessTargetInfo TopDiskTarget { get; set; } = new();
        public int DiskActivityPercent { get; set; }

        public string SummaryTooltip { get; set; } = string.Empty;
    }

    public event Action<MetricSnapshot>? OnMetricsUpdated;

    private readonly object _lock = new();
    private Timer? _samplingTimer;
    private bool _isDisposed;
    private int _updateIntervalMs = 1000;

    // Histórico de 16 amostras para cada recurso
    private readonly int[][] _graphSamples = new int[MetricCount][];
    private readonly int[] _graphCount = new int[MetricCount];
    private readonly int[] _graphNext = new int[MetricCount];

    // Estados prévios para cálculo de deltas
    private bool _hasPrevTimes;
    private long _prevIdleTime;
    private long _prevKernelTime;
    private long _prevUserTime;
    private DateTime _prevSampleTime = DateTime.UtcNow;

    private struct ProcDeltaState
    {
        public int Pid;
        public long Created;
        public long CpuTime;
        public long DiskTransferBytes;
        public string Name;
    }
    private readonly Dictionary<int, ProcDeltaState> _prevProcesses = new(512);

    // Handles PDH para GPU
    private IntPtr _gpuQuery = IntPtr.Zero;
    private IntPtr _gpuCounter = IntPtr.Zero;
    private int _gpuRetryTicks;
    private const int GpuRetryTicksLimit = 20;

    // Memória física total em bytes
    private ulong _totalPhysBytes;

    public MetricSnapshot CurrentSnapshot { get; private set; } = new();

    public int UpdateIntervalMs
    {
        get => _updateIntervalMs;
        set
        {
            if (value < 200) value = 200;
            if (value > 5000) value = 5000;
            _updateIntervalMs = value;
            lock (_lock)
            {
                if (_isActive)
                {
                    _samplingTimer?.Change(0, _updateIntervalMs);
                }
            }
        }
    }

    private bool _isActive = false;
    public bool IsActive => _isActive;

    public void SetActive(bool active)
    {
        lock (_lock)
        {
            if (_isActive == active) return;
            _isActive = active;
            if (_isActive)
            {
                if (!_hasPrevTimes)
                {
                    // Duplo tick rápido inicial: leitura imediata da linha de base e segundo tick em 80ms para deltas sem espera perceptível
                    _samplingTimer?.Change(0, 80);
                }
                else
                {
                    _samplingTimer?.Change(0, _updateIntervalMs);
                }
            }
            else
            {
                _hasPrevTimes = false;
                _prevProcesses.Clear();
                _samplingTimer?.Change(Timeout.Infinite, Timeout.Infinite);
            }
        }
    }

    private MiniTaskManagerService()
    {
        for (int i = 0; i < MetricCount; i++)
        {
            _graphSamples[i] = new int[HistorySamples];
        }

        // Leitura de memória física instalada
        try
        {
            var mem = new MEMORYSTATUSEX();
            if (GlobalMemoryStatusEx(mem))
            {
                _totalPhysBytes = mem.ullTotalPhys;
            }
        }
        catch { }

        // Inicialização em repouso: só amostra quando a janela estiver ativa ou flyout for exibido
        _samplingTimer = new Timer(_ => CollectStats(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public int[] GetHistory(int metric)
    {
        lock (_lock)
        {
            if (metric < 0 || metric >= MetricCount) return Array.Empty<int>();
            int count = _graphCount[metric];
            int next = _graphNext[metric];
            int[] result = new int[count];
            for (int i = 0; i < count; i++)
            {
                result[i] = _graphSamples[metric][(next + HistorySamples - count + i) % HistorySamples];
            }
            return result;
        }
    }

    private void AppendSample(int metric, int value)
    {
        if (metric < 0 || metric >= MetricCount) return;
        _graphSamples[metric][_graphNext[metric]] = value;
        _graphNext[metric] = (_graphNext[metric] + 1) % HistorySamples;
        if (_graphCount[metric] < HistorySamples) _graphCount[metric]++;
    }

    private void CollectStats()
    {
        if (_isDisposed || !_isActive) return;

        try
        {
            DateTime now = DateTime.UtcNow;
            double elapsedSec = (now - _prevSampleTime).TotalSeconds;
            if (elapsedSec <= 0.05) elapsedSec = 0.05;
            _prevSampleTime = now;

            // 1. Leitura de RAM global
            int totalRamPct = -1;
            var mem = new MEMORYSTATUSEX();
            if (GlobalMemoryStatusEx(mem))
            {
                totalRamPct = (int)mem.dwMemoryLoad;
                if (_totalPhysBytes == 0) _totalPhysBytes = mem.ullTotalPhys;
            }

            // 2. Leitura de Tempos Globais do Sistema (GetSystemTimes)
            int totalCpuPct = -1;
            bool timesValid = GetSystemTimes(out var curIdleFt, out var curKernelFt, out var curUserFt);
            long curIdle = FileTimeToLong(curIdleFt);
            long curKernel = FileTimeToLong(curKernelFt);
            long curUser = FileTimeToLong(curUserFt);

            long totalDeltaTime = 0;
            if (timesValid && _hasPrevTimes)
            {
                long kernelDelta = curKernel - _prevKernelTime;
                long userDelta = curUser - _prevUserTime;
                long idleDelta = curIdle - _prevIdleTime;
                totalDeltaTime = kernelDelta + userDelta;

                if (totalDeltaTime > 0 && idleDelta <= totalDeltaTime)
                {
                    totalCpuPct = (int)Math.Round(100.0 - (100.0 * idleDelta / totalDeltaTime));
                    if (totalCpuPct < 0) totalCpuPct = 0;
                    if (totalCpuPct > 100) totalCpuPct = 100;
                }
            }

            if (timesValid)
            {
                _prevIdleTime = curIdle;
                _prevKernelTime = curKernel;
                _prevUserTime = curUser;
                _hasPrevTimes = true;
            }

            // 3. Varredura Atômica via NtQuerySystemInformation
            var curProcList = new List<ProcDeltaState>(300);
            long bestCpuTimeDelta = 0;
            string bestCpuName = string.Empty;
            ProcessTargetInfo bestCpuTarget = new();

            long bestRamWorkingSet = 0;
            string bestRamName = string.Empty;
            ProcessTargetInfo bestRamTarget = new();

            double totalDiskBps = 0;
            double bestDiskBps = 0;
            string bestDiskName = string.Empty;
            ProcessTargetInfo bestDiskTarget = new();

            CollectProcessInformation(
                elapsedSec,
                curProcList,
                ref bestCpuTimeDelta,
                ref bestCpuName,
                ref bestCpuTarget,
                ref bestRamWorkingSet,
                ref bestRamName,
                ref bestRamTarget,
                ref totalDiskBps,
                ref bestDiskBps,
                ref bestDiskName,
                ref bestDiskTarget);

            // Atualiza cache de processos para próximo delta
            lock (_lock)
            {
                _prevProcesses.Clear();
                foreach (var p in curProcList)
                {
                    _prevProcesses[p.Pid] = p;
                }
            }

            // Cálculo dos Top Consumers de CPU e RAM
            int topCpuPct = 0;
            if (bestCpuTimeDelta > 0 && totalDeltaTime > 0)
            {
                double pct = 100.0 * (double)bestCpuTimeDelta / totalDeltaTime;
                if (pct >= 0.5)
                {
                    topCpuPct = (int)Math.Round(pct);
                }
            }

            int topRamPct = 0;
            if (bestRamWorkingSet > 0 && _totalPhysBytes > 0)
            {
                int pct = (int)Math.Round(100.0 * (double)bestRamWorkingSet / (double)_totalPhysBytes);
                topRamPct = pct < 1 ? 1 : pct;
            }

            // 4. Coleta de GPU (PDH Engine Counters)
            int totalGpuPct = -1;
            int topGpuPct = 0;
            string topGpuName = string.Empty;
            ProcessTargetInfo topGpuTarget = new();
            CollectGpuStats(ref totalGpuPct, ref topGpuPct, ref topGpuName, ref topGpuTarget);

            // Formatação de Disco
            string totalDiskFormatted = FormatDataRate(totalDiskBps);
            string topDiskFormatted = FormatDataRate(bestDiskBps);
            int diskActivityPct = Math.Min(100, (int)Math.Round(totalDiskBps / (50.0 * 1024.0 * 1024.0) * 100.0));
            if (totalDiskBps > 0 && diskActivityPct < 1) diskActivityPct = 1;

            // Histórico de 16 amostras
            lock (_lock)
            {
                AppendSample(MetricCpu, totalCpuPct >= 0 ? totalCpuPct : 0);
                AppendSample(MetricGpu, totalGpuPct >= 0 ? totalGpuPct : 0);
                AppendSample(MetricRam, totalRamPct >= 0 ? totalRamPct : 0);
                AppendSample(MetricDisk, diskActivityPct);
            }

            // Monta Snapshot
            string cpuStr = totalCpuPct < 0 ? "--" : $"{totalCpuPct}%";
            string gpuStr = totalGpuPct < 0 ? "--" : $"{totalGpuPct}%";
            string ramStr = totalRamPct < 0 ? "--" : $"{totalRamPct}%";
            string tip = $"CPU: {cpuStr} | GPU: {gpuStr} | RAM: {ramStr} | Disco: {totalDiskFormatted}";

            var snap = new MetricSnapshot
            {
                TotalCpu = totalCpuPct,
                TopCpuPct = topCpuPct,
                TopCpuName = bestCpuName,
                TopCpuTarget = bestCpuTarget,

                TotalGpu = totalGpuPct,
                TopGpuPct = topGpuPct,
                TopGpuName = topGpuName,
                TopGpuTarget = topGpuTarget,

                TotalRam = totalRamPct,
                TopRamPct = topRamPct,
                TopRamName = bestRamName,
                TopRamTarget = bestRamTarget,

                TotalDiskBps = totalDiskBps,
                TotalDiskFormatted = totalDiskFormatted,
                TopDiskBps = bestDiskBps,
                TopDiskFormatted = topDiskFormatted,
                TopDiskName = bestDiskName,
                TopDiskTarget = bestDiskTarget,
                DiskActivityPercent = diskActivityPct,

                SummaryTooltip = tip
            };

            CurrentSnapshot = snap;
            OnMetricsUpdated?.Invoke(snap);

            // Se completamos a leitura válida inicial com cálculo de deltas, restaura o intervalo de amostragem padrão
            if (_isActive && totalCpuPct >= 0)
            {
                lock (_lock)
                {
                    _samplingTimer?.Change(_updateIntervalMs, _updateIntervalMs);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MiniTaskManager] Erro na coleta: {ex.Message}");
        }
    }

    private void CollectProcessInformation(
        double elapsedSec,
        List<ProcDeltaState> outProcList,
        ref long bestCpuTimeDelta,
        ref string bestCpuName,
        ref ProcessTargetInfo bestCpuTarget,
        ref long bestRamWorkingSet,
        ref string bestRamName,
        ref ProcessTargetInfo bestRamTarget,
        ref double totalDiskBps,
        ref double bestDiskBps,
        ref string bestDiskName,
        ref ProcessTargetInfo bestDiskTarget)
    {
        int bufSize = 1024 * 1024 * 2; // 2 MB
        IntPtr buffer = Marshal.AllocHGlobal(bufSize);
        try
        {
            int status = NtQuerySystemInformation(5, buffer, bufSize, out int needed);
            if (status != 0)
            {
                Marshal.FreeHGlobal(buffer);
                bufSize = Math.Max(bufSize * 2, needed + 65536);
                buffer = Marshal.AllocHGlobal(bufSize);
                status = NtQuerySystemInformation(5, buffer, bufSize, out _);
                if (status != 0) return;
            }

            IntPtr current = buffer;
            while (current != IntPtr.Zero)
            {
                int nextOffset = Marshal.ReadInt32(current, 0);
                long createTime = Marshal.ReadInt64(current, 32);
                long userTime = Marshal.ReadInt64(current, 40);
                long kernelTime = Marshal.ReadInt64(current, 48);
                ushort nameLen = (ushort)Marshal.ReadInt16(current, 56);
                IntPtr nameBuffer = Marshal.ReadIntPtr(current, 64);
                IntPtr pidPtr = Marshal.ReadIntPtr(current, 80);
                int pid = pidPtr.ToInt32();
                long workingSet = Marshal.ReadIntPtr(current, 144).ToInt64();
                long readTransfer = Marshal.ReadInt64(current, 232);
                long writeTransfer = Marshal.ReadInt64(current, 240);
                long curDiskTransfer = readTransfer + writeTransfer;
                long curCpuTime = kernelTime + userTime;

                string procName;
                if (nameBuffer != IntPtr.Zero && nameLen > 0)
                {
                    procName = Marshal.PtrToStringUni(nameBuffer, nameLen / 2) ?? $"PID {pid}";
                }
                else if (pid == 0) procName = "System Idle Process";
                else if (pid == 4) procName = "System";
                else procName = $"PID {pid}";

                outProcList.Add(new ProcDeltaState
                {
                    Pid = pid,
                    Created = createTime,
                    CpuTime = curCpuTime,
                    DiskTransferBytes = curDiskTransfer,
                    Name = procName
                });

                // Avaliação de Top CPU
                if (_prevProcesses.TryGetValue(pid, out var prev) && prev.Created == createTime && pid != 0)
                {
                    long deltaCpu = curCpuTime >= prev.CpuTime ? (curCpuTime - prev.CpuTime) : 0;
                    if (deltaCpu > bestCpuTimeDelta)
                    {
                        bestCpuTimeDelta = deltaCpu;
                        bestCpuName = procName;
                        bestCpuTarget = new ProcessTargetInfo { Pid = pid, CreatedTime = createTime, Name = procName };
                    }

                    // Avaliação de Disco
                    if (curDiskTransfer >= prev.DiskTransferBytes && elapsedSec > 0)
                    {
                        double deltaDisk = (curDiskTransfer - prev.DiskTransferBytes) / elapsedSec;
                        totalDiskBps += deltaDisk;
                        if (deltaDisk > bestDiskBps && pid != 0)
                        {
                            bestDiskBps = deltaDisk;
                            bestDiskName = procName;
                            bestDiskTarget = new ProcessTargetInfo { Pid = pid, CreatedTime = createTime, Name = procName };
                        }
                    }
                }

                // Avaliação de Top RAM (WorkingSet)
                if (pid != 0 && workingSet > bestRamWorkingSet)
                {
                    bestRamWorkingSet = workingSet;
                    bestRamName = procName;
                    bestRamTarget = new ProcessTargetInfo { Pid = pid, CreatedTime = createTime, Name = procName };
                }

                if (nextOffset == 0) break;
                current = IntPtr.Add(current, nextOffset);
            }
        }
        finally
        {
            if (buffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
    }

    private void CollectGpuStats(
        ref int outTotalGpu,
        ref int outTopGpuPct,
        ref string outTopGpuName,
        ref ProcessTargetInfo outTopGpuTarget)
    {
        outTotalGpu = -1;
        outTopGpuPct = 0;
        outTopGpuName = string.Empty;

        if (_gpuQuery == IntPtr.Zero)
        {
            if (_gpuRetryTicks > 0)
            {
                _gpuRetryTicks--;
                return;
            }
            InitGpuQuery();
            if (_gpuQuery == IntPtr.Zero) return;
        }

        try
        {
            int ps = PdhCollectQueryData(_gpuQuery);
            if (ps != 0) return;

            uint bufSize = 0;
            uint itemCount = 0;
            ps = PdhGetFormattedCounterArrayW(_gpuCounter, 0x00000200 /* PDH_FMT_DOUBLE */, ref bufSize, ref itemCount, IntPtr.Zero);
            if (ps != unchecked((int)0x800007D2) || bufSize == 0 || itemCount == 0) return;

            IntPtr buffer = Marshal.AllocHGlobal((int)bufSize);
            try
            {
                ps = PdhGetFormattedCounterArrayW(_gpuCounter, 0x00000200, ref bufSize, ref itemCount, buffer);
                if (ps != 0) return;

                int itemSize = 24; // 8 bytes ptr + 4 bytes status + 4 bytes padding + 8 bytes double
                double grandTotal = 0;
                var gpuByPid = new Dictionary<int, double>(128);

                for (int i = 0; i < (int)itemCount; i++)
                {
                    IntPtr itemPtr = IntPtr.Add(buffer, i * itemSize);
                    IntPtr namePtr = Marshal.ReadIntPtr(itemPtr, 0);
                    uint cstatus = (uint)Marshal.ReadInt32(itemPtr, 8);
                    double val = BitConverter.Int64BitsToDouble(Marshal.ReadInt64(itemPtr, 16));

                    if (cstatus != 0 || val <= 0) continue;
                    grandTotal += val;

                    string? name = Marshal.PtrToStringUni(namePtr);
                    if (string.IsNullOrEmpty(name) || !name.StartsWith("pid_")) continue;

                    // Extrai PID do formato: "pid_1234_luid_0x00000000_phys_0_eng_0_enum_1"
                    int endIdx = name.IndexOf('_', 4);
                    string pidStr = endIdx > 4 ? name.Substring(4, endIdx - 4) : name.Substring(4);
                    if (int.TryParse(pidStr, out int pid))
                    {
                        if (gpuByPid.TryGetValue(pid, out double curVal))
                            gpuByPid[pid] = curVal + val;
                        else
                            gpuByPid[pid] = val;
                    }
                }

                outTotalGpu = (int)Math.Min(100.0, Math.Round(grandTotal));

                // Acha o maior consumidor de GPU
                int bestPid = 0;
                double bestVal = 0;
                foreach (var kvp in gpuByPid)
                {
                    if (kvp.Value > bestVal)
                    {
                        bestVal = kvp.Value;
                        bestPid = kvp.Key;
                    }
                }

                if (bestPid > 0 && bestVal >= 0.5)
                {
                    outTopGpuPct = (int)Math.Min(100.0, Math.Round(bestVal));
                    lock (_lock)
                    {
                        if (_prevProcesses.TryGetValue(bestPid, out var p))
                        {
                            outTopGpuName = p.Name;
                            outTopGpuTarget = new ProcessTargetInfo { Pid = bestPid, CreatedTime = p.Created, Name = p.Name };
                        }
                        else
                        {
                            outTopGpuName = $"PID {bestPid}";
                            outTopGpuTarget = new ProcessTargetInfo { Pid = bestPid, Name = outTopGpuName };
                        }
                    }
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        catch
        {
            _gpuRetryTicks = GpuRetryTicksLimit;
        }
    }

    private void InitGpuQuery()
    {
        try
        {
            if (PdhOpenQueryW(null, IntPtr.Zero, out _gpuQuery) != 0)
            {
                _gpuQuery = IntPtr.Zero;
                _gpuRetryTicks = GpuRetryTicksLimit;
                return;
            }

            if (PdhAddEnglishCounterW(_gpuQuery, @"\GPU Engine(*)\Utilization Percentage", IntPtr.Zero, out _gpuCounter) != 0)
            {
                PdhCloseQuery(_gpuQuery);
                _gpuQuery = IntPtr.Zero;
                _gpuCounter = IntPtr.Zero;
                _gpuRetryTicks = GpuRetryTicksLimit;
                return;
            }

            // Coleta inicial de calibração
            PdhCollectQueryData(_gpuQuery);
        }
        catch
        {
            _gpuQuery = IntPtr.Zero;
            _gpuCounter = IntPtr.Zero;
            _gpuRetryTicks = GpuRetryTicksLimit;
        }
    }

    public static string FormatDataRate(double bytesPerSec)
    {
        if (bytesPerSec < 1024) return $"{bytesPerSec:0} B/s";
        if (bytesPerSec < 1024 * 1024) return $"{bytesPerSec / 1024.0:0.0} KB/s";
        if (bytesPerSec < 1024 * 1024 * 1024) return $"{bytesPerSec / (1024.0 * 1024.0):0.0} MB/s";
        return $"{bytesPerSec / (1024.0 * 1024.0 * 1024.0):0.00} GB/s";
    }

    #region Ações de Processo (Finalizar, Árvore, Abrir Local)

    public bool EndTask(ProcessTargetInfo target, Window? owner = null)
    {
        if (target == null || target.Pid <= 4 || target.Pid == Environment.ProcessId)
        {
            MessageBox.Show(owner ?? Application.Current.MainWindow,
                "Não é permitido finalizar processos críticos do sistema ou o próprio Lumini Tray.",
                "Ação Bloqueada", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        var result = MessageBox.Show(owner ?? Application.Current.MainWindow,
            $"Deseja realmente finalizar o processo '{target.Name}' (PID {target.Pid})?\n\nQualquer trabalho não salvo poderá ser perdido.",
            "Confirmar Finalização de Tarefa", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);

        if (result != MessageBoxResult.Yes) return false;

        try
        {
            using var proc = Process.GetProcessById(target.Pid);
            proc.Kill();
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(owner ?? Application.Current.MainWindow,
                $"Não foi possível encerrar o processo '{target.Name}':\n{ex.Message}",
                "Erro ao Finalizar Processo", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    public bool EndProcessTree(ProcessTargetInfo target, Window? owner = null)
    {
        if (target == null || target.Pid <= 4 || target.Pid == Environment.ProcessId)
        {
            MessageBox.Show(owner ?? Application.Current.MainWindow,
                "Não é permitido finalizar processos críticos do sistema ou o próprio Lumini Tray.",
                "Ação Bloqueada", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        if (target.Name.Equals("explorer.exe", StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(owner ?? Application.Current.MainWindow,
                "Encerrar a árvore de processos do Windows Explorer não é recomendado pois fechará o shell e a barra de tarefas do Windows. Utilize 'Finalizar tarefa' individualmente.",
                "Proteção do Sistema", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        // 1. Coleta todos os processos e monta árvore
        var allProcs = GetAllProcessesSnapshot();
        var targetItem = allProcs.Find(p => p.Pid == target.Pid);
        if (targetItem == null)
        {
            MessageBox.Show(owner ?? Application.Current.MainWindow,
                "O processo selecionado já foi encerrado ou seu identificador não está mais acessível.",
                "Processo Inacessível", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }

        // 2. Sobe a cadeia até encontrar a raiz com mesmo nome
        var root = targetItem;
        bool movedUp = true;
        while (movedUp)
        {
            movedUp = false;
            foreach (var p in allProcs)
            {
                if (p.Pid == root.ParentPid && p.Pid != root.Pid &&
                    p.Name.Equals(root.Name, StringComparison.OrdinalIgnoreCase) &&
                    p.CreatedTime < root.CreatedTime &&
                    CanSafelyTerminate(p.Pid, p.Name))
                {
                    root = p;
                    movedUp = true;
                    break;
                }
            }
        }

        // 3. Reúne todos os descendentes recursivamente
        var tree = new List<ProcessInfoItem> { root };
        int idx = 0;
        while (idx < tree.Count)
        {
            var parent = tree[idx++];
            foreach (var child in allProcs)
            {
                if (child.ParentPid == parent.Pid && child.Pid != parent.Pid &&
                    child.CreatedTime > parent.CreatedTime &&
                    CanSafelyTerminate(child.Pid, child.Name) &&
                    !tree.Exists(e => e.Pid == child.Pid))
                {
                    tree.Add(child);
                }
            }
        }

        // 4. Confirmação com contagem exata
        string prompt = tree.Count <= 1
            ? $"Deseja encerrar '{root.Name}' (PID {root.Pid})?\n\nQualquer trabalho não salvo poderá ser perdido."
            : $"Deseja encerrar '{root.Name}' (PID {root.Pid}) e mais {tree.Count - 1} processo(s) filho(s) relacionados?\n\nQualquer trabalho não salvo poderá ser perdido.";

        var confirm = MessageBox.Show(owner ?? Application.Current.MainWindow,
            prompt, "Confirmar Finalização da Árvore de Processos",
            MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);

        if (confirm != MessageBoxResult.Yes) return false;

        // 5. Encerramento de baixo para cima (filhos primeiro, raiz por último)
        for (int i = tree.Count - 1; i >= 0; i--)
        {
            try
            {
                using var p = Process.GetProcessById(tree[i].Pid);
                p.Kill();
            }
            catch { }
        }

        return true;
    }

    public bool OpenFileLocation(ProcessTargetInfo target, Window? owner = null)
    {
        if (target == null || target.Pid <= 0) return false;

        string? path = target.ExecutablePath;
        if (string.IsNullOrEmpty(path))
        {
            path = GetExecutablePath(target.Pid);
        }

        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            MessageBox.Show(owner ?? Application.Current.MainWindow,
                $"Não foi possível localizar o executável do processo '{target.Name}'.",
                "Arquivo Não Encontrado", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }

        try
        {
            Process.Start("explorer.exe", $"/select,\"{path}\"");
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(owner ?? Application.Current.MainWindow,
                $"Erro ao abrir pasta: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    private static bool CanSafelyTerminate(int pid, string name)
    {
        if (pid <= 4 || pid == Environment.ProcessId) return false;
        if (name.Equals("explorer.exe", StringComparison.OrdinalIgnoreCase)) return false;
        if (name.Equals("csrss.exe", StringComparison.OrdinalIgnoreCase)) return false;
        if (name.Equals("wininit.exe", StringComparison.OrdinalIgnoreCase)) return false;
        if (name.Equals("services.exe", StringComparison.OrdinalIgnoreCase)) return false;
        if (name.Equals("lsass.exe", StringComparison.OrdinalIgnoreCase)) return false;
        if (name.Equals("smss.exe", StringComparison.OrdinalIgnoreCase)) return false;
        return true;
    }

    private sealed class ProcessInfoItem
    {
        public int Pid { get; set; }
        public int ParentPid { get; set; }
        public long CreatedTime { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    private List<ProcessInfoItem> GetAllProcessesSnapshot()
    {
        var list = new List<ProcessInfoItem>(300);
        int bufSize = 1024 * 1024 * 2;
        IntPtr buffer = Marshal.AllocHGlobal(bufSize);
        try
        {
            if (NtQuerySystemInformation(5, buffer, bufSize, out _) == 0)
            {
                IntPtr cur = buffer;
                while (cur != IntPtr.Zero)
                {
                    int next = Marshal.ReadInt32(cur, 0);
                    long created = Marshal.ReadInt64(cur, 32);
                    ushort nameLen = (ushort)Marshal.ReadInt16(cur, 56);
                    IntPtr nameBuf = Marshal.ReadIntPtr(cur, 64);
                    int pid = Marshal.ReadIntPtr(cur, 80).ToInt32();
                    int parentPid = Marshal.ReadIntPtr(cur, 88).ToInt32();

                    string name;
                    if (nameBuf != IntPtr.Zero && nameLen > 0)
                        name = Marshal.PtrToStringUni(nameBuf, nameLen / 2) ?? $"PID {pid}";
                    else if (pid == 0) name = "System Idle Process";
                    else if (pid == 4) name = "System";
                    else name = $"PID {pid}";

                    list.Add(new ProcessInfoItem
                    {
                        Pid = pid,
                        ParentPid = parentPid,
                        CreatedTime = created,
                        Name = name
                    });

                    if (next == 0) break;
                    cur = IntPtr.Add(cur, next);
                }
            }
        }
        catch { }
        finally
        {
            if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer);
        }
        return list;
    }

    public static string? GetExecutablePath(int pid)
    {
        if (pid <= 4) return null;
        try
        {
            IntPtr hProcess = OpenProcess(0x1000 /* PROCESS_QUERY_LIMITED_INFORMATION */, false, pid);
            if (hProcess != IntPtr.Zero)
            {
                try
                {
                    var sb = new StringBuilder(1024);
                    int size = sb.Capacity;
                    if (QueryFullProcessImageName(hProcess, 0, sb, ref size))
                    {
                        return sb.ToString();
                    }
                }
                finally
                {
                    CloseHandle(hProcess);
                }
            }
        }
        catch { }

        try
        {
            using var p = Process.GetProcessById(pid);
            return p.MainModule?.FileName;
        }
        catch { }

        return null;
    }

    #endregion

    private static long FileTimeToLong(System.Runtime.InteropServices.ComTypes.FILETIME ft)
    {
        return ((long)ft.dwHighDateTime << 32) | (uint)ft.dwLowDateTime;
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        _samplingTimer?.Dispose();
        _samplingTimer = null;

        if (_gpuQuery != IntPtr.Zero)
        {
            try { PdhCloseQuery(_gpuQuery); } catch { }
            _gpuQuery = IntPtr.Zero;
            _gpuCounter = IntPtr.Zero;
        }
    }

    #region P/Invoke

    [DllImport("ntdll.dll")]
    private static extern int NtQuerySystemInformation(
        int systemInformationClass,
        IntPtr systemInformation,
        int systemInformationLength,
        out int returnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(
        out System.Runtime.InteropServices.ComTypes.FILETIME lpIdleTime,
        out System.Runtime.InteropServices.ComTypes.FILETIME lpKernelTime,
        out System.Runtime.InteropServices.ComTypes.FILETIME lpUserTime);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private class MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;

        public MEMORYSTATUSEX()
        {
            dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(int processAccess, bool bInheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageName(IntPtr hProcess, int flags, StringBuilder text, ref int size);

    [DllImport("pdh.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern int PdhOpenQueryW(string? szDataSource, IntPtr dwUserData, out IntPtr phQuery);

    [DllImport("pdh.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern int PdhAddEnglishCounterW(IntPtr hQuery, string szFullCounterPath, IntPtr dwUserData, out IntPtr phCounter);

    [DllImport("pdh.dll", SetLastError = true)]
    private static extern int PdhCollectQueryData(IntPtr hQuery);

    [DllImport("pdh.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern int PdhGetFormattedCounterArrayW(
        IntPtr hCounter,
        uint dwFormat,
        ref uint lpdwBufferSize,
        ref uint lpdwItemCount,
        IntPtr itemBuffer);

    [DllImport("pdh.dll", SetLastError = true)]
    private static extern int PdhCloseQuery(IntPtr hQuery);

    #endregion
}
