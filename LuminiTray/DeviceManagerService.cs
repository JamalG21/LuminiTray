using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;

namespace LuminiTray;

public sealed class ConnectedDeviceItem
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Category { get; init; } = "";
    public string Details { get; init; } = "";
    public string DriveLetter { get; init; } = "";
    public string PnpDeviceId { get; init; } = "";
    public int? PhysicalDriveIndex { get; init; }
    public bool HasStorage { get; init; }
    public bool CanEject { get; init; }
}

/// <summary>
/// Descoberta de dispositivos USB removíveis, pen drives, HDs externos e celulares MTP/WPD,
/// com abertura direta no Explorer, ejeção segura via cfgmgr32 e integração com análise de discos.
/// </summary>
public static class DeviceManagerService
{
    public static List<ConnectedDeviceItem> ScanConnectedDevices()
    {
        var list = new List<ConnectedDeviceItem>();
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1. Discos USB / Removíveis via Win32_DiskDrive + partições/unidades lógicas
        try
        {
            var diskToLetters = MapDiskIndexToDriveLetters();
            using var searcher = new ManagementObjectSearcher(
                "SELECT Index, Model, InterfaceType, MediaType, Size, PNPDeviceID FROM Win32_DiskDrive");
            foreach (ManagementObject mo in searcher.Get())
            {
                string iface = mo["InterfaceType"]?.ToString() ?? "";
                string media = mo["MediaType"]?.ToString() ?? "";
                string pnp = mo["PNPDeviceID"]?.ToString() ?? "";
                bool isUsbOrRemovable =
                    iface.Equals("USB", StringComparison.OrdinalIgnoreCase) ||
                    media.Contains("Removable", StringComparison.OrdinalIgnoreCase) ||
                    media.Contains("External", StringComparison.OrdinalIgnoreCase) ||
                    pnp.StartsWith("USBSTOR\\", StringComparison.OrdinalIgnoreCase);

                if (!isUsbOrRemovable) continue;

                int idx = Convert.ToInt32(mo["Index"]);
                string model = mo["Model"]?.ToString()?.Trim() ?? $"Disco USB {idx}";
                ulong sizeBytes = mo["Size"] != null ? Convert.ToUInt64(mo["Size"]) : 0UL;
                string sizeStr = sizeBytes > 0 ? FormatBytes(sizeBytes) : "Tamanho desconhecido";

                diskToLetters.TryGetValue(idx, out var letters);
                string primaryLetter = letters?.FirstOrDefault() ?? "";
                string volumeInfo = "";
                if (!string.IsNullOrEmpty(primaryLetter))
                {
                    try
                    {
                        var di = new DriveInfo(primaryLetter);
                        if (di.IsReady)
                        {
                            string lbl = string.IsNullOrWhiteSpace(di.VolumeLabel) ? "Sem rótulo" : di.VolumeLabel;
                            volumeInfo = $"{primaryLetter} ({lbl}) · {FormatBytes((ulong)di.AvailableFreeSpace)} livres de {FormatBytes((ulong)di.TotalSize)}";
                        }
                        else
                        {
                            volumeInfo = $"{primaryLetter} · {sizeStr}";
                        }
                    }
                    catch
                    {
                        volumeInfo = $"{primaryLetter} · {sizeStr}";
                    }
                }
                else
                {
                    volumeInfo = $"{sizeStr} (sem letra atribuída)";
                }

                seenIds.Add(pnp);
                list.Add(new ConnectedDeviceItem
                {
                    Id = $"DISK_{idx}_{pnp}",
                    Name = model,
                    Category = "Armazenamento USB",
                    Details = volumeInfo,
                    DriveLetter = primaryLetter,
                    PnpDeviceId = pnp,
                    PhysicalDriveIndex = idx,
                    HasStorage = true,
                    CanEject = !string.IsNullOrEmpty(pnp)
                });
            }
        }
        catch { }

        // 2. Celulares, tablets e câmeras conectados via MTP/PTP (WPD)
        try
        {
            using var wpdSearcher = new ManagementObjectSearcher(
                "SELECT Name, DeviceID, Manufacturer, PNPClass, Status FROM Win32_PnPEntity WHERE PNPClass = 'WPD' OR PNPClass = 'Image'");
            foreach (ManagementObject mo in wpdSearcher.Get())
            {
                string status = mo["Status"]?.ToString() ?? "";
                if (!status.Equals("OK", StringComparison.OrdinalIgnoreCase)) continue;

                string pnp = mo["DeviceID"]?.ToString() ?? "";
                string pnpClass = mo["PNPClass"]?.ToString() ?? "";
                if (!IsRealPortableMtpDevice(pnp, pnpClass) || seenIds.Contains(pnp)) continue;

                string name = mo["Name"]?.ToString()?.Trim() ?? "Dispositivo Portátil (MTP)";
                string maker = mo["Manufacturer"]?.ToString()?.Trim() ?? "";
                string details = string.IsNullOrWhiteSpace(maker) ? "Conexão MTP/WPD (Celular / Mídia)" : $"{maker} · Conexão MTP/WPD";

                seenIds.Add(pnp);
                list.Add(new ConnectedDeviceItem
                {
                    Id = $"WPD_{pnp}",
                    Name = name,
                    Category = "Celular / Mídia (MTP)",
                    Details = details,
                    DriveLetter = "",
                    PnpDeviceId = pnp,
                    PhysicalDriveIndex = null,
                    HasStorage = false,
                    CanEject = true
                });
            }
        }
        catch { }

        return list;
    }

