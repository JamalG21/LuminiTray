using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace LuminiTray;

public sealed class BatteryDeviceInfo
{
    public string Id { get; set; } = "BAT0";
    public string Name { get; set; } = "Bateria Principal";
    public string Manufacturer { get; set; } = "—";
    public string SerialNumber { get; set; } = "—";
    public string ModelName { get; set; } = "—";
    public string Chemistry { get; set; } = "Íon-lítio (Li-Ion)";
    public long DesignCapacityMwh { get; set; }
    public long FullChargeCapacityMwh { get; set; }
    public long? CycleCount { get; set; }

    public double DesignCapacityWh => DesignCapacityMwh / 1000.0;
    public double FullChargeCapacityWh => FullChargeCapacityMwh / 1000.0;

    public double HealthPercentage => DesignCapacityMwh > 0
        ? Math.Clamp(Math.Round((double)FullChargeCapacityMwh / DesignCapacityMwh * 100.0, 1), 0.0, 100.0)
        : 100.0;

    public double DegradationPercentage => Math.Max(0.0, Math.Round(100.0 - HealthPercentage, 1));

    public string HealthVerdict => HealthPercentage switch
    {
        >= 80.0 => "Bateria boa",
        >= 60.0 => "Bateria gasta",
        _ => "Troca recomendada"
    };

    public string HealthBadgeColor => HealthPercentage switch
    {
        >= 80.0 => "#10B981", // Verde
        >= 60.0 => "#F59E0B", // Âmbar
        _ => "#EF4444"         // Vermelho
    };

    public string HealthExplanation => HealthPercentage switch
    {
        >= 80.0 => $"Ela ainda guarda {HealthPercentage:0}% da carga de quando era nova. Esse desgaste é normal: continue usando sem preocupação.",
        >= 60.0 => $"A capacidade caiu para {HealthPercentage:0}% do original ({DegradationPercentage:0}% de desgaste). A autonomia longe da tomada já diminuiu consideravelmente.",
        _ => $"A capacidade caiu para {HealthPercentage:0}%. A bateria perdeu a maior parte de sua autonomia original: vale a pena planejar a substituição."
    };

    public string RuntimeEstimatedNew { get; set; } = "—";
    public string RuntimeEstimatedCurrent { get; set; } = "—";
}

public sealed class BatteryHistoryEntry
{
    public DateTime Date { get; set; }
    public long FullChargeCapacityMwh { get; set; }
    public long DesignCapacityMwh { get; set; }
    public double HealthPercentage => DesignCapacityMwh > 0
        ? Math.Clamp(Math.Round((double)FullChargeCapacityMwh / DesignCapacityMwh * 100.0, 1), 0.0, 100.0)
        : 100.0;
}

public sealed class BatteryDetailedReport
{
    public bool HasBattery { get; set; }
    public string PlatformRole { get; set; } = "Desktop";
    public string ComputerName { get; set; } = "";
    public DateTime ScanTime { get; set; } = DateTime.Now;
    public List<BatteryDeviceInfo> Batteries { get; set; } = new();
    public List<BatteryHistoryEntry> History { get; set; } = new();

    public BatteryDeviceInfo? PrimaryBattery => Batteries.FirstOrDefault();
    public string HistorySummary
    {
        get
        {
            if (History.Count < 2) return "Histórico inicial sendo registrado pelo Windows.";
            var first = History.First();
            var last = History.Last();
            return $"De {first.Date:dd/MM/yyyy} a {last.Date:dd/MM/yyyy}: a capacidade foi de {first.HealthPercentage:0}% para {last.HealthPercentage:0}% da original.";
        }
    }
}

public static class BatteryReportService
{
    public static async Task<BatteryDetailedReport> GenerateReportAsync(CancellationToken ct = default)
    {
        string tempXml = Path.Combine(Path.GetTempPath(), $"battery_report_{Guid.NewGuid():N}.xml");
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powercfg.exe",
                Arguments = $"/batteryreport /xml /output \"{tempXml}\"",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var proc = Process.Start(psi);
            if (proc != null)
            {
                using var reg = ct.Register(() => { try { if (!proc.HasExited) proc.Kill(true); } catch { } });
                await proc.WaitForExitAsync(ct);
            }

            if (File.Exists(tempXml))
            {
                string xml = await File.ReadAllTextAsync(tempXml, ct);
                return ParseXml(xml);
            }
        }
        catch { }
        finally
        {
            try { if (File.Exists(tempXml)) File.Delete(tempXml); } catch { }
        }

