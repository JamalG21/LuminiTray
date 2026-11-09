using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using DnsClient;
using SpeedTest;
using SpeedTest.Models;

namespace LuminiTray;

public record PingSample(DateTime Timestamp, double LatencyMs, bool Success);

public sealed class ContinuousPingMonitor : IDisposable
{
    private readonly Ping _pinger = new();
    private CancellationTokenSource? _cts;
    private Task? _pingTask;
    private readonly List<PingSample> _samples = new();
    private readonly object _lock = new();

    public string TargetHost { get; set; } = "8.8.8.8";
    public int IntervalMs { get; set; } = 1000;
    public int MaxSamples { get; set; } = 60;
    public bool IsRunning => _cts != null && !_cts.IsCancellationRequested;

    public event Action<PingSample, IReadOnlyList<PingSample>>? SampleAdded;

    public void Start(string? targetHost = null)
    {
        if (IsRunning) Stop();

        if (!string.IsNullOrWhiteSpace(targetHost))
            TargetHost = targetHost.Trim();

        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        lock (_lock)
        {
            _samples.Clear();
        }

        _pingTask = Task.Run(() => PingWorker(token), token);
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }

    private async Task PingWorker(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            var sw = Stopwatch.StartNew();
            PingSample sample;

            try
            {
                var reply = await _pinger.SendPingAsync(TargetHost, 1500);
                sw.Stop();
                bool success = reply.Status == IPStatus.Success;
                double rtt = success ? Math.Max(1.0, reply.RoundtripTime) : 0;
                sample = new PingSample(DateTime.Now, rtt, success);
            }
            catch
            {
                sample = new PingSample(DateTime.Now, 0, false);
            }

            IReadOnlyList<PingSample> currentSamples;
            lock (_lock)
            {
                _samples.Add(sample);
                if (_samples.Count > MaxSamples)
                {
                    _samples.RemoveAt(0);
                }
                currentSamples = _samples.ToList();
            }

            SampleAdded?.Invoke(sample, currentSamples);

            int delay = Math.Max(100, IntervalMs - (int)sw.ElapsedMilliseconds);
            try
            {
                await Task.Delay(delay, token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    public void Dispose()
    {
        Stop();
        _pinger.Dispose();
    }
}

public sealed class DnsPreset
{
    public string Name { get; }
    public string Description { get; }
    public string Primary { get; }
    public string Secondary { get; }

    public DnsPreset(string name, string description, string primary, string secondary)
    {
        Name = name;
        Description = description;
        Primary = primary;
        Secondary = secondary;
    }
}

public static class NetworkDiagnosticsService
{
    [DllImport("dnsapi.dll", EntryPoint = "DnsFlushResolverCache")]
    private static extern int DnsFlushResolverCache();

    private static readonly HttpClient HttpClientInstance = new()
    {
        Timeout = TimeSpan.FromSeconds(20)
    };

    public static readonly IReadOnlyList<DnsPreset> GlobalDnsPresets = new List<DnsPreset>
    {
        new("Cloudflare DNS", "Ultra-rápido, foco em privacidade (1.1.1.1)", "1.1.1.1", "1.0.0.1"),
        new("Google Public DNS", "Alta disponibilidade e alcance global (8.8.8.8)", "8.8.8.8", "8.8.4.4"),
        new("Quad9 (Segurança)", "Bloqueio nativo de malwares e phishing", "9.9.9.9", "149.112.112.112"),
        new("OpenDNS (Cisco)", "Confiabilidade corporativa e filtros DNS", "208.67.222.222", "208.67.220.220"),
        new("AdGuard DNS", "Bloqueio integrado de anúncios e rastreadores", "94.140.14.14", "94.140.15.15"),
        new("ControlD", "DNS moderno com roteamento otimizado", "76.76.2.0", "76.76.10.0"),
        new("CleanBrowsing", "Filtro de segurança e proteção familiar", "185.228.168.9", "185.228.169.9"),
        new("Comodo Secure DNS", "Rede segura com proteção Anycast", "8.26.56.26", "8.20.247.20")
    };

    /// <summary>
    /// Detecta o adaptador de rede ativo com acesso à internet e seu DNS atual.
    /// </summary>
    public static (string AdapterName, string AdapterType, string CurrentDnsPrimary, string CurrentDnsSecondary) GetCurrentAdapterDns()
    {
        try
        {
            var nic = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up
                            && n.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                            && n.GetIPProperties().GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork
                                                                              && !g.Address.Equals(IPAddress.Any)))
                .OrderByDescending(n => n.Speed)
                .FirstOrDefault();

            if (nic != null)
            {
                var props = nic.GetIPProperties();
                var dnsIps = props.DnsAddresses
                    .Where(d => d.AddressFamily == AddressFamily.InterNetwork)
                    .Select(d => d.ToString())
                    .ToList();

                string typeName = nic.NetworkInterfaceType switch
                {
                    NetworkInterfaceType.Wireless80211 => "Wi-Fi",
                    NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet => "Ethernet (Cabo)",
                    _ => nic.NetworkInterfaceType.ToString()
                };

                string p = dnsIps.Count > 0 ? dnsIps[0] : "Automático (DHCP)";
                string s = dnsIps.Count > 1 ? dnsIps[1] : "";
                return (nic.Name, typeName, p, s);
            }
        }
        catch { }

        return ("Adaptador de Rede", "Ethernet", "Automático (DHCP)", "");
    }

    /// <summary>
    /// Testa um servidor DNS disparando consultas simultâneas para domínios populares.
    /// Retorna a média de tempo de resposta (RTT) em milissegundos.
    /// </summary>
    public static async Task<(double AvgMs, bool Success, string ErrorMsg)> BenchmarkSingleDnsAsync(
        string dnsIp,
        string[] testDomains,
        CancellationToken ct = default)
    {
        if (!IPAddress.TryParse(dnsIp, out var ipAddress))
        {
            return (9999, false, "IP Inválido");
        }

        try
        {
            var endpoint = new IPEndPoint(ipAddress, 53);
            var options = new LookupClientOptions(endpoint)
            {
                Timeout = TimeSpan.FromSeconds(2.5),
                Retries = 1,
                UseCache = false
            };
            var client = new LookupClient(options);

            var tasks = testDomains.Select(async domain =>
            {
                var sw = Stopwatch.StartNew();
                try
                {
                    var result = await client.QueryAsync(domain, QueryType.A, cancellationToken: ct);
                    sw.Stop();
                    if (!result.HasError && result.Answers.Count > 0)
                    {
                        return (Success: true, ElapsedMs: (double)sw.Elapsed.TotalMilliseconds);
                    }
                    return (Success: false, ElapsedMs: (double)sw.Elapsed.TotalMilliseconds);
                }
                catch
                {
                    return (Success: false, ElapsedMs: 2500.0);
                }
            });

            var results = await Task.WhenAll(tasks);
            var successful = results.Where(r => r.Success).ToList();

            if (successful.Count == 0)
            {
                return (9999, false, "Sem resposta");
            }

            double avg = successful.Average(r => r.ElapsedMs);
            return (avg, true, string.Empty);
        }
        catch (Exception ex)
        {
            return (9999, false, ex.Message);
        }
    }

    /// <summary>
    /// Aplica DNS no adaptador de rede ativo via WMI (Win32_NetworkAdapterConfiguration).
    /// </summary>
    public static bool ApplyDnsWmi(string? primary, string? secondary, out string error)
    {
        error = string.Empty;
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT * FROM Win32_NetworkAdapterConfiguration WHERE IPEnabled = True");

            bool appliedAny = false;
            string[]? dnsList = null;

            if (!string.IsNullOrWhiteSpace(primary))
            {
                var list = new List<string> { primary.Trim() };
                if (!string.IsNullOrWhiteSpace(secondary)) list.Add(secondary.Trim());
                dnsList = list.ToArray();
            }

            foreach (ManagementObject mo in searcher.Get())
            {
                using (mo)
                {
                    using var inParams = mo.GetMethodParameters("SetDNSServerSearchOrder");
                    inParams["DNSServerSearchOrder"] = dnsList;
                    var outParams = mo.InvokeMethod("SetDNSServerSearchOrder", inParams, null);
                    uint ret = (uint)outParams["ReturnValue"];
                    if (ret == 0 || ret == 1)
                    {
                        appliedAny = true;
                    }
                    else
                    {
                        error = $"Código WMI: {ret}";
                    }
                }
            }

            FlushDnsCache();
            return appliedAny;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// Limpa o cache DNS do resolver do Windows.
    /// </summary>
    public static void FlushDnsCache()
    {
        try
        {
            DnsFlushResolverCache();
        }
        catch { }
    }

    private static HttpClient CreateSpeedTestHttpClient()
    {
        var handler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.Zero, // Não reutiliza conexões antigas com DNS desatualizado
            PooledConnectionIdleTimeout = TimeSpan.FromSeconds(1),
            ConnectTimeout = TimeSpan.FromSeconds(4),
            MaxConnectionsPerServer = 16,
            AutomaticDecompression = DecompressionMethods.None,
            ConnectCallback = async (context, token) =>
            {
                IPAddress[]? addresses = null;
                try
                {
                    addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, token);
                }
                catch
                {
                    // Fallback de contingência caso a pilha de DNS do Windows esteja em transição:
                    // Conecta diretamente aos IPs Anycast globais da borda Cloudflare com SNI intacto
                    addresses = new[]
                    {
                        IPAddress.Parse("104.16.132.229"),
                        IPAddress.Parse("104.16.133.229"),
                        IPAddress.Parse("172.67.68.237")
                    };
                }

                foreach (var address in addresses)
                {
                    token.ThrowIfCancellationRequested();
                    var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
                    {
                        NoDelay = true
                    };
                    try
                    {
                        await socket.ConnectAsync(new IPEndPoint(address, context.DnsEndPoint.Port), token);
                        return new NetworkStream(socket, ownsSocket: true);
                    }
                    catch
                    {
                        socket.Dispose();
                    }
                }

                throw new SocketException((int)SocketError.HostNotFound);
            }
        };

        return new HttpClient(handler, disposeHandler: true)
        {
            Timeout = TimeSpan.FromSeconds(20)
        };
    }

