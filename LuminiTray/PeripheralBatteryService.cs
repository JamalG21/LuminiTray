using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace LuminiTray;

public enum PeripheralKind
{
    Device,
    Mouse,
    Keyboard,
    Headset,
    Gamepad
}

public enum PeripheralConnectionType
{
    Unknown,
    Dongle24GHz,
    Bluetooth,
    UsbCable,
    XInput,
    Hid
}

public sealed class PeripheralBatteryInfo
{
    public string Key { get; init; } = "";
    public string Name { get; set; } = "";
    public int? Level { get; set; }
    public bool IsCharging { get; set; }
    public bool IsOnline { get; set; } = true;
    public bool IsAsleep { get; set; }
    public int? Millivolts { get; set; }
    public string Source { get; init; } = "";
    public PeripheralKind Kind { get; init; } = PeripheralKind.Device;
    public string ApproxText { get; set; } = "";
    public bool ViaBluetooth { get; set; }
    public PeripheralConnectionType ConnectionType { get; set; } = PeripheralConnectionType.Unknown;

    public bool IsDongle => ConnectionType == PeripheralConnectionType.Dongle24GHz;

    public string ConnectionLabel => ConnectionType switch
    {
        PeripheralConnectionType.Dongle24GHz => "Dongle 2.4 GHz",
        PeripheralConnectionType.Bluetooth => "Bluetooth",
        PeripheralConnectionType.UsbCable => "Cabo USB",
        PeripheralConnectionType.XInput => "XInput",
        PeripheralConnectionType.Hid => "HID",
        _ => ViaBluetooth ? "Bluetooth" : Source
    };

    public string ConnectionBadge => ConnectionType switch
    {
        PeripheralConnectionType.Dongle24GHz => "📡 Dongle 2.4 GHz",
        PeripheralConnectionType.Bluetooth => "ᛒ Bluetooth",
        PeripheralConnectionType.UsbCable => "🔌 Cabo USB",
        PeripheralConnectionType.XInput => "🎮 XInput",
        PeripheralConnectionType.Hid => "HID",
        _ => ViaBluetooth ? "ᛒ Bluetooth" : ""
    };

    public string KindGlyph => Kind switch
    {
        PeripheralKind.Mouse => "\uE962",
        PeripheralKind.Keyboard => "\uE765",
        PeripheralKind.Headset => "\uE7F6",
        PeripheralKind.Gamepad => "\uE7FC",
        _ => "\uE83F"
    };

    public string KindLabel => Kind switch
    {
        PeripheralKind.Mouse => "Mouse",
        PeripheralKind.Keyboard => "Teclado",
        PeripheralKind.Headset => "Headset / Áudio",
        PeripheralKind.Gamepad => "Controle",
        _ => "Periférico"
    };

    public string DisplayLevel => Level is int l
        ? (!string.IsNullOrEmpty(ApproxText) ? $"~{l}% ({ApproxText})" : $"{l}%")
        : (IsAsleep ? "Em espera" : (IsCharging ? "Carregando" : (IsOnline ? "Conectado" : "—")));
}

/// <summary>
/// Motor nativo em C# baseado no HaloBattery (HeyOkay/HaloBattery) para monitorar
/// bateria de mouses, teclados, headsets e controles sem fio via Bluetooth PnP, XInput e HID 2.4GHz/USB.
/// </summary>
public static class PeripheralBatteryService
{
    [StructLayout(LayoutKind.Sequential)]
    private struct XINPUT_BATTERY_INFORMATION
    {
        public byte BatteryType;
        public byte BatteryLevel;
    }

    [DllImport("xinput1_4.dll", EntryPoint = "XInputGetBatteryInformation")]
    private static extern uint XInput14GetBatteryInformation(uint dwUserIndex, byte devType, out XINPUT_BATTERY_INFORMATION pBatteryInformation);

    [DllImport("xinput1_3.dll", EntryPoint = "XInputGetBatteryInformation")]
    private static extern uint XInput13GetBatteryInformation(uint dwUserIndex, byte devType, out XINPUT_BATTERY_INFORMATION pBatteryInformation);

    private static readonly Dictionary<ushort, string> RazerKnown = new()
    {
        [0x0528] = "Razer BlackShark V2 Pro (2020)",
        [0x0536] = "Razer Barracuda X (2022)",
        [0x0555] = "Razer BlackShark V2 Pro (2023)",
        [0x0556] = "Razer BlackShark V2 Pro (2023)",
        [0x0557] = "Razer BlackShark V2 Pro (2023)",
        [0x057A] = "Razer BlackShark V3",
        [0x0290] = "Razer DeathStalker V2 Pro",
        [0x0292] = "Razer DeathStalker V2 Pro",
        [0x0296] = "Razer DeathStalker V2 Pro TKL",
        [0x0298] = "Razer DeathStalker V2 Pro TKL",
        [0x0271] = "Razer BlackWidow V3 Mini HyperSpeed",
        [0x0258] = "Razer BlackWidow V3 Mini HyperSpeed",
        [0x02BA] = "Razer BlackWidow V4 Mini HyperSpeed",
        [0x02B9] = "Razer BlackWidow V4 Mini HyperSpeed",
        [0x007A] = "Razer Viper Ultimate",
        [0x007B] = "Razer Viper Ultimate",
        [0x007C] = "Razer DeathAdder V2 Pro",
        [0x007D] = "Razer DeathAdder V2 Pro",
        [0x0083] = "Razer Basilisk X HyperSpeed",
        [0x0086] = "Razer Basilisk Ultimate",
        [0x0088] = "Razer Basilisk Ultimate",
        [0x008F] = "Razer Naga Pro",
        [0x0090] = "Razer Naga Pro",
        [0x0094] = "Razer Orochi V2",
        [0x00A5] = "Razer Viper V2 Pro",
        [0x00A6] = "Razer Viper V2 Pro",
        [0x00AA] = "Razer Basilisk V3 Pro",
        [0x00AB] = "Razer Basilisk V3 Pro",
        [0x00AF] = "Razer Cobra Pro",
        [0x00B0] = "Razer Cobra Pro",
        [0x00B3] = "Razer HyperPolling Dongle",
        [0x00B6] = "Razer DeathAdder V3 Pro",
        [0x00B7] = "Razer DeathAdder V3 Pro",
        [0x00C1] = "Razer Viper V3 Pro",
        [0x00C2] = "Razer Viper V3 Pro"
    };

    private static readonly Dictionary<ushort, string> LogitechReceivers = new()
    {
        [0xC52B] = "Logitech Unifying Receiver",
        [0xC532] = "Logitech Unifying Receiver",
        [0xC539] = "Logitech Lightspeed Receiver",
        [0xC53A] = "Logitech Lightspeed Receiver",
        [0xC53D] = "Logitech Lightspeed Receiver",
        [0xC53F] = "Logitech Lightspeed Receiver",
        [0xC541] = "Logitech Lightspeed Receiver",
        [0xC545] = "Logitech Lightspeed Receiver",
        [0xC547] = "Logitech Lightspeed Receiver",
        [0xC548] = "Logitech Bolt Receiver",
        [0xC54D] = "Logitech Lightspeed Receiver"
    };

    private static readonly Dictionary<(ushort Vid, ushort Pid), (string Name, PeripheralKind Kind)> GamingChecksum17Devices = new()
    {
        [(0x258A, 0x2015)] = ("Glorious Model O Wireless", PeripheralKind.Mouse),
        [(0x258A, 0x2022)] = ("Glorious Model D Wireless", PeripheralKind.Mouse),
        [(0x258A, 0x2023)] = ("Glorious Model O- Wireless", PeripheralKind.Mouse),
        [(0x3710, 0x5402)] = ("Pulsar Wireless Mouse", PeripheralKind.Mouse),
        [(0x3710, 0x5406)] = ("Pulsar X2 / Xlite Wireless", PeripheralKind.Mouse),
        [(0x373E, 0x001E)] = ("WLmouse Beast X", PeripheralKind.Mouse),
        [(0x373E, 0x0024)] = ("WLmouse Beast X Max", PeripheralKind.Mouse),
        [(0x3554, 0xF58A)] = ("Lamzu Atlantis Wireless", PeripheralKind.Mouse),
        [(0x3554, 0xF58F)] = ("ATK / VXE Wireless Mouse", PeripheralKind.Mouse),
        [(0x3554, 0xF59F)] = ("Mchose Wireless Mouse", PeripheralKind.Mouse),
        [(0x3367, 0x1950)] = ("Endgame Gear XM2we", PeripheralKind.Mouse)
    };

