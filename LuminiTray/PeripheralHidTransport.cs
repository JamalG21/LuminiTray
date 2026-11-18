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
    bool IsBluetooth);

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

    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_GetFeature(SafeFileHandle hidDeviceObject, [In, Out] byte[] reportBuffer, uint reportBufferLength);

    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_SetFeature(SafeFileHandle hidDeviceObject, byte[] reportBuffer, uint reportBufferLength);

    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_FlushQueue(SafeFileHandle hidDeviceObject);

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

            return new HidEndpointInfo(
                path, vid, pid, usagePage, usage,
                inLen, outLen, featLen,
                ExtractInterfaceNumber(path),
                prodName,
                IsBluetoothHidPath(path));
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
}
