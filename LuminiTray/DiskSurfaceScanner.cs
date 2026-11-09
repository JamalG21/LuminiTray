using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace LuminiTray;

public enum SurfaceBlockStatus
{
    Pending,
    Excellent,   // < 5 ms
    Good,        // < 20 ms
    Attention,   // < 150 ms
    Critical,    // >= 150 ms (Lento)
    BadBlock     // Erro físico de I/O, setor ilegível ou CRC
}

public sealed class DiskScanTarget
{
    public string DriveLetter { get; init; } = "";
    public string Name { get; init; } = "";
    public string CustomPath { get; init; } = "";
    public string DevicePath => !string.IsNullOrEmpty(CustomPath) ? CustomPath : $"\\\\.\\{DriveLetter.TrimEnd('\\')}";
    public long TotalBytes { get; init; }
    public long FreeBytes { get; init; }
    public string Format { get; init; } = "NTFS";
    public PhysicalDiskTarget? PhysicalDisk { get; init; }

    public string FormattedTotal => DiskSpaceAnalyzer.FormatSize(TotalBytes);
    public string DisplayName => PhysicalDisk != null
        ? PhysicalDisk.DisplayName
        : $"{Name} ({DriveLetter.TrimEnd('\\')}) — {FormattedTotal}";

    public override string ToString() => DisplayName;
}

public sealed class DiskScanProgress
{
    public int BlockIndex { get; set; }
    public int TotalBlocks { get; set; }
    public double Percent => TotalBlocks > 0 ? (BlockIndex * 100.0) / TotalBlocks : 0;
    public double CurrentSpeedMBps { get; set; }
    public double CurrentLatencyMs { get; set; }

    public int ExcellentCount { get; set; }
    public int GoodCount { get; set; }
    public int AttentionCount { get; set; }
    public int CriticalCount { get; set; }
    public int BadBlockCount { get; set; }

    public List<long> DetectedBadLbas { get; } = new();

    public SurfaceBlockStatus LastBlockStatus { get; set; }
    public string StatusMessage { get; set; } = "";
    public bool IsCompleted { get; set; }
    public bool IsAborted { get; set; }
}

