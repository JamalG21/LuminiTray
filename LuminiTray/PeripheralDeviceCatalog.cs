using System;
using System.Collections.Generic;
using System.Linq;

namespace LuminiTray;

public sealed record KnownDeviceEntry(
    string ModelName,
    PeripheralKind Kind,
    PeripheralConnectionType? ConnectionType = null);

public sealed record UsbGamepadDescriptor(
    ushort VendorId,
    ushort ProductId,
    string ProductName,
    string ManufacturerName,
    string BusReportedName,
    string InstanceId,
    bool IsBluetooth);

public sealed record ResolvedXInputIdentity(
    string Key,
    string DisplayName,
    PeripheralConnectionType ConnectionType,
    int? Level,
    string ApproxText,
    bool IsCharging,
    string Source);

/// <summary>
/// Catálogo multi-marca de periféricos USB/HID/XInput/Bluetooth (mouses, teclados, headsets e controles),
/// responsável por traduzir VID/PID + strings de hardware em nomes legíveis e distinguir
/// corretamente Dongle 2.4 GHz vs Cabo USB vs Bluetooth (especialmente para controles XInput).
/// </summary>
public static class PeripheralDeviceCatalog
{
    private static readonly Dictionary<ushort, string> VendorNames = new()
    {
        [0x045E] = "Microsoft Xbox",
        [0x046D] = "Logitech",
        [0x054C] = "Sony",
        [0x057E] = "Nintendo",
        [0x05AC] = "Apple",
        [0x05A7] = "Bose",
        [0x03F0] = "HyperX",
        [0x0951] = "HyperX",
        [0x0B05] = "ASUS ROG",
        [0x0ECB] = "JBL",
        [0x0E6F] = "PDP",
        [0x0F0D] = "Hori",
        [0x1038] = "SteelSeries",
        [0x10F5] = "Turtle Beach",
        [0x11FF] = "PXN",
        [0x1395] = "EPOS / Sennheiser",
        [0x1532] = "Razer",
        [0x19F5] = "NuPhy",
        [0x1A34] = "Machenike",
        [0x1B1C] = "Corsair",
        [0x1EA7] = "Sharkoon",
        [0x20D6] = "PowerA",
        [0x2345] = "Machenike",
        [0x248A] = "Darmoshark",
        [0x24AE] = "Rapoo",
        [0x2563] = "EasySMX",
        [0x258A] = "Glorious",
        [0x25A7] = "Redragon",
        [0x291A] = "Anker Soundcore",
        [0x2DC8] = "8BitDo",
        [0x3142] = "Fifine",
        [0x3151] = "Akko / MonsGeek",
        [0x31E3] = "Wooting",
        [0x3285] = "BigBig Won",
        [0x3367] = "Endgame Gear",
        [0x33E4] = "G-Wolves",
        [0x3434] = "Keychron",
        [0x3537] = "GameSir",
        [0x3554] = "ATK / VXE / Kysona",
        [0x35BB] = "Edifier HECATE",
        [0x3710] = "Pulsar",
        [0x373B] = "ATK",
        [0x373E] = "WLmouse",
        [0x3770] = "Hitscan",
        [0x37D7] = "Flydigi",
        [0x41E4] = "MCHOSE",
        [0x9886] = "Astro Gaming",
        [0x04B4] = "Flydigi"
    };

