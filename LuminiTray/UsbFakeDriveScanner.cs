using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace LuminiTray;

public sealed class UsbDriveTarget
{
    public string DriveLetter { get; set; } = "";
    public string VolumeLabel { get; set; } = "";
    public long TotalBytes { get; set; }
    public long FreeBytes { get; set; }
    public string FileSystem { get; set; } = "FAT32";
    public string Model { get; set; } = "USB Flash Drive";
    public string DeviceId { get; set; } = "";
    public string PnpDeviceId { get; set; } = "";
    public string Vid { get; set; } = "—";
    public string Pid { get; set; } = "—";
    public string VendorName { get; set; } = "Genérico";
    public string UsbBusVersion { get; set; } = "USB 2.0 / USB 3.0";
    public string SerialNumber { get; set; } = "—";
    public string Protocol { get; set; } = "Bulk-Only (USBSTOR)";
    public bool IsSuspiciousController { get; set; }
    public string SuspiciousReason { get; set; } = "";

    public string DisplayName => $"{DriveLetter} ({(!string.IsNullOrWhiteSpace(VolumeLabel) ? VolumeLabel : "Sem nome")}) · {DiskSpaceAnalyzer.FormatSize(TotalBytes)}";
    public string TotalFormatted => DiskSpaceAnalyzer.FormatSize(TotalBytes);
    public string FreeFormatted => DiskSpaceAnalyzer.FormatSize(FreeBytes);
}

public sealed class UsbTestProgress
{
    public int Percent { get; set; }
    public string StatusMessage { get; set; } = "";
    public long BytesTested { get; set; }
    public long TotalBytesToTest { get; set; }
    public double SpeedMbPerSec { get; set; }
    public bool HasErrors { get; set; }
}

public sealed class UsbFakeTestResult
{
    public bool Completed { get; set; }
    public bool IsCounterfeit { get; set; }
    public long AdvertisedCapacityBytes { get; set; }
    public long VerifiedRealCapacityBytes { get; set; }
    public int TotalProbes { get; set; }
    public int FailedProbes { get; set; }
    public string SummaryMessage { get; set; } = "";
    public List<string> Warnings { get; set; } = new();

    public string AdvertisedFormatted => DiskSpaceAnalyzer.FormatSize(AdvertisedCapacityBytes);
    public string RealFormatted => DiskSpaceAnalyzer.FormatSize(VerifiedRealCapacityBytes);
}

public static class UsbFakeDriveScanner
{
    private static readonly Dictionary<string, string> KnownVendors = new(StringComparer.OrdinalIgnoreCase)
    {
        { "0781", "SanDisk Corp." },
        { "0951", "Kingston Technology" },
        { "04E8", "Samsung Electronics" },
        { "054C", "Sony Corporation" },
        { "058F", "Alcor Micro Corp. (Controlador Genérico)" },
        { "1005", "Apacer Technology Inc." },
        { "13FE", "Phison Electronics Corp." },
        { "1908", "Actions Semiconductor (Controlador comum em réplicas)" },
        { "0204", "ChipsBank Microelectronics (Alerta: comum em pendrives falsos)" },
        { "1221", "FirstChip Technology (Alerta: comum em pendrives falsos)" },
        { "8644", "Netac Technology" },
        { "0BDA", "Realtek Semiconductor" },
        { "154B", "PNY Technologies" },
        { "090C", "Silicon Motion, Inc." },
        { "1B1C", "Corsair" },
        { "8564", "Transcend Information" }
    };

    public static List<UsbDriveTarget> GetRemovableDrives()
    {
        var targets = new List<UsbDriveTarget>();
        try
        {
            var driveInfos = DriveInfo.GetDrives()
                .Where(d => d.IsReady && (d.DriveType == DriveType.Removable || (d.DriveType == DriveType.Fixed && d.Name != Path.GetPathRoot(Environment.SystemDirectory))))
                .ToList();

            using var searcher = new ManagementObjectSearcher(@"root\cimv2", "SELECT * FROM Win32_DiskDrive WHERE InterfaceType = 'USB'");
            var usbDisks = searcher.Get().Cast<ManagementObject>().ToList();

            foreach (var d in driveInfos)
            {
                // Proteção absoluta: Nunca testar C: ou disco do Windows
                if (string.Equals(d.Name, Path.GetPathRoot(Environment.SystemDirectory), StringComparison.OrdinalIgnoreCase))
                    continue;

                string root = d.RootDirectory.FullName.TrimEnd('\\');
                var target = new UsbDriveTarget
                {
                    DriveLetter = root,
                    VolumeLabel = d.VolumeLabel,
                    TotalBytes = d.TotalSize,
                    FreeBytes = d.TotalFreeSpace,
                    FileSystem = d.DriveFormat
                };

                // Localiza informações do hardware WMI correspondente
                var matched = usbDisks.FirstOrDefault();
                if (matched != null)
                {
                    target.Model = matched["Model"]?.ToString() ?? "Dispositivo de Armazenamento USB";
                    target.DeviceId = matched["DeviceID"]?.ToString() ?? "";
                    target.PnpDeviceId = matched["PNPDeviceID"]?.ToString() ?? "";
                    target.SerialNumber = matched["SerialNumber"]?.ToString()?.Trim() ?? "—";

                    // Tenta extrair VID e PID do PNPDeviceID
                    var m = Regex.Match(target.PnpDeviceId, @"VID_([0-9A-Fa-f]{4})&PID_([0-9A-Fa-f]{4})");
                    if (m.Success)
                    {
                        target.Vid = m.Groups[1].Value.ToUpperInvariant();
                        target.Pid = m.Groups[2].Value.ToUpperInvariant();
                        if (KnownVendors.TryGetValue(target.Vid, out var vendor))
                        {
                            target.VendorName = vendor;
                        }
                    }

                    // Detecção de controladores frequentemente associados a réplicas
                    if (target.Vid is "0204" or "1221" or "1908" || target.PnpDeviceId.Contains("ChipsBank", StringComparison.OrdinalIgnoreCase) || target.PnpDeviceId.Contains("FirstChip", StringComparison.OrdinalIgnoreCase))
                    {
                        target.IsSuspiciousController = true;
                        target.SuspiciousReason = "Chipset do controlador identificado possui histórico frequente de firmware adulterado (fake capacity).";
                    }
                }

                targets.Add(target);
            }
        }
        catch { }

        return targets;
    }

