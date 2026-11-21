using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace LuminiTray;

public sealed record HidEndpointInfo(
    string Path,
    ushort VendorId,
    ushort ProductId,
    ushort UsagePage,
    ushort Usage,
    int InputReportLength,
    int OutputReportLength,
    int FeatureReportLength,
    int InterfaceNumber,
    string ProductName,
    bool IsBluetooth,
    string ManufacturerName = "");

/// <summary>
/// Transporte HID e PnP nativo em C# (inspirado no hidlist.py do HaloBattery):
/// enumera interfaces via cfgmgr32.dll sem abrir dispositivos alheios e realiza
/// leitura/escrita assíncrona via OVERLAPPED para nunca travar a UI.
/// </summary>
public static class PeripheralHidTransport
{
    private static readonly Guid GuidDevInterfaceHid = new("4D1E55B2-F16F-11CF-88CB-001111000030");
    private static readonly Regex RxVidPid = new(@"vid[_&]([0-9a-f]{4,8})&pid[_&]([0-9a-f]{4})", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex RxMi = new(@"&mi_([0-9a-f]{2})", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex RxMacDev = new(@"DEV_([0-9A-F]{12})(?![0-9A-F])", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex RxMacHex12 = new(@"(?<![0-9A-F])([0-9A-F]{12})(?![0-9A-F])", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private const uint CR_SUCCESS = 0x00000000;
    private const uint CR_BUFFER_SMALL = 0x0000001A;
    private const uint CM_GET_DEVICE_INTERFACE_LIST_PRESENT = 0x00000000;

    private const uint GENERIC_READ = 0x80000000;
    private const uint GENERIC_WRITE = 0x40000000;
    private const uint FILE_SHARE_READ = 0x00000001;
    private const uint FILE_SHARE_WRITE = 0x00000002;
    private const uint OPEN_EXISTING = 3;
    private const uint FILE_FLAG_OVERLAPPED = 0x40000000;
    private const uint ERROR_IO_PENDING = 997;

    private const uint DIGCF_PRESENT = 0x00000002;
    private const uint DIGCF_ALLCLASSES = 0x00000004;
    private const int HidP_Input = 0;
    private const int HidP_Feature = 2;
    private const int HIDP_STATUS_SUCCESS = 0x00110000;

    [StructLayout(LayoutKind.Explicit, Size = 72)]
    private struct HIDP_VALUE_CAPS
    {
        [FieldOffset(0)] public ushort UsagePage;
        [FieldOffset(2)] public byte ReportID;
        [FieldOffset(3)] public byte IsAlias;
        [FieldOffset(4)] public ushort BitField;
        [FieldOffset(6)] public ushort LinkCollection;
        [FieldOffset(8)] public ushort LinkUsage;
        [FieldOffset(10)] public ushort LinkUsagePage;
        [FieldOffset(12)] public byte IsRange;
        [FieldOffset(18)] public ushort BitSize;
        [FieldOffset(20)] public ushort ReportCount;
        [FieldOffset(40)] public int LogicalMin;
        [FieldOffset(44)] public int LogicalMax;
        [FieldOffset(56)] public ushort UsageMin; // == NotRange.Usage
        [FieldOffset(58)] public ushort UsageMax;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SP_DEVICE_INTERFACE_DATA
    {
        public int cbSize;
        public Guid InterfaceClassGuid;
        public uint Flags;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HIDD_ATTRIBUTES
    {
        public int Size;
        public ushort VendorID;
        public ushort ProductID;
        public ushort VersionNumber;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HIDP_CAPS
    {
        public ushort Usage;
        public ushort UsagePage;
        public ushort InputReportByteLength;
        public ushort OutputReportByteLength;
        public ushort FeatureReportByteLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)]
        public ushort[] Reserved;
        public ushort NumberLinkCollectionNodes;
        public ushort NumberInputButtonCaps;
        public ushort NumberInputValueCaps;
        public ushort NumberInputDataIndices;
        public ushort NumberOutputButtonCaps;
        public ushort NumberOutputValueCaps;
        public ushort NumberOutputDataIndices;
        public ushort NumberFeatureButtonCaps;
        public ushort NumberFeatureValueCaps;
        public ushort NumberFeatureDataIndices;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SP_DEVINFO_DATA
    {
        public int cbSize;
        public Guid ClassGuid;
        public uint DevInst;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DEVPROPKEY
    {
        public Guid fmtid;
        public uint pid;
    }

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern uint CM_Get_Device_Interface_List_SizeW(
        out uint pulLen, ref Guid interfaceClassGuid, string? pDeviceID, uint ulFlags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern uint CM_Get_Device_Interface_ListW(
        ref Guid interfaceClassGuid, string? pDeviceID, [Out] char[] buffer, uint bufferLen, uint ulFlags);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(
        string lpFileName, uint dwDesiredAccess, uint dwShareMode,
        IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadFile(
        SafeFileHandle hFile, [Out] byte[] lpBuffer, uint nNumberOfBytesToRead,
        out uint lpNumberOfBytesRead, ref NativeOverlapped lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool WriteFile(
        SafeFileHandle hFile, byte[] lpBuffer, uint nNumberOfBytesToWrite,
        out uint lpNumberOfBytesWritten, ref NativeOverlapped lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetOverlappedResult(
        SafeFileHandle hFile, ref NativeOverlapped lpOverlapped, out uint lpNumberOfBytesTransferred, bool bWait);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CancelIoEx(SafeFileHandle hFile, ref NativeOverlapped lpOverlapped);

    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_GetAttributes(SafeFileHandle hidDeviceObject, ref HIDD_ATTRIBUTES attributes);

    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_GetPreparsedData(SafeFileHandle hidDeviceObject, out IntPtr preparsedData);

    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_FreePreparsedData(IntPtr preparsedData);

    [DllImport("hid.dll", SetLastError = true)]
    private static extern int HidP_GetCaps(IntPtr preparsedData, out HIDP_CAPS capabilities);

    [DllImport("hid.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool HidD_GetProductString(SafeFileHandle hidDeviceObject, [Out] char[] buffer, uint bufferLength);

    [DllImport("hid.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool HidD_GetManufacturerString(SafeFileHandle hidDeviceObject, [Out] char[] buffer, uint bufferLength);

    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_GetFeature(SafeFileHandle hidDeviceObject, [In, Out] byte[] reportBuffer, uint reportBufferLength);

    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_GetInputReport(SafeFileHandle hidDeviceObject, [In, Out] byte[] reportBuffer, uint reportBufferLength);

    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_SetFeature(SafeFileHandle hidDeviceObject, byte[] reportBuffer, uint reportBufferLength);

    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_FlushQueue(SafeFileHandle hidDeviceObject);

    [DllImport("hid.dll", SetLastError = true)]
    private static extern int HidP_GetValueCaps(
        int reportType, [Out] HIDP_VALUE_CAPS[] valueCaps, ref ushort valueCapsLength, IntPtr preparsedData);

    [DllImport("hid.dll", SetLastError = true)]
    private static extern int HidP_GetUsageValue(
        int reportType, ushort usagePage, ushort linkCollection, ushort usage,
        out uint usageValue, IntPtr preparsedData, byte[] report, uint reportLength);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SetupDiCreateDeviceInfoList(IntPtr classGuid, IntPtr hwndParent);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetupDiOpenDeviceInterfaceW(
        IntPtr deviceInfoSet, string devicePath, uint openFlags, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetupDiGetDeviceInterfaceDetailW(
        IntPtr deviceInfoSet, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData,
        IntPtr deviceInterfaceDetailData, uint deviceInterfaceDetailDataSize,
        out uint requiredSize, ref SP_DEVINFO_DATA deviceInfoData);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevsW(
        IntPtr classGuid, string? enumerator, IntPtr hwndParent, uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInfo(IntPtr deviceInfoSet, uint memberIndex, ref SP_DEVINFO_DATA deviceInfoData);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetupDiGetDeviceInstanceIdW(
        IntPtr deviceInfoSet, ref SP_DEVINFO_DATA deviceInfoData, [Out] char[] deviceInstanceId, uint deviceInstanceIdSize, out uint requiredSize);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetupDiGetDevicePropertyW(
        IntPtr deviceInfoSet, ref SP_DEVINFO_DATA deviceInfoData, ref DEVPROPKEY propertyKey,
        out uint propertyType, [Out] byte[] propertyBuffer, uint propertyBufferSize, out uint requiredSize, uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    public static List<string> EnumerateHidInterfacePaths()
    {
        var list = new List<string>();
        try
        {
            var g = GuidDevInterfaceHid;
            for (int attempt = 0; attempt < 4; attempt++)
            {
                if (CM_Get_Device_Interface_List_SizeW(out uint len, ref g, null, CM_GET_DEVICE_INTERFACE_LIST_PRESENT) != CR_SUCCESS || len == 0)
                    return list;
                var buf = new char[len + 2];
                uint rc = CM_Get_Device_Interface_ListW(ref g, null, buf, (uint)buf.Length, CM_GET_DEVICE_INTERFACE_LIST_PRESENT);
                if (rc == CR_BUFFER_SMALL) continue;
                if (rc != CR_SUCCESS) return list;

                string raw = new string(buf);
                foreach (string part in raw.Split('\0', StringSplitOptions.RemoveEmptyEntries))
                    list.Add(part);
                return list;
            }
        }
        catch { }
        return list;
    }

    public static bool TryExtractVidPid(string path, out ushort vid, out ushort pid)
    {
        vid = 0;
        pid = 0;
        if (string.IsNullOrEmpty(path)) return false;
        var m = RxVidPid.Match(path);
        if (!m.Success) return false;
        string vStr = m.Groups[1].Value;
        if (vStr.Length > 4) vStr = vStr[^4..];
        return ushort.TryParse(vStr, System.Globalization.NumberStyles.HexNumber, null, out vid)
            && ushort.TryParse(m.Groups[2].Value, System.Globalization.NumberStyles.HexNumber, null, out pid);
    }

    public static int ExtractInterfaceNumber(string path)
    {
        var m = RxMi.Match(path ?? "");
        return m.Success && int.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.HexNumber, null, out int mi) ? mi : -1;
    }

    public static bool IsBluetoothHidPath(string path) =>
        path.Contains("vid&", StringComparison.OrdinalIgnoreCase)
        || path.Contains("{00001124-", StringComparison.OrdinalIgnoreCase)
        || path.Contains("{00001812-", StringComparison.OrdinalIgnoreCase)
        || path.Contains("BTHLE", StringComparison.OrdinalIgnoreCase);

    public static List<HidEndpointInfo> InspectVendorInterfaces(IEnumerable<string> allPaths, ushort targetVid)
    {
        var result = new List<HidEndpointInfo>();
        foreach (string path in allPaths)
        {
            if (!TryExtractVidPid(path, out ushort vid, out ushort pid) || vid != targetVid)
                continue;

            using var h = OpenPath(path, readWrite: false);
            if (h == null) continue;

            var ep = ReadEndpointInfo(h, path, vid, pid);
            if (ep != null) result.Add(ep);
        }
        return result;
    }

    public static HidEndpointInfo? InspectSinglePath(string path)
    {
        if (!TryExtractVidPid(path, out ushort vid, out ushort pid))
            return null;
        using var h = OpenPath(path, readWrite: false);
        return h == null ? null : ReadEndpointInfo(h, path, vid, pid);
    }

    private static HidEndpointInfo? ReadEndpointInfo(SafeFileHandle h, string path, ushort vid, ushort pid)
    {
        try
        {
            var attrs = new HIDD_ATTRIBUTES { Size = Marshal.SizeOf<HIDD_ATTRIBUTES>() };
            if (HidD_GetAttributes(h, ref attrs))
            {
                vid = attrs.VendorID;
                pid = attrs.ProductID;
            }

            ushort usagePage = 0, usage = 0;
            int inLen = 0, outLen = 0, featLen = 0;
            if (HidD_GetPreparsedData(h, out IntPtr ppd) && ppd != IntPtr.Zero)
            {
                try
                {
                    if (HidP_GetCaps(ppd, out HIDP_CAPS caps) > 0)
                    {
                        usagePage = caps.UsagePage;
                        usage = caps.Usage;
                        inLen = caps.InputReportByteLength;
                        outLen = caps.OutputReportByteLength;
                        featLen = caps.FeatureReportByteLength;
                    }
                }
                finally { HidD_FreePreparsedData(ppd); }
            }

            string prodName = "";
            var nameBuf = new char[256];
            if (HidD_GetProductString(h, nameBuf, (uint)(nameBuf.Length * 2)))
            {
                int z = Array.IndexOf(nameBuf, '\0');
                prodName = (z >= 0 ? new string(nameBuf, 0, z) : new string(nameBuf)).Trim();
            }

            string mfgName = "";
            Array.Clear(nameBuf, 0, nameBuf.Length);
            if (HidD_GetManufacturerString(h, nameBuf, (uint)(nameBuf.Length * 2)))
            {
                int z = Array.IndexOf(nameBuf, '\0');
                mfgName = (z >= 0 ? new string(nameBuf, 0, z) : new string(nameBuf)).Trim();
            }

            return new HidEndpointInfo(
                path, vid, pid, usagePage, usage,
                inLen, outLen, featLen,
                ExtractInterfaceNumber(path),
                prodName,
                IsBluetoothHidPath(path),
                mfgName);
        }
        catch { return null; }
    }

    public static SafeFileHandle? OpenPath(string path, bool readWrite = true)
    {
        uint access = readWrite ? (GENERIC_READ | GENERIC_WRITE) : 0;
        var h = CreateFileW(path, access, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_OVERLAPPED, IntPtr.Zero);
        if (h.IsInvalid && readWrite)
        {
            h.Dispose();
            h = CreateFileW(path, GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_OVERLAPPED, IntPtr.Zero);
        }
        if (h.IsInvalid) { h.Dispose(); return null; }
        return h;
    }

    public static void FlushInput(SafeFileHandle h)
    {
        try { HidD_FlushQueue(h); } catch { }
    }

    public static byte[]? GetFeatureReport(SafeFileHandle h, byte reportId, int length)
    {
        if (length <= 0) return null;
        var buf = new byte[length];
        buf[0] = reportId;
        return HidD_GetFeature(h, buf, (uint)buf.Length) ? buf : null;
    }

    public static bool SetFeatureReport(SafeFileHandle h, byte[] report)
    {
        if (report == null || report.Length == 0) return false;
        return HidD_SetFeature(h, report, (uint)report.Length);
    }

    public static bool WriteReport(SafeFileHandle h, byte[] report, int timeoutMs = 400)
    {
        if (report == null || report.Length == 0) return false;
        using var ev = new ManualResetEvent(false);
        var ov = new NativeOverlapped { EventHandle = ev.SafeWaitHandle.DangerousGetHandle() };
        bool ok = WriteFile(h, report, (uint)report.Length, out _, ref ov);
        if (!ok)
        {
            if (Marshal.GetLastWin32Error() != ERROR_IO_PENDING) return false;
            if (!ev.WaitOne(timeoutMs))
            {
                CancelIoEx(h, ref ov);
                return false;
            }
        }
        return GetOverlappedResult(h, ref ov, out _, true);
    }

    public static byte[]? ReadReport(SafeFileHandle h, int reportLength, int timeoutMs = 400)
    {
        if (reportLength <= 0) return null;
        var buf = new byte[reportLength];
        using var ev = new ManualResetEvent(false);
        var ov = new NativeOverlapped { EventHandle = ev.SafeWaitHandle.DangerousGetHandle() };
        bool ok = ReadFile(h, buf, (uint)buf.Length, out uint read, ref ov);
        if (!ok)
        {
            if (Marshal.GetLastWin32Error() != ERROR_IO_PENDING) return null;
            if (!ev.WaitOne(timeoutMs))
            {
                CancelIoEx(h, ref ov);
                ev.WaitOne(50);
                return null;
            }
        }
        if (!GetOverlappedResult(h, ref ov, out read, true) || read == 0)
            return null;
        if (read < buf.Length)
            Array.Resize(ref buf, (int)read);
        return buf;
    }

    /// <summary>
    /// Lê DEVPKEY_Bluetooth_Battery ({104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2) de todos os nós PnP Bluetooth ativos,
    /// indexados por MAC (12 dígitos hexadecimais sem separadores), sem invocar PowerShell.
    /// </summary>
    public static Dictionary<string, int> ReadBluetoothBatteryByMac()
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        IntPtr hDevInfo = IntPtr.Zero;
        try
        {
            hDevInfo = SetupDiGetClassDevsW(IntPtr.Zero, null, IntPtr.Zero, DIGCF_PRESENT | DIGCF_ALLCLASSES);
            if (hDevInfo == IntPtr.Zero || hDevInfo == new IntPtr(-1)) return map;

            var keyBat = new DEVPROPKEY { fmtid = new Guid("104EA319-6EE2-4701-BD47-8DDBF425BBE5"), pid = 2 };
            var devData = new SP_DEVINFO_DATA { cbSize = Marshal.SizeOf<SP_DEVINFO_DATA>() };
            var idBuf = new char[512];
            var valBuf = new byte[16];

            for (uint i = 0; SetupDiEnumDeviceInfo(hDevInfo, i, ref devData); i++)
            {
                if (!SetupDiGetDeviceInstanceIdW(hDevInfo, ref devData, idBuf, (uint)idBuf.Length, out _))
                    continue;
                int z = Array.IndexOf(idBuf, '\0');
                string instId = z >= 0 ? new string(idBuf, 0, z) : new string(idBuf);
                if (!instId.StartsWith("BTHENUM\\", StringComparison.OrdinalIgnoreCase)
                    && !instId.StartsWith("BTHLE\\", StringComparison.OrdinalIgnoreCase)
                    && !instId.StartsWith("BTHLEDEVICE\\", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (SetupDiGetDevicePropertyW(hDevInfo, ref devData, ref keyBat, out _, valBuf, (uint)valBuf.Length, out uint req, 0) && req >= 1)
                {
                    int level = valBuf[0];
                    if (level is >= 0 and <= 100)
                    {
                        string? mac = ExtractMacFromInstanceId(instId);
                        if (!string.IsNullOrEmpty(mac) && !map.ContainsKey(mac))
                            map[mac] = level;
                    }
                }
            }
        }
        catch { }
        finally
        {
            if (hDevInfo != IntPtr.Zero && hDevInfo != new IntPtr(-1))
                SetupDiDestroyDeviceInfoList(hDevInfo);
        }
        return map;
    }

    public static string? ExtractMacFromInstanceId(string? instId)
    {
        if (string.IsNullOrWhiteSpace(instId)) return null;
        var m = RxMacDev.Match(instId);
        if (m.Success) return m.Groups[1].Value.ToUpperInvariant();
        var all = RxMacHex12.Matches(instId);
        return all.Count > 0 ? all[^1].Groups[1].Value.ToUpperInvariant() : null;
    }

    /// <summary>
    /// Lê a bateria padrão HID (UsagePage 0x06 / Usage 0x20 "Battery Strength" ou
    /// UsagePage 0x85 / Usage 0x65 "Absolute State Of Charge"), inspirado no generic.cpp do WDBM.
    /// </summary>
    public static bool TryReadStandardHidBattery(HidEndpointInfo ep, out int percent)
    {
        percent = -1;
        // Ignora Digitizers (0x0D - canetas/touchscreens) e UPS (0x84 / 0x85)
        if (ep.UsagePage is 0x000D or 0x0084 or 0x0085)
            return false;

        using var h = OpenPath(ep.Path, readWrite: false);
        if (h == null) return false;
        if (!HidD_GetPreparsedData(h, out IntPtr ppd) || ppd == IntPtr.Zero) return false;

        try
        {
            if (HidP_GetCaps(ppd, out HIDP_CAPS caps) <= 0) return false;

            if (TryReadBatteryFromReportType(h, ppd, ep.VendorId, HidP_Feature, caps.NumberFeatureValueCaps, caps.FeatureReportByteLength, out percent))
                return true;
            if (TryReadBatteryFromReportType(h, ppd, ep.VendorId, HidP_Input, caps.NumberInputValueCaps, caps.InputReportByteLength, out percent))
                return true;
        }
        catch { }
        finally
        {
            HidD_FreePreparsedData(ppd);
        }
        return false;
    }

    private static bool TryReadBatteryFromReportType(
        SafeFileHandle h, IntPtr ppd, ushort vid, int reportType, ushort valueCapCount, ushort reportLen, out int percent)
    {
        percent = -1;
        if (valueCapCount == 0 || reportLen == 0) return false;

        ushort len = valueCapCount;
        var capsArr = new HIDP_VALUE_CAPS[len];
        if (HidP_GetValueCaps(reportType, capsArr, ref len, ppd) != HIDP_STATUS_SUCCESS)
            return false;

        for (int i = 0; i < len; i++)
        {
            var vc = capsArr[i];
            if (vc.IsRange != 0) continue;
            bool isBatteryUsage =
                (vc.UsagePage == 0x0006 && vc.UsageMin == 0x0020) ||
                (vc.UsagePage == 0x0085 && vc.UsageMin == 0x0065);
            if (!isBatteryUsage) continue;

            var report = new byte[reportLen];
            report[0] = vc.ReportID;
            bool ok = reportType == HidP_Feature
                ? HidD_GetFeature(h, report, (uint)report.Length)
                : HidD_GetInputReport(h, report, (uint)report.Length);
            if (!ok) continue;

            if (HidP_GetUsageValue(reportType, vc.UsagePage, vc.LinkCollection, vc.UsageMin, out uint val, ppd, report, (uint)report.Length) != HIDP_STATUS_SUCCESS)
                continue;

            int scaled = ScaleHidBatteryValue(val, vc.LogicalMin, vc.LogicalMax, vid);
            if (scaled >= 0)
            {
                percent = scaled;
                return true;
            }
        }
        return false;
    }

    public static int ScaleHidBatteryValue(uint rawValue, int logicalMin, int logicalMax, ushort vendorId)
    {
        // Apple (0x05AC) declara LogicalMax=255 mas reporta porcentagem direta 0..100
        if (vendorId == 0x05AC && rawValue <= 100)
            return (int)rawValue;

        if (logicalMax > logicalMin)
        {
            if (rawValue > (uint)logicalMax) return -1;
            double pct = (rawValue - logicalMin) * 100.0 / (logicalMax - logicalMin);
            return Math.Clamp((int)Math.Round(pct), 0, 100);
        }

        return rawValue <= 100 ? (int)rawValue : -1;
    }

    public static List<HidEndpointInfo> InspectAllInterfaces(IEnumerable<string> allPaths)
    {
        var result = new List<HidEndpointInfo>();
        foreach (string path in allPaths)
        {
            if (!TryExtractVidPid(path, out ushort vid, out ushort pid))
                continue;
            using var h = OpenPath(path, readWrite: false);
            if (h == null) continue;
            var ep = ReadEndpointInfo(h, path, vid, pid);
            if (ep != null) result.Add(ep);
        }
        return result;
    }

    public static Guid GetDeviceContainerId(string hidPath)
    {
        if (string.IsNullOrWhiteSpace(hidPath)) return Guid.Empty;
        IntPtr hDevInfo = IntPtr.Zero;
        try
        {
            hDevInfo = SetupDiCreateDeviceInfoList(IntPtr.Zero, IntPtr.Zero);
            if (hDevInfo == IntPtr.Zero || hDevInfo == new IntPtr(-1)) return Guid.Empty;

            var ifData = new SP_DEVICE_INTERFACE_DATA { cbSize = Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>() };
            if (!SetupDiOpenDeviceInterfaceW(hDevInfo, hidPath, 0, ref ifData))
                return Guid.Empty;

            var devData = new SP_DEVINFO_DATA { cbSize = Marshal.SizeOf<SP_DEVINFO_DATA>() };
            SetupDiGetDeviceInterfaceDetailW(hDevInfo, ref ifData, IntPtr.Zero, 0, out _, ref devData);
            if (devData.DevInst == 0) return Guid.Empty;

            var keyContainer = new DEVPROPKEY { fmtid = new Guid("8C7ED206-3F8A-4827-B3AB-AE9E1FAEFC6C"), pid = 2 };
            var buf = new byte[16];
            if (SetupDiGetDevicePropertyW(hDevInfo, ref devData, ref keyContainer, out _, buf, 16, out uint req, 0) && req == 16)
                return new Guid(buf);
        }
        catch { }
        finally
        {
            if (hDevInfo != IntPtr.Zero && hDevInfo != new IntPtr(-1))
                SetupDiDestroyDeviceInfoList(hDevInfo);
        }
        return Guid.Empty;
    }

    public static HashSet<Guid> ReadBluetoothContainerIds()
    {
        var set = new HashSet<Guid>();
        IntPtr hDevInfo = IntPtr.Zero;
        try
        {
            hDevInfo = SetupDiGetClassDevsW(IntPtr.Zero, null, IntPtr.Zero, DIGCF_PRESENT | DIGCF_ALLCLASSES);
            if (hDevInfo == IntPtr.Zero || hDevInfo == new IntPtr(-1)) return set;

            var keyContainer = new DEVPROPKEY { fmtid = new Guid("8C7ED206-3F8A-4827-B3AB-AE9E1FAEFC6C"), pid = 2 };
            var devData = new SP_DEVINFO_DATA { cbSize = Marshal.SizeOf<SP_DEVINFO_DATA>() };
            var idBuf = new char[512];
            var valBuf = new byte[16];

            for (uint i = 0; SetupDiEnumDeviceInfo(hDevInfo, i, ref devData); i++)
            {
                if (!SetupDiGetDeviceInstanceIdW(hDevInfo, ref devData, idBuf, (uint)idBuf.Length, out _))
                    continue;
                int z = Array.IndexOf(idBuf, '\0');
                string instId = z >= 0 ? new string(idBuf, 0, z) : new string(idBuf);
                if (!instId.StartsWith("BTHENUM\\", StringComparison.OrdinalIgnoreCase)
                    && !instId.StartsWith("BTHLE\\", StringComparison.OrdinalIgnoreCase)
                    && !instId.StartsWith("BTHLEDEVICE\\", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (SetupDiGetDevicePropertyW(hDevInfo, ref devData, ref keyContainer, out _, valBuf, 16, out uint req, 0) && req == 16)
                {
                    var g = new Guid(valBuf);
                    if (g != Guid.Empty) set.Add(g);
                }
            }
        }
        catch { }
        finally
        {
            if (hDevInfo != IntPtr.Zero && hDevInfo != new IntPtr(-1))
                SetupDiDestroyDeviceInfoList(hDevInfo);
        }
        return set;
    }

    /// <summary>
    /// Enumera controles/gamepads USB, XUSB e Bluetooth atualmente conectados (com VID/PID,
    /// Manufacturer, ProductString e DEVPKEY_Device_BusReportedDeviceDesc) para correlacionar
    /// slots XInput genéricos ao hardware real (ex.: Machenike G5 Pro V2, GameSir, 8BitDo, Flydigi).
    /// </summary>
    public static List<UsbGamepadDescriptor> EnumeratePresentUsbGamepads(IEnumerable<string>? hidPaths = null)
    {
        var list = new List<UsbGamepadDescriptor>();
        var seen = new HashSet<(ushort Vid, ushort Pid)>();

        // 1. Primeiro verifica interfaces HID com UsagePage 0x01 / Usage 0x04-0x05 (Joystick/Gamepad)
        //    ou VIDs/PIDs de controles conhecidos no catálogo.
        if (hidPaths != null)
        {
            foreach (string path in hidPaths)
            {
                if (!TryExtractVidPid(path, out ushort vid, out ushort pid)) continue;
                bool isXInputPath = path.Contains("&ig_", StringComparison.OrdinalIgnoreCase);
                var catalog = PeripheralDeviceCatalog.TryLookupDevice(vid, pid);
                bool looksGamepadByCatalog = catalog?.Kind == PeripheralKind.Gamepad;

                if (!isXInputPath && !looksGamepadByCatalog)
                {
                    // Verifica se é coleção HID de controle (Joystick 0x04 / Gamepad 0x05)
                    var epCheck = InspectSinglePath(path);
                    if (epCheck == null) continue;
                    bool isHidGamepad = epCheck.UsagePage == 0x0001 && epCheck.Usage is 0x0004 or 0x0005;
                    if (!isHidGamepad) continue;

                    if (seen.Add((epCheck.VendorId, epCheck.ProductId)))
                    {
                        list.Add(new UsbGamepadDescriptor(
                            epCheck.VendorId, epCheck.ProductId, epCheck.ProductName,
                            epCheck.ManufacturerName, "", path, epCheck.IsBluetooth));
                    }
                    continue;
                }

                var ep = InspectSinglePath(path);
                if (seen.Add((vid, pid)))
                {
                    list.Add(new UsbGamepadDescriptor(
                        vid, pid,
                        ep?.ProductName ?? "",
                        ep?.ManufacturerName ?? "",
                        "",
                        path,
                        ep?.IsBluetooth ?? IsBluetoothHidPath(path)));
                }
            }
        }

        // 2. Complementa com nós PnP presentes (captura XUSB / Xbox / dongles que não expõem coleção HID 0x01:0x05
        //    e lê DEVPKEY_Device_BusReportedDeviceDesc para nomes reais de hardware).
        IntPtr hDevInfo = IntPtr.Zero;
        try
        {
            hDevInfo = SetupDiGetClassDevsW(IntPtr.Zero, null, IntPtr.Zero, DIGCF_PRESENT | DIGCF_ALLCLASSES);
            if (hDevInfo != IntPtr.Zero && hDevInfo != new IntPtr(-1))
            {
                var keyBusDesc = new DEVPROPKEY { fmtid = new Guid("540B947E-8B40-45BC-A8A2-6A0B894CBDA2"), pid = 4 };
                var keyMfg = new DEVPROPKEY { fmtid = new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), pid = 13 };
                var keyDesc = new DEVPROPKEY { fmtid = new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), pid = 2 };

                var devData = new SP_DEVINFO_DATA { cbSize = Marshal.SizeOf<SP_DEVINFO_DATA>() };
                var idBuf = new char[512];

                for (uint i = 0; SetupDiEnumDeviceInfo(hDevInfo, i, ref devData); i++)
                {
                    if (!SetupDiGetDeviceInstanceIdW(hDevInfo, ref devData, idBuf, (uint)idBuf.Length, out _))
                        continue;
                    int z = Array.IndexOf(idBuf, '\0');
                    string instId = z >= 0 ? new string(idBuf, 0, z) : new string(idBuf);
                    if (!TryExtractVidPid(instId, out ushort vid, out ushort pid))
                        continue;

                    bool isXInputNode = instId.Contains("&IG_", StringComparison.OrdinalIgnoreCase)
                        || instId.StartsWith("USB\\VID_", StringComparison.OrdinalIgnoreCase)
                        || instId.StartsWith("HID\\VID_", StringComparison.OrdinalIgnoreCase);
                    if (!isXInputNode) continue;

                    var known = PeripheralDeviceCatalog.TryLookupDevice(vid, pid);
                    bool isKnownGamepad = known?.Kind == PeripheralKind.Gamepad;
                    string busDesc = ReadDevPropString(hDevInfo, ref devData, keyBusDesc);
                    string devDesc = ReadDevPropString(hDevInfo, ref devData, keyDesc);
                    string mfg = ReadDevPropString(hDevInfo, ref devData, keyMfg);

                    bool looksLikeController = isKnownGamepad
                        || instId.Contains("&IG_", StringComparison.OrdinalIgnoreCase)
                        || busDesc.Contains("controller", StringComparison.OrdinalIgnoreCase)
                        || busDesc.Contains("gamepad", StringComparison.OrdinalIgnoreCase)
                        || busDesc.Contains("machenike", StringComparison.OrdinalIgnoreCase)
                        || busDesc.Contains("gamesir", StringComparison.OrdinalIgnoreCase)
                        || busDesc.Contains("8bitdo", StringComparison.OrdinalIgnoreCase)
                        || busDesc.Contains("flydigi", StringComparison.OrdinalIgnoreCase)
                        || busDesc.Contains("xbox", StringComparison.OrdinalIgnoreCase)
                        || devDesc.Contains("xbox", StringComparison.OrdinalIgnoreCase)
                        || devDesc.Contains("gamepad", StringComparison.OrdinalIgnoreCase);

                    if (!looksLikeController) continue;

                    // Atualiza entrada existente com BusReportedName se ela ainda estiver sem nome rico
                    int idx = list.FindIndex(g => g.VendorId == vid && g.ProductId == pid);
                    if (idx >= 0)
                    {
                        var cur = list[idx];
                        if (string.IsNullOrWhiteSpace(cur.BusReportedName) && !string.IsNullOrWhiteSpace(busDesc))
                            list[idx] = cur with { BusReportedName = busDesc, ManufacturerName = string.IsNullOrWhiteSpace(cur.ManufacturerName) ? mfg : cur.ManufacturerName };
                        continue;
                    }

                    seen.Add((vid, pid));
                    list.Add(new UsbGamepadDescriptor(
                        vid, pid,
                        !string.IsNullOrWhiteSpace(busDesc) ? busDesc : devDesc,
                        mfg,
                        busDesc,
                        instId,
                        IsBluetoothHidPath(instId)));
                }
            }
        }
        catch { }
        finally
        {
            if (hDevInfo != IntPtr.Zero && hDevInfo != new IntPtr(-1))
                SetupDiDestroyDeviceInfoList(hDevInfo);
        }

        // Prioriza dispositivos conhecidos no catálogo (como Machenike G5 Pro V2) e dispositivos não-sintéticos
        return list
            .OrderByDescending(g => PeripheralDeviceCatalog.TryLookupDevice(g.VendorId, g.ProductId) != null)
            .ThenByDescending(g => g.VendorId != 0x045E) // dongles de terceiros antes de emuladores genéricos
            .ToList();
    }

    private static string ReadDevPropString(IntPtr hDevInfo, ref SP_DEVINFO_DATA devData, DEVPROPKEY key)
    {
        var buf = new byte[512];
        if (!SetupDiGetDevicePropertyW(hDevInfo, ref devData, ref key, out _, buf, (uint)buf.Length, out uint req, 0) || req < 2)
            return "";
        string raw = Encoding.Unicode.GetString(buf, 0, (int)Math.Min(req, (uint)buf.Length));
        int z = raw.IndexOf('\0');
        return (z >= 0 ? raw[..z] : raw).Trim();
    }

    public static bool IsLikelyWirelessDongle(ushort vid, ushort pid, string? productName, bool isBluetooth)
    {
        if (isBluetooth) return false;

        if (PeripheralDeviceCatalog.TryLookupDevice(vid, pid) is { ConnectionType: PeripheralConnectionType.Dongle24GHz })
            return true;

        // Receptores / dongles 2.4 GHz conhecidos por VID/PID
        if (vid == 0x046D && pid is 0xC52B or 0xC532 or 0xC539 or 0xC53A or 0xC53D or 0xC53F or 0xC541 or 0xC545 or 0xC547 or 0xC548 or 0xC54D)
            return true;
        if (vid == 0x1532 && pid is 0x00B3 or 0x00A6 or 0x00B7 or 0x00C2 or 0x00AB or 0x00B0 or 0x007B or 0x007D or 0x0088 or 0x0090 or 0x0292 or 0x0298 or 0x0271 or 0x02BA or 0x0528 or 0x0536 or 0x0555 or 0x0556 or 0x0557 or 0x057A)
            return true;
        if (vid == 0x0B05 && pid is 0x1ACE) // ASUS ROG Omni Receiver
            return true;
        if (vid == 0x25A7 && pid is 0xFA07 or 0xFA08) // Areson / Redragon 2.4 GHz
            return true;
        if (vid == 0x3554 && pid is 0xFA09 or 0xFB40 or 0xF55D or 0xF5D5 or 0xF58A or 0xF58E or 0xF505)
            return true;
        if (vid == 0x41E4 && pid is 0x2001)
            return true;
        if (vid == 0x3710 && pid is 0x5402 or 0x5406) // Pulsar 4K/8K Dongle
            return true;
        if (vid == 0x3770 && pid is 0x0200) // Hitscan 1K/8K Dongle
            return true;
        if (vid == 0x045E && pid is 0x02FE or 0x02E6 or 0x0291 or 0x0719) // Xbox Wireless Adapter
            return true;
        if (vid == 0x1A34 && pid is 0xF517) // Machenike G5 Pro V2 2.4 GHz Dongle
            return true;
        if (vid == 0x2345 && pid is 0xE02F or 0xE02E) // Machenike G5 Pro Dongle
            return true;

        string name = (productName ?? "").ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(name)) return false;

        return name.Contains("dongle")
            || name.Contains("receiver")
            || name.Contains("receptor")
            || name.Contains("2.4g")
            || name.Contains("2.4 ghz")
            || name.Contains("lightspeed")
            || name.Contains("slipstream")
            || name.Contains("hyperpolling")
            || name.Contains("hyperspeed")
            || name.Contains("unifying")
            || name.Contains("logi bolt")
            || name.Contains("omni receiver")
            || name.Contains("wireless adapter")
            || name.Contains("rf receiver")
            || name.Contains("transceiver");
    }
}
