using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace LuminiTray;

/// <summary>
/// Camada nativa de acesso S.M.A.R.T. portada do smartmontools 7.5
/// (os_win32.cpp, atacmds.cpp, nvmecmds.cpp — pasta smartmontools-RELEASE_7_5).
///
/// Implementa, em C#, os mesmos caminhos de IOCTL usados pelo smartctl no Windows:
///  • ATA/SATA: IOCTL_ATA_PASS_THROUGH (preferencial) com fallback para SMART_RCV_DRIVE_DATA /
///    SMART_SEND_DRIVE_COMMAND — igual à ordem "saim3"/"aim3" de win_ata_device::ata_pass_through.
///  • NVMe: IOCTL_STORAGE_QUERY_PROPERTY (StorageDeviceProtocolSpecificProperty) para Log Pages
///    e IOCTL_STORAGE_PROTOCOL_COMMAND para o Device Self-test (opcode 0x14) — igual a win10_nvme_device.
/// </summary>
internal static class SmartNative
{
    // ---- IOCTLs (valores conferidos pelos STATIC_ASSERT do os_win32.cpp) ----
    public const uint IOCTL_ATA_PASS_THROUGH = 0x0004D02C;
    public const uint SMART_GET_VERSION = 0x00074080;
    public const uint SMART_SEND_DRIVE_COMMAND = 0x0007C084;
    public const uint SMART_RCV_DRIVE_DATA = 0x0007C088;
    public const uint IOCTL_STORAGE_QUERY_PROPERTY = 0x002D1400;
    public const uint IOCTL_STORAGE_PROTOCOL_COMMAND = 0x002DD3C0;
    public const uint IOCTL_STORAGE_PREDICT_FAILURE = 0x002D1100;
    public const uint IOCTL_DISK_GET_DRIVE_GEOMETRY_EX = 0x000700A0;
    public const uint IOCTL_VOLUME_GET_VOLUME_DISK_EXTENTS = 0x00560000;

    // ---- Comandos ATA (atacmds.h) ----
    public const byte ATA_SMART_CMD = 0xB0;
    public const byte ATA_IDENTIFY_DEVICE = 0xEC;
    public const byte ATA_SMART_READ_VALUES = 0xD0;
    public const byte ATA_SMART_READ_THRESHOLDS = 0xD1;
    public const byte ATA_SMART_IMMEDIATE_OFFLINE = 0xD4;
    public const byte ATA_SMART_READ_LOG_SECTOR = 0xD5;
    public const byte ATA_SMART_WRITE_LOG_SECTOR = 0xD6;
    public const byte ATA_SMART_ENABLE = 0xD8;
    public const byte ATA_SMART_STATUS = 0xDA;
    public const byte SMART_CYL_LOW = 0x4F;
    public const byte SMART_CYL_HI = 0xC2;
    public const byte SRET_STATUS_MID_EXCEEDED = 0xF4;
    public const byte SRET_STATUS_HI_EXCEEDED = 0x2C;

    // Subcomandos de EXECUTE OFF-LINE IMMEDIATE (atacmds.h)
    public const byte SHORT_SELF_TEST = 1;
    public const byte EXTEND_SELF_TEST = 2;
    public const byte CONVEYANCE_SELF_TEST = 3;
    public const byte SELECTIVE_SELF_TEST = 4;
    public const byte ABORT_SELF_TEST = 127;

    // Endereços de logs SMART
    public const byte LOG_SUMMARY_ERROR = 0x01;
    public const byte LOG_SELFTEST = 0x06;
    public const byte LOG_SELECTIVE = 0x09;

    // NVMe (nvmecmds.h)
    public const byte NVME_ADMIN_DEV_SELF_TEST = 0x14;
    public const byte NVME_LOG_SMART = 0x02;
    public const byte NVME_LOG_SELF_TEST = 0x06;

    private const ushort ATA_FLAGS_DATA_IN = 0x02;
    private const ushort ATA_FLAGS_DATA_OUT = 0x04;

    private const uint GENERIC_READ = 0x80000000;
    private const uint GENERIC_WRITE = 0x40000000;
    private const uint FILE_SHARE_READ = 0x1;
    private const uint FILE_SHARE_WRITE = 0x2;
    private const uint OPEN_EXISTING = 3;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr sa,
        uint disposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(SafeFileHandle h, uint code, byte[]? inBuf, int inSize,
        byte[]? outBuf, int outSize, out int returned, IntPtr overlapped);