    public static List<PeripheralBatteryInfo> PollAll()
    {
        var results = new List<PeripheralBatteryInfo>();
        var claimed = new HashSet<(ushort Vid, ushort Pid)>();
        List<string> hidPaths = PeripheralHidTransport.EnumerateHidInterfacePaths();
        var presentVids = new HashSet<ushort>();
        foreach (string p in hidPaths)
        {
            if (PeripheralHidTransport.TryExtractVidPid(p, out ushort vid, out _))
                presentVids.Add(vid);
        }

        // 1. Bluetooth nativo (Windows PnP DEVPKEY_Bluetooth_Battery)
        PollBluetooth(results);
        var btContainers = PeripheralHidTransport.ReadBluetoothContainerIds();

        // 2. XInput (Xbox / Machenike / GameSir / 8BitDo / Flydigi / XInput gamepads)
        PollXInput(hidPaths, results, claimed);

        // 3. HID diretos (apenas provedores cujo VID está realmente plugado, evitando abrir outros dispositivos)
        if (presentVids.Contains(0x046D)) PollLogitech(hidPaths, results, claimed);
        if (presentVids.Contains(0x1532)) PollRazer(hidPaths, results, claimed);
        if (presentVids.Contains(0x054C)) PollPlayStation(hidPaths, results, claimed);
        if (presentVids.Contains(0x1B1C)) PollCorsair(hidPaths, results, claimed);
        if (presentVids.Contains(0x1038)) PollSteelSeries(hidPaths, results, claimed);
        if (presentVids.Contains(0x03F0) || presentVids.Contains(0x0951)) PollHyperX(hidPaths, results, claimed);
        if (presentVids.Contains(0x3434)) PollKeychron(hidPaths, results, claimed);
        if (presentVids.Contains(0x0ECB)) PollJblQuantum(hidPaths, results, claimed);

        // 4. Provedores inspirados no Wireless-Device-Battery-Monitor (WDBM)
        if (presentVids.Contains(0x0B05)) PollAsusRog(hidPaths, results, claimed);
        if (presentVids.Contains(0x3554) || presentVids.Contains(0x41E4)) PollAulaMchose(hidPaths, results, claimed);
        if (presentVids.Contains(0x3554) || presentVids.Contains(0x373B) || presentVids.Contains(0x3770) ||
            presentVids.Contains(0x33E4) || presentVids.Contains(0x3710) || presentVids.Contains(0x35BB))
            PollKysonaFamily(hidPaths, presentVids, results, claimed);
        if (presentVids.Contains(0x24AE)) PollRapoo(hidPaths, results, claimed);
        if (presentVids.Contains(0x25A7)) PollAreson(hidPaths, results, claimed);
        if (presentVids.Contains(0x057E)) PollSwitchPro(hidPaths, results, claimed);

        PollChecksum17HidDevices(hidPaths, presentVids, results, claimed);

        // 5. Fallback HID padrão (Usage 0x06:0x20 / 0x85:0x65) + Identificação de dongles 2.4 GHz não reclamados
        PollGenericHidAndDongles(hidPaths, claimed, btContainers, results);

        return MergeAndOrder(results);
    }

    private static List<PeripheralBatteryInfo> MergeAndOrder(List<PeripheralBatteryInfo> raw)
    {
        var byKey = new Dictionary<string, PeripheralBatteryInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in raw)
        {
            if (string.IsNullOrWhiteSpace(item.Key)) continue;
            if (!byKey.TryGetValue(item.Key, out var existing))
            {
                byKey[item.Key] = item;
                continue;
            }
            if ((existing.Level == null && item.Level != null) || item.IsCharging)
            {
                existing.Level = item.Level ?? existing.Level;
                existing.IsCharging = existing.IsCharging || item.IsCharging;
                existing.IsOnline = existing.IsOnline || item.IsOnline;
                existing.IsAsleep = existing.IsAsleep && item.IsAsleep;
                existing.Millivolts ??= item.Millivolts;
                if (existing.ConnectionType == PeripheralConnectionType.Unknown && item.ConnectionType != PeripheralConnectionType.Unknown)
                    existing.ConnectionType = item.ConnectionType;
                if (!string.IsNullOrWhiteSpace(item.ApproxText)) existing.ApproxText = item.ApproxText;
            }
        }

        return FilterConnectedOnly(byKey.Values)
            .OrderByDescending(d => d.Level.HasValue)
            .ThenBy(d => d.Level ?? 999)
            .ThenBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static List<PeripheralBatteryInfo> FilterConnectedOnly(IEnumerable<PeripheralBatteryInfo> devices) =>
        devices.Where(d => d.IsOnline && !d.IsAsleep).ToList();

    // ------------------------------------------------------------------ 1. Bluetooth PnP

    private static void PollBluetooth(List<PeripheralBatteryInfo> results)
    {
        try
        {
            var btBatteries = PeripheralHidTransport.ReadBluetoothBatteryByMac();
            var paired = BluetoothManagerService.ListPairedDevices();
            foreach (var dev in paired)
            {
                // Exibe estritamente dispositivos atualmente conectados
                if (!dev.IsConnected) continue;

                string mac = dev.AddressText.Replace(":", "").ToUpperInvariant();
                bool hasBat = btBatteries.TryGetValue(mac, out int batLevel);
                if (!hasBat) continue;

                results.Add(new PeripheralBatteryInfo
                {
                    Key = $"bt:{mac}",
                    Name = dev.Name,
                    Level = Math.Clamp(batLevel, 0, 100),
                    IsCharging = false,
                    IsOnline = true,
                    Source = "Bluetooth",
                    Kind = InferKind(dev.DeviceTypeLabel, dev.Name),
                    ViaBluetooth = true,
                    ConnectionType = PeripheralConnectionType.Bluetooth
                });
            }
        }
        catch { }
    }

    // ------------------------------------------------------------------ 2. XInput (Xbox / Machenike / GameSir / 8BitDo / Flydigi)

    private static void PollXInput(List<string> hidPaths, List<PeripheralBatteryInfo> results, HashSet<(ushort Vid, ushort Pid)> claimed)
    {
        var activeSlots = new List<(uint Index, XINPUT_BATTERY_INFORMATION Info)>();
        for (uint idx = 0; idx < 4; idx++)
        {
            try
            {
                uint rc;
                XINPUT_BATTERY_INFORMATION info;
                try { rc = XInput14GetBatteryInformation(idx, 0, out info); }
                catch { rc = XInput13GetBatteryInformation(idx, 0, out info); }

                if (rc != 0 || info.BatteryType == 0x00) continue; // desconectado
                activeSlots.Add((idx, info));
            }
            catch { }
        }

        if (activeSlots.Count == 0) return;

        var presentGamepads = PeripheralHidTransport.EnumeratePresentUsbGamepads(hidPaths);
        for (int i = 0; i < activeSlots.Count; i++)
        {
            var (slotIdx, info) = activeSlots[i];
            int matchIdx = i < presentGamepads.Count ? i : -1;
            var resolved = PeripheralDeviceCatalog.ResolveXInputDevice(
                slotIdx, info.BatteryType, info.BatteryLevel, presentGamepads, matchIdx);

            if (matchIdx >= 0)
                claimed.Add((presentGamepads[matchIdx].VendorId, presentGamepads[matchIdx].ProductId));

            results.Add(new PeripheralBatteryInfo
            {
                Key = resolved.Key,
                Name = resolved.DisplayName,
                Level = resolved.Level,
                ApproxText = resolved.ApproxText,
                IsCharging = resolved.IsCharging,
                IsOnline = true,
                Source = resolved.Source,
                Kind = PeripheralKind.Gamepad,
                ViaBluetooth = resolved.ConnectionType == PeripheralConnectionType.Bluetooth,
                ConnectionType = resolved.ConnectionType
            });
        }
    }

    // ------------------------------------------------------------------ 3. Logitech HID++ 2.0 & 1.0

    private static void PollLogitech(List<string> hidPaths, List<PeripheralBatteryInfo> results, HashSet<(ushort Vid, ushort Pid)> claimed)
    {
        var eps = PeripheralHidTransport.InspectVendorInterfaces(hidPaths, 0x046D);
        var byDevice = eps.GroupBy(e => $"{e.ProductId:X4}:{e.InterfaceNumber}");
        foreach (var grp in byDevice)
        {
            var shortEp = grp.FirstOrDefault(e => e.UsagePage == 0xFF00 && e.Usage == 0x0001);
            var longEp = grp.FirstOrDefault(e => (e.UsagePage == 0xFF00 && e.Usage == 0x0002) || e.UsagePage == 0xFF43);
            var ep = longEp ?? shortEp;
            if (ep == null) continue;

            using var h = PeripheralHidTransport.OpenPath(ep.Path);
            if (h == null) continue;

            bool isReceiver = LogitechReceivers.ContainsKey(ep.ProductId) || ep.ProductName.Contains("Receiver", StringComparison.OrdinalIgnoreCase);
            var connType = isReceiver
                ? PeripheralConnectionType.Dongle24GHz
                : (ep.IsBluetooth ? PeripheralConnectionType.Bluetooth : ClassifyConnectionType(ep.VendorId, ep.ProductId, ep.ProductName, ep.IsBluetooth));
            int[] slots = isReceiver ? new[] { 1, 2, 3, 4, 5, 6 } : new[] { 0xFF };
            foreach (int slot in slots)
            {
                var info = QueryLogitechSlot(h, ep, (byte)slot, shortEp != null, connType);
                if (info != null)
                {
                    results.Add(info);
                    claimed.Add((ep.VendorId, ep.ProductId));
                }
            }
        }
    }

