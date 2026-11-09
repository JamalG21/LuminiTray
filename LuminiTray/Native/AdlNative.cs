using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using Microsoft.Win32;

namespace LuminiTray;

/// <summary>
/// Wrapper P/Invoke para a biblioteca AMD Display Library (ADL: atiadlxx.dll / atiadlxy.dll).
/// Suporta controle de fan (OD5, OD6, ODN) e ajuste de performance/SPPT.
/// </summary>
public static class AdlNative
{
    private const string Dll64 = "atiadlxx.dll";
    private const string Dll32 = "atiadlxy.dll";

    public const int ADL_OK = 0;
    public const int ADL_TRUE = 1;
    public const int ADL_FALSE = 0;

    public const int ADL_DL_FANCTRL_SUPPORTS_PERCENT_READ = 1;
    public const int ADL_DL_FANCTRL_SUPPORTS_PERCENT_WRITE = 2;
    public const int ADL_DL_FANCTRL_SUPPORTS_RPM_READ = 4;
    public const int ADL_DL_FANCTRL_SUPPORTS_RPM_WRITE = 8;
    public const int ADL_DL_FANCTRL_FLAG_USER_DEFINED_SPEED = 1;

    public const int ADL_DL_FANCTRL_SPEED_TYPE_PERCENT = 1;
    public const int ADL_DL_FANCTRL_SPEED_TYPE_RPM = 2;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate IntPtr ADL_Main_Memory_Alloc(int size);

    // Callbacks de alocação padrão
    private static readonly ADL_Main_Memory_Alloc AllocCallback = Marshal.AllocHGlobal;

