using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;

namespace LuminiTray;

// =======================================================================================
// Modelos
// =======================================================================================

/// <summary>Disco físico (\\.\PhysicalDriveN) alvo do diagnóstico S.M.A.R.T. e da varredura de superfície.</summary>
public sealed class PhysicalDiskTarget
{
    public int Index { get; init; }
    public string Model { get; init; } = "";
    public string Serial { get; init; } = "";
    public string Firmware { get; init; } = "";
    public int BusType { get; init; }
    public string BusText => SmartNative.BusName(BusType);
    public bool IsNvme => BusType == 17;
    public bool IsAtaLike => BusType is 3 or 11 or 2;
    public bool? IsSsd { get; init; }
    public long SizeBytes { get; init; }
    public int SectorSize { get; init; } = 512;
    public long TotalSectors => SectorSize > 0 ? SizeBytes / SectorSize : 0;
    public List<DiskVolumeExtent> Volumes { get; init; } = new();

    public string DevicePath => $@"\\.\PhysicalDrive{Index}";
    public string Letters => Volumes.Count == 0 ? "" : string.Join(" ", Volumes.Select(v => v.Letter).Distinct());
    public string MediaText => IsNvme ? "SSD NVMe" : IsSsd == true ? "SSD" : IsSsd == false ? "HDD" : "Disco";

    public string DisplayName =>
        $"Disco {Index} · {(string.IsNullOrWhiteSpace(Model) ? "Sem nome" : Model)} · {MediaText} {BusText} · " +
        $"{DiskSpaceAnalyzer.FormatSize(SizeBytes)}{(Letters.Length > 0 ? $" ({Letters})" : "")}";

    public override string ToString() => DisplayName;
}

public sealed class DiskVolumeExtent
{
    public string Letter { get; init; } = "";      // "C:"
    public long StartOffset { get; init; }          // bytes no disco físico
    public long Length { get; init; }
    public int BytesPerCluster { get; init; } = 4096;
    public string FileSystem { get; init; } = "";
}

public sealed class SmartAttribute
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public ushort Flags { get; init; }
    public int Current { get; init; }
    public int Worst { get; init; }
    public int Threshold { get; init; }
    public long RawValue { get; init; }
    public string RawText { get; init; } = "";
    public bool IsPrefailure => (Flags & 0x01) != 0;
    public bool FailingNow => Threshold > 0 && Current <= Threshold;
    public bool FailedInPast => Threshold > 0 && Worst <= Threshold && !FailingNow;
    public bool IsBadBlockRelated => SmartDiagnostics.BadBlockAttributeIds.Contains(Id);

    public string TypeText => IsPrefailure ? "Pré-falha" : "Desgaste";
    public string StatusText => FailingNow ? "FALHANDO" : FailedInPast ? "Falhou antes"
        : IsBadBlockRelated && RawValue > 0 && Id != 199 ? "Atenção" : "OK";
    public string IdText => Id.ToString("000");
    public string CurrentText => Current.ToString("000");
    public string WorstText => Worst.ToString("000");
    public string ThresholdText => Threshold.ToString("000");
}

public sealed class SelfTestEntry
{
    public int Number { get; init; }
    public string TypeText { get; init; } = "";
    public string StatusText { get; init; } = "";
    public int RemainingPercent { get; init; }
    public long LifetimeHours { get; init; }
    public long? FailingLba { get; init; }
    public bool Failed { get; init; }
    public bool Outdated { get; init; }
    public string LbaText => FailingLba?.ToString("N0") ?? "—";
    public string HoursText => LifetimeHours.ToString("N0") + " h";
}

public sealed class DeviceErrorEntry
{
    public int Number { get; init; }
    public long LifetimeHours { get; init; }
    public long? Lba { get; init; }
    public string Description { get; init; } = "";
    public bool IsUncorrectable { get; init; }
    public string LbaText => Lba?.ToString("N0") ?? "—";
    public string HoursText => LifetimeHours.ToString("N0") + " h";
}

public sealed class SmartKeyCounter
{
    public string Title { get; init; } = "";
    public string Value { get; init; } = "";
    public string Hint { get; init; } = "";
    public int Severity { get; init; } // 0 ok, 1 atenção, 2 crítico, -1 indisponível
}

public enum BadBlockLevel { Healthy, Attention, Critical, Unknown }

public sealed class SmartSnapshot
{
    public PhysicalDiskTarget Disk { get; init; } = new();
    public bool Available { get; set; }
    public string Protocol { get; set; } = "";
    public string? Error { get; set; }
    public bool? HealthPassed { get; set; }
    public List<SmartAttribute> Attributes { get; } = new();

    // Execução de auto-teste
    public bool SelfTestSupported { get; set; }
    public bool ConveyanceSupported { get; set; }
    public bool SelectiveSupported { get; set; }
    public bool SelfTestRunning { get; set; }
    public int SelfTestRemainingPercent { get; set; }
    public string SelfTestExecText { get; set; } = "";
    public int ShortTestMinutes { get; set; }
    public int ExtendedTestMinutes { get; set; }
    public List<SelfTestEntry> SelfTests { get; } = new();

    // Log de erros
    public int DeviceErrorCount { get; set; }
    public List<DeviceErrorEntry> Errors { get; } = new();

    // NVMe
    public int? NvmeCriticalWarning { get; set; }
    public int? AvailableSpare { get; set; }
    public int? SpareThreshold { get; set; }
    public int? PercentUsed { get; set; }
    public long? MediaErrors { get; set; }
    public long? ErrorLogEntries { get; set; }
    public long? PowerOnHours { get; set; }
    public long? UnsafeShutdowns { get; set; }
    public double? TemperatureC { get; set; }

    // Contadores consolidados de bad blocks
    public long Reallocated { get; set; } = -1;
    public long Pending { get; set; } = -1;
    public long OfflineUncorrectable { get; set; } = -1;
    public long ReportedUncorrect { get; set; } = -1;
    public long ReallocEvents { get; set; } = -1;
    public long CrcErrors { get; set; } = -1;

    public List<SmartKeyCounter> KeyCounters { get; } = new();
    public HashSet<long> SuspectLbas { get; } = new();

    public BadBlockLevel Level { get; set; } = BadBlockLevel.Unknown;
    public string VerdictTitle { get; set; } = "";
    public string VerdictText { get; set; } = "";
    public List<string> Recommendations { get; } = new();
}