    private static PeripheralBatteryInfo? QueryLogitechSlot(
        Microsoft.Win32.SafeHandles.SafeFileHandle h, HidEndpointInfo ep, byte slot, bool allowHidPp1, PeripheralConnectionType connType)
    {
        // Feature 0x1004 (Unified Battery) -> 0x1000 (Battery Status) -> 0x1001 (Voltage) -> 0x1F20 (Headset ADC)
        foreach (ushort feat in new ushort[] { 0x1004, 0x1000, 0x1001, 0x1F20 })
        {
            byte? idx = LogitechGetFeatureIndex(h, slot, feat);
            if (idx == null) continue;
            byte[]? resp = LogitechSendShortOrLong(h, slot, idx.Value, feat == 0x1004 ? (byte)0x1A : (byte)0x0A);
            if (resp == null || resp.Length < 7) continue;

            int? lvl = null;
            bool chg = false;
            if (feat == 0x1004) (lvl, chg) = ParseLogitechHidPp2Unified(resp);
            else if (feat == 0x1000) (lvl, chg) = ParseLogitechHidPp2Status(resp);
            else if (feat == 0x1F20 && resp.Length >= 7)
            {
                int mv = (resp[4] << 8) | resp[5];
                lvl = VoltageToPercent(mv, 3500, 4180);
                chg = (resp[6] & 0x02) != 0;
            }
            if (lvl != null)
            {
                string? realName = LogitechGetDeviceName(h, slot);
                string name = !string.IsNullOrWhiteSpace(realName)
                    ? (realName.Contains("Logitech", StringComparison.OrdinalIgnoreCase) ? realName : $"Logitech {realName}")
                    : (!string.IsNullOrWhiteSpace(ep.ProductName) ? ep.ProductName : $"Logitech HID++ ({ep.ProductId:X4})");
                if (string.IsNullOrWhiteSpace(realName) && slot != 0xFF) name = $"{name} #{slot}";
                return new PeripheralBatteryInfo
                {
                    Key = $"logi:{ep.ProductId:X4}:{slot}",
                    Name = name,
                    Level = lvl,
                    IsCharging = chg,
                    IsOnline = true,
                    Source = "Logitech HID++",
                    Kind = InferKind(ep.UsagePage == 0xFF43 ? "Headset" : "", name),
                    ViaBluetooth = ep.IsBluetooth,
                    ConnectionType = connType
                };
            }
        }

        if (allowHidPp1 && slot <= 6)
        {
            foreach (byte reg in new byte[] { 0x0D, 0x07 })
            {
                var req = new byte[] { 0x10, slot, 0x81, reg, 0x00, 0x00, 0x00 };
                PeripheralHidTransport.FlushInput(h);
                if (!PeripheralHidTransport.WriteReport(h, req, 200)) continue;
                var resp = PeripheralHidTransport.ReadReport(h, 20, 300);
                if (resp != null)
                {
                    var (lvl, chg, approx) = ParseLogitechHidPp1Register(reg, resp);
                    if (lvl != null)
                    {
                        return new PeripheralBatteryInfo
                        {
                            Key = $"logi10:{ep.ProductId:X4}:{slot}",
                            Name = string.IsNullOrWhiteSpace(ep.ProductName) ? $"Logitech #{slot}" : $"{ep.ProductName} #{slot}",
                            Level = lvl,
                            IsCharging = chg,
                            ApproxText = approx,
                            IsOnline = true,
                            Source = "Logitech HID++ 1.0",
                            Kind = PeripheralKind.Mouse,
                            ViaBluetooth = ep.IsBluetooth,
                            ConnectionType = connType
                        };
                    }
                }
            }
        }
        return null;
    }

    private static byte? LogitechGetFeatureIndex(Microsoft.Win32.SafeHandles.SafeFileHandle h, byte slot, ushort featureId)
    {
        var req = new byte[20];
        req[0] = 0x11; req[1] = slot; req[2] = 0x00; req[3] = 0x0B;
        req[4] = (byte)(featureId >> 8); req[5] = (byte)(featureId & 0xFF);
        PeripheralHidTransport.FlushInput(h);
        if (!PeripheralHidTransport.WriteReport(h, req, 250)) return null;
        var resp = PeripheralHidTransport.ReadReport(h, 20, 300);
        if (resp == null || resp.Length < 5) return null;
        if (resp[2] == 0xFF || resp[2] == 0x8F || resp[4] == 0x00) return null;
        return resp[4];
    }

    private static byte[]? LogitechSendShortOrLong(Microsoft.Win32.SafeHandles.SafeFileHandle h, byte slot, byte featIdx, byte fnSwid, byte param0 = 0x00)
    {
        var req = new byte[20];
        req[0] = 0x11; req[1] = slot; req[2] = featIdx; req[3] = fnSwid; req[4] = param0;
        PeripheralHidTransport.FlushInput(h);
        if (!PeripheralHidTransport.WriteReport(h, req, 250)) return null;
        return PeripheralHidTransport.ReadReport(h, 20, 350);
    }

    private static string? LogitechGetDeviceName(Microsoft.Win32.SafeHandles.SafeFileHandle h, byte slot)
    {
        try
        {
            byte? nameIdx = LogitechGetFeatureIndex(h, slot, 0x0005);
            if (nameIdx == null) return null;
            byte[]? lenResp = LogitechSendShortOrLong(h, slot, nameIdx.Value, 0x0A);
            if (lenResp == null || lenResp.Length < 5) return null;
            int totalLen = lenResp[4];
            if (totalLen is <= 0 or > 48) return null;

            var sb = new System.Text.StringBuilder(totalLen);
            for (int off = 0; off < totalLen; off += 16)
            {
                byte[]? chunk = LogitechSendShortOrLong(h, slot, nameIdx.Value, 0x1A, (byte)off);
                if (chunk == null || chunk.Length < 5) break;
                int take = Math.Min(totalLen - off, Math.Min(16, chunk.Length - 4));
                for (int i = 0; i < take; i++)
                {
                    byte b = chunk[4 + i];
                    if (b == 0) break;
                    if (b is >= 0x20 and <= 0x7E) sb.Append((char)b);
                }
            }
            string result = sb.ToString().Trim();
            return string.IsNullOrWhiteSpace(result) ? null : result;
        }
        catch { return null; }
    }

    // ------------------------------------------------------------------ 4. Razer (0x1532)

    private static void PollRazer(List<string> hidPaths, List<PeripheralBatteryInfo> results, HashSet<(ushort Vid, ushort Pid)> claimed)
    {
        var eps = PeripheralHidTransport.InspectVendorInterfaces(hidPaths, 0x1532)
            .Where(e => e.FeatureReportLength >= 90 || (e.ProductId == 0x0528 && e.UsagePage == 0xFF00));

        foreach (var ep in eps.GroupBy(e => e.ProductId).Select(g => g.First()))
        {
            using var h = PeripheralHidTransport.OpenPath(ep.Path);
            if (h == null) continue;

            byte txId = (ep.ProductId >= 0x0500 && ep.ProductId <= 0x05FF) ? (byte)0x3F : (byte)0x1F;
            byte[]? batResp = QueryRazerFeature(h, txId, 0x80);
            int? level = batResp != null ? ParseRazerBatteryResponse(batResp) : null;
            if (level == null) continue;

            byte[]? chgResp = QueryRazerFeature(h, txId, 0x84);
            bool chg = chgResp != null && chgResp.Length >= 10 && chgResp[0] == 0x02 && chgResp[9] == 0x01;

            string name = RazerKnown.TryGetValue(ep.ProductId, out var known)
                ? known
                : (!string.IsNullOrWhiteSpace(ep.ProductName) ? ep.ProductName : $"Razer ({ep.ProductId:X4})");

            results.Add(new PeripheralBatteryInfo
            {
                Key = $"razer:{ep.ProductId:X4}",
                Name = name,
                Level = level,
                IsCharging = chg,
                IsOnline = true,
                Source = "Razer HID",
                Kind = InferKind("", name),
                ViaBluetooth = ep.IsBluetooth,
                ConnectionType = ClassifyConnectionType(ep.VendorId, ep.ProductId, ep.ProductName, ep.IsBluetooth)
            });
            claimed.Add((ep.VendorId, ep.ProductId));
        }
    }

    private static byte[]? QueryRazerFeature(Microsoft.Win32.SafeHandles.SafeFileHandle h, byte txId, byte cmdId)
    {
        var buf = new byte[91]; // ReportId 0 + 90 bytes payload
        buf[0] = 0x00;
        buf[1] = 0x00;
        buf[2] = txId;
        buf[6] = 0x02;
        buf[7] = 0x07;
        buf[8] = cmdId;
        byte crc = 0;
        for (int i = 3; i <= 88; i++) crc ^= buf[i];
        buf[89] = crc;

        if (!PeripheralHidTransport.SetFeatureReport(h, buf)) return null;
        Thread.Sleep(35);
        var resp = new byte[91];
        resp[0] = 0x00;
        if (PeripheralHidTransport.GetFeatureReport(h, 0x00, 91) is not byte[] got) return null;
        var out90 = new byte[90];
        Array.Copy(got, 1, out90, 0, Math.Min(90, got.Length - 1));
        return out90;
    }

