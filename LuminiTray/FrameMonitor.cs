using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Session;

namespace LuminiTray;

public sealed record FrameStats(int ProcessId, string ProcessName, double Fps, double FrameTimeMs, double Low1Fps,
                                IReadOnlyList<double> RecentFrameTimes);

/// <summary>
/// Mede o FPS de qualquer processo sem injetar código, como o PresentMon: escuta via ETW os eventos
/// "Present" que o Windows emite a cada frame entregue por DXGI (DirectX 10/11/12) e Direct3D 9.
/// Jogos Vulkan/OpenGL só aparecem se apresentarem via DXGI (comum em drivers recentes, não garantido).
/// </summary>
public sealed class FrameMonitor : IDisposable
{
    private const string SessionName = "LuminiTray-Frames";
    private static readonly Guid DxgiProvider = new("CA11C036-0102-4A2D-A6AD-F03CFED5D3C9");
    private static readonly Guid D3D9Provider = new("783ACA0A-790E-4D7F-8451-AA850511C6B9");
    private const int DxgiPresentStart = 42, D3D9PresentStart = 1;
    private const double HistoryMs = 10_000; // janela para o 1% low

    private TraceEventSession? _session;
    private Thread? _thread;
    private readonly object _lock = new();
    private IntPtr _nativeStore = IntPtr.Zero;
    private readonly Dictionary<int, Queue<double>> _presents = new(); // pid → timestamps (ms, relógio do ETW)
    private readonly Dictionary<int, long> _lastSeen = new();           // pid → Stopwatch do último evento recebido
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, (string Name, long Ticks)> PidNameCache = new();

    private static double HostNowMs() => Stopwatch.GetTimestamp() * 1000.0 / Stopwatch.Frequency;

    public bool Running => _thread?.IsAlive == true;
    public string? Error { get; private set; }

    public void Start()
    {
        if (Running) return;
        try
        {
            if (NativeCoreBridge.IsEnabled() && _nativeStore == IntPtr.Zero)
            {
                _nativeStore = NativeCoreBridge.TryCreateFrameStore();
            }

            // Opção Create derruba uma sessão antiga com o mesmo nome (ex.: app fechado à força).
            // BufferSizeMB = 1 minimiza a alocação de buffers não paginados e buffers do ETW.
            _session = new TraceEventSession(SessionName, TraceEventSessionOptions.Create)
            {
                StopOnDispose = true,
                BufferSizeMB = 1
            };
            _session.EnableProvider(DxgiProvider, TraceEventLevel.Informational, ulong.MaxValue, null);
            _session.EnableProvider(D3D9Provider, TraceEventLevel.Informational, ulong.MaxValue, null);
            _session.Source.AllEvents += OnEvent;
            _thread = new Thread(() =>
            {
                try { _session.Source.Process(); }
                catch (Exception ex) { Error = ex.Message; }
            }) { IsBackground = true, Name = "ETW frames" };
            _thread.Start();
            Error = null;
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            _session?.Dispose();
            _session = null;
            if (_nativeStore != IntPtr.Zero)
            {
                NativeCoreBridge.DestroyFrameStore(_nativeStore);
                _nativeStore = IntPtr.Zero;
            }
        }
    }

    /// <summary>Interrompe a sessão ETW e libera os buffers de memória do kernel e processo.</summary>
    public void Stop()
    {
        lock (_lock)
        {
            try { _session?.Source.StopProcessing(); } catch { }
            try { _session?.Dispose(); } catch { }
            _session = null;
            _thread = null;
            if (_nativeStore != IntPtr.Zero)
            {
                NativeCoreBridge.DestroyFrameStore(_nativeStore);
                _nativeStore = IntPtr.Zero;
            }
            _presents.Clear();
            _lastSeen.Clear();
        }
    }

    private void OnEvent(TraceEvent e)
    {
        int id = (int)e.ID;
        bool present = (e.ProviderGuid == DxgiProvider && id == DxgiPresentStart) ||
                       (e.ProviderGuid == D3D9Provider && id == D3D9PresentStart);
        if (!present) return;

        double t = e.TimeStampRelativeMSec;
        lock (_lock)
        {
            if (_nativeStore != IntPtr.Zero && NativeCoreBridge.IsEnabled() &&
                NativeCoreBridge.TryRecordFrame(_nativeStore, e.ProcessID, t, HostNowMs()))
            {
                return;
            }

            if (!_presents.TryGetValue(e.ProcessID, out var q)) _presents[e.ProcessID] = q = new Queue<double>();
            q.Enqueue(t);
            while (q.Count > 0 && q.Peek() < t - HistoryMs) q.Dequeue();
            _lastSeen[e.ProcessID] = Stopwatch.GetTimestamp();
        }
    }