    private static readonly Dictionary<(ushort Vid, ushort Pid), KnownDeviceEntry> KnownDevices = new()
    {
        // --- Controles / Gamepads ---
        // Machenike G5 Pro / G5 Pro V2 / Max
        [(0x1A34, 0xF517)] = new("Machenike G5 Pro V2", PeripheralKind.Gamepad, PeripheralConnectionType.Dongle24GHz),
        [(0x2345, 0xE02F)] = new("Machenike G5 Pro V2", PeripheralKind.Gamepad, PeripheralConnectionType.Dongle24GHz),
        [(0x2345, 0xE02E)] = new("Machenike G5 Pro V2", PeripheralKind.Gamepad, PeripheralConnectionType.Dongle24GHz),
        [(0x2345, 0xE00B)] = new("Machenike G5 Pro V2", PeripheralKind.Gamepad, PeripheralConnectionType.UsbCable),

        // Microsoft Xbox Oficial
        [(0x045E, 0x02FE)] = new("Controle Xbox Wireless", PeripheralKind.Gamepad, PeripheralConnectionType.Dongle24GHz),
        [(0x045E, 0x02E6)] = new("Controle Xbox Wireless", PeripheralKind.Gamepad, PeripheralConnectionType.Dongle24GHz),
        [(0x045E, 0x0291)] = new("Controle Xbox 360 Wireless", PeripheralKind.Gamepad, PeripheralConnectionType.Dongle24GHz),
        [(0x045E, 0x0719)] = new("Controle Xbox 360 Wireless", PeripheralKind.Gamepad, PeripheralConnectionType.Dongle24GHz),
        [(0x045E, 0x0B12)] = new("Controle Xbox Series X|S", PeripheralKind.Gamepad, PeripheralConnectionType.UsbCable),
        [(0x045E, 0x0B13)] = new("Controle Xbox Series X|S", PeripheralKind.Gamepad, PeripheralConnectionType.Bluetooth),
        [(0x045E, 0x02EA)] = new("Controle Xbox One S", PeripheralKind.Gamepad, PeripheralConnectionType.UsbCable),
        [(0x045E, 0x02E0)] = new("Controle Xbox One S", PeripheralKind.Gamepad, PeripheralConnectionType.Bluetooth),
        [(0x045E, 0x0B00)] = new("Controle Xbox Elite Series 2", PeripheralKind.Gamepad, PeripheralConnectionType.UsbCable),
        [(0x045E, 0x0B05)] = new("Controle Xbox Elite Series 2", PeripheralKind.Gamepad, PeripheralConnectionType.Bluetooth),

        // GameSir
        [(0x3537, 0x1041)] = new("GameSir Nova Lite", PeripheralKind.Gamepad, PeripheralConnectionType.Dongle24GHz),
        [(0x3537, 0x1042)] = new("GameSir Cyclone 2", PeripheralKind.Gamepad, PeripheralConnectionType.Dongle24GHz),
        [(0x3537, 0x1043)] = new("GameSir Tarantula Pro", PeripheralKind.Gamepad, PeripheralConnectionType.Dongle24GHz),
        [(0x3537, 0x1022)] = new("GameSir T4 Kaleid", PeripheralKind.Gamepad, PeripheralConnectionType.UsbCable),
        [(0x3537, 0x1010)] = new("GameSir G7 SE", PeripheralKind.Gamepad, PeripheralConnectionType.UsbCable),

        // 8BitDo
        [(0x2DC8, 0x3106)] = new("8BitDo Ultimate 2.4G", PeripheralKind.Gamepad, PeripheralConnectionType.Dongle24GHz),
        [(0x2DC8, 0x310A)] = new("8BitDo Ultimate 2C Wireless", PeripheralKind.Gamepad, PeripheralConnectionType.Dongle24GHz),
        [(0x2DC8, 0x3016)] = new("8BitDo Ultimate Wireless", PeripheralKind.Gamepad, PeripheralConnectionType.Dongle24GHz),
        [(0x2DC8, 0x6006)] = new("8BitDo Pro 2", PeripheralKind.Gamepad, PeripheralConnectionType.Bluetooth),

        // Flydigi
        [(0x04B4, 0x2412)] = new("Flydigi Vader 3 / 4 Pro", PeripheralKind.Gamepad, PeripheralConnectionType.Dongle24GHz),
        [(0x04B4, 0x2410)] = new("Flydigi Apex 4", PeripheralKind.Gamepad, PeripheralConnectionType.Dongle24GHz),
        [(0x37D7, 0x2412)] = new("Flydigi Vader 4 Pro", PeripheralKind.Gamepad, PeripheralConnectionType.Dongle24GHz),
        [(0x37D7, 0x2410)] = new("Flydigi Apex 4", PeripheralKind.Gamepad, PeripheralConnectionType.Dongle24GHz),

        // EasySMX / BigBig Won
        [(0x2563, 0x058D)] = new("EasySMX X10 / X05", PeripheralKind.Gamepad, PeripheralConnectionType.Dongle24GHz),
        [(0x3285, 0x0601)] = new("BigBig Won Rainbow 2 Pro", PeripheralKind.Gamepad, PeripheralConnectionType.Dongle24GHz),
        [(0x3285, 0x0603)] = new("BigBig Won Blitz 2", PeripheralKind.Gamepad, PeripheralConnectionType.Dongle24GHz),

        // --- Mouses / Teclados / Receptores ---
        [(0x25A7, 0xFA07)] = new("Redragon Wireless Mouse", PeripheralKind.Mouse, PeripheralConnectionType.Dongle24GHz),
        [(0x25A7, 0xFA08)] = new("Redragon Wireless Mouse", PeripheralKind.Mouse, PeripheralConnectionType.Dongle24GHz),
        [(0x1EA7, 0x0067)] = new("Sharkoon 2.4G Wireless Mouse", PeripheralKind.Mouse, PeripheralConnectionType.Dongle24GHz),
        [(0x1EA7, 0x0907)] = new("Sharkoon 2.4G Wireless Keyboard", PeripheralKind.Keyboard, PeripheralConnectionType.Dongle24GHz),
        [(0x0B05, 0x1ACE)] = new("ASUS ROG Omni Receiver", PeripheralKind.Device, PeripheralConnectionType.Dongle24GHz),
        [(0x3710, 0x5402)] = new("Pulsar 4K Dongle Mouse", PeripheralKind.Mouse, PeripheralConnectionType.Dongle24GHz),
        [(0x3710, 0x5406)] = new("Pulsar X2 / Xlite 8K", PeripheralKind.Mouse, PeripheralConnectionType.Dongle24GHz),
        [(0x3770, 0x0200)] = new("Hitscan Hyperlight", PeripheralKind.Mouse, PeripheralConnectionType.Dongle24GHz),
        [(0x3554, 0xF58A)] = new("VXE Dragonfly R1", PeripheralKind.Mouse, PeripheralConnectionType.Dongle24GHz),
        [(0x3554, 0xF58E)] = new("VXE Dragonfly R1 Pro", PeripheralKind.Mouse, PeripheralConnectionType.Dongle24GHz),
        [(0x3554, 0xF505)] = new("VXE Dragonfly R1 SE+", PeripheralKind.Mouse, PeripheralConnectionType.Dongle24GHz),
        [(0x3554, 0xF55D)] = new("Redragon M916 Pro 1K", PeripheralKind.Mouse, PeripheralConnectionType.Dongle24GHz),
        [(0x3554, 0xF5D5)] = new("Redragon M913 Tri-Mode", PeripheralKind.Mouse, PeripheralConnectionType.Dongle24GHz),
        [(0x3554, 0xFA09)] = new("AULA / MCHOSE Keyboard", PeripheralKind.Keyboard, PeripheralConnectionType.Dongle24GHz),
        [(0x3554, 0xFB40)] = new("AULA / MCHOSE Keyboard", PeripheralKind.Keyboard, PeripheralConnectionType.Dongle24GHz),
        [(0x41E4, 0x2001)] = new("MCHOSE Wireless Keyboard", PeripheralKind.Keyboard, PeripheralConnectionType.Dongle24GHz)
    };