    // ------------------------------------------------------------------ 5. PlayStation (0x054C)

    private static void PollPlayStation(List<string> hidPaths, List<PeripheralBatteryInfo> results, HashSet<(ushort Vid, ushort Pid)> claimed)
    {
        var eps = PeripheralHidTransport.InspectVendorInterfaces(hidPaths, 0x054C)
            .Where(e => e.UsagePage == 0x0001 && (e.Usage == 0x0005 || e.Usage == 0x0004));

        foreach (var ep in eps)
        {
            if (ep.ProductId is not (0x05C4 or 0x09CC or 0x05C5 or 0x0BA0 or 0x0CE6 or 0x0DF2)) continue;
            bool isDualSense = ep.ProductId is 0x0CE6 or 0x0DF2;
            using var h = PeripheralHidTransport.OpenPath(ep.Path);
            if (h == null) continue;

            var report = PeripheralHidTransport.ReadReport(h, Math.Max(64, ep.InputReportLength), 250);
            if (report == null) continue;

            int? lvl = null;
            bool chg = false;
            if (isDualSense)
            {
                int offset = (report[0] == 0x31) ? 54 : (report[0] == 0x01 ? 53 : -1);
                if (offset > 0 && report.Length > offset && ParsePlayStationDualSense(report[offset]) is { } ds)
                {
                    lvl = ds.Level;
                    chg = ds.Charging;
                }
            }
            else
            {
                int offset = (report[0] == 0x11) ? 32 : (report[0] == 0x01 ? 30 : -1);
                if (offset > 0 && report.Length > offset)
                    (lvl, chg) = ParsePlayStationDs4(report[offset]);
            }

            if (lvl != null)
            {
                results.Add(new PeripheralBatteryInfo
                {
                    Key = $"sony:{ep.ProductId:X4}:{(ep.IsBluetooth ? "bt" : "usb")}",
                    Name = isDualSense ? (ep.ProductId == 0x0DF2 ? "Sony DualSense Edge" : "Sony DualSense") : "Sony DualShock 4",
                    Level = lvl,
                    IsCharging = chg,
                    IsOnline = true,
                    Source = ep.IsBluetooth ? "PlayStation BT" : "PlayStation USB",
                    Kind = PeripheralKind.Gamepad,
                    ViaBluetooth = ep.IsBluetooth,
                    ConnectionType = ep.IsBluetooth ? PeripheralConnectionType.Bluetooth : (ep.ProductId == 0x0BA0 ? PeripheralConnectionType.Dongle24GHz : PeripheralConnectionType.UsbCable)
                });
                claimed.Add((ep.VendorId, ep.ProductId));
            }
        }
    }

    // ------------------------------------------------------------------ 6. Corsair (0x1B1C)

    private static void PollCorsair(List<string> hidPaths, List<PeripheralBatteryInfo> results, HashSet<(ushort Vid, ushort Pid)> claimed)
    {
        var eps = PeripheralHidTransport.InspectVendorInterfaces(hidPaths, 0x1B1C)
            .Where(e => e.UsagePage >= 0xFF00);

        foreach (var ep in eps.GroupBy(e => e.ProductId).Select(g => g.First()))
        {
            using var h = PeripheralHidTransport.OpenPath(ep.Path);
            if (h == null) continue;

            if (ep.ProductId == 0x0A55) // Void Elite
            {
                PeripheralHidTransport.FlushInput(h);
                if (PeripheralHidTransport.WriteReport(h, new byte[] { 0xC9, 0x64 }, 250))
                {
                    var r = PeripheralHidTransport.ReadReport(h, 64, 300);
                    if (r != null && ParseCorsairVoidElite(r) is { } ve)
                    {
                        results.Add(new PeripheralBatteryInfo
                        {
                            Key = $"corsair:{ep.ProductId:X4}",
                            Name = string.IsNullOrWhiteSpace(ep.ProductName) ? "Corsair Void Elite" : ep.ProductName,
                            Level = ve.Level,
                            IsCharging = ve.Charging,
                            IsOnline = ve.Online,
                            Source = "Corsair HID",
                            Kind = PeripheralKind.Headset,
                            ConnectionType = PeripheralConnectionType.Dongle24GHz
                        });
                        claimed.Add((ep.VendorId, ep.ProductId));
                    }
                }
            }
            else if (ep.OutputReportLength >= 64)
            {
                var req = new byte[Math.Max(64, ep.OutputReportLength)];
                if (req.Length == 65) { req[0] = 0x00; req[1] = 0x02; req[2] = 0x09; req[3] = 0x02; req[4] = 0x0F; }
                else { req[0] = 0x02; req[1] = 0x09; req[2] = 0x02; req[3] = 0x0F; }

                PeripheralHidTransport.FlushInput(h);
                if (PeripheralHidTransport.WriteReport(h, req, 250))
                {
                    var r = PeripheralHidTransport.ReadReport(h, Math.Max(64, ep.InputReportLength), 350);
                    if (r != null && ParseCorsairBragi(r) is int pct)
                    {
                        results.Add(new PeripheralBatteryInfo
                        {
                            Key = $"corsair:{ep.ProductId:X4}",
                            Name = string.IsNullOrWhiteSpace(ep.ProductName) ? $"Corsair ({ep.ProductId:X4})" : ep.ProductName,
                            Level = pct,
                            IsOnline = true,
                            Source = "Corsair Slipstream",
                            Kind = InferKind("", ep.ProductName),
                            ViaBluetooth = ep.IsBluetooth,
                            ConnectionType = ep.IsBluetooth ? PeripheralConnectionType.Bluetooth : PeripheralConnectionType.Dongle24GHz
                        });
                        claimed.Add((ep.VendorId, ep.ProductId));
                    }
                }
            }
        }
    }

    // ------------------------------------------------------------------ 7. SteelSeries (0x1038)

    private static void PollSteelSeries(List<string> hidPaths, List<PeripheralBatteryInfo> results, HashSet<(ushort Vid, ushort Pid)> claimed)
    {
        var eps = PeripheralHidTransport.InspectVendorInterfaces(hidPaths, 0x1038)
            .Where(e => e.UsagePage == 0xFFC0);

        foreach (var ep in eps.GroupBy(e => e.ProductId).Select(g => g.First()))
        {
            using var h = PeripheralHidTransport.OpenPath(ep.Path);
            if (h == null || ep.OutputReportLength < 2) continue;

            var req = new byte[Math.Max(64, ep.OutputReportLength)];
            req[0] = 0x00; req[1] = 0xB0;
            PeripheralHidTransport.FlushInput(h);
            if (!PeripheralHidTransport.WriteReport(h, req, 250)) continue;
            var r = PeripheralHidTransport.ReadReport(h, Math.Max(64, ep.InputReportLength), 350);
            if (r != null && ParseSteelSeriesNova7(r) is { Online: true, Level: int lvl } st)
            {
                results.Add(new PeripheralBatteryInfo
                {
                    Key = $"steel:{ep.ProductId:X4}",
                    Name = string.IsNullOrWhiteSpace(ep.ProductName) ? $"SteelSeries ({ep.ProductId:X4})" : ep.ProductName,
                    Level = lvl,
                    IsCharging = st.Charging,
                    IsOnline = true,
                    Source = "SteelSeries HID",
                    Kind = PeripheralKind.Headset,
                    ConnectionType = PeripheralConnectionType.Dongle24GHz
                });
                claimed.Add((ep.VendorId, ep.ProductId));
            }
        }
    }

    // ------------------------------------------------------------------ 8. HyperX (0x03F0 / 0x0951)

    private static void PollHyperX(List<string> hidPaths, List<PeripheralBatteryInfo> results, HashSet<(ushort Vid, ushort Pid)> claimed)
    {
        foreach (ushort vid in new ushort[] { 0x03F0, 0x0951 })
        {
            var eps = PeripheralHidTransport.InspectVendorInterfaces(hidPaths, vid)
                .Where(e => e.UsagePage >= 0xFF00);
            foreach (var ep in eps.GroupBy(e => e.ProductId).Select(g => g.First()))
            {
                using var h = PeripheralHidTransport.OpenPath(ep.Path);
                if (h == null || ep.OutputReportLength < 10) continue;

                var req = new byte[Math.Max(62, ep.OutputReportLength)];
                req[0] = 0x66; req[1] = 0x89;
                PeripheralHidTransport.FlushInput(h);
                if (!PeripheralHidTransport.WriteReport(h, req, 250)) continue;
                var r = PeripheralHidTransport.ReadReport(h, Math.Max(62, ep.InputReportLength), 350);
                if (r != null && r.Length >= 5 && r[0] == 0x66 && (r[1] == 0x89 || r[1] == 0x0D) && r[4] <= 100 && (r[2] != 0 || r[3] != 0))
                {
                    results.Add(new PeripheralBatteryInfo
                    {
                        Key = $"hyperx:{ep.ProductId:X4}",
                        Name = string.IsNullOrWhiteSpace(ep.ProductName) ? $"HyperX ({ep.ProductId:X4})" : ep.ProductName,
                        Level = r[4],
                        IsOnline = true,
                        Source = "HyperX HID",
                        Kind = PeripheralKind.Headset,
                        ConnectionType = PeripheralConnectionType.Dongle24GHz
                    });
                    claimed.Add((ep.VendorId, ep.ProductId));
                }
            }
        }
    }