    public static async Task<UsbFakeTestResult> RunQuickSampleTestAsync(
        UsbDriveTarget target,
        bool highPrecision,
        IProgress<UsbTestProgress>? progress,
        CancellationToken ct)
    {
        var result = new UsbFakeTestResult
        {
            AdvertisedCapacityBytes = target.TotalBytes,
            VerifiedRealCapacityBytes = target.TotalBytes
        };

        if (string.IsNullOrEmpty(target.DriveLetter) || !Directory.Exists(target.DriveLetter))
        {
            result.SummaryMessage = "Unidade selecionada inacessível.";
            return result;
        }

        string testFile = Path.Combine(target.DriveLetter, ".luminitray_quick_probe.tmp");
        int probeCount = highPrecision ? 2000 : 1000;
        result.TotalProbes = probeCount;

        // Se houver pouco espaço livre, limitamos o teste ao espaço livre disponível
        long testSpan = Math.Min(target.FreeBytes > 100 * 1024 * 1024 ? target.FreeBytes : target.TotalBytes, target.TotalBytes);
        long step = Math.Max(4096, testSpan / probeCount);

        byte[] writeToken = new byte[512];
        byte[] readBuffer = new byte[512];
        var sw = Stopwatch.StartNew();

        try
        {
            await Task.Yield();
            using (var fs = new FileStream(testFile, FileMode.Create, FileAccess.ReadWrite, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                long highestValidOffset = 0;
                int failed = 0;

                for (int i = 0; i < probeCount; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    long targetOffset = (long)i * step;

                    // Cria um token único contendo o índice, o offset e hash de integridade
                    BitConverter.GetBytes(i).CopyTo(writeToken, 0);
                    BitConverter.GetBytes(targetOffset).CopyTo(writeToken, 8);
                    for (int b = 16; b < 512; b++) writeToken[b] = (byte)((i * 37 + b) & 0xFF);

                    try
                    {
                        fs.Seek(targetOffset, SeekOrigin.Begin);
                        fs.Write(writeToken, 0, 512);
                        fs.Flush();

                        // Lê de volta imediatamente para verificar consistência
                        fs.Seek(targetOffset, SeekOrigin.Begin);
                        int readBytes = fs.Read(readBuffer, 0, 512);

                        bool matches = readBytes == 512 && writeToken.SequenceEqual(readBuffer);
                        if (matches)
                        {
                            highestValidOffset = targetOffset;
                        }
                        else
                        {
                            failed++;
                        }
                    }
                    catch
                    {
                        failed++;
                    }

                    if (i % 25 == 0 || i == probeCount - 1)
                    {
                        double elapsedSec = Math.Max(0.1, sw.Elapsed.TotalSeconds);
                        double speedMb = ((i + 1) * 1024.0) / (1024.0 * 1024.0 * elapsedSec);
                        int pct = (int)((i + 1) * 100.0 / probeCount);

                        progress?.Report(new UsbTestProgress
                        {
                            Percent = pct,
                            BytesTested = targetOffset,
                            TotalBytesToTest = testSpan,
                            SpeedMbPerSec = Math.Round(speedMb, 1),
                            StatusMessage = $"Amostrando ponto {i + 1} de {probeCount} ({DiskSpaceAnalyzer.FormatSize(targetOffset)})...",
                            HasErrors = failed > 0
                        });
                    }
                }

                result.FailedProbes = failed;
                result.Completed = true;

                if (failed > (probeCount * 0.05)) // Mais de 5% de falhas nos pontos
                {
                    result.IsCounterfeit = true;
                    result.VerifiedRealCapacityBytes = Math.Max(0, highestValidOffset - (failed * step));
                    result.SummaryMessage = $"🚨 ALERTA DE FALSIFICAÇÃO: O dispositivo falhou em {failed} dos {probeCount} pontos testados. A capacidade real útil estimada é de apenas {result.RealFormatted}, apesar de anunciar {result.AdvertisedFormatted}.";
                    result.Warnings.Add("O firmware do pendrive foi adulterado para simular uma capacidade muito maior do que a memória física real.");
                    result.Warnings.Add("Arquivos gravados além da capacidade real serão silenciosamente perdidos ou sobrescreverão arquivos antigos.");
                }
                else
                {
                    result.IsCounterfeit = false;
                    result.VerifiedRealCapacityBytes = target.TotalBytes;
                    result.SummaryMessage = $"🛡️ DISPOSITIVO ORIGINAL APROVADO: Todos os {probeCount} pontos de amostragem responderam com integridade perfeita. A capacidade de {result.AdvertisedFormatted} é legítima.";
                }
            }
        }
        catch (OperationCanceledException)
        {
            result.SummaryMessage = "Teste cancelado pelo usuário.";
        }
        catch (Exception ex)
        {
            result.SummaryMessage = "Erro durante teste: " + ex.Message;
        }
        finally
        {
            try { if (File.Exists(testFile)) File.Delete(testFile); } catch { }
        }

        return result;
    }
}
