using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace LuminiTray;

public sealed class NativeDiskFileEntry
{
    public string Name { get; set; } = "";
    public long Size { get; set; }
}

public sealed class NativeDiskDirNode
{
    public string Name { get; set; } = "";
    public long TotalSize { get; set; }
    public int FileCount { get; set; }
    public int FolderCount { get; set; }
    public List<NativeDiskFileEntry> DirectFiles { get; } = new();
    public List<NativeDiskDirNode> SubDirs { get; } = new();
}

public sealed class NativeDiskScanPayload
{
    public NativeDiskDirNode Root { get; set; } = null!;
    public int AccessDeniedCount { get; set; }
    public int ReparseCount { get; set; }
    public List<ExtensionStat> Extensions { get; } = new();
}

[StructLayout(LayoutKind.Sequential)]
public struct NativeDspParams
{
    public float SuppressionIntensity;
    public float GainDb;
    public float GateThresholdDb;
    public float GateReleaseMs;
    public float GateHysteresisDb;
    public float GateHoldMs;
    public float VadThreshold;
    public float VadHoldMs;
    public float LowEqDb;
    public float MidEqDb;
    public float HighEqDb;
    public byte LimiterEnabled;
    public byte DeesserEnabled;
}

public interface IDiskScannerBackend
{
    Task<DiskScanResult> ScanAsync(string rootPath, Action<string, int>? onProgress, CancellationToken ct);
}

public interface IFrameMonitorBackend : IDisposable
{
    bool Running { get; }
    string? Error { get; }
    void Start();
    void Stop();
    FrameStats? GetStats(int pid);
    FrameStats? GetForegroundStats();
}

public interface IVoiceDspBackend : IDisposable
{
    bool ProcessFrame(Span<float> frame480);
}

/// <summary>
/// Ponte FFI segura para a DLL nativa em Rust (LuminiTray.NativeCore.dll),
/// com validação de ABI, carregamento sob demanda (arquivo local ou recurso embutido),
/// proteções anti-crash e fallback transparente para C#.
/// </summary>
public static class NativeCoreBridge
{
    public const uint ExpectedAbiVersion = 2;
    private const string DllName = "LuminiTray.NativeCore.dll";
    private const uint DiskScanMagic = 0x4C544453; // "LTDS"

    public static bool IsAvailable { get; }
    public static uint LoadedAbiVersion { get; }
    public static string? FailureReason { get; }
    public static bool ForceManagedOverride { get; set; }

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    private static extern uint nc_abi_version();

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    private static extern int nc_ping(int value);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    private static extern int nc_alloc_buffer(nuint size, out IntPtr outPtr);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    private static extern int nc_free_buffer(IntPtr ptr, nuint size);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int nc_last_error([Out] char[] bufUtf16, nuint capChars);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    private static extern unsafe int nc_disk_scan_utf16(
        char* pathPtr,
        nuint pathLen,
        nuint maxFilesPerDir,
        IntPtr cancelPtr,
        IntPtr progressCb,
        out IntPtr outBuf,
        out nuint outLen);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    private static extern IntPtr nc_frames_create();

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    private static extern int nc_frames_destroy(IntPtr ptr);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    private static extern int nc_frames_clear(IntPtr ptr);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    private static extern int nc_frames_record(IntPtr ptr, int pid, double timestampMs, double hostNowMs);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    private static extern int nc_frames_query(
        IntPtr ptr,
        int pid,
        double hostNowMs,
        out double outFps,
        out double outFtMs,
        out double outLow1,
        [Out] double[] outRecent,
        nuint maxRecent,
        out nuint outRecentLen);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    private static extern unsafe int nc_frames_compute_metrics(
        double* tsPtr,
        nuint count,
        out double outFps,
        out double outFtMs,
        out double outLow1);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    private static extern IntPtr nc_dsp_create();

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    private static extern int nc_dsp_destroy(IntPtr ptr);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    private static extern float nc_dsp_soft_limit(float x);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    private static extern int nc_dsp_pre_process(
        IntPtr ptr,
        ref float samples480,
        [Out] float[] rnnIn480,
        ref NativeDspParams p,
        out float outInPeakDb,
        out float outInRmsDb);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    private static extern int nc_dsp_post_rnnoise(
        IntPtr ptr,
        ref float samples480,
        [In] float[] rnnOut480,
        float vadProb,
        ref NativeDspParams p);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    private static extern int nc_dsp_fallback_process(
        IntPtr ptr,
        ref float samples480,
        float inPeakDb,
        float inRmsDb,
        ref NativeDspParams p,
        out float outVadProb);