/// <summary>LBA defeituoso/suspeito identificado (superfície, auto-teste SMART ou log de erros).</summary>
public sealed class BadLbaEntry : System.ComponentModel.INotifyPropertyChanged
{
    public long Lba { get; init; }
    public string Source { get; set; } = "";
    private string _location = "—", _file = "—", _status = "Detectado";
    public string Location { get => _location; set { _location = value; OnChanged(nameof(Location)); } }
    public string AffectedFile { get => _file; set { _file = value; OnChanged(nameof(AffectedFile)); } }
    public string Status { get => _status; set { _status = value; OnChanged(nameof(Status)); } }
    public string LbaText => Lba.ToString("N0");

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged(string p) => PropertyChanged?.Invoke(this, new(p));
}

public sealed class LbaLocation
{
    public string? Letter { get; init; }
    public long? Cluster { get; init; }
    public string Region { get; init; } = "";
}

// =======================================================================================
// Diagnóstico
// =======================================================================================

/// <summary>
/// Diagnóstico S.M.A.R.T. orientado a bad blocks, com a lógica de interpretação portada do smartmontools 7.5
/// (ataprint.cpp, nvmeprint.cpp, atacmds.cpp e tabela de atributos padrão do drivedb.h).
/// </summary>
public static class SmartDiagnostics
{
    /// <summary>Atributos ATA ligados a setores defeituosos / realocação.</summary>
    public static readonly HashSet<int> BadBlockAttributeIds = new() { 5, 183, 187, 196, 197, 198, 199, 160, 169, 171, 172, 181, 182 };

    // Nomes padrão (entrada "DEFAULT" do drivedb.h do smartmontools 7.5)
    private static readonly Dictionary<int, string> AttrNames = new()
    {
        [1] = "Raw_Read_Error_Rate", [2] = "Throughput_Performance", [3] = "Spin_Up_Time", [4] = "Start_Stop_Count",
        [5] = "Reallocated_Sector_Ct", [6] = "Read_Channel_Margin", [7] = "Seek_Error_Rate", [8] = "Seek_Time_Performance",
        [9] = "Power_On_Hours", [10] = "Spin_Retry_Count", [11] = "Calibration_Retry_Count", [12] = "Power_Cycle_Count",
        [13] = "Read_Soft_Error_Rate", [22] = "Helium_Level", [23] = "Helium_Condition_Lower", [24] = "Helium_Condition_Upper",
        [160] = "Uncorrectable_Error_Cnt", [161] = "Spare_Blocks_Remaining", [163] = "Initial_Bad_Block_Count",
        [164] = "Total_Erase_Count", [165] = "Max_Erase_Count", [166] = "Min_Erase_Count", [167] = "Average_Erase_Count",
        [168] = "Max_Erase_Count_of_Spec", [169] = "Power_On_Uncorr_Err_Cnt", [170] = "Available_Reservd_Space",
        [171] = "Program_Fail_Count", [172] = "Erase_Fail_Count", [173] = "Wear_Leveling_Count", [174] = "Unexpect_Power_Loss_Ct",
        [175] = "Program_Fail_Count_Chip", [176] = "Erase_Fail_Count_Chip", [177] = "Wear_Leveling_Count",
        [178] = "Used_Rsvd_Blk_Cnt_Chip", [179] = "Used_Rsvd_Blk_Cnt_Tot", [180] = "Unused_Rsvd_Blk_Cnt_Tot",
        [181] = "Program_Fail_Cnt_Total", [182] = "Erase_Fail_Count_Total", [183] = "Runtime_Bad_Block",
        [184] = "End-to-End_Error", [187] = "Reported_Uncorrect", [188] = "Command_Timeout", [189] = "High_Fly_Writes",
        [190] = "Airflow_Temperature_Cel", [191] = "G-Sense_Error_Rate", [192] = "Power-Off_Retract_Count",
        [193] = "Load_Cycle_Count", [194] = "Temperature_Celsius", [195] = "Hardware_ECC_Recovered",
        [196] = "Reallocated_Event_Count", [197] = "Current_Pending_Sector", [198] = "Offline_Uncorrectable",
        [199] = "UDMA_CRC_Error_Count", [200] = "Multi_Zone_Error_Rate", [201] = "Soft_Read_Error_Rate",
        [202] = "Data_Address_Mark_Errs", [203] = "Run_Out_Cancel", [204] = "Soft_ECC_Correction",
        [205] = "Thermal_Asperity_Rate", [206] = "Flying_Height", [207] = "Spin_High_Current", [208] = "Spin_Buzz",
        [209] = "Offline_Seek_Performnce", [220] = "Disk_Shift", [221] = "G-Sense_Error_Rate", [222] = "Loaded_Hours",
        [223] = "Load_Retry_Count", [224] = "Load_Friction", [225] = "Load_Cycle_Count", [226] = "Load-in_Time",
        [227] = "Torq-amp_Count", [228] = "Power-off_Retract_Count", [230] = "Head_Amplitude", [231] = "Temperature_Celsius",
        [232] = "Available_Reservd_Space", [233] = "Media_Wearout_Indicator", [240] = "Head_Flying_Hours",
        [241] = "Total_LBAs_Written", [242] = "Total_LBAs_Read", [250] = "Read_Error_Retry_Rate", [254] = "Free_Fall_Sensor",
    };

    private static string AttrName(int id) => AttrNames.TryGetValue(id, out var n) ? n : $"Unknown_Attribute_{id}";

    // -----------------------------------------------------------------------------------
    // Enumeração de discos físicos
    // -----------------------------------------------------------------------------------