        return new BatteryDetailedReport { HasBattery = false, PlatformRole = "Desktop" };
    }

    public static BatteryDetailedReport ParseXml(string xmlContent)
    {
        var report = new BatteryDetailedReport();
        if (string.IsNullOrWhiteSpace(xmlContent)) return report;

        try
        {
            var doc = XDocument.Parse(xmlContent);
            XNamespace ns = doc.Root?.GetDefaultNamespace() ?? XNamespace.None;

            var sysInfo = doc.Descendants(ns + "SystemInformation").FirstOrDefault();
            if (sysInfo != null)
            {
                report.ComputerName = sysInfo.Element(ns + "ComputerName")?.Value ?? "";
                report.PlatformRole = sysInfo.Element(ns + "PlatformRole")?.Value ?? "Desktop";
            }

            var repInfo = doc.Descendants(ns + "ReportInformation").FirstOrDefault();
            if (repInfo != null)
            {
                if (DateTime.TryParse(repInfo.Element(ns + "LocalScanTime")?.Value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var st))
                {
                    report.ScanTime = st;
                }
            }

            // Runtime Estimates
            string rtNew = "—";
            string rtCur = "—";
            var rtEstimates = doc.Descendants(ns + "RuntimeEstimates").FirstOrDefault();
            if (rtEstimates != null)
            {
                var desEl = rtEstimates.Element(ns + "DesignCapacity")?.Element(ns + "ActiveRuntime")?.Value;
                var fulEl = rtEstimates.Element(ns + "FullChargeCapacity")?.Element(ns + "ActiveRuntime")?.Value;
                if (!string.IsNullOrEmpty(desEl)) rtNew = FormatTimeSpanString(desEl);
                if (!string.IsNullOrEmpty(fulEl)) rtCur = FormatTimeSpanString(fulEl);
            }

            // Batteries
            var batteriesEl = doc.Descendants(ns + "Batteries").Elements(ns + "Battery");
            foreach (var b in batteriesEl)
            {
                var dev = new BatteryDeviceInfo
                {
                    Id = b.Element(ns + "Id")?.Value ?? "BAT0",
                    Manufacturer = b.Element(ns + "Manufacturer")?.Value ?? "—",
                    SerialNumber = b.Element(ns + "SerialNumber")?.Value ?? "—",
                    ModelName = b.Element(ns + "DeviceName")?.Value ?? "—",
                    Chemistry = TranslateChemistry(b.Element(ns + "Chemistry")?.Value),
                    RuntimeEstimatedNew = rtNew,
                    RuntimeEstimatedCurrent = rtCur
                };

                if (long.TryParse(b.Element(ns + "DesignCapacity")?.Value, out long des)) dev.DesignCapacityMwh = des;
                if (long.TryParse(b.Element(ns + "FullChargeCapacity")?.Value, out long ful)) dev.FullChargeCapacityMwh = ful;
                if (long.TryParse(b.Element(ns + "CycleCount")?.Value, out long cyc) && cyc > 0) dev.CycleCount = cyc;

                report.Batteries.Add(dev);
            }

            report.HasBattery = report.Batteries.Count > 0 && report.Batteries.Any(x => x.DesignCapacityMwh > 0 || x.FullChargeCapacityMwh > 0);

            // History entries
            var historyEl = doc.Descendants(ns + "BatteryHistory").Elements(ns + "HistoryEntry");
            foreach (var h in historyEl)
            {
                if (DateTime.TryParse(h.Attribute("LocalTimestamp")?.Value ?? h.Attribute("Timestamp")?.Value, out var dt))
                {
                    long desCap = 0, fulCap = 0;
                    long.TryParse(h.Element(ns + "DesignCapacity")?.Value, out desCap);
                    long.TryParse(h.Element(ns + "FullChargeCapacity")?.Value, out fulCap);

                    if (desCap > 0 && fulCap > 0)
                    {
                        report.History.Add(new BatteryHistoryEntry
                        {
                            Date = dt,
                            DesignCapacityMwh = desCap,
                            FullChargeCapacityMwh = fulCap
                        });
                    }
                }
            }

            report.History = report.History.OrderBy(x => x.Date).ToList();
        }
        catch { }

        return report;
    }

    public static async Task<string?> GenerateHtmlReportAsync()
    {
        string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), $"Relatorio_Bateria_{DateTime.Now:yyyyMMdd_HHmmss}.html");
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powercfg.exe",
                Arguments = $"/batteryreport /output \"{path}\"",
                CreateNoWindow = true,
                UseShellExecute = false
            };
            using var proc = Process.Start(psi);
            if (proc != null)
            {
                await proc.WaitForExitAsync();
                if (File.Exists(path)) return path;
            }
        }
        catch { }
        return null;
    }

    private static string TranslateChemistry(string? chem) => chem?.Trim().ToUpperInvariant() switch
    {
        "LION" or "LI-ION" => "Íon-lítio (Li-Ion)",
        "LIPO" or "LI-PO" or "LIP" => "Polímero de lítio (Li-Po)",
        "NICD" => "Níquel-Cádmio (NiCd)",
        "NIMH" => "Níquel-Hidreto Metálico (NiMH)",
        "PBAC" => "Chumbo-ácido",
        _ => !string.IsNullOrEmpty(chem) ? chem : "Íon-lítio (Li-Ion)"
    };

    private static string FormatTimeSpanString(string isoDuration)
    {
        try
        {
            var ts = System.Xml.XmlConvert.ToTimeSpan(isoDuration);
            if (ts.TotalMinutes < 1) return "—";
            return $"{(int)ts.TotalHours} h {ts.Minutes:00} min";
        }
        catch
        {
            return "—";
        }
    }
}
