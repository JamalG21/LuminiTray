using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;

namespace LuminiTray;

public sealed class MonitorDeviceItem
{
    public string DeviceName { get; set; } = "";
    public string FriendlyName { get; set; } = "";
    public bool IsPrimary { get; set; }
    public int CurrentWidth { get; set; }
    public int CurrentHeight { get; set; }
    public int CurrentRefreshRate { get; set; }
    public int CurrentOrientation { get; set; } // 0=0°, 1=90°, 2=180°, 3=270°
    public bool SupportsDdcCi { get; set; }
    public int Brightness { get; set; } = 100;
    public int Contrast { get; set; } = 50;
    public bool HdrSupported { get; set; }
    public bool HdrEnabled { get; set; }
    public List<string> SupportedResolutions { get; set; } = new();
    public List<int> SupportedRefreshRates { get; set; } = new();

    public override string ToString() =>
        $"{FriendlyName} ({(IsPrimary ? "Principal - " : "")}{CurrentWidth}x{CurrentHeight} @ {CurrentRefreshRate}Hz)";
}

public static class DisplayManager
{
    #region Win32 P/Invoke - dxva2.dll (DDC/CI)

    [DllImport("dxva2.dll", SetLastError = true)]
    private static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, out uint pdwNumberOfPhysicalMonitors);

    [DllImport("dxva2.dll", SetLastError = true)]
    private static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, uint dwPhysicalMonitorArraySize, [Out] PHYSICAL_MONITOR[] pPhysicalMonitorArray);

    [DllImport("dxva2.dll", SetLastError = true)]
    private static extern bool DestroyPhysicalMonitors(uint dwPhysicalMonitorArraySize, [In] PHYSICAL_MONITOR[] pPhysicalMonitorArray);

    [DllImport("dxva2.dll", SetLastError = true)]
    private static extern bool GetMonitorBrightness(IntPtr hMonitor, out uint pdwMinimumBrightness, out uint pdwCurrentBrightness, out uint pdwMaximumBrightness);

    [DllImport("dxva2.dll", SetLastError = true)]
    private static extern bool SetMonitorBrightness(IntPtr hMonitor, uint dwNewBrightness);

    [DllImport("dxva2.dll", SetLastError = true)]
    private static extern bool GetMonitorContrast(IntPtr hMonitor, out uint pdwMinimumContrast, out uint pdwCurrentContrast, out uint pdwMaximumContrast);

    [DllImport("dxva2.dll", SetLastError = true)]
    private static extern bool SetMonitorContrast(IntPtr hMonitor, uint dwNewContrast);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct PHYSICAL_MONITOR
    {
        public IntPtr hPhysicalMonitor;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szPhysicalMonitorDescription;
    }

    #endregion

    #region Win32 P/Invoke - user32.dll (Monitores & Display Settings)

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);
    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);

    [DllImport("user32.dll", CharSet = CharSet.Ansi)]
    private static extern bool EnumDisplayDevices(string? lpDevice, int iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Ansi)]
    private static extern bool EnumDisplaySettings(string? lpszDeviceName, int iModeNum, ref DEVMODE lpDevMode);

    [DllImport("user32.dll", CharSet = CharSet.Ansi)]
    private static extern int ChangeDisplaySettingsEx(string? lpszDeviceName, ref DEVMODE lpDevMode, IntPtr hwnd, uint dwflags, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public struct DISPLAY_DEVICE
    {
        public uint cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceString;
        public uint StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceKey;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string dmDeviceName;
        public short dmSpecVersion;
        public short dmDriverVersion;
        public short dmSize;
        public short dmDriverExtra;
        public int dmFields;
        public int dmPositionX;
        public int dmPositionY;
        public int dmDisplayOrientation;
        public int dmDisplayFixedOutput;
        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string dmFormName;
        public short dmLogPixels;
        public int dmBitsPerPel;
        public int dmPelsWidth;
        public int dmPelsHeight;
        public int dmDisplayFlags;
        public int dmDisplayFrequency;
        public int dmICMMethod;
        public int dmICMIntent;
        public int dmMediaType;
        public int dmDitherType;
        public int dmReserved1;
        public int dmReserved2;
        public int dmPanningWidth;
        public int dmPanningHeight;
    }

    private const int ENUM_CURRENT_SETTINGS = -1;
    private const int DM_PELSWIDTH = 0x00080000;
    private const int DM_PELSHEIGHT = 0x00100000;
    private const int DM_DISPLAYFREQUENCY = 0x00400000;
    private const int DM_DISPLAYORIENTATION = 0x00000080;

    private const uint CDS_UPDATEREGISTRY = 0x00000001;
    private const uint CDS_GLOBAL = 0x00000008;
    private const uint CDS_SET_PRIMARY = 0x00000010;

    public const int DISP_CHANGE_SUCCESSFUL = 0;

    #endregion

    #region Win32 P/Invoke - DisplayConfig & Advanced Color (HDR)

    [DllImport("user32.dll")]
    private static extern int GetDisplayConfigBufferSizes(uint flags, out uint numPathArrayElements, out uint numModeInfoArrayElements);

    [DllImport("user32.dll")]
    private static extern int QueryDisplayConfig(uint flags, ref uint numPathArrayElements, [Out] DISPLAYCONFIG_PATH_INFO[] pathArray, ref uint numModeInfoArrayElements, [Out] DISPLAYCONFIG_MODE_INFO[] modeInfoArray, IntPtr currentTopologyId);

    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO requestPacket);

    private const uint QDC_ONLY_ACTIVE_PATHS = 0x00000002;
    private const int DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO = 9;

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_RATIONAL
    {
        public uint Numerator;
        public uint Denominator;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_PATH_SOURCE_INFO
    {
        public LUID adapterId;
        public uint id;
        public uint modeInfoIdx;
        public uint statusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_PATH_TARGET_INFO
    {
        public LUID adapterId;
        public uint id;
        public uint modeInfoIdx;
        public uint outputTechnology;
        public uint rotation;
        public uint scaling;
        public DISPLAYCONFIG_RATIONAL refreshRate;
        public uint scanLineOrdering;
        public bool targetAvailable;
        public uint statusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_PATH_INFO
    {
        public DISPLAYCONFIG_PATH_SOURCE_INFO sourceInfo;
        public DISPLAYCONFIG_PATH_TARGET_INFO targetInfo;
        public uint flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_MODE_INFO
    {
        public uint infoType;
        public uint id;
        public LUID adapterId;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)]
        public byte[] modeInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_DEVICE_INFO_HEADER
    {
        public int type;
        public int size;
        public LUID adapterId;
        public uint id;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO
    {
        public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
        public uint value;
        public uint colorEncoding;
        public uint bitsPerColorChannel;
    }

    #endregion

    /// <summary>
    /// Lista todos os monitores conectados, suas resoluções, taxas suportadas, brilho DDC/CI e status de HDR.
    /// </summary>
    public static List<MonitorDeviceItem> GetMonitors()
    {
        var result = new List<MonitorDeviceItem>();
        var monitorHandles = new Dictionary<string, IntPtr>(StringComparer.OrdinalIgnoreCase);

        // Mapear identificador de monitor -> HMONITOR
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMon, IntPtr hdc, ref RECT rc, IntPtr data) =>
        {
            var mi = new MONITORINFOEX();
            mi.cbSize = Marshal.SizeOf(typeof(MONITORINFOEX));
            if (GetMonitorInfo(hMon, ref mi))
            {
                string dev = mi.szDevice ?? "";
                if (!string.IsNullOrEmpty(dev) && !monitorHandles.ContainsKey(dev))
                    monitorHandles[dev] = hMon;
            }
            return true;
        }, IntPtr.Zero);

        // Enumerar dispositivos de exibição
        var d = new DISPLAY_DEVICE();
        d.cb = (uint)Marshal.SizeOf(d);

        for (int i = 0; EnumDisplayDevices(null, i, ref d, 0); i++)
        {
            // Ignorar adaptadores não ativos
            if ((d.StateFlags & 0x00000001) == 0) continue; // DISPLAY_DEVICE_ATTACHED_TO_DESKTOP

            string devName = d.DeviceName;
            bool isPrimary = (d.StateFlags & 0x00000004) != 0; // DISPLAY_DEVICE_PRIMARY_DEVICE

            // Obter nome amigável do monitor (ex: "Dell S2721DGF", "LG UltraGear")
            string friendly = d.DeviceString;
            var monitorDev = new DISPLAY_DEVICE();
            monitorDev.cb = (uint)Marshal.SizeOf(monitorDev);
            if (EnumDisplayDevices(devName, 0, ref monitorDev, 0))
            {
                if (!string.IsNullOrWhiteSpace(monitorDev.DeviceString))
                    friendly = monitorDev.DeviceString;
            }

            var item = new MonitorDeviceItem
            {
                DeviceName = devName,
                FriendlyName = string.IsNullOrWhiteSpace(friendly) ? $"Monitor {i + 1}" : friendly,
                IsPrimary = isPrimary
            };

            // Modo atual
            var dmCurrent = new DEVMODE();
            dmCurrent.dmSize = (short)Marshal.SizeOf(dmCurrent);
            if (EnumDisplaySettings(devName, ENUM_CURRENT_SETTINGS, ref dmCurrent))
            {
                item.CurrentWidth = dmCurrent.dmPelsWidth;
                item.CurrentHeight = dmCurrent.dmPelsHeight;
                item.CurrentRefreshRate = dmCurrent.dmDisplayFrequency;
                item.CurrentOrientation = dmCurrent.dmDisplayOrientation;
            }

            // Resoluções e taxas de atualização suportadas
            var modes = new HashSet<string>();
            var freqs = new SortedSet<int>();
            var dm = new DEVMODE();
            dm.dmSize = (short)Marshal.SizeOf(dm);

            for (int m = 0; EnumDisplaySettings(devName, m, ref dm); m++)
            {
                if (dm.dmPelsWidth >= 800 && dm.dmPelsHeight >= 600)
                {
                    modes.Add($"{dm.dmPelsWidth} x {dm.dmPelsHeight}");
                    freqs.Add(dm.dmDisplayFrequency);
                }
            }

            item.SupportedResolutions = modes
                .OrderByDescending(r =>
                {
                    var p = r.Split('x');
                    int w = int.TryParse(p[0].Trim(), out int val) ? val : 0;
                    return w;
                })
                .ToList();

            item.SupportedRefreshRates = freqs.OrderByDescending(f => f).ToList();

            // Obter Brilho e Contraste via DDC/CI (dxva2.dll)
            if (monitorHandles.TryGetValue(devName, out IntPtr hMonitor))
            {
                if (GetNumberOfPhysicalMonitorsFromHMONITOR(hMonitor, out uint count) && count > 0)
                {
                    var phys = new PHYSICAL_MONITOR[count];
                    if (GetPhysicalMonitorsFromHMONITOR(hMonitor, count, phys))
                    {
                        if (GetMonitorBrightness(phys[0].hPhysicalMonitor, out _, out uint curB, out _))
                        {
                            item.Brightness = (int)curB;
                            item.SupportsDdcCi = true;
                        }
                        if (GetMonitorContrast(phys[0].hPhysicalMonitor, out _, out uint curC, out _))
                        {
                            item.Contrast = (int)curC;
                        }
                        DestroyPhysicalMonitors(count, phys);
                    }
                }
            }

            // Se DDC/CI não respondeu (ex: notebook/tela interna), tentar via WMI
            if (!item.SupportsDdcCi)
            {
                int wmiB = GetWmiBrightness();
                if (wmiB >= 0)
                {
                    item.Brightness = wmiB;
                    item.SupportsDdcCi = true;
                }
            }

            // HDR Status
            var (hdrSup, hdrAct) = CheckHdrStatus(devName);
            item.HdrSupported = hdrSup;
            item.HdrEnabled = hdrAct;

            result.Add(item);
        }

        if (result.Count == 0)
        {
            // Fallback para monitor virtual/padrão
            result.Add(new MonitorDeviceItem
            {
                DeviceName = @"\\.\DISPLAY1",
                FriendlyName = "Monitor Principal",
                IsPrimary = true,
                CurrentWidth = 1920,
                CurrentHeight = 1080,
                CurrentRefreshRate = 60,
                SupportedResolutions = new() { "1920 x 1080", "1366 x 768", "1280 x 720" },
                SupportedRefreshRates = new() { 60 }
            });
        }

        return result;
    }

    /// <summary>
    /// Altera o brilho do monitor via DDC/CI ou WMI (0 a 100).
    /// </summary>
    public static bool SetBrightness(string deviceName, int brightness)
    {
        brightness = Math.Clamp(brightness, 0, 100);
        bool success = false;

        // 1. Tentar via DDC/CI
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMon, IntPtr hdc, ref RECT rc, IntPtr data) =>
        {
            var mi = new MONITORINFOEX();
            mi.cbSize = Marshal.SizeOf(typeof(MONITORINFOEX));
            if (GetMonitorInfo(hMon, ref mi) && string.Equals(mi.szDevice, deviceName, StringComparison.OrdinalIgnoreCase))
            {
                if (GetNumberOfPhysicalMonitorsFromHMONITOR(hMon, out uint count) && count > 0)
                {
                    var phys = new PHYSICAL_MONITOR[count];
                    if (GetPhysicalMonitorsFromHMONITOR(hMon, count, phys))
                    {
                        success = SetMonitorBrightness(phys[0].hPhysicalMonitor, (uint)brightness);
                        DestroyPhysicalMonitors(count, phys);
                    }
                }
            }
            return !success;
        }, IntPtr.Zero);

        // 2. Se falhou, tentar via WMI (telas de laptop)
        if (!success)
        {
            success = SetWmiBrightness(brightness);
        }

        return success;
    }

    /// <summary>
    /// Altera o contraste do monitor via DDC/CI (0 a 100).
    /// </summary>
    public static bool SetContrast(string deviceName, int contrast)
    {
        contrast = Math.Clamp(contrast, 0, 100);
        bool success = false;

        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMon, IntPtr hdc, ref RECT rc, IntPtr data) =>
        {
            var mi = new MONITORINFOEX();
            mi.cbSize = Marshal.SizeOf(typeof(MONITORINFOEX));
            if (GetMonitorInfo(hMon, ref mi) && string.Equals(mi.szDevice, deviceName, StringComparison.OrdinalIgnoreCase))
            {
                if (GetNumberOfPhysicalMonitorsFromHMONITOR(hMon, out uint count) && count > 0)
                {
                    var phys = new PHYSICAL_MONITOR[count];
                    if (GetPhysicalMonitorsFromHMONITOR(hMon, count, phys))
                    {
                        success = SetMonitorContrast(phys[0].hPhysicalMonitor, (uint)contrast);
                        DestroyPhysicalMonitors(count, phys);
                    }
                }
            }
            return !success;
        }, IntPtr.Zero);

        return success;
    }

    /// <summary>
    /// Altera a resolução e/ou frequência de atualização do monitor.
    /// </summary>
    public static bool SetResolutionAndRefreshRate(string deviceName, int width, int height, int refreshRate = 0)
    {
        try
        {
            var dm = new DEVMODE();
            dm.dmSize = (short)Marshal.SizeOf(dm);
            if (!EnumDisplaySettings(deviceName, ENUM_CURRENT_SETTINGS, ref dm))
                return false;

            dm.dmPelsWidth = width;
            dm.dmPelsHeight = height;
            dm.dmFields = DM_PELSWIDTH | DM_PELSHEIGHT;

            if (refreshRate > 0)
            {
                dm.dmDisplayFrequency = refreshRate;
                dm.dmFields |= DM_DISPLAYFREQUENCY;
            }

            int ret = ChangeDisplaySettingsEx(deviceName, ref dm, IntPtr.Zero, CDS_UPDATEREGISTRY | CDS_GLOBAL, IntPtr.Zero);
            return ret == DISP_CHANGE_SUCCESSFUL;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Altera apenas a taxa de atualização (Hertz).
    /// </summary>
    public static bool SetRefreshRate(string deviceName, int refreshRate)
    {
        try
        {
            var dm = new DEVMODE();
            dm.dmSize = (short)Marshal.SizeOf(dm);
            if (!EnumDisplaySettings(deviceName, ENUM_CURRENT_SETTINGS, ref dm))
                return false;

            dm.dmDisplayFrequency = refreshRate;
            dm.dmFields = DM_DISPLAYFREQUENCY;

            int ret = ChangeDisplaySettingsEx(deviceName, ref dm, IntPtr.Zero, CDS_UPDATEREGISTRY | CDS_GLOBAL, IntPtr.Zero);
            return ret == DISP_CHANGE_SUCCESSFUL;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Altera a orientação da tela (0=Normal, 1=90°, 2=180°, 3=270°).
    /// </summary>
    public static bool SetOrientation(string deviceName, int orientation)
    {
        try
        {
            var dm = new DEVMODE();
            dm.dmSize = (short)Marshal.SizeOf(dm);
            if (!EnumDisplaySettings(deviceName, ENUM_CURRENT_SETTINGS, ref dm))
                return false;

            bool currentlyPortrait = (dm.dmDisplayOrientation == 1 || dm.dmDisplayOrientation == 3);
            bool targetPortrait = (orientation == 1 || orientation == 3);

            if (currentlyPortrait != targetPortrait)
            {
                int temp = dm.dmPelsWidth;
                dm.dmPelsWidth = dm.dmPelsHeight;
                dm.dmPelsHeight = temp;
                dm.dmFields |= (DM_PELSWIDTH | DM_PELSHEIGHT);
            }

            dm.dmDisplayOrientation = orientation;
            dm.dmFields |= DM_DISPLAYORIENTATION;

            int ret = ChangeDisplaySettingsEx(deviceName, ref dm, IntPtr.Zero, CDS_UPDATEREGISTRY | CDS_GLOBAL, IntPtr.Zero);
            return ret == DISP_CHANGE_SUCCESSFUL;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Define o monitor indicado como principal do Windows.
    /// </summary>
    public static bool SetPrimaryMonitor(string deviceName)
    {
        try
        {
            var dm = new DEVMODE();
            dm.dmSize = (short)Marshal.SizeOf(dm);
            if (!EnumDisplaySettings(deviceName, ENUM_CURRENT_SETTINGS, ref dm))
                return false;

            int ret = ChangeDisplaySettingsEx(deviceName, ref dm, IntPtr.Zero, CDS_SET_PRIMARY | CDS_UPDATEREGISTRY | CDS_GLOBAL, IntPtr.Zero);
            return ret == DISP_CHANGE_SUCCESSFUL;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Abre as configurações nativas de HDR do Windows 10/11.
    /// </summary>
    public static void OpenWindowsHdrSettings()
    {
        try
        {
            Process.Start(new ProcessStartInfo("ms-settings:display-advancedcolor") { UseShellExecute = true });
        }
        catch
        {
            try { Process.Start(new ProcessStartInfo("ms-settings:display") { UseShellExecute = true }); } catch { }
        }
    }

    #region Helpers Privados (WMI & HDR)

    private static int GetWmiBrightness()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(@"\\.\root\wmi", "SELECT CurrentBrightness FROM WmiMonitorBrightness");
            foreach (ManagementObject mo in searcher.Get())
            {
                return Convert.ToInt32(mo["CurrentBrightness"]);
            }
        }
        catch { }
        return -1;
    }

    private static bool SetWmiBrightness(int brightness)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(@"\\.\root\wmi", "SELECT * FROM WmiMonitorBrightnessMethods");
            foreach (ManagementObject mo in searcher.Get())
            {
                mo.InvokeMethod("WmiSetBrightness", new object[] { 1, (byte)brightness });
                return true;
            }
        }
        catch { }
        return false;
    }

    private static (bool Supported, bool Enabled) CheckHdrStatus(string deviceName)
    {
        try
        {
            int err = GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out uint pathCount, out uint modeCount);
            if (err != 0 || pathCount == 0) return (false, false);

            var paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
            var modes = new DISPLAYCONFIG_MODE_INFO[modeCount];

            err = QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero);
            if (err != 0) return (false, false);

            foreach (var path in paths)
            {
                var adv = new DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO
                {
                    header = new DISPLAYCONFIG_DEVICE_INFO_HEADER
                    {
                        type = DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO,
                        size = Marshal.SizeOf(typeof(DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO)),
                        adapterId = path.targetInfo.adapterId,
                        id = path.targetInfo.id
                    }
                };

                if (DisplayConfigGetDeviceInfo(ref adv) == 0)
                {
                    bool sup = (adv.value & 0x1) != 0 || (adv.value & 0x10) != 0;
                    bool act = (adv.value & 0x2) != 0 || (adv.value & 0x20) != 0;
                    return (sup, act);
                }
            }
        }
        catch { }

        return (false, false);
    }

    #endregion
}