    /// <summary>
    /// Filtra apenas dispositivos portáteis reais (MTP/PTP via USB), ignorando volumes internos
    /// montados pelo Windows sob SWD\WPDBUSENUM\ (HDs/SSDs internos e partições).
    /// </summary>
    public static bool IsRealPortableMtpDevice(string deviceId, string pnpClass)
    {
        if (string.IsNullOrWhiteSpace(deviceId)) return false;
        if (deviceId.StartsWith("SWD\\WPDBUSENUM\\", StringComparison.OrdinalIgnoreCase) ||
            deviceId.StartsWith("STORAGE\\", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        return pnpClass.Equals("WPD", StringComparison.OrdinalIgnoreCase) ||
               pnpClass.Equals("Image", StringComparison.OrdinalIgnoreCase);
    }

    private static Dictionary<int, List<string>> MapDiskIndexToDriveLetters()
    {
        var map = new Dictionary<int, List<string>>();
        var partToDisk = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var s1 = new ManagementObjectSearcher("SELECT Antecedent, Dependent FROM Win32_DiskDriveToDiskPartition");
            foreach (ManagementObject mo in s1.Get())
            {
                string ant = mo["Antecedent"]?.ToString() ?? "";
                string dep = mo["Dependent"]?.ToString() ?? "";
                var m = System.Text.RegularExpressions.Regex.Match(ant, @"PHYSICALDRIVE(\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                var p = System.Text.RegularExpressions.Regex.Match(dep, @"DeviceID=""([^""]+)""");
                if (m.Success && p.Success && int.TryParse(m.Groups[1].Value, out int diskIdx))
                {
                    partToDisk[p.Groups[1].Value] = diskIdx;
                }
            }

            using var s2 = new ManagementObjectSearcher("SELECT Antecedent, Dependent FROM Win32_LogicalDiskToPartition");
            foreach (ManagementObject mo in s2.Get())
            {
                string ant = mo["Antecedent"]?.ToString() ?? "";
                string dep = mo["Dependent"]?.ToString() ?? "";
                var p = System.Text.RegularExpressions.Regex.Match(ant, @"DeviceID=""([^""]+)""");
                var l = System.Text.RegularExpressions.Regex.Match(dep, @"DeviceID=""([A-Z]:)""");
                if (p.Success && l.Success && partToDisk.TryGetValue(p.Groups[1].Value, out int diskIdx))
                {
                    if (!map.TryGetValue(diskIdx, out var letters))
                    {
                        letters = new List<string>();
                        map[diskIdx] = letters;
                    }
                    letters.Add(l.Groups[1].Value);
                }
            }
        }
        catch { }

        return map;
    }

    public static bool OpenDevice(ConnectedDeviceItem item)
    {
        try
        {
            string target = !string.IsNullOrWhiteSpace(item.DriveLetter)
                ? item.DriveLetter + "\\"
                : "shell:MyComputerFolder";
            Process.Start(new ProcessStartInfo("explorer.exe", target) { UseShellExecute = true });
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool EjectDevice(string pnpDeviceId, out string message)
    {
        message = "";
        if (string.IsNullOrWhiteSpace(pnpDeviceId))
        {
            message = "Identificador PnP indisponível para ejeção.";
            return false;
        }

        try
        {
            if (CM_Locate_DevNodeW(out uint devInst, pnpDeviceId, 0) != 0)
            {
                message = "Dispositivo não encontrado na árvore PnP do Windows.";
                return false;
            }

            // Para USBSTOR/DISK, o nó pai (dispositivo USB composto) é quem aceita CM_Request_Device_Eject
            uint targetNode = devInst;
            if (pnpDeviceId.StartsWith("USBSTOR\\", StringComparison.OrdinalIgnoreCase) &&
                CM_Get_Parent(out uint parentNode, devInst, 0) == 0)
            {
                targetNode = parentNode;
            }

            var vetoName = new StringBuilder(260);
            int cr = CM_Request_Device_EjectW(targetNode, out int vetoType, vetoName, vetoName.Capacity, 0);
            if (cr == 0)
            {
                message = "Dispositivo removido com segurança.";
                return true;
            }

            // Tenta no próprio nó caso o pai não aceite ejeção
            if (targetNode != devInst)
            {
                cr = CM_Request_Device_EjectW(devInst, out vetoType, vetoName, vetoName.Capacity, 0);
                if (cr == 0)
                {
                    message = "Dispositivo removido com segurança.";
                    return true;
                }
            }

            string vetoStr = vetoName.Length > 0 ? $" ({vetoName})" : "";
            message = $"Não foi possível ejetar: dispositivo em uso{vetoStr}.";
            return false;
        }
        catch (Exception ex)
        {
            message = $"Erro ao ejetar: {ex.Message}";
            return false;
        }
    }

    private static string FormatBytes(ulong bytes)
    {
        if (bytes >= 1099511627776UL) return $"{bytes / 1099511627776.0:0.0} TB";
        if (bytes >= 1073741824UL) return $"{bytes / 1073741824.0:0.0} GB";
        if (bytes >= 1048576UL) return $"{bytes / 1048576.0:0.0} MB";
        return $"{bytes / 1024.0:0} KB";
    }

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Locate_DevNodeW(out uint pdnDevInst, string pDeviceID, uint ulFlags);

    [DllImport("cfgmgr32.dll")]
    private static extern int CM_Get_Parent(out uint pdnDevInst, uint dnDevInst, uint ulFlags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Request_Device_EjectW(uint dnDevInst, out int pVetoType, StringBuilder pszVetoName, int ulNameLength, uint ulFlags);
}
