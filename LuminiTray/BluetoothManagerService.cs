using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace LuminiTray;

public sealed class BluetoothDeviceModel
{
    public ulong Address { get; init; }
    public string AddressText { get; init; } = "";
    public string Name { get; init; } = "";
    public bool IsConnected { get; init; }
    public bool IsRemembered { get; init; }
    public bool IsAuthenticated { get; init; }
    public uint ClassOfDevice { get; init; }
    public string DeviceTypeLabel { get; init; } = "Bluetooth";
    public bool IsFavorite { get; set; }
}

/// <summary>
/// Módulo de controle Bluetooth via BluetoothAPIs.dll (Win32 nativo compatível com Windows 10/11).
/// Suporta listagem de pareados, conexão/desconexão de perfis, remoção, favoritos com auto-conexão e modo manual.
/// </summary>
public static class BluetoothManagerService
{
    private static readonly Guid[] CommonServiceGuids =
    {
        new("0000110b-0000-1000-8000-00805f9b34fb"), // AudioSink (A2DP)
        new("0000111e-0000-1000-8000-00805f9b34fb"), // Handsfree (HFP)
        new("00001108-0000-1000-8000-00805f9b34fb"), // Headset (HSP)
        new("00001124-0000-1000-8000-00805f9b34fb"), // HID (Teclado / Mouse / Controle)
        new("00001101-0000-1000-8000-00805f9b34fb")  // Serial Port (SPP)
    };

    private static readonly Dictionary<ulong, DateTime> _lastAutoAttempts = new();

    public static bool IsBluetoothRadioActive()
    {
        try
        {
            var findParams = new BLUETOOTH_FIND_RADIO_PARAMS { dwSize = (uint)Marshal.SizeOf<BLUETOOTH_FIND_RADIO_PARAMS>() };
            IntPtr findHandle = BluetoothFindFirstRadio(ref findParams, out IntPtr radioHandle);
            if (findHandle == IntPtr.Zero) return false;

            try
            {
                if (radioHandle != IntPtr.Zero)
                {
                    CloseHandle(radioHandle);
                    return true;
                }
                return false;
            }
            finally
            {
                BluetoothFindRadioClose(findHandle);
            }
        }
        catch
        {
            return false;
        }
    }