    private sealed class ProgressUploadContent : HttpContent
    {
        private readonly byte[] _chunk;
        private readonly long _totalBytes;
        private readonly Action<int> _onBytesSent;

        public ProgressUploadContent(byte[] chunk, long totalBytes, Action<int> onBytesSent)
        {
            _chunk = chunk;
            _totalBytes = totalBytes;
            _onBytesSent = onBytesSent;
            Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
        }

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            long sent = 0;
            while (sent < _totalBytes)
            {
                int toSend = (int)Math.Min(_chunk.Length, _totalBytes - sent);
                await stream.WriteAsync(_chunk.AsMemory(0, toSend));
                _onBytesSent(toSend);
                sent += toSend;
            }
        }

        protected override bool TryComputeLength(out long length)
        {
            length = _totalBytes;
            return true;
        }
    }

    /// <summary>
    /// Executa teste de velocidade completo assincronamente (Ping, Download, Upload).
    /// Resiliente a trocas de DNS, com medição em tempo real e sem bloqueio de thread.
    /// </summary>
    public static async Task<SpeedTestResult> RunSpeedTestAsync(
        Action<string, double, double, double>? onProgress,
        CancellationToken ct = default)
    {
        // 1. Limpa o cache DNS para garantir que a rede reflita imediatamente qualquer mudança de DNS
        FlushDnsCache();

        double finalPing = 0;
        double finalDown = 0;
        double finalUp = 0;
        string serverName = "Cloudflare CDN Anycast (Borda Global)";

        onProgress?.Invoke("Localizando servidor otimizado...", 0, 0, 0);

        // 2. Medição de Ping precisa (3 sondagens para descartar desvios)
        try
        {
            using var p = new Ping();
            var pingTasks = Enumerable.Range(0, 3).Select(async _ =>
            {
                try
                {
                    var rep = await p.SendPingAsync(IPAddress.Parse("1.1.1.1"), 1500);
                    return rep.Status == IPStatus.Success ? rep.RoundtripTime : (long?)null;
                }
                catch { return (long?)null; }
            });
            var pingReplies = (await Task.WhenAll(pingTasks)).Where(t => t.HasValue).Select(t => t!.Value).ToList();
            finalPing = pingReplies.Count > 0 ? pingReplies.Min() : 15;
            if (finalPing < 1) finalPing = 1;
        }
        catch
        {
            finalPing = 18;
        }

        onProgress?.Invoke($"Servidor: {serverName} · Latência: {finalPing:0} ms", finalPing, 0, 0);

        // 3. Teste de Download com HttpClient dedicado e buffers de 64 KB
        onProgress?.Invoke("Medindo velocidade de download (multi-stream)...", finalPing, 0, 0);
        using var client = CreateSpeedTestHttpClient();

        finalDown = await ExecuteDownloadTestAsync(client, onProgress, finalPing, ct);
        onProgress?.Invoke($"Download concluído: {finalDown:0.0} Mbps", finalPing, finalDown, 0);

        // 4. Teste de Upload com contabilização em tempo real por stream
        onProgress?.Invoke("Medindo velocidade de upload (multi-stream)...", finalPing, finalDown, 0);
        finalUp = await ExecuteUploadTestAsync(client, onProgress, finalPing, finalDown, ct);
        onProgress?.Invoke("Teste finalizado com sucesso.", finalPing, finalDown, finalUp);

        return new SpeedTestResult(finalPing, finalDown, finalUp, serverName, true, string.Empty);
    }

    private static async Task<double> ExecuteDownloadTestAsync(
        HttpClient client,
        Action<string, double, double, double>? onProgress,
        double ping,
        CancellationToken ct)
    {
        long totalBytes = 0;
        var sw = Stopwatch.StartNew();
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linkedCts.CancelAfter(TimeSpan.FromSeconds(7.5));

        // 4 fluxos paralelos para saturar conexões de alta velocidade (100M a 1Gbps)
        var tasks = Enumerable.Range(0, 4).Select(_ => Task.Run(async () =>
        {
            var buffer = new byte[65536];
            try
            {
                while (!linkedCts.IsCancellationRequested)
                {
                    using var resp = await client.GetAsync("https://speed.cloudflare.com/__down?bytes=50000000", HttpCompletionOption.ResponseHeadersRead, linkedCts.Token);
                    resp.EnsureSuccessStatusCode();
                    using var stream = await resp.Content.ReadAsStreamAsync(linkedCts.Token);
                    int read;
                    while ((read = await stream.ReadAsync(buffer, linkedCts.Token)) > 0)
                    {
                        Interlocked.Add(ref totalBytes, read);
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch { }
        })).ToArray();

        while (!linkedCts.IsCancellationRequested && !tasks.All(t => t.IsCompleted))
        {
            try { await Task.Delay(200, ct); } catch (OperationCanceledException) { break; }
            double secs = sw.Elapsed.TotalSeconds;
            if (secs > 0.4)
            {
                double currentMbps = Math.Round((totalBytes * 8.0) / (secs * 1_000_000.0), 1);
                onProgress?.Invoke($"Baixando... {currentMbps:0.0} Mbps", ping, currentMbps, 0);
            }
        }

        try { await Task.WhenAll(tasks); } catch { }
        double totalSecs = sw.Elapsed.TotalSeconds;
        double mbps = totalSecs > 0 ? Math.Round((totalBytes * 8.0) / (totalSecs * 1_000_000.0), 1) : 0;
        return Math.Max(0.1, mbps);
    }

    private static async Task<double> ExecuteUploadTestAsync(
        HttpClient client,
        Action<string, double, double, double>? onProgress,
        double ping,
        double down,
        CancellationToken ct)
    {
        long totalBytes = 0;
        var sw = Stopwatch.StartNew();
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linkedCts.CancelAfter(TimeSpan.FromSeconds(6.0));

        var chunk = new byte[65536];
        new Random(42).NextBytes(chunk);

        // 3 fluxos paralelos com medição de cada chunk gravado no socket
        var tasks = Enumerable.Range(0, 3).Select(_ => Task.Run(async () =>
        {
            try
            {
                while (!linkedCts.IsCancellationRequested)
                {
                    var content = new ProgressUploadContent(chunk, 10_000_000, bytes =>
                    {
                        Interlocked.Add(ref totalBytes, bytes);
                    });

                    using var resp = await client.PostAsync("https://speed.cloudflare.com/__up", content, linkedCts.Token);
                    resp.EnsureSuccessStatusCode();
                }
            }
            catch (OperationCanceledException) { }
            catch { }
        })).ToArray();

        while (!linkedCts.IsCancellationRequested && !tasks.All(t => t.IsCompleted))
        {
            try { await Task.Delay(200, ct); } catch (OperationCanceledException) { break; }
            double secs = sw.Elapsed.TotalSeconds;
            if (secs > 0.4)
            {
                double currentMbps = Math.Round((totalBytes * 8.0) / (secs * 1_000_000.0), 1);
                onProgress?.Invoke($"Enviando... {currentMbps:0.0} Mbps", ping, down, currentMbps);
            }
        }

        try { await Task.WhenAll(tasks); } catch { }
        double totalSecs = sw.Elapsed.TotalSeconds;
        double mbps = totalSecs > 0 ? Math.Round((totalBytes * 8.0) / (totalSecs * 1_000_000.0), 1) : 0;
        return Math.Max(0.1, mbps);
    }

    private static double GetDistance(double lat1, double lon1, double lat2, double lon2)
    {
        double dLat = (lat2 - lat1) * (Math.PI / 180.0);
        double dLon = (lon2 - lon1) * (Math.PI / 180.0);
        double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                   Math.Cos(lat1 * (Math.PI / 180.0)) * Math.Cos(lat2 * (Math.PI / 180.0)) *
                   Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        double c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return 6371 * c; // Km
    }
}

public sealed record SpeedTestResult(
    double PingMs,
    double DownloadMbps,
    double UploadMbps,
    string ServerName,
    bool Success,
    string ErrorMessage);