    public static List<PhysicalDiskTarget> EnumerateDisks()
    {
        // Mapa disco → volumes (letras)
        var volumesByDisk = new Dictionary<int, List<DiskVolumeExtent>>();
        foreach (var d in SafeDrives())
        {
            string letter = d.Name.TrimEnd('\\');
            int bpc = 4096;
            try { if (GetDiskFreeSpace(d.Name, out uint spc, out uint bps, out _, out _)) bpc = (int)(spc * bps); } catch { }
            string fs = "";
            try { fs = d.DriveFormat; } catch { }
            foreach (var (disk, off, len) in SmartNative.GetVolumeExtents($@"\\.\{letter}"))
            {
                if (!volumesByDisk.TryGetValue(disk, out var l)) volumesByDisk[disk] = l = new();
                l.Add(new DiskVolumeExtent { Letter = letter, StartOffset = off, Length = len, BytesPerCluster = bpc, FileSystem = fs });
            }
        }

        var list = new List<PhysicalDiskTarget>();
        for (int i = 0; i < 32; i++)
        {
            using var h = SmartNative.Open($@"\\.\PhysicalDrive{i}", false);
            if (h == null) continue;
            var desc = SmartNative.QueryDeviceDescriptor(h);
            if (desc == null) continue;
            if (desc.BusType is 14 or 15 or 16) continue; // virtual / VHD / Storage Spaces

            long size = 0; int bps = 512;
            using (var hr = SmartNative.OpenRead($@"\\.\PhysicalDrive{i}"))
                if (hr != null) SmartNative.GetGeometry(hr, out size, out bps);
            if (size <= 0) SmartNative.GetGeometry(h, out size, out bps);

            bool? seek = SmartNative.QuerySeekPenalty(h);
            string model = string.Join(" ", new[] { desc.Vendor, desc.Product }.Where(s => !string.IsNullOrWhiteSpace(s))).Trim();
            list.Add(new PhysicalDiskTarget
            {
                Index = i,
                Model = model,
                Serial = desc.Serial,
                Firmware = desc.Revision,
                BusType = desc.BusType,
                IsSsd = desc.BusType == 17 ? true : seek.HasValue ? !seek.Value : null,
                SizeBytes = size,
                SectorSize = bps,
                Volumes = volumesByDisk.TryGetValue(i, out var v) ? v.OrderBy(x => x.StartOffset).ToList() : new()
            });
        }
        return list;
    }