    static NativeCoreBridge()
    {
        try
        {
            NativeLibrary.SetDllImportResolver(typeof(NativeCoreBridge).Assembly, ResolveDll);
        }
        catch { }

        try
        {
            uint ver = nc_abi_version();
            LoadedAbiVersion = ver;
            if (ver == ExpectedAbiVersion)
            {
                IsAvailable = true;
            }
            else
            {
                IsAvailable = false;
                FailureReason = $"Versão ABI incompatível: esperado {ExpectedAbiVersion}, obtido {ver}.";
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException)
        {
            IsAvailable = false;
            FailureReason = ex.Message;
        }
        catch (Exception ex)
        {
            IsAvailable = false;
            FailureReason = ex.Message;
        }
    }

    private static IntPtr ResolveDll(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (!string.Equals(libraryName, DllName, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(libraryName, "LuminiTray.NativeCore", StringComparison.OrdinalIgnoreCase))
        {
            return IntPtr.Zero;
        }

        string baseDir = AppContext.BaseDirectory;
        string[] candidates =
        {
            Path.Combine(baseDir, DllName),
            Path.Combine(baseDir, "Native", DllName)
        };

        foreach (string path in candidates)
        {
            if (File.Exists(path) && VerifySha256IfSidecarExists(path) && NativeLibrary.TryLoad(path, out IntPtr handle))
                return handle;
        }

        // Em executável Single-File, extrai recurso embutido para LocalAppData se necessário
        try
        {
            using Stream? stream = assembly.GetManifestResourceStream(DllName);
            if (stream != null)
            {
                string extractDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "LuminiTray", "Native");
                Directory.CreateDirectory(extractDir);
                string targetPath = Path.Combine(extractDir, DllName);
                bool writeNeeded = !File.Exists(targetPath) || new FileInfo(targetPath).Length != stream.Length;
                if (writeNeeded)
                {
                    using var fs = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.Read);
                    stream.CopyTo(fs);
                }
                if (VerifySha256IfSidecarExists(targetPath) && NativeLibrary.TryLoad(targetPath, out IntPtr handle))
                    return handle;
            }
        }
        catch { }

        return IntPtr.Zero;
    }