    // ------------------------------------------------------------------ 9. Keychron (0x3434) & JBL (0x0ECB) & 17-byte 0x55 Mice

    private static void PollKeychron(List<string> hidPaths, List<PeripheralBatteryInfo> results, HashSet<(ushort Vid, ushort Pid)> claimed)
    {
        var eps = PeripheralHidTransport.InspectVendorInterfaces(hidPaths, 0x3434)
            .Where(e => e.InterfaceNumber == 4 || e.UsagePage >= 0xFF00);
        foreach (var ep in eps.GroupBy(e => e.ProductId).Select(g => g.First()))
        {
            using var h = PeripheralHidTransport.OpenPath(ep.Path);
            if (h == null) continue;
            var feat = new byte[64];
            feat[0] = 0xB3; feat[1] = 0x06;
            PeripheralHidTransport.FlushInput(h);
            if (!PeripheralHidTransport.SetFeatureReport(h, feat)) continue;
            var r = PeripheralHidTransport.ReadReport(h, Math.Max(64, ep.InputReportLength), 400);
            if (r != null && ParseKeychronLevel(r) is int pct)
            {
                results.Add(new PeripheralBatteryInfo
                {
                    Key = $"keychron:{ep.ProductId:X4}",
                    Name = string.IsNullOrWhiteSpace(ep.ProductName) ? $"Keychron ({ep.ProductId:X4})" : ep.ProductName,
                    Level = pct,
                    IsOnline = true,
                    Source = "Keychron HID",
                    Kind = ep.ProductName.Contains("M5", StringComparison.OrdinalIgnoreCase) ? PeripheralKind.Mouse : PeripheralKind.Keyboard,
                    ViaBluetooth = ep.IsBluetooth,
                    ConnectionType = ClassifyConnectionType(ep.VendorId, ep.ProductId, ep.ProductName, ep.IsBluetooth)
                });
                claimed.Add((ep.VendorId, ep.ProductId));
            }
        }
    }

    private static void PollJblQuantum(List<string> hidPaths, List<PeripheralBatteryInfo> results, HashSet<(ushort Vid, ushort Pid)> claimed)
    {
        var eps = PeripheralHidTransport.InspectVendorInterfaces(hidPaths, 0x0ECB)
            .Where(e => e.UsagePage == 0xFF13);
        foreach (var ep in eps.GroupBy(e => e.ProductId).Select(g => g.First()))
        {
            using var h = PeripheralHidTransport.OpenPath(ep.Path);
            if (h == null) continue;
            var r = PeripheralHidTransport.ReadReport(h, Math.Max(16, ep.InputReportLength), 120);
            if (r != null && r.Length >= 2 && r[0] == 0x08 && r[1] <= 100)
            {
                results.Add(new PeripheralBatteryInfo
                {
                    Key = $"jbl:{ep.ProductId:X4}",
                    Name = string.IsNullOrWhiteSpace(ep.ProductName) ? "JBL Quantum Wireless" : ep.ProductName,
                    Level = r[1],
                    IsOnline = true,
                    Source = "JBL HID",
                    Kind = PeripheralKind.Headset,
                    ConnectionType = PeripheralConnectionType.Dongle24GHz
                });
                claimed.Add((ep.VendorId, ep.ProductId));
            }
        }
    }

    private static void PollChecksum17HidDevices(
        List<string> hidPaths, HashSet<ushort> presentVids, List<PeripheralBatteryInfo> results, HashSet<(ushort Vid, ushort Pid)> claimed)
    {
        foreach (var kv in GamingChecksum17Devices)
        {
            if (!presentVids.Contains(kv.Key.Vid) || claimed.Contains(kv.Key)) continue;
            var eps = PeripheralHidTransport.InspectVendorInterfaces(hidPaths, kv.Key.Vid)
                .Where(e => e.ProductId == kv.Key.Pid && e.UsagePage >= 0xFF00);

            foreach (var ep in eps)
            {
                using var h = PeripheralHidTransport.OpenPath(ep.Path);
                if (h == null) continue;
                var req = new byte[17];
                req[0] = 0x08;
                req[1] = 0x04;
                int sum = 0;
                for (int i = 1; i < 16; i++) sum += req[i];
                req[16] = (byte)((0x55 - (sum & 0xFF)) & 0xFF);

                if (!PeripheralHidTransport.SetFeatureReport(h, req)) continue;
                Thread.Sleep(25);
                var r = PeripheralHidTransport.GetFeatureReport(h, 0x08, 17);
                if (r != null && r.Length >= 8 && r[6] <= 100)
                {
                    results.Add(new PeripheralBatteryInfo
                    {
                        Key = $"hid17:{kv.Key.Vid:X4}:{kv.Key.Pid:X4}",
                        Name = string.IsNullOrWhiteSpace(ep.ProductName) ? kv.Value.Name : ep.ProductName,
                        Level = r[6],
                        IsCharging = r[7] != 0,
                        IsOnline = true,
                        Source = "2.4 GHz HID",
                        Kind = kv.Value.Kind,
                        ConnectionType = PeripheralConnectionType.Dongle24GHz
                    });
                    claimed.Add((ep.VendorId, ep.ProductId));
                    break;
                }
            }
        }
    }

    // ------------------------------------------------------------------ 10. Provedores WDBM (ASUS ROG, Kysona/ATK/VXE/Pulsar, Rapoo, Areson, AULA/MCHOSE, Switch Pro, Generic HID & Dongles)

    private static void PollAsusRog(List<string> hidPaths, List<PeripheralBatteryInfo> results, HashSet<(ushort Vid, ushort Pid)> claimed)
    {
        var eps = PeripheralHidTransport.InspectVendorInterfaces(hidPaths, 0x0B05)
            .Where(e => e.UsagePage >= 0xFF00 && e.OutputReportLength >= 64 && e.InputReportLength >= 64);

        foreach (var grp in eps.GroupBy(e => e.ProductId))
        {
            var ep = grp.OrderByDescending(e => e.UsagePage == 0xFF01 || e.UsagePage == 0xFF00).First();
            using var h = PeripheralHidTransport.OpenPath(ep.Path);
            if (h == null) continue;

            byte rid = ep.OutputReportLength >= 65 ? (byte)0x00 : (byte)0x00;
            foreach (byte subCmd in new byte[] { 0x07, 0x01 })
            {
                var req = new byte[Math.Max(65, ep.OutputReportLength)];
                req[0] = rid;
                req[1] = 0x12;
                req[2] = subCmd;
                PeripheralHidTransport.FlushInput(h);
                if (!PeripheralHidTransport.WriteReport(h, req, 250)) continue;
                var r = PeripheralHidTransport.ReadReport(h, Math.Max(65, ep.InputReportLength), 350);
                if (r != null && ParseAsusRogBatteryResponse(r) is { } parsed)
                {
                    var conn = ep.ProductId == 0x1ACE
                        ? PeripheralConnectionType.Dongle24GHz
                        : ClassifyConnectionType(ep.VendorId, ep.ProductId, ep.ProductName, ep.IsBluetooth);
                    string name = string.IsNullOrWhiteSpace(ep.ProductName) ? $"ASUS ROG ({ep.ProductId:X4})" : ep.ProductName;
                    results.Add(new PeripheralBatteryInfo
                    {
                        Key = $"rog:{ep.ProductId:X4}",
                        Name = name,
                        Level = parsed.Level,
                        IsCharging = parsed.Charging,
                        IsAsleep = parsed.Asleep,
                        IsOnline = !parsed.Asleep,
                        Source = "ASUS ROG HID",
                        Kind = InferKind("", name),
                        ViaBluetooth = ep.IsBluetooth,
                        ConnectionType = conn
                    });
                    claimed.Add((ep.VendorId, ep.ProductId));
                    break;
                }
            }
        }
    }