    private static readonly string[] GenericHardwareNames =
    {
        "dispositivo de entrada usb",
        "usb input device",
        "hid-compliant",
        "hid compliant",
        "dispositivo compatível com hid",
        "dispositivo compativel com hid",
        "xbox 360 controller for windows",
        "xbox 360 peripherals",
        "controller (xbox 360 for windows)",
        "usb receiver",
        "wireless receiver",
        "2.4g wireless receiver",
        "2.4g receiver",
        "gaming mouse",
        "gaming keyboard",
        "rapoo gaming device"
    };

    public static string? GetVendorName(ushort vid) =>
        VendorNames.TryGetValue(vid, out var name) ? name : null;

    public static KnownDeviceEntry? TryLookupDevice(ushort vid, ushort pid) =>
        KnownDevices.TryGetValue((vid, pid), out var entry) ? entry : null;

    public static bool IsGenericName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return true;
        string lower = name.Trim().ToLowerInvariant();
        if (lower.Length <= 2) return true;
        foreach (string g in GenericHardwareNames)
        {
            if (lower == g || lower.StartsWith("hid-compliant ") || lower.StartsWith("dispositivo compatível com hid"))
                return true;
        }
        return false;
    }

    public static string BuildDisplayName(
        ushort vid,
        ushort pid,
        string? productName,
        string? manufacturerName = null,
        string? busReportedName = null,
        string? fallbackName = null)
    {
        if (TryLookupDevice(vid, pid) is { } known && !string.IsNullOrWhiteSpace(known.ModelName))
            return known.ModelName;

        string? best = new[] { productName, busReportedName, fallbackName }
            .Select(CleanHardwareName)
            .FirstOrDefault(s => !string.IsNullOrWhiteSpace(s) && !IsGenericName(s));

        string? vendor = !string.IsNullOrWhiteSpace(manufacturerName) && !IsGenericName(manufacturerName)
            ? manufacturerName.Trim()
            : GetVendorName(vid);

        if (!string.IsNullOrWhiteSpace(best))
        {
            if (!string.IsNullOrWhiteSpace(vendor) &&
                !best.Contains(vendor, StringComparison.OrdinalIgnoreCase) &&
                !vendor.Contains(best, StringComparison.OrdinalIgnoreCase))
            {
                return $"{vendor} {best}";
            }
            return best;
        }

        if (!string.IsNullOrWhiteSpace(vendor))
            return $"{vendor} ({pid:X4})";

        return !string.IsNullOrWhiteSpace(fallbackName) ? fallbackName.Trim() : $"Periférico ({vid:X4}:{pid:X4})";
    }

    public static string CleanHardwareName(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        string s = raw.Trim();
        foreach (string suffix in new[] { " Wireless Receiver", " USB Receiver", " Receiver", " Dongle", " 2.4G", " (Xbox 360 For Windows)" })
        {
            if (s.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) && s.Length > suffix.Length + 2)
                s = s[..^suffix.Length].Trim();
        }
        return s;
    }

    /// <summary>
    /// Resolve nome real, tipo de conexão (Dongle 2.4 GHz vs Cabo USB vs Bluetooth) e bateria
    /// para um slot XInput ativo, evitando classificar dongles 2.4 GHz de terceiros (como o Machenike G5 Pro V2)
    /// como "Controle Xbox Cabeado 100%".
    /// </summary>
    public static ResolvedXInputIdentity ResolveXInputDevice(
        uint slotIndex,
        byte batteryType,
        byte batteryLevel,
        IReadOnlyList<UsbGamepadDescriptor> presentGamepads,
        int matchedIndex)
    {
        UsbGamepadDescriptor? hw = matchedIndex >= 0 && matchedIndex < presentGamepads.Count
            ? presentGamepads[matchedIndex]
            : null;

        bool isOfficialMicrosoft = hw != null && hw.VendorId == 0x045E;
        bool isOfficialWirelessAdapter = hw != null && hw.VendorId == 0x045E && hw.ProductId is 0x02FE or 0x02E6 or 0x0291 or 0x0719;

        string name;
        PeripheralConnectionType conn;
        string key;

        if (hw != null)
        {
            name = BuildDisplayName(hw.VendorId, hw.ProductId, hw.ProductName, hw.ManufacturerName, hw.BusReportedName, $"Controle XInput #{slotIndex + 1}");
            key = $"xinput:{hw.VendorId:X4}:{hw.ProductId:X4}:{slotIndex}";

            if (TryLookupDevice(hw.VendorId, hw.ProductId) is { ConnectionType: { } knownConn })
            {
                conn = knownConn;
            }
            else if (hw.IsBluetooth)
            {
                conn = PeripheralConnectionType.Bluetooth;
            }
            else if (isOfficialWirelessAdapter || PeripheralHidTransport.IsLikelyWirelessDongle(hw.VendorId, hw.ProductId, $"{hw.ProductName} {hw.BusReportedName}", isBluetooth: false))
            {
                conn = PeripheralConnectionType.Dongle24GHz;
            }
            else if (!isOfficialMicrosoft)
            {
                // A maioria dos controles multi-modo de terceiros em modo XInput usa dongle 2.4 GHz
                // quando não há PID explícito de cabo registrado no catálogo.
                bool looksWired = $"{hw.ProductName} {hw.BusReportedName}".Contains("wired", StringComparison.OrdinalIgnoreCase)
                    || $"{hw.ProductName} {hw.BusReportedName}".Contains("cable", StringComparison.OrdinalIgnoreCase);
                conn = looksWired ? PeripheralConnectionType.UsbCable : PeripheralConnectionType.Dongle24GHz;
            }
            else
            {
                conn = batteryType == 0x01 ? PeripheralConnectionType.UsbCable : PeripheralConnectionType.XInput;
            }
        }
        else
        {
            name = $"Controle Xbox / XInput #{slotIndex + 1}";
            key = $"xinput:{slotIndex}";
            conn = batteryType == 0x01 ? PeripheralConnectionType.XInput : PeripheralConnectionType.Dongle24GHz;
        }

        // Bateria real via XInput (Alcalina = 0x02, NiMH = 0x03)
        if (batteryType is 0x02 or 0x03)
        {
            var (lvl, approx, chg) = PeripheralBatteryService.ParseXboxXInputLevel(batteryType, batteryLevel);
            return new ResolvedXInputIdentity(key, name, conn, lvl, approx, chg, "XInput");
        }

        // BATTERY_TYPE_WIRED (0x01): apenas controles Xbox oficiais realmente cabeados devem reportar "Cabeado".
        // Dongles 2.4 GHz de terceiros (Machenike, GameSir, 8BitDo, Flydigi, etc.) emulam XUSB wired e NÃO expõem %.
        if (batteryType == 0x01)
        {
            if (isOfficialMicrosoft && conn == PeripheralConnectionType.UsbCable)
                return new ResolvedXInputIdentity(key, name, PeripheralConnectionType.UsbCable, 100, "Cabeado", true, "XInput USB");

            return new ResolvedXInputIdentity(key, name, conn, null, "Conectado", false, "XInput");
        }

        return new ResolvedXInputIdentity(key, name, conn, null, "Conectado", false, "XInput");
    }
}