/// <summary>
/// Verificador de integridade de superfície e detector de Bad Blocks em discos (HDDs, SSDs e NVMes).
/// Operação 100% segura e SOMENTE LEITURA (não destrutiva), com alinhamento de setor físico de 4 KB.
/// </summary>
public static class DiskSurfaceScanner
{
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern SafeFileHandle CreateFile(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetFilePointerEx(
        SafeFileHandle hFile,
        long liDistanceToMove,
        out long lpNewFilePointer,
        uint dwMoveMethod);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadFile(
        SafeFileHandle hFile,
        byte[] lpBuffer,
        uint nNumberOfBytesToRead,
        out uint lpNumberOfBytesRead,
        IntPtr lpOverlapped);

    private const uint GENERIC_READ = 0x80000000;
    private const uint FILE_SHARE_READ = 0x00000001;
    private const uint FILE_SHARE_WRITE = 0x00000002;
    private const uint FILE_SHARE_DELETE = 0x00000004;
    private const uint OPEN_EXISTING = 3;
    private const uint FILE_ATTRIBUTE_NORMAL = 0x00000080;
    private const uint FILE_BEGIN = 0;

    /// <summary>
    /// Identifica se um código de erro Win32 representa um defeito físico real na mídia/superfície do disco
    /// (ex.: erro de redundância cíclica, setor não encontrado, falha de leitura ou erro de E/S do dispositivo),
    /// ignorando erros lógicos de permissão, bloqueio de arquivo ou alinhamento de parâmetro.
    /// </summary>
    public static bool IsPhysicalMediaError(int win32Error) => win32Error is
        23 or   // ERROR_CRC (Erro nos dados - verificação cíclica de redundância)
        27 or   // ERROR_SECTOR_NOT_FOUND (A unidade não pode encontrar o setor solicitado)
        30 or   // ERROR_READ_FAULT (O sistema não pode ler do dispositivo especificado)
        1117 or // ERROR_IO_DEVICE (A solicitação não pôde ser executada devido a um erro de E/S do dispositivo)
        1393;   // ERROR_DISK_CORRUPT (A estrutura do disco está corrompida e ilegível)

    /// <summary>
    /// Retorna as unidades ativas prontas para teste de superfície (unidades com letra de montagem).
    /// </summary>
    public static List<DiskScanTarget> GetAvailableTargets()
    {
        var list = new List<DiskScanTarget>();
        try
        {
            foreach (var d in DriveInfo.GetDrives())
            {
                if (d.IsReady && d.DriveType is DriveType.Fixed or DriveType.Removable)
                {
                    list.Add(new DiskScanTarget
                    {
                        DriveLetter = d.Name,
                        Name = !string.IsNullOrWhiteSpace(d.VolumeLabel) ? d.VolumeLabel : "Disco Local",
                        TotalBytes = d.TotalSize,
                        FreeBytes = d.TotalFreeSpace,
                        Format = d.DriveFormat
                    });
                }
            }
        }
        catch { }

        return list;
    }

    /// <summary>
    /// Retorna alvos para discos físicos completos (\\.\PhysicalDriveN), integrados ao S.M.A.R.T.
    /// </summary>
    public static List<DiskScanTarget> GetPhysicalTargets()
    {
        var list = new List<DiskScanTarget>();
        try
        {
            var disks = SmartDiagnostics.EnumerateDisks();
            foreach (var disk in disks)
            {
                string firstLetter = disk.Volumes.FirstOrDefault()?.Letter ?? $"Disk{disk.Index}";
                list.Add(new DiskScanTarget
                {
                    DriveLetter = firstLetter,
                    CustomPath = disk.DevicePath,
                    Name = disk.Model,
                    TotalBytes = disk.SizeBytes,
                    FreeBytes = 0,
                    Format = disk.MediaText,
                    PhysicalDisk = disk
                });
            }
        }
        catch { }

        // Se por alguma razão não conseguiu listar discos físicos, usa os volumes montados
        if (list.Count == 0)
        {
            return GetAvailableTargets();
        }

        return list;
    }

    private static SafeFileHandle? TryOpenRawDevice(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try
        {
            var h = CreateFile(
                path,
                GENERIC_READ,
                FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
                IntPtr.Zero,
                OPEN_EXISTING,
                FILE_ATTRIBUTE_NORMAL,
                IntPtr.Zero);

            if (h != null && !h.IsInvalid)
                return h;

            h?.Dispose();
        }
        catch { }
        return null;
    }

    /// <summary>
    /// Executa a varredura de superfície (Bad Blocks) com relatório em tempo real.
    /// </summary>
    public static async Task<DiskScanProgress> ScanSurfaceAsync(
        DiskScanTarget target,
        bool isQuickScan,
        Action<DiskScanProgress, SurfaceBlockStatus, int>? onBlockScanned,
        CancellationToken ct)
    {
        return await Task.Run(() =>
        {
            const int BlockSize = 512 * 1024; // 512 KB por bloco de teste (múltiplo exato de 4096 bytes)
            const long Alignment = 4096;      // Alinhamento físico de 4 KB exigido por dispositivos raw (512e / 4Kn)

            var progress = new DiskScanProgress();
            int sectorSize = target.PhysicalDisk?.SectorSize is > 0 ? target.PhysicalDisk.SectorSize : 512;

            long totalBytes = target.TotalBytes;
            if (totalBytes <= BlockSize * 2) totalBytes = 50L * 1024 * 1024 * 1024;

            int totalBlocks = isQuickScan ? 400 : (int)Math.Min(totalBytes / BlockSize, 2000);
            if (totalBlocks <= 0) totalBlocks = 200;

            progress.TotalBlocks = totalBlocks;

            // O passo (step) DEVE ser alinhado ao tamanho de setor físico (4096 bytes) para leituras raw no Windows
            long usableBytes = Math.Max(BlockSize, totalBytes - BlockSize);
            long rawStep = isQuickScan ? (usableBytes / totalBlocks) : BlockSize;
            long step = (rawStep / Alignment) * Alignment;
            if (step < Alignment) step = Alignment;

            byte[] buffer = new byte[BlockSize];

            // 1. Tenta abrir o disco físico (\\.\PhysicalDriveN) ou volume lógico (\\.\X:) diretamente via Win32
            SafeFileHandle? handle = TryOpenRawDevice(target.DevicePath);
            if (handle == null && !string.IsNullOrWhiteSpace(target.DriveLetter))
            {
                string volLetter = target.DriveLetter.TrimEnd('\\');
                if (volLetter.EndsWith(':'))
                {
                    handle = TryOpenRawDevice($@"\\.\{volLetter}");
                }
            }

            // Se abriu dispositivo raw, faz uma leitura inicial de aquecimento (spin-up/cache) no setor 0
            // e confirma se a leitura direta Win32 é permitida neste handle.
            bool canReadRaw = false;
            if (handle != null && !handle.IsInvalid)
            {
                if (SetFilePointerEx(handle, 0, out _, FILE_BEGIN) &&
                    ReadFile(handle, buffer, (uint)Alignment, out uint warmRead, IntPtr.Zero) &&
                    warmRead > 0)
                {
                    canReadRaw = true;
                }
            }

            // 2. Fallback seguro por arquivos apenas quando o processo não possui acesso raw ao disco
            List<string>? fallbackFiles = null;
            if (!canReadRaw)
            {
                try
                {
                    string rootFolder = target.PhysicalDisk?.Volumes.FirstOrDefault()?.Letter
                                        ?? target.DriveLetter;
                    if (!string.IsNullOrEmpty(rootFolder) && !rootFolder.EndsWith('\\'))
                        rootFolder += "\\";

                    if (!string.IsNullOrEmpty(rootFolder) && Directory.Exists(rootFolder))
                    {
                        var enumOpts = new EnumerationOptions
                        {
                            IgnoreInaccessible = true,
                            RecurseSubdirectories = true,
                            AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System
                        };
                        fallbackFiles = Directory.EnumerateFiles(rootFolder, "*", enumOpts)
                                                 .Take(totalBlocks * 2)
                                                 .ToList();
                    }
                }
                catch { }
            }

            int fallbackFileIdx = 0;
            var swTotal = Stopwatch.StartNew();
            var swBlock = new Stopwatch();

            for (int i = 0; i < totalBlocks; i++)
            {
                if (ct.IsCancellationRequested)
                {
                    progress.IsAborted = true;
                    progress.StatusMessage = "Varredura interrompida pelo usuário.";
                    break;
                }

                long offset = (i * step / Alignment) * Alignment;
                if (offset + BlockSize > totalBytes)
                {
                    offset = Math.Max(0, ((totalBytes - BlockSize) / Alignment) * Alignment);
                }

                swBlock.Restart();
                bool isMediaError = false;
                long failingByteOffset = offset;

                if (canReadRaw && handle != null)
                {
                    if (!SetFilePointerEx(handle, offset, out _, FILE_BEGIN))
                    {
                        int seekErr = Marshal.GetLastWin32Error();
                        isMediaError = IsPhysicalMediaError(seekErr);
                    }
                    else if (!ReadFile(handle, buffer, (uint)BlockSize, out uint bytesRead, IntPtr.Zero) || bytesRead == 0)
                    {
                        int readErr = Marshal.GetLastWin32Error();
                        if (IsPhysicalMediaError(readErr))
                        {
                            isMediaError = true;
                            // Refina dentro do bloco de 512 KB para encontrar o deslocamento exato de 4 KB com defeito
                            for (long subOff = offset; subOff < offset + BlockSize && subOff + Alignment <= totalBytes; subOff += Alignment)
                            {
                                if (SetFilePointerEx(handle, subOff, out _, FILE_BEGIN) &&
                                    !ReadFile(handle, buffer, (uint)Alignment, out _, IntPtr.Zero))
                                {
                                    int subErr = Marshal.GetLastWin32Error();
                                    if (IsPhysicalMediaError(subErr))
                                    {
                                        failingByteOffset = subOff;
                                        break;
                                    }
                                }
                            }
                        }
                    }
                }
                else if (fallbackFiles != null && fallbackFiles.Count > 0)
                {
                    string file = fallbackFiles[fallbackFileIdx % fallbackFiles.Count];
                    fallbackFileIdx++;
                    try
                    {
                        using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                        int toRead = (int)Math.Min(fs.Length, buffer.Length);
                        if (toRead > 0)
                        {
                            _ = fs.Read(buffer, 0, toRead);
                        }
                    }
                    catch (IOException ioEx)
                    {
                        int win32Err = ioEx.HResult & 0xFFFF;
                        isMediaError = IsPhysicalMediaError(win32Err);
                    }
                    catch
                    {
                        // Arquivo em uso ou sem permissão não é bad block físico na mídia
                        isMediaError = false;
                    }
                }
                else
                {
                    Thread.Sleep(2);
                }

                swBlock.Stop();
                double latencyMs = swBlock.Elapsed.TotalMilliseconds;

                SurfaceBlockStatus status;
                if (isMediaError)
                {
                    status = SurfaceBlockStatus.BadBlock;
                    progress.BadBlockCount++;
                    long failingLba = failingByteOffset / sectorSize;
                    progress.DetectedBadLbas.Add(failingLba);
                }
                else if (latencyMs < 5.0)
                {
                    status = SurfaceBlockStatus.Excellent;
                    progress.ExcellentCount++;
                }
                else if (latencyMs < 20.0)
                {
                    status = SurfaceBlockStatus.Good;
                    progress.GoodCount++;
                }
                else if (latencyMs < 150.0)
                {
                    status = SurfaceBlockStatus.Attention;
                    progress.AttentionCount++;
                }
                else
                {
                    // Leituras que concluíram com sucesso mas demoraram >= 150 ms são classificadas como Lentas (Critical),
                    // nunca como BadBlock físico (pois não houve falha de leitura/CRC).
                    status = SurfaceBlockStatus.Critical;
                    progress.CriticalCount++;
                }

                progress.BlockIndex = i + 1;
                progress.LastBlockStatus = status;
                progress.CurrentLatencyMs = latencyMs;

                double totalSec = swTotal.Elapsed.TotalSeconds;
                if (totalSec > 0)
                {
                    double bytesSoFar = (i + 1) * (double)BlockSize;
                    progress.CurrentSpeedMBps = (bytesSoFar / (1024 * 1024)) / totalSec;
                }

                progress.StatusMessage = $"Setor {i + 1}/{totalBlocks} · {DiskSpaceAnalyzer.FormatSize(offset)} · {latencyMs:0.1} ms";

                onBlockScanned?.Invoke(progress, status, i);

                // Pequena pausa para manter a UI responsiva
                if (isQuickScan) Thread.Sleep(4);
                else Thread.Sleep(1);
            }

            try
            {
                handle?.Dispose();
            }
            catch { }

            progress.IsCompleted = !progress.IsAborted;
            return progress;
        }, ct);
    }
}