    /// <summary>Abre um dispositivo. access=0 → somente consultas (IOCTL_STORAGE_*), como o smartctl faz no modo não-admin.</summary>
    public static SafeFileHandle? Open(string path, bool readWrite)
    {
        var h = CreateFileW(path, readWrite ? (GENERIC_READ | GENERIC_WRITE) : 0,
            FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
        if (h.IsInvalid) { h.Dispose(); return null; }
        return h;
    }

    public static SafeFileHandle? OpenRead(string path)
    {
        var h = CreateFileW(path, GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
        if (h.IsInvalid) { h.Dispose(); return null; }
        return h;
    }

    public static bool Ioctl(SafeFileHandle h, uint code, byte[]? inBuf, byte[]? outBuf, out int returned)
        => DeviceIoControl(h, code, inBuf, inBuf?.Length ?? 0, outBuf, outBuf?.Length ?? 0, out returned, IntPtr.Zero);

    public static int LastError => Marshal.GetLastWin32Error();

    /// <summary>Registradores ATA (IDEREGS).</summary>
    public struct IdeRegs
    {
        public byte Features, SectorCount, LbaLow, LbaMid, LbaHigh, Device, Command, Reserved;

        public static IdeRegs Smart(byte feature, byte lbaLow = 0, byte count = 0) => new()
        {
            Command = ATA_SMART_CMD, Features = feature, LbaLow = lbaLow, SectorCount = count,
            LbaMid = SMART_CYL_LOW, LbaHigh = SMART_CYL_HI
        };

        public void WriteTo(byte[] b, int o)
        {
            b[o] = Features; b[o + 1] = SectorCount; b[o + 2] = LbaLow; b[o + 3] = LbaMid;
            b[o + 4] = LbaHigh; b[o + 5] = Device; b[o + 6] = Command; b[o + 7] = Reserved;
        }

        public static IdeRegs ReadFrom(byte[] b, int o) => new()
        {
            Features = b[o], SectorCount = b[o + 1], LbaLow = b[o + 2], LbaMid = b[o + 3],
            LbaHigh = b[o + 4], Device = b[o + 5], Command = b[o + 6], Reserved = b[o + 7]
        };
    }

    // ===================================================================================
    // ATA PASS THROUGH (porte de ata_pass_through_ioctl em os_win32.cpp)
    // ===================================================================================

    /// <summary>
    /// Executa um comando ATA via IOCTL_ATA_PASS_THROUGH. data: null = sem dados; dataOut=false lê
    /// (DATA_IN) para o buffer; dataOut=true envia o buffer (DATA_OUT). Retorna os registradores de saída.
    /// </summary>
    public static bool AtaPassThrough(SafeFileHandle h, ref IdeRegs regs, byte[]? data, bool dataOut = false)
    {
        // ATA_PASS_THROUGH_EX: x64 = 48 bytes (ULONG_PTR DataBufferOffset alinhado em 8); x86 = 40 bytes.
        bool x64 = Environment.Is64BitProcess;
        int hdr = x64 ? 48 : 40;
        int offBuf = x64 ? 24 : 20, offPrev = x64 ? 32 : 24, offCur = x64 ? 40 : 32;
        int dataOff = hdr + 8; // + "Filler", como no smartmontools
        int size = dataOff + (data?.Length ?? 0);
        var b = new byte[size];

        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(0), (ushort)hdr);
        ushort flags = 0;
        if (data != null) flags = dataOut ? ATA_FLAGS_DATA_OUT : ATA_FLAGS_DATA_IN;
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(2), flags);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(8), (uint)(data?.Length ?? 0));
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(12), 60); // timeout (s)
        if (x64) BinaryPrimitives.WriteUInt64LittleEndian(b.AsSpan(offBuf), (ulong)dataOff);
        else BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(offBuf), (uint)dataOff);
        regs.WriteTo(b, offCur);

        const byte magic = 0xCF;
        if (data != null)
        {
            if (dataOut) Buffer.BlockCopy(data, 0, b, dataOff, data.Length);
            else b[dataOff] = magic;
        }

        if (!Ioctl(h, IOCTL_ATA_PASS_THROUGH, b, b, out int ret)) return false;

        var outRegs = IdeRegs.ReadFrom(b, offCur);
        // Status: ERR (0x01) ou DRQ (0x08) = falha (mesma verificação do smartmontools)
        if ((outRegs.Command & 0x09) != 0) return false;

        if (data != null && !dataOut)
        {
            if (ret < size) return false;
            bool any = false;
            for (int i = 1; i < data.Length && !any; i++) any = b[dataOff + i] != 0;
            if (b[dataOff] == magic && !any) return false; // dados não retornados
            Buffer.BlockCopy(b, dataOff, data, 0, data.Length);
        }
        _ = offPrev;
        regs = outRegs;
        return true;
    }

    // ===================================================================================
    // SMART_RCV_DRIVE_DATA / SMART_SEND_DRIVE_COMMAND (porte de smart_ioctl em os_win32.cpp)
    // ===================================================================================

    public static bool SmartIoctl(SafeFileHandle h, ref IdeRegs regs, byte[]? data512)
    {
        // SENDCMDINPARAMS (packed): cBufferSize(4) IDEREGS(8) bDriveNumber(1) res[3] dwRes[4] → 32 bytes sem bBuffer
        var inBuf = new byte[32];
        var r = regs; r.Device |= 0xA0; // drivers antigos exigem bits 5 e 7
        uint code;
        int sizeOut;
        if (data512 != null)
        {
            code = SMART_RCV_DRIVE_DATA;
            BinaryPrimitives.WriteUInt32LittleEndian(inBuf.AsSpan(0), 512);
            sizeOut = 512;
        }
        else
        {
            code = SMART_SEND_DRIVE_COMMAND;
            sizeOut = regs.Features == ATA_SMART_STATUS ? 8 : 0;
        }
        r.WriteTo(inBuf, 4);

        // SENDCMDOUTPARAMS: cBufferSize(4) DRIVERSTATUS(12) bBuffer → 16 bytes de cabeçalho
        var outBuf = new byte[16 + Math.Max(sizeOut, 0)];
        if (!Ioctl(h, code, inBuf, outBuf, out _)) return false;
        if (outBuf[4] != 0) return false; // bDriverError

        if (data512 != null) Buffer.BlockCopy(outBuf, 16, data512, 0, 512);
        else if (regs.Features == ATA_SMART_STATUS)
        {
            bool any = false;
            for (int i = 0; i < 8; i++) any |= outBuf[16 + i] != 0;
            regs = any ? IdeRegs.ReadFrom(outBuf, 16) : r;
        }
        return true;
    }

    /// <summary>Comando SMART com a mesma estratégia de fallback do smartctl.</summary>
    public static bool SmartCommand(SafeFileHandle h, ref IdeRegs regs, byte[]? data, bool dataOut = false)
    {
        var copy = regs;
        if (AtaPassThrough(h, ref copy, data, dataOut)) { regs = copy; return true; }

        // SMART_* IOCTL não suporta READ_LOG / WRITE_LOG nem ABORT (comentários do os_win32.cpp)
        bool smartIoctlOk = regs.Command == ATA_SMART_CMD && !dataOut && regs.Features switch
        {
            ATA_SMART_READ_VALUES or ATA_SMART_READ_THRESHOLDS or ATA_SMART_STATUS or ATA_SMART_ENABLE => true,
            ATA_SMART_IMMEDIATE_OFFLINE => regs.LbaLow != ABORT_SELF_TEST,
            _ => false
        };
        if (regs.Command == ATA_IDENTIFY_DEVICE) smartIoctlOk = true;
        if (!smartIoctlOk) return false;

        copy = regs;
        if (SmartIoctl(h, ref copy, data)) { regs = copy; return true; }
        return false;
    }

    public static bool ReadSmartSector(SafeFileHandle h, byte feature, byte lbaLow, out byte[] data)
    {
        data = new byte[512];
        var regs = IdeRegs.Smart(feature, lbaLow, 1);
        return SmartCommand(h, ref regs, data);
    }

    public static bool ReadSmartLog(SafeFileHandle h, byte logAddr, out byte[] data)
        => ReadSmartSector(h, ATA_SMART_READ_LOG_SECTOR, logAddr, out data);

    public static bool WriteSmartLog(SafeFileHandle h, byte logAddr, byte[] data512)
    {
        var regs = IdeRegs.Smart(ATA_SMART_WRITE_LOG_SECTOR, logAddr, 1);
        return SmartCommand(h, ref regs, data512, dataOut: true);
    }

    public static bool IdentifyAta(SafeFileHandle h, out byte[] data)
    {
        data = new byte[512];
        var regs = new IdeRegs { Command = ATA_IDENTIFY_DEVICE, SectorCount = 1 };
        return SmartCommand(h, ref regs, data);
    }

    /// <summary>SMART RETURN STATUS. true = aprovado, false = limite excedido, null = indeterminado.</summary>
    public static bool? SmartReturnStatus(SafeFileHandle h)
    {
        var regs = IdeRegs.Smart(ATA_SMART_STATUS);
        if (!SmartCommand(h, ref regs, null)) return null;
        if (regs.LbaHigh == SMART_CYL_HI && regs.LbaMid == SMART_CYL_LOW) return true;
        if (regs.LbaHigh == SRET_STATUS_HI_EXCEEDED && regs.LbaMid == SRET_STATUS_MID_EXCEEDED) return false;
        return null;
    }

    public static bool ExecuteOfflineImmediate(SafeFileHandle h, byte subcommand)
    {
        var regs = IdeRegs.Smart(ATA_SMART_IMMEDIATE_OFFLINE, subcommand);
        return SmartCommand(h, ref regs, null);
    }

    /// <summary>
    /// Grava o Selective Self-test Log (porte de ataWriteSelectiveSelfTestLog em atacmds.cpp):
    /// lê o log atual, define até 5 spans, zera currentlba/currentspan, limpa flags ACTIVE/PENDING e recalcula checksum.
    /// </summary>
    public static bool WriteSelectiveSpans(SafeFileHandle h, IReadOnlyList<(ulong Start, ulong End)> spans)
    {
        if (!ReadSmartLog(h, LOG_SELECTIVE, out var log)) return false;
        var s = log.AsSpan();
        BinaryPrimitives.WriteUInt16LittleEndian(s, 1); // logversion
        for (int i = 0; i < 5; i++)
        {
            ulong st = i < spans.Count ? spans[i].Start : 0, en = i < spans.Count ? spans[i].End : 0;
            BinaryPrimitives.WriteUInt64LittleEndian(s.Slice(2 + i * 16), st);
            BinaryPrimitives.WriteUInt64LittleEndian(s.Slice(2 + i * 16 + 8), en);
        }
        BinaryPrimitives.WriteUInt64LittleEndian(s.Slice(492), 0); // currentlba
        BinaryPrimitives.WriteUInt16LittleEndian(s.Slice(500), 0); // currentspan
        ushort flags = BinaryPrimitives.ReadUInt16LittleEndian(s.Slice(502));
        flags &= unchecked((ushort)~(0x0010 | 0x0008 | 0x0002)); // sem ACTIVE, PENDING e sem varredura posterior
        BinaryPrimitives.WriteUInt16LittleEndian(s.Slice(502), flags);
        log[511] = 0;
        byte sum = 0;
        for (int i = 0; i < 511; i++) sum += log[i];
        log[511] = (byte)(0x100 - sum);
        return WriteSmartLog(h, LOG_SELECTIVE, log);
    }

    // ===================================================================================
    // NVMe (porte de win10_nvme_device em os_win32.cpp)
    // ===================================================================================

    /// <summary>Get Log Page via IOCTL_STORAGE_QUERY_PROPERTY (StorageDeviceProtocolSpecificProperty).</summary>
    public static bool NvmeGetLogPage(SafeFileHandle h, byte logId, int size, out byte[] data)
        => NvmeQuery(h, propertyId: 50, dataType: 2, requestValue: logId, subValue: 0, size, out data);

    /// <summary>Identify Controller (CNS=1) via StorageAdapterProtocolSpecificProperty.</summary>
    public static bool NvmeIdentifyController(SafeFileHandle h, out byte[] data)
        => NvmeQuery(h, propertyId: 49, dataType: 1, requestValue: 1, subValue: 0, 4096, out data);

    private static bool NvmeQuery(SafeFileHandle h, int propertyId, int dataType, uint requestValue, uint subValue, int size, out byte[] data)
    {
        data = new byte[size];
        // STORAGE_PROPERTY_QUERY (PropertyId, QueryType) + STORAGE_PROTOCOL_SPECIFIC_DATA (40) + buffer
        var b = new byte[8 + 40 + size];
        var s = b.AsSpan();
        BinaryPrimitives.WriteInt32LittleEndian(s, propertyId);
        BinaryPrimitives.WriteInt32LittleEndian(s.Slice(4), 0);         // PropertyStandardQuery
        BinaryPrimitives.WriteInt32LittleEndian(s.Slice(8), 3);         // ProtocolTypeNvme
        BinaryPrimitives.WriteInt32LittleEndian(s.Slice(12), dataType); // Identify=1, LogPage=2
        BinaryPrimitives.WriteUInt32LittleEndian(s.Slice(16), requestValue);
        BinaryPrimitives.WriteUInt32LittleEndian(s.Slice(20), subValue);
        BinaryPrimitives.WriteInt32LittleEndian(s.Slice(24), 40);       // ProtocolDataOffset
        BinaryPrimitives.WriteInt32LittleEndian(s.Slice(28), size);     // ProtocolDataLength
        if (!Ioctl(h, IOCTL_STORAGE_QUERY_PROPERTY, b, b, out int ret) || ret < 48) return false;
        // Saída: STORAGE_PROTOCOL_DATA_DESCRIPTOR (Version, Size) + SPECIFIC_DATA → dados em 8 + ProtocolDataOffset
        int off = 8 + BinaryPrimitives.ReadInt32LittleEndian(s.Slice(24));
        int len = Math.Min(size, BinaryPrimitives.ReadInt32LittleEndian(s.Slice(28)));
        if (off < 48 || off + len > b.Length || len <= 0) off = 48;
        Buffer.BlockCopy(b, off, data, 0, Math.Min(size, b.Length - off));
        return true;
    }

    /// <summary>
    /// NVMe Device Self-test (admin 0x14) via IOCTL_STORAGE_PROTOCOL_COMMAND.
    /// stc: 1 = curto, 2 = estendido, 0xF = abortar. nsid 0xFFFFFFFF = todos os namespaces.
    /// </summary>
    public static bool NvmeDeviceSelfTest(SafeFileHandle h, byte stc, uint nsid = 0xFFFFFFFF)
    {
        var b = new byte[80 + 64];
        var s = b.AsSpan();
        BinaryPrimitives.WriteUInt32LittleEndian(s, 1);                   // Version
        BinaryPrimitives.WriteUInt32LittleEndian(s.Slice(4), 84);         // Length = sizeof(STORAGE_PROTOCOL_COMMAND)
        BinaryPrimitives.WriteUInt32LittleEndian(s.Slice(8), 3);          // ProtocolTypeNvme
        BinaryPrimitives.WriteUInt32LittleEndian(s.Slice(12), 0x80000000);// FLAG_ADAPTER_REQUEST
        BinaryPrimitives.WriteUInt32LittleEndian(s.Slice(24), 64);        // CommandLength
        BinaryPrimitives.WriteUInt32LittleEndian(s.Slice(40), 60);        // TimeOutValue
        BinaryPrimitives.WriteUInt32LittleEndian(s.Slice(56), 1);         // NVME_ADMIN_COMMAND
        b[80] = NVME_ADMIN_DEV_SELF_TEST;                                 // CDW0.OPC
        BinaryPrimitives.WriteUInt32LittleEndian(s.Slice(84), nsid);      // NSID
        BinaryPrimitives.WriteUInt32LittleEndian(s.Slice(120), stc);      // CDW10
        return Ioctl(h, IOCTL_STORAGE_PROTOCOL_COMMAND, b, b, out _);
    }

    // ===================================================================================
    // Consultas gerais de armazenamento
    // ===================================================================================

    public sealed class DeviceDescriptor
    {
        public string Vendor = "", Product = "", Revision = "", Serial = "";
        public int BusType;
        public bool Removable;
    }

    public static DeviceDescriptor? QueryDeviceDescriptor(SafeFileHandle h)
    {
        var q = new byte[12]; // PropertyId=0 (StorageDeviceProperty), QueryType=0
        var o = new byte[1024];
        if (!Ioctl(h, IOCTL_STORAGE_QUERY_PROPERTY, q, o, out int ret) || ret < 36) return null;
        string Str(int offPos)
        {
            int off = BinaryPrimitives.ReadInt32LittleEndian(o.AsSpan(offPos));
            if (off <= 0 || off >= ret) return "";
            int end = off;
            while (end < ret && o[end] != 0) end++;
            return System.Text.Encoding.ASCII.GetString(o, off, end - off).Trim();
        }
        return new DeviceDescriptor
        {
            Removable = o[10] != 0,
            Vendor = Str(12), Product = Str(16), Revision = Str(20), Serial = Str(24),
            BusType = BinaryPrimitives.ReadInt32LittleEndian(o.AsSpan(28))
        };
    }

    /// <summary>StorageDeviceSeekPenaltyProperty (7): true = HDD (sofre penalidade de seek).</summary>
    public static bool? QuerySeekPenalty(SafeFileHandle h)
    {
        var q = new byte[12];
        BinaryPrimitives.WriteInt32LittleEndian(q, 7);
        var o = new byte[12];
        if (!Ioctl(h, IOCTL_STORAGE_QUERY_PROPERTY, q, o, out int ret) || ret < 9) return null;
        return o[8] != 0;
    }

    public static bool GetGeometry(SafeFileHandle h, out long diskSize, out int bytesPerSector)
    {
        diskSize = 0; bytesPerSector = 512;
        var o = new byte[256];
        if (!Ioctl(h, IOCTL_DISK_GET_DRIVE_GEOMETRY_EX, null, o, out int ret) || ret < 32) return false;
        bytesPerSector = BinaryPrimitives.ReadInt32LittleEndian(o.AsSpan(20));
        diskSize = BinaryPrimitives.ReadInt64LittleEndian(o.AsSpan(24));
        if (bytesPerSector <= 0) bytesPerSector = 512;
        return diskSize > 0;
    }

    /// <summary>Extensões físicas de um volume (\\.\C:) → lista de (disco, offset, tamanho).</summary>
    public static List<(int Disk, long Offset, long Length)> GetVolumeExtents(string volumePath)
    {
        var list = new List<(int, long, long)>();
        using var h = Open(volumePath, false);
        if (h == null) return list;
        var o = new byte[8 + 24 * 32];
        if (!Ioctl(h, IOCTL_VOLUME_GET_VOLUME_DISK_EXTENTS, null, o, out int ret) || ret < 8) return list;
        int n = BinaryPrimitives.ReadInt32LittleEndian(o);
        for (int i = 0; i < n && 8 + i * 24 + 24 <= ret; i++)
        {
            int off = 8 + i * 24;
            list.Add((BinaryPrimitives.ReadInt32LittleEndian(o.AsSpan(off)),
                      BinaryPrimitives.ReadInt64LittleEndian(o.AsSpan(off + 8)),
                      BinaryPrimitives.ReadInt64LittleEndian(o.AsSpan(off + 16))));
        }
        return list;
    }

    public static string BusName(int busType) => busType switch
    {
        1 => "SCSI", 2 => "ATAPI", 3 => "ATA", 4 => "IEEE 1394", 5 => "SSA", 6 => "Fibre Channel",
        7 => "USB", 8 => "RAID", 9 => "iSCSI", 10 => "SAS", 11 => "SATA", 12 => "SD", 13 => "MMC",
        14 => "Virtual", 15 => "VHD", 16 => "Storage Spaces", 17 => "NVMe", 18 => "SCM", 19 => "UFS",
        _ => "Desconhecido"
    };

    /// <summary>Lê um inteiro little-endian de 128 bits (contadores NVMe) truncado em long.</summary>
    public static long ReadLe128(byte[] b, int off)
    {
        ulong lo = BinaryPrimitives.ReadUInt64LittleEndian(b.AsSpan(off));
        ulong hi = BinaryPrimitives.ReadUInt64LittleEndian(b.AsSpan(off + 8));
        return hi != 0 || lo > long.MaxValue ? long.MaxValue : (long)lo;
    }
}
