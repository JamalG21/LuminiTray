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

public sealed class PeripheralBatteryInfo
{
    public string Key { get; init; } = "";
    public string Name { get; set; } = "";
    public int? Level { get; set; }
    public bool IsCharging { get; set; }
    public bool IsOnline { get; set; } = true;
    public string Source { get; init; } = "";
    public PeripheralKind Kind { get; init; } = PeripheralKind.Device;
    public string ApproxText { get; set; } = "";
    public bool ViaBluetooth { get; set; }

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
        : (IsCharging ? "Carregando" : "—");
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
        List<string> hidPaths = PeripheralHidTransport.EnumerateHidInterfacePaths();
        var presentVids = new HashSet<ushort>();
        foreach (string p in hidPaths)
        {
            if (PeripheralHidTransport.TryExtractVidPid(p, out ushort vid, out _))
                presentVids.Add(vid);
        }

        // 1. Bluetooth nativo (Windows PnP DEVPKEY_Bluetooth_Battery)
        PollBluetooth(results);

        // 2. XInput (Xbox / GameSir / XInput gamepads)
        PollXInput(results);

        // 3. HID diretos (apenas provedores cujo VID está realmente plugado, evitando abrir outros dispositivos)
        if (presentVids.Contains(0x046D)) PollLogitech(hidPaths, results);
        if (presentVids.Contains(0x1532)) PollRazer(hidPaths, results);
        if (presentVids.Contains(0x054C)) PollPlayStation(hidPaths, results);
        if (presentVids.Contains(0x1B1C)) PollCorsair(hidPaths, results);
        if (presentVids.Contains(0x1038)) PollSteelSeries(hidPaths, results);
        if (presentVids.Contains(0x03F0) || presentVids.Contains(0x0951)) PollHyperX(hidPaths, results);
        if (presentVids.Contains(0x3434)) PollKeychron(hidPaths, results);
        if (presentVids.Contains(0x0ECB)) PollJblQuantum(hidPaths, results);
        PollChecksum17HidDevices(hidPaths, presentVids, results);

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
                if (!string.IsNullOrWhiteSpace(item.ApproxText)) existing.ApproxText = item.ApproxText;
            }
        }

        return byKey.Values
            .OrderByDescending(d => d.IsOnline)
            .ThenBy(d => d.Level ?? 999)
            .ThenBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    // ------------------------------------------------------------------ 1. Bluetooth PnP

    private static void PollBluetooth(List<PeripheralBatteryInfo> results)
    {
        try
        {
            var btBatteries = PeripheralHidTransport.ReadBluetoothBatteryByMac();
            var paired = BluetoothManagerService.ListPairedDevices();
            foreach (var dev in paired)
            {
                string mac = dev.AddressText.Replace(":", "").ToUpperInvariant();
                bool hasBat = btBatteries.TryGetValue(mac, out int batLevel);
                if (!dev.IsConnected && !hasBat) continue;
                if (!hasBat) continue;

                results.Add(new PeripheralBatteryInfo
                {
                    Key = $"bt:{mac}",
                    Name = dev.Name,
                    Level = Math.Clamp(batLevel, 0, 100),
                    IsCharging = false,
                    IsOnline = dev.IsConnected,
                    Source = "Bluetooth",
                    Kind = InferKind(dev.DeviceTypeLabel, dev.Name),
                    ViaBluetooth = true
                });
            }
        }
        catch { }
    }

    // ------------------------------------------------------------------ 2. XInput (Xbox / GameSir)

    private static void PollXInput(List<PeripheralBatteryInfo> results)
    {
        for (uint idx = 0; idx < 4; idx++)
        {
            try
            {
                uint rc;
                XINPUT_BATTERY_INFORMATION info;
                try { rc = XInput14GetBatteryInformation(idx, 0, out info); }
                catch { rc = XInput13GetBatteryInformation(idx, 0, out info); }

                if (rc != 0 || info.BatteryType == 0x00) continue; // desconectado

                var (level, approx, charging) = ParseXboxXInputLevel(info.BatteryType, info.BatteryLevel);
                results.Add(new PeripheralBatteryInfo
                {
                    Key = $"xinput:{idx}",
                    Name = $"Controle Xbox / XInput #{idx + 1}",
                    Level = level,
                    ApproxText = approx,
                    IsCharging = charging,
                    IsOnline = true,
                    Source = "XInput",
                    Kind = PeripheralKind.Gamepad
                });
            }
            catch { }
        }
    }

    // ------------------------------------------------------------------ 3. Logitech HID++ 2.0 & 1.0

    private static void PollLogitech(List<string> hidPaths, List<PeripheralBatteryInfo> results)
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
            int[] slots = isReceiver ? new[] { 1, 2, 3, 4, 5, 6 } : new[] { 0xFF };
            foreach (int slot in slots)
            {
                var info = QueryLogitechSlot(h, ep, (byte)slot, shortEp != null);
                if (info != null) results.Add(info);
            }
        }
    }

    private static PeripheralBatteryInfo? QueryLogitechSlot(Microsoft.Win32.SafeHandles.SafeFileHandle h, HidEndpointInfo ep, byte slot, bool allowHidPp1)
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
                string name = !string.IsNullOrWhiteSpace(ep.ProductName) ? ep.ProductName : $"Logitech HID++ ({ep.ProductId:X4})";
                if (slot != 0xFF) name = $"{name} #{slot}";
                return new PeripheralBatteryInfo
                {
                    Key = $"logi:{ep.ProductId:X4}:{slot}",
                    Name = name,
                    Level = lvl,
                    IsCharging = chg,
                    IsOnline = true,
                    Source = "Logitech HID++",
                    Kind = InferKind(ep.UsagePage == 0xFF43 ? "Headset" : "", name)
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
                            Kind = PeripheralKind.Mouse
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

    private static byte[]? LogitechSendShortOrLong(Microsoft.Win32.SafeHandles.SafeFileHandle h, byte slot, byte featIdx, byte fnSwid)
    {
        var req = new byte[20];
        req[0] = 0x11; req[1] = slot; req[2] = featIdx; req[3] = fnSwid;
        PeripheralHidTransport.FlushInput(h);
        if (!PeripheralHidTransport.WriteReport(h, req, 250)) return null;
        return PeripheralHidTransport.ReadReport(h, 20, 350);
    }

    // ------------------------------------------------------------------ 4. Razer (0x1532)

    private static void PollRazer(List<string> hidPaths, List<PeripheralBatteryInfo> results)
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
                Kind = InferKind("", name)
            });
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

    private static void PollPlayStation(List<string> hidPaths, List<PeripheralBatteryInfo> results)
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
                    ViaBluetooth = ep.IsBluetooth
                });
            }
        }
    }

    // ------------------------------------------------------------------ 6. Corsair (0x1B1C)

    private static void PollCorsair(List<string> hidPaths, List<PeripheralBatteryInfo> results)
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
                            Kind = PeripheralKind.Headset
                        });
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
                            Kind = InferKind("", ep.ProductName)
                        });
                    }
                }
            }
        }
    }

    // ------------------------------------------------------------------ 7. SteelSeries (0x1038)

    private static void PollSteelSeries(List<string> hidPaths, List<PeripheralBatteryInfo> results)
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
                    Kind = PeripheralKind.Headset
                });
            }
        }
    }

    // ------------------------------------------------------------------ 8. HyperX (0x03F0 / 0x0951)

    private static void PollHyperX(List<string> hidPaths, List<PeripheralBatteryInfo> results)
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
                        Kind = PeripheralKind.Headset
                    });
                }
            }
        }
    }

    // ------------------------------------------------------------------ 9. Keychron (0x3434) & JBL (0x0ECB) & 17-byte 0x55 Mice

    private static void PollKeychron(List<string> hidPaths, List<PeripheralBatteryInfo> results)
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
                    Kind = ep.ProductName.Contains("M5", StringComparison.OrdinalIgnoreCase) ? PeripheralKind.Mouse : PeripheralKind.Keyboard
                });
            }
        }
    }

    private static void PollJblQuantum(List<string> hidPaths, List<PeripheralBatteryInfo> results)
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
                    Kind = PeripheralKind.Headset
                });
            }
        }
    }

    private static void PollChecksum17HidDevices(List<string> hidPaths, HashSet<ushort> presentVids, List<PeripheralBatteryInfo> results)
    {
        foreach (var kv in GamingChecksum17Devices)
        {
            if (!presentVids.Contains(kv.Key.Vid)) continue;
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
                        Kind = kv.Value.Kind
                    });
                    break;
                }
            }
        }
    }

    // ------------------------------------------------------------------ Pure Protocol Parsers (Testáveis)

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