    public static bool VerifySha256IfSidecarExists(string dllPath)
    {
        try
        {
            string shaPath = dllPath + ".sha256";
            if (!File.Exists(shaPath)) return true;
            string expectedLine = File.ReadAllText(shaPath).Trim();
            if (string.IsNullOrWhiteSpace(expectedLine)) return true;
            string expectedHash = expectedLine.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)[0];
            using var sha = System.Security.Cryptography.SHA256.Create();
            using var fs = File.OpenRead(dllPath);
            string actual = Convert.ToHexString(sha.ComputeHash(fs)).ToLowerInvariant();
            return string.Equals(actual, expectedHash, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public static bool IsEnabled(AppSettings? settings = null) =>
        IsAvailable
        && !ForceManagedOverride
        && !string.Equals(Environment.GetEnvironmentVariable("LUMINI_FORCE_MANAGED"), "1", StringComparison.Ordinal)
        && (settings?.NativeCoreEnabled ?? true);

    public static int? TryPing(int value)
    {
        if (!IsAvailable) return null;
        try { return nc_ping(value); }
        catch { return null; }
    }

    public static bool TryAllocAndFreeSmokeTest(int sizeBytes, int iterations = 1)
    {
        if (!IsAvailable || sizeBytes <= 0 || iterations <= 0) return false;
        try
        {
            for (int i = 0; i < iterations; i++)
            {
                int rc = nc_alloc_buffer((nuint)sizeBytes, out IntPtr ptr);
                if (rc != 0 || ptr == IntPtr.Zero) return false;
                if (nc_free_buffer(ptr, (nuint)sizeBytes) != 0) return false;
            }
            return true;
        }
        catch { return false; }
    }

    public static string GetLastError()
    {
        if (!IsAvailable) return FailureReason ?? "";
        try
        {
            var buf = new char[256];
            int n = nc_last_error(buf, (nuint)buf.Length);
            return n > 0 ? new string(buf, 0, Math.Min(n, buf.Length)) : "";
        }
        catch { return ""; }
    }

    public static bool VerifyEquivalence<T>(
        Func<T> managedFn,
        Func<T> nativeFn,
        Func<T, T, bool> comparer,
        out T managedResult,
        out T nativeResult)
    {
        managedResult = managedFn();
        nativeResult = nativeFn();
        return comparer(managedResult, nativeResult);
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void DiskProgressCallback(int scannedDirs);

    public static unsafe NativeDiskScanPayload? TryScanDisk(
        string rootPath,
        CancellationToken ct = default,
        int maxFilesPerDir = 1_000_000,
        Action<int>? onProgress = null)
    {
        if (!IsAvailable || string.IsNullOrWhiteSpace(rootPath)) return null;
        ct.ThrowIfCancellationRequested();

        IntPtr cancelMem = Marshal.AllocHGlobal(4);
        Marshal.WriteInt32(cancelMem, 0);
        CancellationTokenRegistration? ctr = ct.CanBeCanceled
            ? ct.Register(() => Marshal.WriteByte(cancelMem, 1))
            : null;

        DiskProgressCallback? cbDelegate = onProgress != null ? count => onProgress(count) : null;
        IntPtr cbPtr = cbDelegate != null ? Marshal.GetFunctionPointerForDelegate(cbDelegate) : IntPtr.Zero;

        IntPtr outBuf = IntPtr.Zero;
        nuint outLen = 0;
        try
        {
            int rc;
            fixed (char* pathPtr = rootPath)
            {
                rc = nc_disk_scan_utf16(
                    pathPtr,
                    (nuint)rootPath.Length,
                    (nuint)Math.Max(1, maxFilesPerDir),
                    cancelMem,
                    cbPtr,
                    out outBuf,
                    out outLen);
            }

            ct.ThrowIfCancellationRequested();
            if (rc != 0 || outBuf == IntPtr.Zero || outLen < 12) return null;

            var span = new ReadOnlySpan<byte>((void*)outBuf, checked((int)outLen));
            return ParseDiskScanPayload(span);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
        finally
        {
            GC.KeepAlive(cbDelegate);
            ctr?.Dispose();
            Marshal.FreeHGlobal(cancelMem);
            if (outBuf != IntPtr.Zero && outLen > 0)
            {
                try { nc_free_buffer(outBuf, outLen); } catch { }
            }
        }
    }

    private static NativeDiskScanPayload? ParseDiskScanPayload(ReadOnlySpan<byte> buf)
    {
        int pos = 0;
        if (buf.Length < 12) return null;

        uint magic = ReadU32(buf, ref pos);
        if (magic != DiskScanMagic) return null;

        int accessDenied = ReadI32(buf, ref pos);
        int reparse = ReadI32(buf, ref pos);
        var root = ReadDirNode(buf, ref pos);
        if (root == null) return null;

        uint extCount = ReadU32(buf, ref pos);
        var payload = new NativeDiskScanPayload
        {
            Root = root,
            AccessDeniedCount = accessDenied,
            ReparseCount = reparse
        };

        for (uint i = 0; i < extCount; i++)
        {
            string ext = ReadUtf8String(buf, ref pos);
            long size = checked((long)ReadU64(buf, ref pos));
            int count = ReadI32(buf, ref pos);
            payload.Extensions.Add(new ExtensionStat(
                string.IsNullOrEmpty(ext) ? "(sem extensão)" : ext,
                size,
                count));
        }

        return payload;
    }

    private static NativeDiskDirNode? ReadDirNode(ReadOnlySpan<byte> buf, ref int pos)
    {
        string name = ReadUtf8String(buf, ref pos);
        long totalSize = checked((long)ReadU64(buf, ref pos));
        int fileCount = ReadI32(buf, ref pos);
        int folderCount = ReadI32(buf, ref pos);

        var node = new NativeDiskDirNode
        {
            Name = name,
            TotalSize = totalSize,
            FileCount = fileCount,
            FolderCount = folderCount
        };

        uint directCount = ReadU32(buf, ref pos);
        if (directCount > 0) node.DirectFiles.Capacity = (int)Math.Min(directCount, 1024);
        for (uint i = 0; i < directCount; i++)
        {
            string fName = ReadUtf8String(buf, ref pos);
            long fSize = checked((long)ReadU64(buf, ref pos));
            node.DirectFiles.Add(new NativeDiskFileEntry { Name = fName, Size = fSize });
        }

        uint subCount = ReadU32(buf, ref pos);
        if (subCount > 0) node.SubDirs.Capacity = (int)Math.Min(subCount, 256);
        for (uint i = 0; i < subCount; i++)
        {
            var sub = ReadDirNode(buf, ref pos);
            if (sub == null) return null;
            node.SubDirs.Add(sub);
        }

        return node;
    }

    private static ushort ReadU16(ReadOnlySpan<byte> buf, ref int pos)
    {
        ushort v = BinaryPrimitives.ReadUInt16LittleEndian(buf.Slice(pos, 2));
        pos += 2;
        return v;
    }

    private static int ReadI32(ReadOnlySpan<byte> buf, ref int pos)
    {
        int v = BinaryPrimitives.ReadInt32LittleEndian(buf.Slice(pos, 4));
        pos += 4;
        return v;
    }

    private static uint ReadU32(ReadOnlySpan<byte> buf, ref int pos)
    {
        uint v = BinaryPrimitives.ReadUInt32LittleEndian(buf.Slice(pos, 4));
        pos += 4;
        return v;
    }

    private static ulong ReadU64(ReadOnlySpan<byte> buf, ref int pos)
    {
        ulong v = BinaryPrimitives.ReadUInt64LittleEndian(buf.Slice(pos, 8));
        pos += 8;
        return v;
    }

    private static string ReadUtf8String(ReadOnlySpan<byte> buf, ref int pos)
    {
        ushort len = ReadU16(buf, ref pos);
        if (len == 0) return "";
        string s = Encoding.UTF8.GetString(buf.Slice(pos, len));
        pos += len;
        return s;
    }

    public static IntPtr TryCreateFrameStore()
    {
        if (!IsAvailable) return IntPtr.Zero;
        try { return nc_frames_create(); }
        catch { return IntPtr.Zero; }
    }

    public static void DestroyFrameStore(IntPtr store)
    {
        if (store == IntPtr.Zero) return;
        try { nc_frames_destroy(store); } catch { }
    }

    public static bool TryClearFrameStore(IntPtr store)
    {
        if (!IsAvailable || store == IntPtr.Zero) return false;
        try { return nc_frames_clear(store) == 0; }
        catch { return false; }
    }

    public static bool TryRecordFrame(IntPtr store, int pid, double timestampMs, double hostNowMs)
    {
        if (!IsAvailable || store == IntPtr.Zero) return false;
        try { return nc_frames_record(store, pid, timestampMs, hostNowMs) == 0; }
        catch { return false; }
    }

    public static bool TryQueryFrameStats(
        IntPtr store,
        int pid,
        double hostNowMs,
        out bool hasStats,
        out double fps,
        out double frameTimeMs,
        out double low1Fps,
        double[] recentBuffer,
        out int recentCount)
    {
        hasStats = false;
        fps = 0;
        frameTimeMs = 0;
        low1Fps = 0;
        recentCount = 0;
        if (!IsAvailable || store == IntPtr.Zero || recentBuffer == null) return false;
        try
        {
            int rc = nc_frames_query(
                store,
                pid,
                hostNowMs,
                out fps,
                out frameTimeMs,
                out low1Fps,
                recentBuffer,
                (nuint)recentBuffer.Length,
                out nuint len);
            if (rc < 0) return false;
            hasStats = rc == 1;
            recentCount = hasStats ? (int)Math.Min(len, (nuint)recentBuffer.Length) : 0;
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static unsafe bool TryComputeFrameMetrics(
        ReadOnlySpan<double> timestamps,
        out double fps,
        out double frameTimeMs,
        out double low1Fps)
    {
        fps = 0;
        frameTimeMs = 0;
        low1Fps = 0;
        if (!IsAvailable) return false;
        if (timestamps.Length < 3) return true;
        try
        {
            fixed (double* ptr = timestamps)
            {
                return nc_frames_compute_metrics(ptr, (nuint)timestamps.Length, out fps, out frameTimeMs, out low1Fps) == 0;
            }
        }
        catch
        {
            return false;
        }
    }

    public static IntPtr TryCreateDsp()
    {
        if (!IsAvailable) return IntPtr.Zero;
        try { return nc_dsp_create(); }
        catch { return IntPtr.Zero; }
    }

    public static void DestroyDsp(IntPtr dsp)
    {
        if (dsp == IntPtr.Zero) return;
        try { nc_dsp_destroy(dsp); } catch { }
    }

    public static bool TrySoftLimit(float x, out float result)
    {
        result = x;
        if (!IsAvailable) return false;
        try
        {
            result = nc_dsp_soft_limit(x);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool TryDspPreProcess(
        IntPtr dsp,
        Span<float> samples480,
        float[] rnnIn480,
        ref NativeDspParams p,
        out float inPeakDb,
        out float inRmsDb)
    {
        inPeakDb = -120f;
        inRmsDb = -120f;
        if (!IsAvailable || dsp == IntPtr.Zero || samples480.Length != 480 || rnnIn480.Length < 480) return false;
        try
        {
            return nc_dsp_pre_process(
                dsp,
                ref MemoryMarshal.GetReference(samples480),
                rnnIn480,
                ref p,
                out inPeakDb,
                out inRmsDb) == 0;
        }
        catch
        {
            return false;
        }
    }

    public static bool TryDspPostRnnoise(
        IntPtr dsp,
        Span<float> samples480,
        float[] rnnOut480,
        float vadProb,
        ref NativeDspParams p)
    {
        if (!IsAvailable || dsp == IntPtr.Zero || samples480.Length != 480 || rnnOut480.Length < 480) return false;
        try
        {
            return nc_dsp_post_rnnoise(
                dsp,
                ref MemoryMarshal.GetReference(samples480),
                rnnOut480,
                vadProb,
                ref p) == 0;
        }
        catch
        {
            return false;
        }
    }

    public static bool TryDspFallbackProcess(
        IntPtr dsp,
        Span<float> samples480,
        float inPeakDb,
        float inRmsDb,
        ref NativeDspParams p,
        out float outVadProb)
    {
        outVadProb = 0f;
        if (!IsAvailable || dsp == IntPtr.Zero || samples480.Length != 480) return false;
        try
        {
            return nc_dsp_fallback_process(
                dsp,
                ref MemoryMarshal.GetReference(samples480),
                inPeakDb,
                inRmsDb,
                ref p,
                out outVadProb) == 0;
        }
        catch
        {
            return false;
        }
    }
}
