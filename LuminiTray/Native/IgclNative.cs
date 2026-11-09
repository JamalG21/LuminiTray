using System;
using System.Runtime.InteropServices;

namespace LuminiTray;

/// <summary>
/// Wrapper P/Invoke para Intel Graphics Control Library (igcl.dll).
/// Suporta controle de cooler e overclock para GPUs Intel Arc e Intel Graphics.
/// </summary>
public static class IgclNative
{
    private const string Dll = "igcl.dll";

    public const int CTL_RESULT_SUCCESS = 0;

    [StructLayout(LayoutKind.Sequential)]
    public struct ctl_init_args_t
    {
        public uint Size;
        public byte Version;
        public uint Flags;
        public uint SupportedVersion;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ctl_device_adapter_properties_t
    {
        public uint Size;
        public byte Version;
        public uint pci_vendor_id;
        public uint pci_device_id;
        public uint rev_id;
        public uint num_slices;
        public uint num_sub_slices;
        public uint num_eus_per_sub_slice;
        public uint num_threads_per_eu;
        public uint max_frequency;
        public uint min_frequency;
        public uint video_memory_size;
        public uint adapter_type;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string name;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ctl_fan_properties_t
    {
        public uint Size;
        public byte Version;
        public uint FanType;
        public uint MaxRPM;
        public uint MinRPM;
        [MarshalAs(UnmanagedType.I1)]
        public bool CanControl;
        public uint SupportedModes;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ctl_fan_speed_t
    {
        public uint Size;
        public byte Version;
        public int SpeedPercent;
        public uint SpeedRPM;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ctl_oc_telemetry_item_t
    {
        [MarshalAs(UnmanagedType.I1)]
        public bool bSupported;
        public double Value;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ctl_oc_properties_t
    {
        public uint Size;
        public byte Version;
        [MarshalAs(UnmanagedType.I1)]
        public bool bSupported;
        public double MaxVoltageOffset;
        public double MinVoltageOffset;
        public double MaxFrequencyOffset;
        public double MinFrequencyOffset;
        public double MaxPowerLimit;
        public double MinPowerLimit;
    }

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ctlInit")]
    private static extern int ctlInit(ref ctl_init_args_t initArgs, out IntPtr pApiHandle);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ctlClose")]
    private static extern int ctlClose(IntPtr apiHandle);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ctlEnumerateDevices")]
    private static extern int ctlEnumerateDevices(IntPtr apiHandle, ref uint deviceCount, [Out] IntPtr[] deviceHandles);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ctlGetDeviceProperties")]
    private static extern int ctlGetDeviceProperties(IntPtr deviceHandle, ref ctl_device_adapter_properties_t properties);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ctlFanGetProperties")]
    private static extern int ctlFanGetProperties(IntPtr deviceHandle, ref ctl_fan_properties_t properties);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ctlFanSetSpeed")]
    private static extern int ctlFanSetSpeed(IntPtr deviceHandle, ref ctl_fan_speed_t fanSpeed);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ctlOverclockSetFrequencyOffset")]
    private static extern int ctlOverclockSetFrequencyOffset(IntPtr deviceHandle, double freqOffsetMhz);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ctlOverclockSetVoltageOffset")]
    private static extern int ctlOverclockSetVoltageOffset(IntPtr deviceHandle, double voltOffsetMv);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ctlOverclockSetPowerLimit")]
    private static extern int ctlOverclockSetPowerLimit(IntPtr deviceHandle, double powerLimitWatts);

    private static bool _initialized;
    private static bool _available;
    private static IntPtr _apiHandle = IntPtr.Zero;
    private static IntPtr[] _devices = Array.Empty<IntPtr>();
    private static readonly object _syncLock = new();

    public static bool IsAvailable
    {
        get
        {
            EnsureInitialized();
            return _available;
        }
    }

    public static int DeviceCount => _devices.Length;

    public static void EnsureInitialized()
    {
        if (_initialized) return;
        lock (_syncLock)
        {
            if (_initialized) return;
            try
            {
                var args = new ctl_init_args_t
                {
                    Size = (uint)Marshal.SizeOf<ctl_init_args_t>(),
                    Version = 1,
                    SupportedVersion = 0
                };
                int res = ctlInit(ref args, out _apiHandle);
                if (res == CTL_RESULT_SUCCESS && _apiHandle != IntPtr.Zero)
                {
                    uint count = 0;
                    ctlEnumerateDevices(_apiHandle, ref count, Array.Empty<IntPtr>());
                    if (count > 0)
                    {
                        _devices = new IntPtr[count];
                        ctlEnumerateDevices(_apiHandle, ref count, _devices);
                        _available = true;
                    }
                }
            }
            catch
            {
                _available = false;
            }
            finally
            {
                _initialized = true;
            }
        }
    }

    public static bool SetFanSpeed(int deviceIndex, int percent)
    {
        if (!IsAvailable || deviceIndex < 0 || deviceIndex >= _devices.Length) return false;
        try
        {
            var speed = new ctl_fan_speed_t
            {
                Size = (uint)Marshal.SizeOf<ctl_fan_speed_t>(),
                Version = 1,
                SpeedPercent = Math.Clamp(percent, 0, 100),
                SpeedRPM = 0
            };
            return ctlFanSetSpeed(_devices[deviceIndex], ref speed) == CTL_RESULT_SUCCESS;
        }
        catch
        {
            return false;
        }
    }

    public static bool SetOverclock(int deviceIndex, double freqOffsetMhz, double voltOffsetMv, double powerLimitW)
    {
        if (!IsAvailable || deviceIndex < 0 || deviceIndex >= _devices.Length) return false;
        try
        {
            IntPtr dev = _devices[deviceIndex];
            bool ok = true;
            if (freqOffsetMhz != 0)
                ok &= ctlOverclockSetFrequencyOffset(dev, freqOffsetMhz) == CTL_RESULT_SUCCESS;
            if (voltOffsetMv != 0)
                ok &= ctlOverclockSetVoltageOffset(dev, voltOffsetMv) == CTL_RESULT_SUCCESS;
            if (powerLimitW > 0)
                ok &= ctlOverclockSetPowerLimit(dev, powerLimitW) == CTL_RESULT_SUCCESS;
            return ok;
        }
        catch
        {
            return false;
        }
    }

    public static void Shutdown()
    {
        lock (_syncLock)
        {
            if (_apiHandle != IntPtr.Zero)
            {
                try { ctlClose(_apiHandle); } catch { }
                _apiHandle = IntPtr.Zero;
            }
            _devices = Array.Empty<IntPtr>();
            _available = false;
            _initialized = false;
        }
    }
}
