using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Management;
using System.Text.RegularExpressions;

namespace LuminiTray;

/// <summary>
/// Cache centralizado para a topologia WMI Disco → Partição → Letra da Unidade,
/// evitando consultas WMI redundantes e garantindo descarte explícito de objetos COM.
/// </summary>
public static class DiskTopologyService
{
    private static readonly object LockObj = new();
    private static Dictionary<string, (string? Model, string? Bus)> _cachedMap = new(StringComparer.OrdinalIgnoreCase);
    private static long _lastReadTicks;
    private const double CacheTtlSeconds = 30.0;

    private static readonly Regex RxDeviceId = new("DeviceID=\"(.*)\"", RegexOptions.Compiled);

    public static void Invalidate()
    {
        lock (LockObj)
        {
            _lastReadTicks = 0;
        }
    }

    public static Dictionary<string, (string? Model, string? Bus)> GetPhysicalDiskByLetter(bool forceRefresh = false)
    {
        lock (LockObj)
        {
            if (!forceRefresh && _lastReadTicks != 0 && Stopwatch.GetElapsedTime(_lastReadTicks).TotalSeconds < CacheTtlSeconds)
            {
                return new Dictionary<string, (string? Model, string? Bus)>(_cachedMap, StringComparer.OrdinalIgnoreCase);
            }
        }

        var fresh = QueryWmiDiskByLetter();

        lock (LockObj)
        {
            _cachedMap = fresh;
            _lastReadTicks = Stopwatch.GetTimestamp();
            return new Dictionary<string, (string? Model, string? Bus)>(fresh, StringComparer.OrdinalIgnoreCase);
        }
    }

    private static Dictionary<string, (string? Model, string? Bus)> QueryWmiDiskByLetter()
    {
        var map = new Dictionary<string, (string?, string?)>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var disks = new Dictionary<string, (string?, string?)>(StringComparer.OrdinalIgnoreCase);
            using (var s = new ManagementObjectSearcher("SELECT DeviceID, Model, InterfaceType, PNPDeviceID FROM Win32_DiskDrive"))
            using (var results = s.Get())
            {
                foreach (ManagementBaseObject d in results)
                {
                    using (d)
                    {
                        string? devId = d["DeviceID"]?.ToString();
                        if (string.IsNullOrEmpty(devId)) continue;
                        string? bus = d["InterfaceType"]?.ToString();
                        if (d["PNPDeviceID"]?.ToString()?.StartsWith("USBSTOR", StringComparison.OrdinalIgnoreCase) == true)
                            bus = "USB";
                        disks[devId] = (d["Model"]?.ToString()?.Trim(), bus);
                    }
                }
            }

            var diskOfPartition = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            using (var s = new ManagementObjectSearcher("SELECT Antecedent, Dependent FROM Win32_DiskDriveToDiskPartition"))
            using (var results = s.Get())
            {
                foreach (ManagementBaseObject a in results)
                {
                    using (a)
                    {
                        if (RefId(a["Antecedent"]) is string disk && RefId(a["Dependent"]) is string part)
                            diskOfPartition[part] = disk;
                    }
                }
            }

            using (var s = new ManagementObjectSearcher("SELECT Antecedent, Dependent FROM Win32_LogicalDiskToPartition"))
            using (var results = s.Get())
            {
                foreach (ManagementBaseObject a in results)
                {
                    using (a)
                    {
                        if (RefId(a["Antecedent"]) is string part && RefId(a["Dependent"]) is string letter &&
                            diskOfPartition.TryGetValue(part, out var disk) && disks.TryGetValue(disk, out var info))
                        {
                            map[letter] = info;
                        }
                    }
                }
            }
        }
        catch { }
        return map;
    }

    /// <summary>Referência WMI → valor de DeviceID: ...Win32_DiskDrive.DeviceID="\\\\.\\PHYSICALDRIVE0" → \\.\PHYSICALDRIVE0.</summary>
    public static string? RefId(object? reference)
    {
        var m = RxDeviceId.Match(reference?.ToString() ?? "");
        return m.Success ? m.Groups[1].Value.Replace("\\\\", "\\") : null;
    }
}