    [StructLayout(LayoutKind.Sequential)]
    public struct AdapterInfo
    {
        public int Size;
        public int AdapterIndex;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string UDID;
        public int BusNumber;
        public int DeviceNumber;
        public int FunctionNumber;
        public int VendorID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string AdapterName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string DisplayName;
        public int Present;
        public int Exist;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string DriverPath;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string DriverPathExt;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string PNPString;
        public int OSDisplayIndex;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ADLFanSpeedValue
    {
        public int Size;
        public int SpeedType;
        public int FanSpeed;
        public int Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ADLOD6FanSpeedValue
    {
        public int Speed;
        public int FanSpeedPercent;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ADLOD6FanSpeedInfo
    {
        public int Supported;
        public int MinPercent;
        public int MaxPercent;
        public int MinRPM;
        public int MaxRPM;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ADLODNFanControl
    {
        public int Mode;
        public int FanControlMode;
        public int CurrentFanSpeed;
        public int CurrentFanSpeedMode;
        public int TargetFanSpeed;
        public int TargetTemperature;
        public int MinPerformanceClock;
        public int MinFanLimit;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ADLOD6PowerControlInfo
    {
        public int Supported;
        public int MinValue;
        public int MaxValue;
        public int StepValue;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ADLOD6StateInfo
    {
        public int NumberOfPerformanceLevels;
        public int State;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public ADLOD6PerformanceLevel[] Levels;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ADLOD6PerformanceLevel
    {
        public int EngineClock;
        public int MemoryClock;
    }

    // P/Invoke dinâmico ou estático com fallback
    [DllImport(Dll64, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ADL_Main_Control_Create")]
    private static extern int ADL64_Main_Control_Create(ADL_Main_Memory_Alloc callback, int enumConnectedAdapters);

    [DllImport(Dll64, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ADL_Main_Control_Destroy")]
    private static extern int ADL64_Main_Control_Destroy();

    [DllImport(Dll64, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ADL_Adapter_NumberOfAdapters_Get")]
    private static extern int ADL64_Adapter_NumberOfAdapters_Get(out int numAdapters);

    [DllImport(Dll64, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ADL_Adapter_AdapterInfo_Get")]
    private static extern int ADL64_Adapter_AdapterInfo_Get(IntPtr infoArray, int inputSize);

    [DllImport(Dll64, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ADL_Overdrive5_FanSpeed_Get")]
    private static extern int ADL64_Overdrive5_FanSpeed_Get(int adapterIndex, int thermalControllerIndex, ref ADLFanSpeedValue fanSpeedValue);

    [DllImport(Dll64, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ADL_Overdrive5_FanSpeed_Set")]
    private static extern int ADL64_Overdrive5_FanSpeed_Set(int adapterIndex, int thermalControllerIndex, ref ADLFanSpeedValue fanSpeedValue);

    [DllImport(Dll64, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ADL_Overdrive5_FanSpeedToDefault_Set")]
    private static extern int ADL64_Overdrive5_FanSpeedToDefault_Set(int adapterIndex, int thermalControllerIndex);

    [DllImport(Dll64, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ADL_Overdrive6_FanSpeed_Get")]
    private static extern int ADL64_Overdrive6_FanSpeed_Get(int adapterIndex, ref ADLOD6FanSpeedValue fanSpeedValue);

    [DllImport(Dll64, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ADL_Overdrive6_FanSpeed_Set")]
    private static extern int ADL64_Overdrive6_FanSpeed_Set(int adapterIndex, ref ADLOD6FanSpeedValue fanSpeedValue);

    [DllImport(Dll64, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ADL_Overdrive6_FanSpeed_Reset")]
    private static extern int ADL64_Overdrive6_FanSpeed_Reset(int adapterIndex);

    [DllImport(Dll64, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ADL_Overdrive6_PowerControl_Set")]
    private static extern int ADL64_Overdrive6_PowerControl_Set(int adapterIndex, int value);

    private static bool _initialized = false;
    private static bool _available = false;
    private static readonly object _syncLock = new();
    private static AdapterInfo[] _adapters = Array.Empty<AdapterInfo>();

    public static bool IsAvailable
    {
        get
        {
            EnsureInitialized();
            return _available;
        }
    }

    public static AdapterInfo[] Adapters
    {
        get
        {
            EnsureInitialized();
            return _adapters;
        }
    }

    public static void EnsureInitialized()
    {
        if (_initialized) return;
        lock (_syncLock)
        {
            if (_initialized) return;
            try
            {
                int res = ADL64_Main_Control_Create(AllocCallback, 1);
                if (res == ADL_OK)
                {
                    _available = true;
                    LoadAdapters();
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

    private static void LoadAdapters()
    {
        try
        {
            if (ADL64_Adapter_NumberOfAdapters_Get(out int num) == ADL_OK && num > 0)
            {
                int size = Marshal.SizeOf<AdapterInfo>();
                IntPtr buffer = Marshal.AllocHGlobal(size * num);
                try
                {
                    if (ADL64_Adapter_AdapterInfo_Get(buffer, size * num) == ADL_OK)
                    {
                        var list = new System.Collections.Generic.List<AdapterInfo>();
                        for (int i = 0; i < num; i++)
                        {
                            IntPtr ptr = new IntPtr(buffer.ToInt64() + i * size);
                            var info = Marshal.PtrToStructure<AdapterInfo>(ptr);
                            if (info.Exist == 1 && info.Present == 1 && !string.IsNullOrWhiteSpace(info.AdapterName))
                            {
                                list.Add(info);
                            }
                        }
                        _adapters = list.ToArray();
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
        }
        catch { }
    }

    public static bool SetFanSpeedPercent(int adapterIndex, int percent)
    {
        if (!IsAvailable) return false;
        percent = Math.Clamp(percent, 0, 100);

        // Tenta OD6
        try
        {
            var od6 = new ADLOD6FanSpeedValue
            {
                Speed = percent,
                FanSpeedPercent = 1
            };
            if (ADL64_Overdrive6_FanSpeed_Set(adapterIndex, ref od6) == ADL_OK)
                return true;
        }
        catch { }

        // Tenta OD5
        try
        {
            var od5 = new ADLFanSpeedValue
            {
                Size = Marshal.SizeOf<ADLFanSpeedValue>(),
                SpeedType = ADL_DL_FANCTRL_SPEED_TYPE_PERCENT,
                FanSpeed = percent,
                Flags = ADL_DL_FANCTRL_FLAG_USER_DEFINED_SPEED
            };
            if (ADL64_Overdrive5_FanSpeed_Set(adapterIndex, 0, ref od5) == ADL_OK)
                return true;
        }
        catch { }

        return false;
    }

    public static bool RestoreFanSpeedToDefault(int adapterIndex)
    {
        if (!IsAvailable) return false;

        try
        {
            if (ADL64_Overdrive6_FanSpeed_Reset(adapterIndex) == ADL_OK)
                return true;
        }
        catch { }

        try
        {
            if (ADL64_Overdrive5_FanSpeedToDefault_Set(adapterIndex, 0) == ADL_OK)
                return true;
        }
        catch { }

        return false;
    }

    public static int GetFanSpeedPercent(int adapterIndex)
    {
        if (!IsAvailable) return -1;

        try
        {
            var od6 = new ADLOD6FanSpeedValue();
            if (ADL64_Overdrive6_FanSpeed_Get(adapterIndex, ref od6) == ADL_OK)
            {
                return od6.Speed;
            }
        }
        catch { }

        try
        {
            var od5 = new ADLFanSpeedValue
            {
                Size = Marshal.SizeOf<ADLFanSpeedValue>(),
                SpeedType = ADL_DL_FANCTRL_SPEED_TYPE_PERCENT
            };
            if (ADL64_Overdrive5_FanSpeed_Get(adapterIndex, 0, ref od5) == ADL_OK)
            {
                return od5.FanSpeed;
            }
        }
        catch { }

        return -1;
    }

    public static bool SetPowerLimitPercent(int adapterIndex, int percentDelta)
    {
        if (!IsAvailable) return false;
        try
        {
            return ADL64_Overdrive6_PowerControl_Set(adapterIndex, percentDelta) == ADL_OK;
        }
        catch
        {
            return false;
        }
    }

    // =========================================================================
    // Soft Power Play Tables (SPPT) no Registro do Windows
    // =========================================================
    private const string VideoClassGuid = "{4d36e968-e325-11ce-bfc1-08002be10318}";
    private const string SpptValueName = "PP_PhmSoftPowerPlayTable";

    public static byte[]? ReadSpptTable(string subKey = "0000")
    {
        try
        {
            string keyPath = $@"SYSTEM\CurrentControlSet\Control\Class\{VideoClassGuid}\{subKey}";
            using var key = Registry.LocalMachine.OpenSubKey(keyPath, false);
            if (key == null) return null;
            return key.GetValue(SpptValueName) as byte[];
        }
        catch
        {
            return null;
        }
    }

    public static bool WriteSpptTable(byte[] tableData, string subKey = "0000")
    {
        try
        {
            string keyPath = $@"SYSTEM\CurrentControlSet\Control\Class\{VideoClassGuid}\{subKey}";
            using var key = Registry.LocalMachine.OpenSubKey(keyPath, true);
            if (key == null) return false;
            key.SetValue(SpptValueName, tableData, RegistryValueKind.Binary);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool DeleteSpptTable(string subKey = "0000")
    {
        try
        {
            string keyPath = $@"SYSTEM\CurrentControlSet\Control\Class\{VideoClassGuid}\{subKey}";
            using var key = Registry.LocalMachine.OpenSubKey(keyPath, true);
            if (key == null) return false;
            if (key.GetValue(SpptValueName) != null)
            {
                key.DeleteValue(SpptValueName);
            }
            return true;
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
            if (_available)
            {
                try { ADL64_Main_Control_Destroy(); } catch { }
                _available = false;
            }
            _initialized = false;
        }
    }
}