    private static IEnumerable<DriveInfo> SafeDrives()
    {
        DriveInfo[] all;
        try { all = DriveInfo.GetDrives(); } catch { yield break; }
        foreach (var d in all)
        {
            bool ok;
            try { ok = d.IsReady && d.DriveType is DriveType.Fixed or DriveType.Removable; } catch { ok = false; }
            if (ok) yield return d;
        }
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    private static extern bool GetDiskFreeSpace(string root, out uint sectorsPerCluster, out uint bytesPerSector,
        out uint freeClusters, out uint totalClusters);

    // -----------------------------------------------------------------------------------
    // Leitura S.M.A.R.T.
    // -----------------------------------------------------------------------------------

    public static SmartSnapshot Read(PhysicalDiskTarget disk)
    {
        var snap = new SmartSnapshot { Disk = disk };
        try
        {
            if (disk.IsNvme) ReadNvme(disk, snap);
            else ReadAta(disk, snap);
        }
        catch (Exception ex)
        {
            snap.Error = ex.Message;
        }
        Assess(snap);
        return snap;
    }

    private static void ReadAta(PhysicalDiskTarget disk, SmartSnapshot snap)
    {
        snap.Protocol = "ATA S.M.A.R.T. (IOCTL_ATA_PASS_THROUGH / SMART_RCV_DRIVE_DATA)";
        using var h = SmartNative.Open(disk.DevicePath, true);
        if (h == null)
        {
            snap.Error = "Não foi possível abrir o disco com acesso de leitura/escrita (requer administrador).";
            return;
        }

        if (!SmartNative.ReadSmartSector(h, SmartNative.ATA_SMART_READ_VALUES, 0, out var values))
        {
            snap.Error = disk.BusType == 7
                ? "Controladora USB sem ponte SAT: o adaptador não repassa comandos S.M.A.R.T."
                : "O disco/controladora não respondeu ao comando SMART READ DATA.";
            return;
        }
        snap.Available = true;
        SmartNative.ReadSmartSector(h, SmartNative.ATA_SMART_READ_THRESHOLDS, 1, out var thresholds);

        // ---- Atributos (ata_smart_values / ata_smart_thresholds_pvt) ----
        for (int i = 0; i < 30; i++)
        {
            int o = 2 + i * 12;
            int id = values[o];
            if (id == 0) continue;
            ushort flags = BinaryPrimitives.ReadUInt16LittleEndian(values.AsSpan(o + 1));
            int cur = values[o + 3], worst = values[o + 4];
            long raw48 = 0;
            for (int k = 5; k >= 0; k--) raw48 = (raw48 << 8) | values[o + 5 + k];
            int thr = 0;
            for (int t = 0; t < 30; t++)
                if (thresholds[2 + t * 12] == id) { thr = thresholds[2 + t * 12 + 1]; break; }

            long val = raw48;
            string rawText = raw48.ToString("N0");
            switch (id)
            {
                case 5: case 196: // raw16(raw16)
                    val = raw48 & 0xFFFF; rawText = val.ToString("N0"); break;
                case 9: case 240: // raw24(raw8)
                    val = raw48 & 0xFFFFFF; rawText = val.ToString("N0") + " h"; break;
                case 190: case 194: // tempminmax
                    val = raw48 & 0xFF;
                    int mn = (int)((raw48 >> 16) & 0xFF), mx = (int)((raw48 >> 32) & 0xFF);
                    rawText = mn > 0 && mx >= mn && mx < 128 ? $"{val} °C (mín/máx {mn}/{mx})" : $"{val} °C";
                    break;
            }
            snap.Attributes.Add(new SmartAttribute
            {
                Id = id, Name = AttrName(id), Flags = flags, Current = cur, Worst = worst,
                Threshold = thr, RawValue = val, RawText = rawText
            });
        }

        long Raw(int id) => snap.Attributes.FirstOrDefault(a => a.Id == id)?.RawValue ?? -1;
        snap.Reallocated = Raw(5);
        snap.Pending = Raw(197);
        snap.OfflineUncorrectable = Raw(198);
        snap.ReportedUncorrect = Raw(187);
        snap.ReallocEvents = Raw(196);
        snap.CrcErrors = Raw(199);
        if (Raw(9) >= 0) snap.PowerOnHours = Raw(9);
        var temp = snap.Attributes.FirstOrDefault(a => a.Id == 194) ?? snap.Attributes.FirstOrDefault(a => a.Id == 190);
        if (temp != null && temp.RawValue is > 0 and < 100) snap.TemperatureC = temp.RawValue;

        // ---- Capacidades (offsets de ata_smart_values) ----
        byte exec = values[363];
        byte offlineCap = values[367];
        ushort smartCap = BinaryPrimitives.ReadUInt16LittleEndian(values.AsSpan(368));
        byte errlogCap = values[370];
        snap.SelfTestSupported = (offlineCap & 0x10) != 0;
        snap.ConveyanceSupported = (offlineCap & 0x20) != 0;
        snap.SelectiveSupported = (offlineCap & 0x40) != 0;
        snap.ShortTestMinutes = values[372];
        snap.ExtendedTestMinutes = values[373] == 0xFF ? BinaryPrimitives.ReadUInt16LittleEndian(values.AsSpan(375)) : values[373];
        _ = smartCap;

        int execStatus = exec >> 4;
        snap.SelfTestRunning = execStatus == 0xF;
        snap.SelfTestRemainingPercent = snap.SelfTestRunning ? (exec & 0x0F) * 10 : 0;
        snap.SelfTestExecText = execStatus switch
        {
            0x0 => "Último auto-teste concluído sem erros (ou nenhum executado).",
            0x1 => "Último auto-teste abortado pelo host.",
            0x2 => "Último auto-teste interrompido (reset do host).",
            0x3 => "Erro fatal impediu a conclusão do último auto-teste.",
            0x4 => "Último auto-teste falhou (elemento desconhecido).",
            0x5 => "Último auto-teste falhou no teste elétrico.",
            0x6 => "Último auto-teste falhou no servo/seek.",
            0x7 => "Último auto-teste falhou na LEITURA (setor defeituoso encontrado).",
            0x8 => "Último auto-teste indicou possível dano de manuseio.",
            0xF => $"Auto-teste em andamento — {snap.SelfTestRemainingPercent}% restante.",
            _ => $"Status desconhecido (0x{execStatus:X})."
        };

        snap.HealthPassed = SmartNative.SmartReturnStatus(h);

        // ---- Self-test log (porte de ataPrintSmartSelfTestlog) ----
        if (snap.SelfTestSupported && SmartNative.ReadSmartLog(h, SmartNative.LOG_SELFTEST, out var stl))
            ParseAtaSelfTestLog(stl, snap);

        // ---- Summary error log (log 0x01) ----
        if ((errlogCap & 0x01) != 0 && SmartNative.ReadSmartLog(h, SmartNative.LOG_SUMMARY_ERROR, out var el))
            ParseAtaErrorLog(el, snap);
    }

    public static void ParseAtaSelfTestLog(byte[] log, SmartSnapshot snap)
    {
        int mostRecent = log[508];
        if (mostRecent == 0) return;
        int testnum = 1;
        bool extOkSeen = false;
        for (int i = 20; i >= 0; i--)
        {
            int j = (i + mostRecent) % 21;
            int o = 2 + j * 24;
            bool nonEmpty = false;
            for (int k = 0; k < 24 && !nonEmpty; k++) nonEmpty = log[o + k] != 0;
            if (!nonEmpty) continue;

            byte type = log[o], status = log[o + 1];
            ushort hours = BinaryPrimitives.ReadUInt16LittleEndian(log.AsSpan(o + 2));
            uint lba = BinaryPrimitives.ReadUInt32LittleEndian(log.AsSpan(o + 5));
            int st = status >> 4;
            bool failed = st is >= 3 and <= 8;
            bool outdated = failed && extOkSeen;

            string typeText = type switch
            {
                0x00 => "Offline", 0x01 => "Curto", 0x02 => "Estendido", 0x03 => "Transporte",
                0x04 => "Seletivo", 0x7F => "Abortar", 0x81 => "Curto (cativo)", 0x82 => "Estendido (cativo)",
                0x83 => "Transporte (cativo)", 0x84 => "Seletivo (cativo)",
                _ => $"Fabricante (0x{type:X2})"
            };
            string statusText = st switch
            {
                0x0 => "Concluído sem erros",
                0x1 => "Abortado pelo host",
                0x2 => "Interrompido (reset)",
                0x3 => "Erro fatal ou desconhecido",
                0x4 => "Concluído: falha desconhecida",
                0x5 => "Concluído: falha elétrica",
                0x6 => "Concluído: falha de servo/seek",
                0x7 => "Concluído: FALHA DE LEITURA",
                0x8 => "Concluído: dano de manuseio",
                0xF => "Em andamento",
                _ => $"Desconhecido (0x{st:X})"
            };

            long? failing = failed && lba < 0xFFFFFFFF ? lba : null;
            snap.SelfTests.Add(new SelfTestEntry
            {
                Number = testnum++, TypeText = typeText, StatusText = outdated ? statusText + " (superado)" : statusText,
                RemainingPercent = (status & 0x0F) * 10, LifetimeHours = hours, FailingLba = failing,
                Failed = failed, Outdated = outdated
            });
            if (failing.HasValue && !outdated) snap.SuspectLbas.Add(failing.Value);
            if (!failed && st == 0 && (type & 0x7F) == 0x02) extOkSeen = true;
        }
    }

    public static void ParseAtaErrorLog(byte[] log, SmartSnapshot snap)
    {
        snap.DeviceErrorCount = BinaryPrimitives.ReadUInt16LittleEndian(log.AsSpan(452));
        int ptr = log[1];
        if (ptr == 0) return;
        int num = snap.DeviceErrorCount;
        for (int k = 4; k >= 0; k--)
        {
            int i = (ptr + k) % 5;
            int o = 2 + i * 90;
            int e = o + 60; // error_struct após 5 command structs
            bool nonEmpty = false;
            for (int b = 0; b < 90 && !nonEmpty; b++) nonEmpty = log[o + b] != 0;
            if (!nonEmpty) continue;

            byte er = log[e + 1], st = log[e + 7], dh = log[e + 6];
            long lba = ((long)(dh & 0x0F) << 24) | ((long)log[e + 5] << 16) | ((long)log[e + 4] << 8) | log[e + 3];
            ushort ts = BinaryPrimitives.ReadUInt16LittleEndian(log.AsSpan(e + 28));
            byte cmd = log[o + 4 * 12 + 7]; // comando que falhou (última command struct)

            var parts = new List<string>();
            bool unc = (er & 0x40) != 0;
            if (unc) parts.Add("UNC — dado incorrigível (setor ilegível)");
            if ((er & 0x10) != 0) parts.Add("IDNF — setor não encontrado");
            if ((er & 0x80) != 0) parts.Add("ICRC — erro de CRC na interface (cabo/porta)");
            if ((er & 0x04) != 0) parts.Add("ABRT — comando abortado");
            if ((er & 0x01) != 0) parts.Add("AMNF — marca de endereço ausente");
            if (parts.Count == 0) parts.Add($"Erro 0x{er:X2} / status 0x{st:X2}");
            string cmdName = cmd switch
            {
                0x20 or 0x21 or 0x24 or 0x25 or 0xC4 or 0xC8 or 0x29 or 0x60 => "LEITURA",
                0x30 or 0x31 or 0x34 or 0x35 or 0xC5 or 0xCA or 0x39 or 0x61 => "ESCRITA",
                0x40 or 0x42 => "VERIFICAÇÃO",
                0xB0 => "SMART",
                _ => $"cmd 0x{cmd:X2}"
            };

            bool lbaUseful = unc || (er & 0x10) != 0;
            snap.Errors.Add(new DeviceErrorEntry
            {
                Number = num--, LifetimeHours = ts, Lba = lbaUseful ? lba : null,
                Description = $"{cmdName}: {string.Join(", ", parts)}", IsUncorrectable = unc
            });
            if (unc && lba > 0) snap.SuspectLbas.Add(lba);
        }
    }

    private static void ReadNvme(PhysicalDiskTarget disk, SmartSnapshot snap)
    {
        snap.Protocol = "NVMe Health Log (IOCTL_STORAGE_QUERY_PROPERTY / PROTOCOL_COMMAND)";
        using var h = SmartNative.Open(disk.DevicePath, true) ?? SmartNative.Open(disk.DevicePath, false);
        if (h == null) { snap.Error = "Não foi possível abrir o dispositivo NVMe."; return; }

        if (!SmartNative.NvmeGetLogPage(h, SmartNative.NVME_LOG_SMART, 512, out var s))
        {
            snap.Error = "O driver NVMe não retornou o SMART/Health Log (página 02h).";
            return;
        }
        snap.Available = true;
        int cw = s[0];
        snap.NvmeCriticalWarning = cw;
        int kelvin = BinaryPrimitives.ReadUInt16LittleEndian(s.AsSpan(1));
        if (kelvin > 0) snap.TemperatureC = kelvin - 273;
        snap.AvailableSpare = s[3];
        snap.SpareThreshold = s[4];
        snap.PercentUsed = s[5];
        snap.PowerOnHours = SmartNative.ReadLe128(s, 128);
        snap.UnsafeShutdowns = SmartNative.ReadLe128(s, 144);
        snap.MediaErrors = SmartNative.ReadLe128(s, 160);
        snap.ErrorLogEntries = SmartNative.ReadLe128(s, 176);
        snap.HealthPassed = cw == 0 || (cw & ~0x02) == 0 ? (cw & 0x02) == 0 ? true : true : false;
        if ((cw & (0x01 | 0x04 | 0x08 | 0x10)) != 0) snap.HealthPassed = false;

        // Device Self-test suportado? OACS bit 4 (Identify Controller, offset 256). EDSTT (offset 316) = minutos do estendido.
        if (SmartNative.NvmeIdentifyController(h, out var idc))
        {
            ushort oacs = BinaryPrimitives.ReadUInt16LittleEndian(idc.AsSpan(256));
            snap.SelfTestSupported = (oacs & 0x10) != 0;
            snap.ExtendedTestMinutes = BinaryPrimitives.ReadUInt16LittleEndian(idc.AsSpan(316));
            snap.ShortTestMinutes = 2;
        }

        if (SmartNative.NvmeGetLogPage(h, SmartNative.NVME_LOG_SELF_TEST, 564, out var stl))
        {
            snap.SelfTestSupported |= stl.Any(b => b != 0);
            ParseNvmeSelfTestLog(stl, snap);
        }
    }

    public static void ParseNvmeSelfTestLog(byte[] log, SmartSnapshot snap)
    {
        int curOp = log[0] & 0x0F;
        int curCompletion = log[1] & 0x7F;
        snap.SelfTestRunning = curOp != 0;
        snap.SelfTestRemainingPercent = snap.SelfTestRunning ? 100 - curCompletion : 0;
        snap.SelfTestExecText = curOp switch
        {
            0 => "Nenhum auto-teste em execução.",
            1 => $"Auto-teste CURTO em andamento — {curCompletion}% concluído.",
            2 => $"Auto-teste ESTENDIDO em andamento — {curCompletion}% concluído.",
            0xE => $"Auto-teste do fabricante em andamento — {curCompletion}% concluído.",
            _ => $"Operação 0x{curOp:X} em andamento."
        };

        int n = 1;
        for (int i = 0; i < 20; i++)
        {
            int o = 4 + i * 28;
            int op = log[o] >> 4, res = log[o] & 0x0F;
            if (op == 0 || res == 0xF) continue;
            byte valid = log[o + 2];
            long poh = (long)BinaryPrimitives.ReadUInt64LittleEndian(log.AsSpan(o + 4));
            long? lba = (valid & 0x02) != 0 ? (long)BinaryPrimitives.ReadUInt64LittleEndian(log.AsSpan(o + 16)) : null;
            bool failed = res is 0x5 or 0x6 or 0x7;
            string typeText = op switch { 1 => "Curto", 2 => "Estendido", 0xE => "Fabricante", _ => $"0x{op:X}" };
            string statusText = res switch
            {
                0x0 => "Concluído sem erros",
                0x1 => "Abortado: comando de auto-teste",
                0x2 => "Abortado: reset da controladora",
                0x3 => "Abortado: namespace removido",
                0x4 => "Abortado: Format NVM",
                0x5 => "Erro fatal ou desconhecido",
                0x6 => "Concluído: segmento com falha desconhecido",
                0x7 => $"Concluído: FALHA no segmento {log[o + 1]}",
                0x8 => "Abortado: motivo desconhecido",
                0x9 => "Abortado: sanitize",
                _ => $"Resultado 0x{res:X}"
            };
            snap.SelfTests.Add(new SelfTestEntry
            {
                Number = n++, TypeText = typeText, StatusText = statusText, LifetimeHours = poh,
                FailingLba = failed ? lba : null, Failed = failed
            });
            if (failed && lba.HasValue) snap.SuspectLbas.Add(lba.Value);
        }
    }

    // -----------------------------------------------------------------------------------
    // Avaliação de bad blocks
    // -----------------------------------------------------------------------------------

    public static void Assess(SmartSnapshot s)
    {
        var rec = s.Recommendations;
        rec.Clear();
        s.KeyCounters.Clear();

        static SmartKeyCounter C(string t, long v, string hint, long warnAt = 1, long critAt = long.MaxValue) => new()
        {
            Title = t,
            Value = v < 0 ? "—" : v.ToString("N0"),
            Hint = hint,
            Severity = v < 0 ? -1 : v >= critAt ? 2 : v >= warnAt ? 1 : 0
        };

        if (!s.Available)
        {
            s.Level = BadBlockLevel.Unknown;
            s.VerdictTitle = "S.M.A.R.T. INDISPONÍVEL PARA ESTE DISPOSITIVO";
            s.VerdictText = (s.Error ?? "O dispositivo não respondeu.") +
                            " A varredura de superfície continua disponível e detecta setores ilegíveis por leitura direta.";
            rec.Add("Execute a VARREDURA COMPLETA da superfície para identificar setores ilegíveis por leitura direta.");
            if (s.Disk.BusType == 7) rec.Add("Conecte o disco diretamente na placa-mãe (SATA/M.2) para liberar o acesso S.M.A.R.T.");
            return;
        }

        int level = 0;
        var why = new List<string>();

        if (s.Disk.IsNvme)
        {
            int cw = s.NvmeCriticalWarning ?? 0;
            long media = s.MediaErrors ?? 0;
            s.KeyCounters.Add(C("ERROS DE MÍDIA", s.MediaErrors ?? -1, "Leituras/escritas incorrigíveis na NAND", 1, 10));
            s.KeyCounters.Add(new SmartKeyCounter
            {
                Title = "RESERVA DISPONÍVEL",
                Value = s.AvailableSpare is int sp ? $"{sp}%" : "—",
                Hint = s.SpareThreshold is int th ? $"Limite do fabricante: {th}%" : "Blocos reserva para remapeamento",
                Severity = s.AvailableSpare is int a && s.SpareThreshold is int t2 ? (a <= t2 ? 2 : a <= t2 + 10 ? 1 : 0) : -1
            });
            s.KeyCounters.Add(new SmartKeyCounter
            {
                Title = "DESGASTE (USO)",
                Value = s.PercentUsed is int pu ? $"{pu}%" : "—",
                Hint = "Vida útil estimada consumida",
                Severity = s.PercentUsed is int p ? (p >= 100 ? 2 : p >= 80 ? 1 : 0) : -1
            });
            s.KeyCounters.Add(C("ENTRADAS NO LOG DE ERROS", s.ErrorLogEntries ?? -1, "Eventos registrados pela controladora", 1000000));
            s.KeyCounters.Add(new SmartKeyCounter
            {
                Title = "AVISO CRÍTICO",
                Value = cw == 0 ? "Nenhum" : $"0x{cw:X2}",
                Hint = cw == 0 ? "Sem alertas da controladora" : NvmeWarningText(cw),
                Severity = cw == 0 ? 0 : (cw & 0x1D) != 0 ? 2 : 1
            });

            if ((cw & 0x01) != 0) { level = 2; why.Add("reserva de blocos abaixo do limite"); }
            if ((cw & 0x04) != 0) { level = 2; why.Add("confiabilidade degradada por erros de mídia"); }
            if ((cw & 0x08) != 0) { level = 2; why.Add("mídia colocada em modo SOMENTE LEITURA"); }
            if ((cw & 0x10) != 0) { level = 2; why.Add("falha no backup volátil"); }
            if ((cw & 0x02) != 0) { level = Math.Max(level, 1); why.Add("temperatura acima do limite"); }
            if (media > 0) { level = Math.Max(level, media >= 10 ? 2 : 1); why.Add($"{media:N0} erro(s) de mídia (blocos NAND ilegíveis)"); }
            if (s.AvailableSpare is int av && s.SpareThreshold is int thr && av <= thr + 10 && av > thr) { level = Math.Max(level, 1); why.Add($"reserva baixa ({av}%)"); }
            if (s.PercentUsed is >= 100) { level = Math.Max(level, 1); why.Add("vida útil nominal esgotada"); }
        }
        else
        {
            s.KeyCounters.Add(C("SETORES REALOCADOS (5)", s.Reallocated, "Já substituídos pela reserva do disco", 1, 100));
            s.KeyCounters.Add(C("SETORES PENDENTES (197)", s.Pending, "Ilegíveis, aguardando realocação", 1, 1));
            s.KeyCounters.Add(C("INCORRIGÍVEIS OFFLINE (198)", s.OfflineUncorrectable, "Falharam na varredura interna", 1, 1));
            s.KeyCounters.Add(C("ERROS REPORTADOS (187)", s.ReportedUncorrect, "Leituras que o ECC não corrigiu", 1, 50));
            s.KeyCounters.Add(C("EVENTOS DE REALOCAÇÃO (196)", s.ReallocEvents, "Tentativas de remapeamento", 1, 100));
            s.KeyCounters.Add(C("ERROS DE CRC (199)", s.CrcErrors, "Cabo/porta SATA — não é defeito de superfície", 1, long.MaxValue));

            if (s.Pending > 0) { level = 2; why.Add($"{s.Pending:N0} setor(es) PENDENTE(S) ilegível(is) neste momento"); }
            if (s.OfflineUncorrectable > 0) { level = 2; why.Add($"{s.OfflineUncorrectable:N0} setor(es) incorrigível(is) na varredura offline"); }
            if (s.Reallocated > 0) { level = Math.Max(level, s.Reallocated >= 100 ? 2 : 1); why.Add($"{s.Reallocated:N0} setor(es) já realocado(s)"); }
            if (s.ReportedUncorrect > 0) { level = Math.Max(level, 1); why.Add($"{s.ReportedUncorrect:N0} erro(s) incorrigível(is) reportado(s)"); }
            foreach (var a in s.Attributes.Where(a => a.FailingNow && a.IsPrefailure))
            { level = 2; why.Add($"atributo {a.Id} ({a.Name}) abaixo do limite de pré-falha"); }
        }

        if (s.HealthPassed == false) { level = 2; why.Insert(0, "o próprio disco reporta FALHA na autoavaliação S.M.A.R.T."); }
        var lastFail = s.SelfTests.FirstOrDefault(t => t.Failed && !t.Outdated);
        if (lastFail != null)
        {
            level = 2;
            why.Add($"auto-teste #{lastFail.Number} falhou{(lastFail.FailingLba.HasValue ? $" no LBA {lastFail.FailingLba:N0}" : "")}");
        }
        if (s.Errors.Any(e => e.IsUncorrectable)) { level = Math.Max(level, 1); why.Add("log de erros contém leituras incorrigíveis (UNC)"); }

        s.Level = level switch { 0 => BadBlockLevel.Healthy, 1 => BadBlockLevel.Attention, _ => BadBlockLevel.Critical };
        switch (s.Level)
        {
            case BadBlockLevel.Healthy:
                s.VerdictTitle = "S.M.A.R.T. SEM INDÍCIOS DE BAD BLOCKS";
                s.VerdictText = "Nenhum setor pendente, incorrigível ou realocado foi reportado pelo firmware. " +
                                "Para confirmar a superfície inteira, rode o auto-teste ESTENDIDO ou a varredura completa.";
                rec.Add("Opcional: execute o auto-teste ESTENDIDO periodicamente para que o firmware varra toda a superfície.");
                break;
            case BadBlockLevel.Attention:
                s.VerdictTitle = "ATENÇÃO · SINAIS DE DEGRADAÇÃO DA MÍDIA";
                s.VerdictText = "Detectado: " + string.Join("; ", why) + ".";
                rec.Add("Faça BACKUP dos arquivos importantes.");
                rec.Add("Execute o auto-teste ESTENDIDO para o firmware reavaliar e remapear setores fracos.");
                rec.Add("Monitore estes contadores: se aumentarem entre testes, a mídia está se deteriorando.");
                break;
            default:
                s.VerdictTitle = "CRÍTICO · BAD BLOCKS ATIVOS NO DISCO";
                s.VerdictText = "Detectado: " + string.Join("; ", why) + ".";
                rec.Add("Faça BACKUP IMEDIATO — setores ilegíveis podem corromper arquivos a qualquer leitura.");
                rec.Add("Use LOCALIZAR ARQUIVOS para saber exatamente quais arquivos estão sobre os LBAs defeituosos.");
                rec.Add("Execute REPARAR COM CHKDSK /R: o Windows marca os clusters ruins e a regravação força o disco a realocar setores pendentes.");
                rec.Add("Depois, rode o auto-teste ESTENDIDO e confira se 'Pendentes' zerou e 'Realocados' parou de crescer.");
                rec.Add("Se os contadores continuarem subindo, substitua o disco.");
                break;
        }

        if (!s.Disk.IsNvme && s.CrcErrors > 0)
            rec.Add($"Erros de CRC ({s.CrcErrors:N0}) indicam cabo SATA ou porta com mau contato — troque o cabo; isso NÃO é bad block.");
    }

    private static string NvmeWarningText(int cw)
    {
        var p = new List<string>();
        if ((cw & 0x01) != 0) p.Add("reserva baixa");
        if ((cw & 0x02) != 0) p.Add("temperatura");
        if ((cw & 0x04) != 0) p.Add("confiabilidade degradada");
        if ((cw & 0x08) != 0) p.Add("somente leitura");
        if ((cw & 0x10) != 0) p.Add("backup volátil");
        return string.Join(", ", p);
    }

    // -----------------------------------------------------------------------------------
    // Auto-testes (porte de ataSmartTest / nvme_self_test)
    // -----------------------------------------------------------------------------------

    public enum SelfTestKind { Short, Extended, Conveyance, Abort }

    public static (bool Ok, string Message) StartSelfTest(PhysicalDiskTarget disk, SelfTestKind kind)
    {
        using var h = SmartNative.Open(disk.DevicePath, true);
        if (h == null) return (false, "Não foi possível abrir o disco (execute como administrador).");

        if (disk.IsNvme)
        {
            byte stc = kind switch { SelfTestKind.Short => 1, SelfTestKind.Extended => 2, SelfTestKind.Abort => 0xF, _ => 0 };
            if (stc == 0) return (false, "Teste de transporte não existe em NVMe.");
            return SmartNative.NvmeDeviceSelfTest(h, stc)
                ? (true, kind == SelfTestKind.Abort ? "Auto-teste NVMe abortado." : "Auto-teste NVMe iniciado. O SSD continua utilizável durante o teste.")
                : (false, $"O driver recusou o Device Self-test (erro {SmartNative.LastError}). Requer Windows 10 2004+ e SSD com NVMe 1.3+.");
        }

        if (kind != SelfTestKind.Abort && SmartNative.ReadSmartSector(h, SmartNative.ATA_SMART_READ_VALUES, 0, out var v) && (v[363] >> 4) == 0xF)
            return (false, $"Já existe um auto-teste em andamento ({(v[363] & 0xF) * 10}% restante). Aborte-o antes de iniciar outro.");

        byte sub = kind switch
        {
            SelfTestKind.Short => SmartNative.SHORT_SELF_TEST,
            SelfTestKind.Extended => SmartNative.EXTEND_SELF_TEST,
            SelfTestKind.Conveyance => SmartNative.CONVEYANCE_SELF_TEST,
            _ => SmartNative.ABORT_SELF_TEST
        };
        return SmartNative.ExecuteOfflineImmediate(h, sub)
            ? (true, kind == SelfTestKind.Abort ? "Auto-teste abortado." : "Auto-teste iniciado em modo off-line: o disco continua utilizável durante o teste.")
            : (false, "O disco ou a controladora recusou o comando SMART EXECUTE OFF-LINE IMMEDIATE.");
    }

    /// <summary>
    /// Auto-teste SELETIVO (ATA) sobre spans ao redor dos LBAs suspeitos — o firmware relê exatamente
    /// essas regiões e realoca o que conseguir. Porte de ataWriteSelectiveSelfTestLog + SELECTIVE_SELF_TEST.
    /// </summary>
    public static (bool Ok, string Message) StartSelectiveSelfTest(PhysicalDiskTarget disk, IEnumerable<long> lbas, long margin = 2048)
    {
        if (disk.IsNvme) return (false, "Auto-teste seletivo é exclusivo de discos ATA/SATA.");
        ulong max = (ulong)Math.Max(1, disk.TotalSectors) - 1;
        var spans = new List<(ulong, ulong)>();
        foreach (var lba in lbas.OrderBy(x => x))
        {
            ulong st = (ulong)Math.Max(0, lba - margin), en = Math.Min(max, (ulong)(lba + margin));
            if (spans.Count > 0 && st <= spans[^1].Item2 + 1) spans[^1] = (spans[^1].Item1, Math.Max(spans[^1].Item2, en));
            else spans.Add((st, en));
        }
        if (spans.Count == 0) return (false, "Nenhum LBA suspeito para testar.");
        if (spans.Count > 5) spans = spans.Take(5).ToList();

        using var h = SmartNative.Open(disk.DevicePath, true);
        if (h == null) return (false, "Não foi possível abrir o disco (execute como administrador).");
        if (!SmartNative.ReadSmartSector(h, SmartNative.ATA_SMART_READ_VALUES, 0, out var v))
            return (false, "SMART indisponível.");
        if ((v[367] & 0x40) == 0) return (false, "Este disco não suporta auto-teste seletivo.");
        if ((v[363] >> 4) == 0xF) return (false, "Já existe um auto-teste em andamento.");
        if (!SmartNative.WriteSelectiveSpans(h, spans)) return (false, "Falha ao gravar o Selective Self-test Log (log 09h).");
        if (!SmartNative.ExecuteOfflineImmediate(h, SmartNative.SELECTIVE_SELF_TEST))
            return (false, "O disco recusou o início do auto-teste seletivo.");
        return (true, $"Auto-teste seletivo iniciado em {spans.Count} faixa(s): " +
                      string.Join(", ", spans.Select(s => $"{s.Item1:N0}–{s.Item2:N0}")));
    }

    // -----------------------------------------------------------------------------------
    // LBA → partição / cluster / arquivo
    // -----------------------------------------------------------------------------------

    public static LbaLocation Locate(PhysicalDiskTarget disk, long lba)
    {
        long byteOff = lba * disk.SectorSize;
        foreach (var v in disk.Volumes)
        {
            if (byteOff >= v.StartOffset && byteOff < v.StartOffset + v.Length)
            {
                long cluster = (byteOff - v.StartOffset) / Math.Max(512, v.BytesPerCluster);
                return new LbaLocation
                {
                    Letter = v.Letter, Cluster = cluster,
                    Region = $"{v.Letter} ({(string.IsNullOrEmpty(v.FileSystem) ? "?" : v.FileSystem)}) · cluster {cluster:N0}"
                };
            }
        }
        string region = lba < 2048 ? "Início do disco (MBR/GPT)"
            : lba >= disk.TotalSectors - 34 ? "Fim do disco (GPT de backup)"
            : "Fora de volumes montados (EFI/recuperação/não alocado)";
        return new LbaLocation { Region = region };
    }

    /// <summary>Descobre o arquivo dono do cluster via "fsutil volume querycluster" (NTFS, requer administrador).</summary>
    public static async Task<string> QueryFileAtClusterAsync(string letter, long cluster, CancellationToken ct = default)
    {
        try
        {
            var psi = new ProcessStartInfo("fsutil.exe", $"volume querycluster {letter} 0x{cluster:X}")
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true
            };
            using var p = Process.Start(psi);
            if (p == null) return "fsutil indisponível";
            var outTask = p.StandardOutput.ReadToEndAsync(ct);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(60));
            try { await p.WaitForExitAsync(cts.Token); }
            catch (OperationCanceledException) { try { p.Kill(true); } catch { } return "Tempo esgotado"; }
            string output = (await outTask).Trim();
            return ParseQueryCluster(output);
        }
        catch (Exception ex) { return "Falha: " + ex.Message; }
    }

    public static string ParseQueryCluster(string output)
    {
        if (string.IsNullOrWhiteSpace(output)) return "Cluster livre / sem arquivo";
        var lower = output.ToLowerInvariant();
        if (lower.Contains("free") || lower.Contains("livre") || lower.Contains("not in use") || lower.Contains("não está em uso")
            || lower.Contains("nao esta em uso") || lower.Contains("não usado"))
            return "Cluster livre (nenhum arquivo afetado)";
        if (lower.Contains("acesso negado") || lower.Contains("access is denied")) return "Requer administrador";

        var files = new List<string>();
        foreach (var raw in output.Split('\n'))
        {
            var line = raw.Trim();
            var m = Regex.Match(line, @"0x[0-9A-Fa-f]+\s+\S+\s+\S+\s+(?<rest>.+)$");
            string? path = null;
            if (m.Success) path = m.Groups["rest"].Value.Trim();
            if (string.IsNullOrWhiteSpace(path))
            {
                int slash = line.IndexOf('\\');
                if (slash >= 0) path = line[slash..].Trim();
            }
            if (!string.IsNullOrWhiteSpace(path) && path.Contains('\\')) files.Add(path);
        }
        if (files.Count > 0) return string.Join(" | ", files.Distinct());
        return output.Split('\n').Last().Trim();
    }

    // -----------------------------------------------------------------------------------
    // Resolução no sistema de arquivos
    // -----------------------------------------------------------------------------------

    /// <summary>
    /// Abre o CHKDSK /R visível para o volume: relê cada cluster, move dados legíveis, marca os clusters
    /// ruins no $BadClus (o Windows deixa de usá-los) e as regravações fazem o disco realocar setores pendentes.
    /// Em volumes em uso (ex.: C:) o próprio chkdsk oferece agendar para o próximo boot.
    /// </summary>
    public static bool LaunchChkdskRepair(string letter, out string message)
    {
        letter = letter.TrimEnd('\\');
        try
        {
            Process.Start(new ProcessStartInfo("cmd.exe",
                $"/k title Lumini Tray - Reparo de Bad Blocks ({letter}) & echo Reparo de setores defeituosos em {letter} (CHKDSK /R)... & echo. & chkdsk {letter} /r /x")
            { UseShellExecute = true });
            message = $"CHKDSK /R aberto para {letter}. Em volumes em uso, responda 'S' para agendar na próxima reinicialização.";
            return true;
        }
        catch (Exception ex)
        {
            message = "Não foi possível iniciar o CHKDSK: " + ex.Message;
            return false;
        }
    }
}