    private static void PollKysonaFamily(
        List<string> hidPaths, HashSet<ushort> presentVids, List<PeripheralBatteryInfo> results, HashSet<(ushort Vid, ushort Pid)> claimed)
    {
        ushort[] vendors = { 0x3554, 0x373B, 0x3770, 0x33E4, 0x3710, 0x35BB };
        foreach (ushort vid in vendors)
        {
            if (!presentVids.Contains(vid)) continue;
            var eps = PeripheralHidTransport.InspectVendorInterfaces(hidPaths, vid)
                .Where(e =>
                {
                    if (claimed.Contains((e.VendorId, e.ProductId))) return false;
                    // ATK N9 headsets usam serial; nunca enviar frames de mouse
                    if (e.VendorId == 0x373B && e.ProductId is 0x11CA or 0x11CB or 0x1203 or 0x1204) return false;
                    // 3554:FA09 / FB40 são receptores de teclado AULA/MCHOSE (tratados por PollAulaMchose)
                    if (e.VendorId == 0x3554 && e.ProductId is 0xFA09 or 0xFB40) return false;
                    // HECATE (0x35BB): apenas IDs de mouse
                    if (e.VendorId == 0x35BB && e.ProductId is not (0xD3FA or 0xD3FB or 0xD3FC or 0xD3FD)) return false;
                    return (e.OutputReportLength == 17 && e.InputReportLength == 17) ||
                           (e.UsagePage >= 0xFF00 && e.OutputReportLength >= 17 && e.InputReportLength >= 17);
                });

            foreach (var grp in eps.GroupBy(e => e.ProductId))
            {
                var ordered = grp.OrderByDescending(e => e.UsagePage == 0xFF02 && e.Usage == 0x0002);
                foreach (var ep in ordered)
                {
                    using var h = PeripheralHidTransport.OpenPath(ep.Path);
                    if (h == null) continue;

                    bool answered = false;
                    bool online = true;
                    var onlineResp = QueryKysonaCmd(h, 0x03, ep.InputReportLength);
                    if (onlineResp != null)
                    {
                        answered = true;
                        online = onlineResp[6] != 0;
                    }

                    int? level = null;
                    bool charging = false;
                    int? mv = null;
                    bool asleep = false;

                    if (!answered || online)
                    {
                        var batResp = QueryKysonaCmd(h, 0x04, ep.InputReportLength);
                        if (batResp != null && ParseKysonaBatteryResponse(batResp) is { } parsed)
                        {
                            answered = true;
                            online = true;
                            level = parsed.Level;
                            charging = parsed.Charging;
                            mv = parsed.Millivolts;
                        }
                        else if (answered && online)
                        {
                            asleep = true;
                        }
                    }

                    if (!answered) continue;
                    claimed.Add((ep.VendorId, ep.ProductId));
                    if (!online) break;

                    bool isCable = (ep.VendorId == 0x3554 && ep.ProductId is 0xF55E or 0xF58C or 0xF58F or 0xF506)
                        || (ep.VendorId == 0x35BB && ep.ProductId is 0xD3FA or 0xD3FC or 0xD3FD);
                    string name = CleanDongleSuffix(ep.ProductName);
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        name = ep.VendorId switch
                        {
                            0x3770 => "Hitscan Hyperlight",
                            0x3710 => "Pulsar Wireless Mouse",
                            0x373B => "ATK Wireless Mouse",
                            0x35BB => "HECATE Wireless Mouse",
                            _ => $"Mouse Sem Fio ({ep.VendorId:X4}:{ep.ProductId:X4})"
                        };
                    }

                    results.Add(new PeripheralBatteryInfo
                    {
                        Key = $"kysona:{ep.VendorId:X4}:{ep.ProductId:X4}",
                        Name = name,
                        Level = level,
                        IsCharging = charging,
                        IsOnline = !asleep,
                        IsAsleep = asleep,
                        Millivolts = mv,
                        Source = "Kysona / CompX HID",
                        Kind = InferKind("", name) is var k && k == PeripheralKind.Device ? PeripheralKind.Mouse : k,
                        ConnectionType = isCable ? PeripheralConnectionType.UsbCable : PeripheralConnectionType.Dongle24GHz
                    });
                    break;
                }
            }
        }
    }

    private static byte[]? QueryKysonaCmd(Microsoft.Win32.SafeHandles.SafeFileHandle h, byte cmd, int inputLen)
    {
        var req = new byte[17];
        req[0] = 0x08;
        req[1] = cmd;
        req[16] = ComputeKysonaChecksum(req);
        PeripheralHidTransport.FlushInput(h);
        if (!PeripheralHidTransport.WriteReport(h, req, 250)) return null;
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < 450)
        {
            int left = Math.Max(50, 450 - (int)sw.ElapsedMilliseconds);
            var r = PeripheralHidTransport.ReadReport(h, Math.Max(17, inputLen), left);
            if (r == null) break;
            if (r.Length >= 17 && r[0] == 0x08 && r[1] == cmd && r[16] == ComputeKysonaChecksum(r))
                return r;
        }
        return null;
    }

    private static void PollRapoo(List<string> hidPaths, List<PeripheralBatteryInfo> results, HashSet<(ushort Vid, ushort Pid)> claimed)
    {
        var eps = PeripheralHidTransport.InspectVendorInterfaces(hidPaths, 0x24AE)
            .Where(e => e.UsagePage == 0xFF00 && e.Usage == 0x0002 &&
                        (e.InputReportLength is 16 or 19 or 13 or 7));

        foreach (var ep in eps.GroupBy(e => e.ProductId).Select(g => g.First()))
        {
            using var h = PeripheralHidTransport.OpenPath(ep.Path);
            if (h == null) continue;
            bool legacy = ep.InputReportLength == 7 && ep.ProductId is 0x1215 or 0x4415 or 0x1233 or 0x4433;
            var r = PeripheralHidTransport.ReadReport(h, ep.InputReportLength, 400);
            if (r != null && ParseRapooBatteryResponse(r, legacy) is { } parsed)
            {
                string name = string.IsNullOrWhiteSpace(ep.ProductName) || ep.ProductName.Equals("Rapoo Gaming Device", StringComparison.OrdinalIgnoreCase)
                    ? "Rapoo Wireless Mouse" : ep.ProductName;
                results.Add(new PeripheralBatteryInfo
                {
                    Key = $"rapoo:{(parsed.MousePid != 0 ? parsed.MousePid : ep.ProductId):X4}",
                    Name = name,
                    Level = parsed.Level,
                    IsCharging = parsed.Charging,
                    IsOnline = true,
                    Source = "Rapoo HID",
                    Kind = PeripheralKind.Mouse,
                    ViaBluetooth = parsed.Connection == PeripheralConnectionType.Bluetooth,
                    ConnectionType = parsed.Connection
                });
                claimed.Add((ep.VendorId, ep.ProductId));
            }
        }
    }

    private static void PollAreson(List<string> hidPaths, List<PeripheralBatteryInfo> results, HashSet<(ushort Vid, ushort Pid)> claimed)
    {
        var eps = PeripheralHidTransport.InspectVendorInterfaces(hidPaths, 0x25A7)
            .Where(e => e.ProductId is 0xFA07 or 0xFA08 && e.InterfaceNumber == 1)
            .ToList();

        foreach (var grp in eps.GroupBy(e => e.ProductId))
        {
            var cmdEp = grp.FirstOrDefault(e => e.UsagePage == 0xFF02 && e.Usage == 0x0002 && e.FeatureReportLength >= 17);
            var replyEp = grp.FirstOrDefault(e => e.UsagePage == 0xFF01 && e.Usage == 0x0000 && e.InputReportLength >= 17);
            if (cmdEp == null || replyEp == null) continue;

            using var hCmd = PeripheralHidTransport.OpenPath(cmdEp.Path, readWrite: false);
            using var hReply = PeripheralHidTransport.OpenPath(replyEp.Path);
            if (hCmd == null || hReply == null) continue;

            var req = new byte[17];
            req[0] = 0x08;
            req[1] = 0x04;
            req[16] = ComputeKysonaChecksum(req);

            PeripheralHidTransport.FlushInput(hReply);
            if (!PeripheralHidTransport.SetFeatureReport(hCmd, req)) continue;
            var r = PeripheralHidTransport.ReadReport(hReply, Math.Max(17, replyEp.InputReportLength), 800);
            if (r != null && ParseAresonBatteryResponse(r) is { } parsed)
            {
                results.Add(new PeripheralBatteryInfo
                {
                    Key = $"areson:{cmdEp.VendorId:X4}:{cmdEp.ProductId:X4}",
                    Name = "Redragon Wireless Mouse",
                    Level = parsed.Level,
                    IsCharging = parsed.Charging,
                    IsOnline = true,
                    Source = "Areson 2.4 GHz",
                    Kind = PeripheralKind.Mouse,
                    ConnectionType = PeripheralConnectionType.Dongle24GHz
                });
                claimed.Add((cmdEp.VendorId, cmdEp.ProductId));
            }
        }
    }

    private static void PollAulaMchose(List<string> hidPaths, List<PeripheralBatteryInfo> results, HashSet<(ushort Vid, ushort Pid)> claimed)
    {
        (ushort Vid, ushort Pid)[] receivers = { (0x3554, 0xFA09), (0x3554, 0xFB40), (0x41E4, 0x2001) };
        foreach (var (vid, pid) in receivers)
        {
            var eps = PeripheralHidTransport.InspectVendorInterfaces(hidPaths, vid)
                .Where(e => e.ProductId == pid && e.UsagePage is 0xFF02 or 0xFF04 && e.Usage == 0x0002 &&
                            e.OutputReportLength >= 20 && e.InputReportLength >= 20);
            foreach (var ep in eps)
            {
                using var h = PeripheralHidTransport.OpenPath(ep.Path);
                if (h == null) continue;

                var req = new byte[20];
                req[0] = 0x13;
                req[1] = 0x4A;
                req[2] = 0x00;
                int sum = 0;
                for (int i = 0; i < 19; i++) sum += req[i];
                req[19] = (byte)(sum & 0xFF);

                PeripheralHidTransport.FlushInput(h);
                if (!PeripheralHidTransport.WriteReport(h, req, 250)) continue;

                var sw = Stopwatch.StartNew();
                while (sw.ElapsedMilliseconds < 1200)
                {
                    var r = PeripheralHidTransport.ReadReport(h, Math.Max(20, ep.InputReportLength), 400);
                    if (r == null) break;
                    if (ParseAulaBatteryResponse(r) is { } parsed)
                    {
                        string name = CleanDongleSuffix(ep.ProductName);
                        if (string.IsNullOrWhiteSpace(name))
                            name = vid == 0x41E4 ? "MCHOSE Wireless Keyboard" : "AULA / MCHOSE Keyboard";
                        results.Add(new PeripheralBatteryInfo
                        {
                            Key = $"aula:{vid:X4}:{pid:X4}",
                            Name = name,
                            Level = parsed.Level,
                            IsCharging = parsed.Charging,
                            IsOnline = true,
                            Source = "AULA / MCHOSE 2.4 GHz",
                            Kind = PeripheralKind.Keyboard,
                            ConnectionType = PeripheralConnectionType.Dongle24GHz
                        });
                        claimed.Add((vid, pid));
                        break;
                    }
                }
                if (claimed.Contains((vid, pid))) break;
            }
        }
    }

    private static void PollSwitchPro(List<string> hidPaths, List<PeripheralBatteryInfo> results, HashSet<(ushort Vid, ushort Pid)> claimed)
    {
        var eps = PeripheralHidTransport.InspectVendorInterfaces(hidPaths, 0x057E)
            .Where(e => e.ProductId == 0x2009 && e.UsagePage == 0x0001 && e.Usage is 0x0004 or 0x0005 && e.InputReportLength >= 13);

        foreach (var ep in eps)
        {
            using var h = PeripheralHidTransport.OpenPath(ep.Path);
            if (h == null) continue;
            var r = PeripheralHidTransport.ReadReport(h, Math.Max(64, ep.InputReportLength), 300);
            if (r != null && ParseSwitchProBatteryResponse(r) is { } parsed)
            {
                results.Add(new PeripheralBatteryInfo
                {
                    Key = $"switchpro:{ep.Path.GetHashCode():X8}",
                    Name = string.IsNullOrWhiteSpace(ep.ProductName) ? "Nintendo Switch Pro Controller" : ep.ProductName,
                    Level = parsed.Level,
                    ApproxText = parsed.ApproxText,
                    IsCharging = parsed.Charging,
                    IsOnline = true,
                    Source = "Switch Pro HID",
                    Kind = PeripheralKind.Gamepad,
                    ViaBluetooth = ep.IsBluetooth,
                    ConnectionType = ClassifyConnectionType(ep.VendorId, ep.ProductId, ep.ProductName, ep.IsBluetooth)
                });
                claimed.Add((ep.VendorId, ep.ProductId));
            }
        }
    }

    private static void PollGenericHidAndDongles(
        List<string> hidPaths,
        HashSet<(ushort Vid, ushort Pid)> claimed,
        HashSet<Guid> btContainers,
        List<PeripheralBatteryInfo> results)
    {
        var allEps = PeripheralHidTransport.InspectAllInterfaces(hidPaths);
        var byVidPid = allEps.GroupBy(e => (e.VendorId, e.ProductId));

        foreach (var grp in byVidPid)
        {
            if (claimed.Contains(grp.Key)) continue;

            var first = grp.First();
            Guid cid = PeripheralHidTransport.GetDeviceContainerId(first.Path);
            if (cid != Guid.Empty && btContainers.Contains(cid))
                continue; // já coberto pelo Bluetooth PnP

            // 1. Tenta leitura padrão HID Battery Strength (0x06:0x20) ou State of Charge (0x85:0x65)
            int? foundBattery = null;
            HidEndpointInfo? batteryEp = null;
            foreach (var ep in grp)
            {
                if (PeripheralHidTransport.TryReadStandardHidBattery(ep, out int pct))
                {
                    foundBattery = pct;
                    batteryEp = ep;
                    break;
                }
            }

            PeripheralKind inferredKind = InferKindFromCollections(grp, first.ProductName);
            var connType = ClassifyConnectionType(grp.Key.VendorId, grp.Key.ProductId, first.ProductName, first.IsBluetooth);

            if (foundBattery != null && batteryEp != null)
            {
                string name = PeripheralDeviceCatalog.BuildDisplayName(
                    grp.Key.VendorId, grp.Key.ProductId,
                    batteryEp.ProductName, batteryEp.ManufacturerName);

                results.Add(new PeripheralBatteryInfo
                {
                    Key = $"hid:{grp.Key.VendorId:X4}:{grp.Key.ProductId:X4}",
                    Name = name,
                    Level = foundBattery,
                    IsOnline = true,
                    Source = connType == PeripheralConnectionType.Dongle24GHz ? "HID via Dongle" : "HID Padrão",
                    Kind = inferredKind,
                    ViaBluetooth = first.IsBluetooth,
                    ConnectionType = connType
                });
                claimed.Add(grp.Key);
            }
        }
    }

    private static PeripheralKind InferKindFromCollections(IEnumerable<HidEndpointInfo> eps, string productName)
    {
        var byName = InferKind("", productName);
        if (byName != PeripheralKind.Device) return byName;
        foreach (var e in eps)
        {
            if (e.UsagePage == 0x0001 && e.Usage == 0x0002) return PeripheralKind.Mouse;
            if (e.UsagePage == 0x0001 && e.Usage == 0x0006) return PeripheralKind.Keyboard;
            if (e.UsagePage == 0x0001 && e.Usage is 0x0004 or 0x0005) return PeripheralKind.Gamepad;
        }
        return PeripheralKind.Device;
    }

    private static string CleanDongleSuffix(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "";
        string s = name.Trim();
        foreach (string suffix in new[] { " Wireless Receiver", " USB Receiver", " Receiver", " Dongle", " 2.4G" })
        {
            if (s.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) && s.Length > suffix.Length + 2)
                s = s[..^suffix.Length].Trim();
        }
        return s;
    }

    // ------------------------------------------------------------------ Pure Protocol Parsers (Testáveis)

    public static PeripheralConnectionType ClassifyConnectionType(ushort vid, ushort pid, string? productName, bool isBluetooth)
    {
        if (isBluetooth) return PeripheralConnectionType.Bluetooth;
        if (PeripheralHidTransport.IsLikelyWirelessDongle(vid, pid, productName, isBluetooth: false))
            return PeripheralConnectionType.Dongle24GHz;
        return PeripheralConnectionType.UsbCable;
    }

    public static (int? Level, bool Charging, bool Asleep)? ParseAsusRogBatteryResponse(byte[] resp)
    {
        if (resp == null || resp.Length < 6) return null;
        int off = resp[0] == 0x00 ? 1 : 0;
        if (resp.Length <= off + 4) return null;
        if (resp[off] == 0xFF && resp[off + 1] == 0xAA)
            return (null, false, true);
        if (resp[off] != 0x12) return null;
        byte sub = resp[off + 1];
        if (sub is not (0x01 or 0x07)) return null;
        int pct = resp[off + 3];
        if (pct > 100) return null;
        bool chg = resp[off + 2] == 0x01 || resp[off + 2] == 0x02;
        return (pct, chg, false);
    }

    public static byte ComputeKysonaChecksum(byte[] frame)
    {
        int sum = 0;
        for (int i = 0; i < 16 && i < frame.Length; i++) sum += frame[i];
        return (byte)((0x55 - (sum & 0xFF)) & 0xFF);
    }

    public static (int? Level, bool Charging, int Millivolts)? ParseKysonaBatteryResponse(byte[] resp)
    {
        if (resp == null || resp.Length < 17) return null;
        if (resp[0] != 0x08 || resp[1] != 0x04) return null;
        if (resp[16] != ComputeKysonaChecksum(resp)) return null;
        int? lvl = resp[6] <= 100 ? resp[6] : null;
        bool chg = resp[7] != 0;
        int mv = (resp[8] << 8) | resp[9];
        return (lvl, chg, mv);
    }

    public static (int? Level, bool Charging, PeripheralConnectionType Connection, ushort MousePid)? ParseRapooBatteryResponse(byte[] resp, bool legacyVt3Pro)
    {
        if (resp == null || resp.Length == 0) return null;
        int state, pct;
        var conn = PeripheralConnectionType.Dongle24GHz;
        ushort mousePid = 0;

        if (!legacyVt3Pro && resp[0] == 0x07)
        {
            if (resp.Length < 9 || (resp[1] & 0x0F) > 2) return null;
            conn = (resp[1] & 0x0F) switch
            {
                0 => PeripheralConnectionType.Dongle24GHz,
                1 => PeripheralConnectionType.Bluetooth,
                2 => PeripheralConnectionType.UsbCable,
                _ => PeripheralConnectionType.Dongle24GHz
            };
            state = resp[7];
            pct = resp[8];
            if (resp.Length >= 15) mousePid = (ushort)(resp[13] | (resp[14] << 8));
        }
        else if (legacyVt3Pro && resp[0] == 0xBB)
        {
            if (resp.Length < 7 || resp[1] != 0xB0) return null;
            state = resp[5];
            pct = resp[6];
        }
        else
        {
            return null;
        }

        if (state is not (1 or 2)) return null;
        if (pct > 100 && !(legacyVt3Pro && state == 2 && pct == 0x7F)) return null;
        return (pct <= 100 ? pct : null, state == 2, conn, mousePid);
    }

    public static (int Level, bool Charging)? ParseAresonBatteryResponse(byte[] resp)
    {
        if (resp == null || resp.Length < 17) return null;
        int total = 0;
        for (int i = 0; i < 17; i++) total += resp[i];
        if ((total & 0xFF) != 0x55 || resp[0] != 0x09 || resp[1] != 0x04 || resp[2] != 0x00 || resp[6] > 10)
            return null;
        return (Math.Clamp(resp[6] * 10, 0, 100), resp[7] != 0);
    }

    public static (int Level, bool Charging)? ParseAulaBatteryResponse(byte[] resp)
    {
        if (resp == null || resp.Length < 20 || resp[0] != 0x13 || (resp[1] & 0x7F) != 0x4A)
            return null;
        int sum = 0;
        for (int i = 0; i < 19; i++) sum += resp[i];
        bool checksumOk = resp[19] == (byte)(sum & 0xFF);
        bool mchoseOk = resp[19] == 0 && resp[2] == 1 && resp[3] == 0;
        if (!checksumOk && !mchoseOk) return null;
        int lvl = resp[5];
        if (lvl is < 1 or > 100) return null;
        bool chg = (resp[6] & 0xF0) != 0;
        return (lvl, chg);
    }

    public static (int Level, string ApproxText, bool Charging)? ParseSwitchProBatteryResponse(byte[] resp)
    {
        if (resp == null || resp.Length < 13 || resp[0] != 0x30) return null;
        int level = (resp[2] & 0xE0) >> 5;
        if (level > 4) return null;
        string text = level switch
        {
            0 => "vazia",
            1 => "crítica",
            2 => "baixa",
            3 => "média",
            _ => "cheia"
        };
        bool chg = (resp[2] & 0x10) != 0 && level < 4;
        return (level * 25, text, chg);
    }

    public static (int? Level, string ApproxText, bool Charging) ParseXboxXInputLevel(byte batteryType, byte batteryLevel)
    {
        if (batteryType == 0x01) // WIRED
            return (100, "Cabeado", true);
        return batteryLevel switch
        {
            0 => (5, "vazia", false),
            1 => (20, "baixa", false),
            2 => (55, "média", false),
            3 => (100, "cheia", false),
            _ => (null, "", false)
        };
    }

    public static int? ParseRazerBatteryResponse(byte[] resp90)
    {
        if (resp90 == null || resp90.Length < 10 || resp90[0] != 0x02) return null;
        int raw = resp90[9];
        return Math.Clamp((int)Math.Round(raw * 100.0 / 255.0), 0, 100);
    }

    public static (int? Level, bool Charging) ParseLogitechHidPp2Unified(byte[] resp)
    {
        if (resp == null || resp.Length < 7) return (null, false);
        int pct = resp[4];
        if (pct > 100) return (null, false);
        bool chg = resp[6] is 1 or 2 or 3 or 4;
        return (pct, chg);
    }

    public static (int? Level, bool Charging) ParseLogitechHidPp2Status(byte[] resp)
    {
        if (resp == null || resp.Length < 7) return (null, false);
        int pct = resp[4];
        if (pct > 100) return (null, false);
        bool chg = resp[6] is 1 or 2 or 3 or 4;
        return (pct, chg);
    }

    public static (int? Level, bool Charging, string ApproxText) ParseLogitechHidPp1Register(byte reg, byte[] resp)
    {
        if (resp == null || resp.Length < 7) return (null, false, "");
        if (reg == 0x0D)
        {
            int pct = resp[4];
            if (pct > 100) return (null, false, "");
            bool chg = resp[6] is 0x50 or 0x90;
            return (pct, chg, "");
        }
        if (reg == 0x07)
        {
            (int pct, string word) = resp[4] switch
            {
                7 => (90, "cheia"),
                5 => (50, "boa"),
                3 => (20, "baixa"),
                1 => (5, "crítica"),
                _ => (-1, "")
            };
            if (pct < 0) return (null, false, "");
            bool chg = (resp[5] & 0x21) != 0 || (resp[5] & 0x22) != 0;
            return (pct, chg, word);
        }
        return (null, false, "");
    }

    public static (int Level, bool Charging) ParsePlayStationDs4(byte statusByte)
    {
        int raw = statusByte & 0x0F;
        bool charging = (statusByte & 0x10) != 0;
        return (Math.Min(raw, 10) * 10, charging);
    }

    public static (int Level, bool Charging)? ParsePlayStationDualSense(byte statusByte)
    {
        int raw = statusByte & 0x0F;
        int state = (statusByte & 0xF0) >> 4;
        bool charging = state == 1;
        if (state == 2) return (100, true);
        if (state > 2 && state is not (0x0A or 0x0B)) return null;
        return (Math.Min(raw * 10 + 5, 100), charging);
    }

    public static int? ParseCorsairBragi(byte[] resp)
    {
        if (resp == null || resp.Length < 5 || resp[2] != 0x00) return null;
        int tenths = resp[3] | (resp[4] << 8);
        if (tenths is <= 0 or > 1000) return null;
        return Math.Clamp((int)Math.Round(tenths / 10.0), 0, 100);
    }

    public static (int? Level, bool Charging, bool Online)? ParseCorsairVoidElite(byte[] resp)
    {
        if (resp == null || resp.Length < 5 || resp[0] != 0x64) return null;
        if (resp[4] == 0x00) return (null, false, false);
        int level = Math.Min(resp[2] & 0x7F, 100);
        bool chg = resp[4] is 4 or 5;
        return (level, chg, true);
    }

    public static (int? Level, bool Charging, bool Online) ParseSteelSeriesNova7(byte[] resp)
    {
        if (resp == null || resp.Length < 4 || resp[0] != 0xB0 || resp[1] != 0x03 || resp[3] == 0x00)
            return (null, false, false);
        return (Math.Min((int)resp[2], 100), resp[3] is 0x01 or 0x02, true);
    }

    public static int? ParseKeychronLevel(byte[] resp)
    {
        if (resp == null || resp.Length <= 20 || resp[0] != 0xB4 || resp[1] != 0x06) return null;
        return resp[20] <= 100 ? resp[20] : null;
    }

    private static int VoltageToPercent(int mv, int minMv, int maxMv) =>
        Math.Clamp((int)Math.Round((mv - minMv) * 100.0 / Math.Max(1, maxMv - minMv)), 0, 100);

    private static PeripheralKind InferKind(string typeLabel, string name)
    {
        string s = $"{typeLabel} {name}".ToLowerInvariant();
        if (s.Contains("mouse") || s.Contains("viper") || s.Contains("deathadder") || s.Contains("basilisk") ||
            s.Contains("superlight") || s.Contains("atlantis") || s.Contains("beast") || s.Contains("aerox"))
            return PeripheralKind.Mouse;
        if (s.Contains("teclado") || s.Contains("keyboard") || s.Contains("blackwidow") || s.Contains("deathstalker") || s.Contains("keychron"))
            return PeripheralKind.Keyboard;
        if (s.Contains("headset") || s.Contains("fone") || s.Contains("áudio") || s.Contains("audio") ||
            s.Contains("arctis") || s.Contains("cloud") || s.Contains("void") || s.Contains("virtuoso") ||
            s.Contains("blackshark") || s.Contains("barracuda") || s.Contains("quantum") || s.Contains("maxwell"))
            return PeripheralKind.Headset;
        if (s.Contains("controle") || s.Contains("gamepad") || s.Contains("xbox") || s.Contains("dualsense") ||
            s.Contains("dualshock") || s.Contains("joystick"))
            return PeripheralKind.Gamepad;
        return PeripheralKind.Device;
    }

    public static string FormatTrayTooltip(IReadOnlyList<PeripheralBatteryInfo> devices)
    {
        if (devices == null || devices.Count == 0)
            return "Baterias: nenhum periférico detectado";

        var lines = devices
            .Where(d => d.Level != null)
            .Take(3)
            .Select(d => $"{d.Name}: {d.Level}%{(d.IsCharging ? "⚡" : "")}");
        string summary = string.Join(" | ", lines);
        if (string.IsNullOrEmpty(summary)) summary = $"{devices.Count} periférico(s)";
        return summary.Length >= 63 ? summary[..60] + "..." : summary;
    }
}