    public static List<BluetoothDeviceModel> ListPairedDevices(IEnumerable<string>? favoriteAddresses = null)
    {
        var result = new List<BluetoothDeviceModel>();
        var favSet = new HashSet<string>(favoriteAddresses ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);

        try
        {
            var searchParams = new BLUETOOTH_DEVICE_SEARCH_PARAMS
            {
                dwSize = (uint)Marshal.SizeOf<BLUETOOTH_DEVICE_SEARCH_PARAMS>(),
                fReturnAuthenticated = true,
                fReturnRemembered = true,
                fReturnConnected = true,
                fReturnUnknown = false,
                fIssueInquiry = false,
                cTimeoutMultiplier = 0,
                hRadio = IntPtr.Zero
            };

            var info = new BLUETOOTH_DEVICE_INFO { dwSize = (uint)Marshal.SizeOf<BLUETOOTH_DEVICE_INFO>() };
            IntPtr findHandle = BluetoothFindFirstDevice(ref searchParams, ref info);
            if (findHandle == IntPtr.Zero) return result;

            try
            {
                do
                {
                    string addrText = FormatAddress(info.Address);
                    result.Add(new BluetoothDeviceModel
                    {
                        Address = info.Address,
                        AddressText = addrText,
                        Name = string.IsNullOrWhiteSpace(info.szName) ? addrText : info.szName.Trim(),
                        IsConnected = info.fConnected,
                        IsRemembered = info.fRemembered,
                        IsAuthenticated = info.fAuthenticated,
                        ClassOfDevice = info.ulClassofDevice,
                        DeviceTypeLabel = ClassifyDevice(info.ulClassofDevice),
                        IsFavorite = favSet.Contains(addrText)
                    });

                    info = new BLUETOOTH_DEVICE_INFO { dwSize = (uint)Marshal.SizeOf<BLUETOOTH_DEVICE_INFO>() };
                } while (BluetoothFindNextDevice(findHandle, ref info));
            }
            finally
            {
                BluetoothFindDeviceClose(findHandle);
            }
        }
        catch { }

        return result
            .OrderByDescending(d => d.IsConnected)
            .ThenByDescending(d => d.IsFavorite)
            .ThenBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static bool SetDeviceConnected(ulong address, bool connect, out string message)
    {
        message = "";
        try
        {
            var info = new BLUETOOTH_DEVICE_INFO
            {
                dwSize = (uint)Marshal.SizeOf<BLUETOOTH_DEVICE_INFO>(),
                Address = address
            };

            if (BluetoothGetDeviceInfo(IntPtr.Zero, ref info) != 0)
            {
                message = "Dispositivo Bluetooth não encontrado.";
                return false;
            }

            uint flag = connect ? 1u : 0u; // BLUETOOTH_SERVICE_ENABLE = 1, DISABLE = 0
            bool anyOk = false;

            foreach (var guid in CommonServiceGuids)
            {
                Guid g = guid;
                uint err = BluetoothSetServiceState(IntPtr.Zero, ref info, ref g, flag);
                if (err == 0) anyOk = true;
            }

            if (anyOk)
            {
                message = connect ? "Conexão solicitada com sucesso." : "Dispositivo desconectado.";
                return true;
            }

            message = connect
                ? "Não foi possível conectar automaticamente (verifique se o dispositivo está ligado e ao alcance)."
                : "Não foi possível desconectar este perfil Bluetooth.";
            return false;
        }
        catch (Exception ex)
        {
            message = $"Erro Bluetooth: {ex.Message}";
            return false;
        }
    }

    public static bool RemoveDevice(ulong address)
    {
        try
        {
            var addr = new BLUETOOTH_ADDRESS { ullLong = address };
            return BluetoothRemoveDevice(ref addr) == 0;
        }
        catch
        {
            return false;
        }
    }

    public static bool ShouldAutoConnect(
        BluetoothDeviceModel device,
        bool radioActive,
        bool manualMode,
        DateTime utcNow,
        IReadOnlyDictionary<ulong, DateTime> lastAttempts)
    {
        if (!radioActive || manualMode) return false;
        if (!device.IsFavorite || device.IsConnected) return false;
        if (lastAttempts.TryGetValue(device.Address, out var last) && (utcNow - last).TotalSeconds < 20)
            return false;
        return true;
    }

    public static int AutoConnectFavoritesIfNeeded(AppSettings settings)
    {
        if (settings.BluetoothManualMode || settings.BluetoothFavoriteAddresses.Count == 0)
            return 0;

        if (!IsBluetoothRadioActive())
            return 0;

        int connectedCount = 0;
        var now = DateTime.UtcNow;
        var devices = ListPairedDevices(settings.BluetoothFavoriteAddresses);

        foreach (var dev in devices)
        {
            if (!ShouldAutoConnect(dev, radioActive: true, settings.BluetoothManualMode, now, _lastAutoAttempts))
                continue;

            _lastAutoAttempts[dev.Address] = now;
            if (SetDeviceConnected(dev.Address, connect: true, out _))
                connectedCount++;
        }

        return connectedCount;
    }

    public static string FormatAddress(ulong address)
    {
        byte[] b = BitConverter.GetBytes(address);
        return $"{b[5]:X2}:{b[4]:X2}:{b[3]:X2}:{b[2]:X2}:{b[1]:X2}:{b[0]:X2}";
    }

    private static string ClassifyDevice(uint cod)
    {
        uint major = (cod >> 8) & 0x1F;
        return major switch
        {
            0x02 => "Celular / Smartphone",
            0x04 => "Áudio / Fone / Caixa",
            0x05 => "Periférico (Teclado/Mouse/Controle)",
            0x06 => "Imagem / Câmera",
            0x07 => "Wearable",
            _ => "Dispositivo Bluetooth"
        };
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BLUETOOTH_FIND_RADIO_PARAMS
    {
        public uint dwSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BLUETOOTH_ADDRESS
    {
        public ulong ullLong;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEMTIME
    {
        public ushort wYear, wMonth, wDayOfWeek, wDay, wHour, wMinute, wSecond, wMilliseconds;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct BLUETOOTH_DEVICE_INFO
    {
        public uint dwSize;
        public ulong Address;
        public uint ulClassofDevice;
        [MarshalAs(UnmanagedType.Bool)] public bool fConnected;
        [MarshalAs(UnmanagedType.Bool)] public bool fRemembered;
        [MarshalAs(UnmanagedType.Bool)] public bool fAuthenticated;
        public SYSTEMTIME stLastSeen;
        public SYSTEMTIME stLastUsed;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 248)]
        public string szName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BLUETOOTH_DEVICE_SEARCH_PARAMS
    {
        public uint dwSize;
        [MarshalAs(UnmanagedType.Bool)] public bool fReturnAuthenticated;
        [MarshalAs(UnmanagedType.Bool)] public bool fReturnRemembered;
        [MarshalAs(UnmanagedType.Bool)] public bool fReturnUnknown;
        [MarshalAs(UnmanagedType.Bool)] public bool fReturnConnected;
        [MarshalAs(UnmanagedType.Bool)] public bool fIssueInquiry;
        public byte cTimeoutMultiplier;
        public IntPtr hRadio;
    }

    [DllImport("BluetoothAPIs.dll", SetLastError = true)]
    private static extern IntPtr BluetoothFindFirstRadio(ref BLUETOOTH_FIND_RADIO_PARAMS pbtfrp, out IntPtr phRadio);

    [DllImport("BluetoothAPIs.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BluetoothFindRadioClose(IntPtr hFind);

    [DllImport("BluetoothAPIs.dll", SetLastError = true)]
    private static extern IntPtr BluetoothFindFirstDevice(ref BLUETOOTH_DEVICE_SEARCH_PARAMS pbtsp, ref BLUETOOTH_DEVICE_INFO pbtdi);

    [DllImport("BluetoothAPIs.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BluetoothFindNextDevice(IntPtr hFind, ref BLUETOOTH_DEVICE_INFO pbtdi);

    [DllImport("BluetoothAPIs.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BluetoothFindDeviceClose(IntPtr hFind);

    [DllImport("BluetoothAPIs.dll", SetLastError = true)]
    private static extern uint BluetoothGetDeviceInfo(IntPtr hRadio, ref BLUETOOTH_DEVICE_INFO pbtdi);

    [DllImport("BluetoothAPIs.dll", SetLastError = true)]
    private static extern uint BluetoothSetServiceState(IntPtr hRadio, ref BLUETOOTH_DEVICE_INFO pbtdi, ref Guid pGuidService, uint dwServiceFlags);

    [DllImport("BluetoothAPIs.dll", SetLastError = true)]
    private static extern uint BluetoothRemoveDevice(ref BLUETOOTH_ADDRESS pAddress);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);
}