    /// <summary>Estatísticas do processo, ou null se ele não apresentou frames nos últimos 2 s.</summary>
    public FrameStats? GetStats(int pid)
    {
        double[]? ts = null;
        double nativeFps = 0, nativeFtMs = 0, nativeLow1 = 0;
        double[]? nativeRecentBuf = null;
        int nativeRecentCount = 0;
        bool usedNative = false;

        lock (_lock)
        {
            if (_nativeStore != IntPtr.Zero && NativeCoreBridge.IsEnabled())
            {
                nativeRecentBuf = new double[120];
                if (NativeCoreBridge.TryQueryFrameStats(
                    _nativeStore,
                    pid,
                    HostNowMs(),
                    out bool hasStats,
                    out nativeFps,
                    out nativeFtMs,
                    out nativeLow1,
                    nativeRecentBuf,
                    out nativeRecentCount))
                {
                    if (!hasStats) return null;
                    usedNative = true;
                }
            }

            if (!usedNative)
            {
                if (!_presents.TryGetValue(pid, out var q) || q.Count < 3) return null;
                if (Stopwatch.GetElapsedTime(_lastSeen[pid]).TotalSeconds > 2) return null;
                ts = q.ToArray();
            }
        }

        if (usedNative && nativeRecentBuf != null)
        {
            var recent = new List<double>(nativeRecentCount);
            for (int i = 0; i < nativeRecentCount; i++)
                recent.Add(nativeRecentBuf[i]);
            return new FrameStats(pid, ProcessName(pid), nativeFps, nativeFtMs, nativeLow1, recent);
        }

        if (ts == null) return null;
        var (fps, ftMs, low1) = ComputeFrameMetrics(ts);
        int ftCount = Math.Max(0, ts.Length - 1);
        int recentTake = Math.Min(120, ftCount);
        var recentManaged = new List<double>(recentTake);
        for (int i = ts.Length - recentTake; i < ts.Length; i++)
            recentManaged.Add(ts[i] - ts[i - 1]);

        return new FrameStats(pid, ProcessName(pid), fps, ftMs, low1, recentManaged);
    }

    public static (double Fps, double FrameTimeMs, double Low1Fps) ComputeFrameMetrics(ReadOnlySpan<double> timestamps)
    {
        if (timestamps.Length < 3) return (0, 0, 0);

        if (NativeCoreBridge.IsEnabled() &&
            NativeCoreBridge.TryComputeFrameMetrics(timestamps, out double fps, out double ftMs, out double low1))
        {
            return (fps, ftMs, low1);
        }

        return ComputeFrameMetricsManaged(timestamps);
    }

    public static (double Fps, double FrameTimeMs, double Low1Fps) ComputeFrameMetricsManaged(ReadOnlySpan<double> timestamps)
    {
        if (timestamps.Length < 3) return (0, 0, 0);

        int n = timestamps.Length - 1;
        double[] frameTimes = System.Buffers.ArrayPool<double>.Shared.Rent(n);
        try
        {
            for (int i = 0; i < n; i++)
                frameTimes[i] = timestamps[i + 1] - timestamps[i];

            double last = timestamps[^1];
            int first = 0;
            double cutoff = last - 1000.0;
            for (int i = 0; i < timestamps.Length; i++)
            {
                if (timestamps[i] >= cutoff) { first = i; break; }
            }

            double span = last - timestamps[first];
            int frames = timestamps.Length - 1 - first;
            double fps = span > 0 ? frames * 1000.0 / span : 0;

            Array.Sort(frameTimes, 0, n);
            int p99Idx = Math.Min(n - 1, (int)Math.Floor(n * 0.99));
            double p99 = frameTimes[p99Idx];
            double low1 = p99 > 0 ? 1000.0 / p99 : 0;

            return (fps, fps > 0 ? 1000.0 / fps : 0, low1);
        }
        finally
        {
            System.Buffers.ArrayPool<double>.Shared.Return(frameTimes);
        }
    }

    /// <summary>Estatísticas do programa em primeiro plano (o jogo que está na tela).</summary>
    public FrameStats? GetForegroundStats()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return null;
        GetWindowThreadProcessId(hwnd, out uint pid);
        return pid == 0 || pid == Environment.ProcessId ? null : GetStats((int)pid);
    }

    private static string ProcessName(int pid)
    {
        if (PidNameCache.TryGetValue(pid, out var cached) && Stopwatch.GetElapsedTime(cached.Ticks).TotalSeconds < 15.0)
            return cached.Name;

        string name;
        try
        {
            using var proc = Process.GetProcessById(pid);
            name = proc.ProcessName;
        }
        catch
        {
            name = $"PID {pid}";
        }

        if (PidNameCache.Count > 128) PidNameCache.Clear();
        PidNameCache[pid] = (name, Stopwatch.GetTimestamp());
        return name;
    }

    public void Dispose()
    {
        Stop();
    }

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
}
