using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;

namespace LuminiTray;

public sealed class DnsBenchmarkItem : INotifyPropertyChanged
{
    private int _rank;
    private double _responseTimeMs = 9999;
    private string _responseDisplay = "—";
    private bool _isFastest;
    private bool _isCurrent;
    private bool _isTesting;
    private bool _hasFailed;
    private string _statusText = "Aguardando teste";
    private Brush _statusBrush = new SolidColorBrush(Color.FromArgb(160, 160, 160, 160));
    private Brush _rankBadgeBackground = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255));
    private double _speedBarRatio = 0.0;

    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Primary { get; set; } = string.Empty;
    public string Secondary { get; set; } = string.Empty;
    public bool IsIspDefault { get; set; }

    public int Rank
    {
        get => _rank;
        set { _rank = value; OnPropertyChanged(); }
    }

    public double ResponseTimeMs
    {
        get => _responseTimeMs;
        set
        {
            _responseTimeMs = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ResponseDisplay));
        }
    }

    public string ResponseDisplay
    {
        get => _responseDisplay;
        set { _responseDisplay = value; OnPropertyChanged(); }
    }

    public bool IsFastest
    {
        get => _isFastest;
        set { _isFastest = value; OnPropertyChanged(); }
    }

    public bool IsCurrent
    {
        get => _isCurrent;
        set { _isCurrent = value; OnPropertyChanged(); }
    }

    public bool IsTesting
    {
        get => _isTesting;
        set { _isTesting = value; OnPropertyChanged(); }
    }

    public bool HasFailed
    {
        get => _hasFailed;
        set { _hasFailed = value; OnPropertyChanged(); }
    }

    public string StatusText
    {
        get => _statusText;
        set { _statusText = value; OnPropertyChanged(); }
    }

    public Brush StatusBrush
    {
        get => _statusBrush;
        set { _statusBrush = value; OnPropertyChanged(); }
    }

    public Brush RankBadgeBackground
    {
        get => _rankBadgeBackground;
        set { _rankBadgeBackground = value; OnPropertyChanged(); }
    }

    public double SpeedBarRatio
    {
        get => _speedBarRatio;
        set { _speedBarRatio = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class NetworkDiagnosticsViewModel : INotifyPropertyChanged, IDisposable
{
    private static readonly string[] TestDomains = { "google.com", "microsoft.com", "netflix.com" };

    // SpeedTest
    private double _pingMs = 0;
    private double _downloadMbps = 0;
    private double _uploadMbps = 0;
    private string _speedTestStatus = "Pronto para iniciar diagnóstico de velocidade";
    private string _speedTestServer = "Servidor mais próximo via CDN Anycast";
    private bool _isTestingSpeed = false;
    private double _speedProgressFrac = 0.0;
    private CancellationTokenSource? _speedTestCts;

    // Ping Contínuo
    private readonly ContinuousPingMonitor _pingMonitor = new();
    private string _pingTargetHost = "8.8.8.8";
    private bool _isPingRunning = false;
    private double _currentPingMs = 0;
    private double _minPingMs = 0;
    private double _avgPingMs = 0;
    private double _maxPingMs = 0;
    private double _jitterMs = 0;
    private double _packetLossPercent = 0;
    private int _totalPacketsSent = 0;
    private int _totalPacketsLost = 0;

    // DNS Jumper / Benchmark
    private bool _isDnsBenchmarking = false;
    private string _dnsStatusMessage = "Clique em 'Testar Todos os DNS' para medir a latência em tempo real";
    private string _activeAdapterInfo = "Adaptador de Rede Ativo: Identificando...";
    private string _activeDnsInfo = "DNS Atual: DHCP (Automático)";
    private DnsBenchmarkItem? _bestDnsItem;
    private DnsBenchmarkItem? _selectedDnsItem;
    private CancellationTokenSource? _dnsCts;

    public ObservableCollection<DnsBenchmarkItem> DnsPresets { get; } = new();
    public ObservableCollection<PingSample> PingHistory { get; } = new();

    public event Action? OnPingSampleReceived;

    public NetworkDiagnosticsViewModel()
    {
        _pingMonitor.SampleAdded += OnPingSampleAdded;
        RefreshActiveDnsInfo();
        InitializeDnsList();
    }

    #region Properties - SpeedTest

    public double PingMs
    {
        get => _pingMs;
        set { _pingMs = value; OnPropertyChanged(); }
    }

    public double DownloadMbps
    {
        get => _downloadMbps;
        set { _downloadMbps = value; OnPropertyChanged(); }
    }

    public double UploadMbps
    {
        get => _uploadMbps;
        set { _uploadMbps = value; OnPropertyChanged(); }
    }

    public string SpeedTestStatus
    {
        get => _speedTestStatus;
        set { _speedTestStatus = value; OnPropertyChanged(); }
    }

    public string SpeedTestServer
    {
        get => _speedTestServer;
        set { _speedTestServer = value; OnPropertyChanged(); }
    }

    public bool IsTestingSpeed
    {
        get => _isTestingSpeed;
        set
        {
            _isTestingSpeed = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanStartSpeedTest));
        }
    }

    public bool CanStartSpeedTest => !IsTestingSpeed;

    public double SpeedProgressFrac
    {
        get => _speedProgressFrac;
        set { _speedProgressFrac = value; OnPropertyChanged(); }
    }

    #endregion

    #region Properties - Ping Contínuo

    public string PingTargetHost
    {
        get => _pingTargetHost;
        set { _pingTargetHost = value; OnPropertyChanged(); }
    }

    public bool IsPingRunning
    {
        get => _isPingRunning;
        set { _isPingRunning = value; OnPropertyChanged(); }
    }

    public double CurrentPingMs
    {
        get => _currentPingMs;
        set { _currentPingMs = value; OnPropertyChanged(); }
    }

    public double MinPingMs
    {
        get => _minPingMs;
        set { _minPingMs = value; OnPropertyChanged(); }
    }

    public double AvgPingMs
    {
        get => _avgPingMs;
        set { _avgPingMs = value; OnPropertyChanged(); }
    }

    public double MaxPingMs
    {
        get => _maxPingMs;
        set { _maxPingMs = value; OnPropertyChanged(); }
    }

    public double JitterMs
    {
        get => _jitterMs;
        set { _jitterMs = value; OnPropertyChanged(); }
    }

    public double PacketLossPercent
    {
        get => _packetLossPercent;
        set { _packetLossPercent = value; OnPropertyChanged(); }
    }

    public int TotalPacketsSent
    {
        get => _totalPacketsSent;
        set { _totalPacketsSent = value; OnPropertyChanged(); }
    }

    public int TotalPacketsLost
    {
        get => _totalPacketsLost;
        set { _totalPacketsLost = value; OnPropertyChanged(); }
    }

    #endregion

    #region Properties - DNS Jumper

    public bool IsDnsBenchmarking
    {
        get => _isDnsBenchmarking;
        set
        {
            _isDnsBenchmarking = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanBenchmarkDns));
        }
    }

    public bool CanBenchmarkDns => !IsDnsBenchmarking;

    public string DnsStatusMessage
    {
        get => _dnsStatusMessage;
        set { _dnsStatusMessage = value; OnPropertyChanged(); }
    }

    public string ActiveAdapterInfo
    {
        get => _activeAdapterInfo;
        set { _activeAdapterInfo = value; OnPropertyChanged(); }
    }

    public string ActiveDnsInfo
    {
        get => _activeDnsInfo;
        set { _activeDnsInfo = value; OnPropertyChanged(); }
    }

    public DnsBenchmarkItem? BestDnsItem
    {
        get => _bestDnsItem;
        set { _bestDnsItem = value; OnPropertyChanged(); }
    }

    public DnsBenchmarkItem? SelectedDnsItem
    {
        get => _selectedDnsItem;
        set { _selectedDnsItem = value; OnPropertyChanged(); }
    }

    #endregion

    #region SpeedTest Methods

    public async Task RunSpeedTestAsync()
    {
        if (IsTestingSpeed) return;

        IsTestingSpeed = true;
        SpeedProgressFrac = 0.05;
        SpeedTestStatus = "Inicializando motores de teste...";
        _speedTestCts = new CancellationTokenSource();

        try
        {
            var res = await NetworkDiagnosticsService.RunSpeedTestAsync((status, ping, down, up) =>
            {
                App.Current?.Dispatcher.BeginInvoke(() =>
                {
                    SpeedTestStatus = status;
                    if (ping > 0) PingMs = ping;
                    if (down > 0) DownloadMbps = down;
                    if (up > 0) UploadMbps = up;

                    if (up > 0) SpeedProgressFrac = 0.95;
                    else if (down > 0) SpeedProgressFrac = 0.60;
                    else if (ping > 0) SpeedProgressFrac = 0.25;
                });
            }, _speedTestCts.Token);

            PingMs = res.PingMs;
            DownloadMbps = res.DownloadMbps;
            UploadMbps = res.UploadMbps;
            SpeedTestServer = res.ServerName;
            SpeedTestStatus = $"Teste finalizado com sucesso · {DateTime.Now:HH:mm:ss}";
            SpeedProgressFrac = 1.0;
        }
        catch (OperationCanceledException)
        {
            SpeedTestStatus = "Teste de velocidade cancelado.";
            SpeedProgressFrac = 0;
        }
        catch (Exception ex)
        {
            SpeedTestStatus = $"Falha no teste: {ex.Message}";
            SpeedProgressFrac = 0;
        }
        finally
        {
            IsTestingSpeed = false;
            _speedTestCts?.Dispose();
            _speedTestCts = null;
        }
    }

    public void CancelSpeedTest()
    {
        _speedTestCts?.Cancel();
    }

    #endregion

    #region Ping Contínuo Methods

    public void ToggleContinuousPing()
    {
        if (IsPingRunning)
        {
            StopContinuousPing();
        }
        else
        {
            StartContinuousPing(PingTargetHost);
        }
    }

    public void StartContinuousPing(string? host = null)
    {
        string target = string.IsNullOrWhiteSpace(host) ? "8.8.8.8" : host.Trim();
        PingTargetHost = target;
        TotalPacketsSent = 0;
        TotalPacketsLost = 0;
        PingHistory.Clear();

        _pingMonitor.Start(target);
        IsPingRunning = true;
    }

    public void StopContinuousPing()
    {
        _pingMonitor.Stop();
        IsPingRunning = false;
    }

    private void OnPingSampleAdded(PingSample sample, IReadOnlyList<PingSample> allSamples)
    {
        App.Current?.Dispatcher.BeginInvoke(() =>
        {
            TotalPacketsSent++;
            if (!sample.Success) TotalPacketsLost++;

            PingHistory.Add(sample);
            if (PingHistory.Count > 60)
            {
                PingHistory.RemoveAt(0);
            }

            CurrentPingMs = sample.Success ? Math.Round(sample.LatencyMs, 1) : 0;

            var validSamples = allSamples.Where(s => s.Success).Select(s => s.LatencyMs).ToList();
            if (validSamples.Count > 0)
            {
                MinPingMs = Math.Round(validSamples.Min(), 1);
                MaxPingMs = Math.Round(validSamples.Max(), 1);
                AvgPingMs = Math.Round(validSamples.Average(), 1);

                // Cálculo de Jitter (média das diferenças sucessivas)
                if (validSamples.Count > 1)
                {
                    double sumDiff = 0;
                    for (int i = 1; i < validSamples.Count; i++)
                    {
                        sumDiff += Math.Abs(validSamples[i] - validSamples[i - 1]);
                    }
                    JitterMs = Math.Round(sumDiff / (validSamples.Count - 1), 1);
                }
                else
                {
                    JitterMs = 0;
                }
            }

            int total = allSamples.Count;
            int lost = allSamples.Count(s => !s.Success);
            PacketLossPercent = total > 0 ? Math.Round((double)lost / total * 100.0, 1) : 0;

            OnPingSampleReceived?.Invoke();
        });
    }

    #endregion

    #region DNS Jumper Methods

    public void RefreshActiveDnsInfo()
    {
        var (adapterName, typeName, primary, secondary) = NetworkDiagnosticsService.GetCurrentAdapterDns();
        ActiveAdapterInfo = $"{typeName}: {adapterName}";
        if (string.IsNullOrWhiteSpace(secondary))
        {
            ActiveDnsInfo = $"DNS: {primary}";
        }
        else
        {
            ActiveDnsInfo = $"DNS: {primary} / {secondary}";
        }

        // Atualiza IsCurrent nos itens
        foreach (var item in DnsPresets)
        {
            bool match = string.Equals(item.Primary, primary, StringComparison.OrdinalIgnoreCase);
            item.IsCurrent = match;
        }
    }

    private void InitializeDnsList()
    {
        DnsPresets.Clear();

        var (_, _, primary, secondary) = NetworkDiagnosticsService.GetCurrentAdapterDns();

        // 1. Preset do ISP / Atual
        DnsPresets.Add(new DnsBenchmarkItem
        {
            Name = "DNS Atual da Conexão",
            Description = "Configuração em uso no adaptador de rede (Provedor/DHCP)",
            Primary = primary,
            Secondary = secondary,
            IsIspDefault = true,
            IsCurrent = true,
            StatusText = "Aguardando teste",
            StatusBrush = new SolidColorBrush(Color.FromArgb(160, 160, 160, 160))
        });

        // 2. Presets Globais
        foreach (var p in NetworkDiagnosticsService.GlobalDnsPresets)
        {
            bool match = string.Equals(p.Primary, primary, StringComparison.OrdinalIgnoreCase);
            DnsPresets.Add(new DnsBenchmarkItem
            {
                Name = p.Name,
                Description = p.Description,
                Primary = p.Primary,
                Secondary = p.Secondary,
                IsIspDefault = false,
                IsCurrent = match,
                StatusText = "Aguardando teste",
                StatusBrush = new SolidColorBrush(Color.FromArgb(160, 160, 160, 160))
            });
        }
    }

    public async Task RunDnsBenchmarkAsync()
    {
        if (IsDnsBenchmarking) return;

        IsDnsBenchmarking = true;
        DnsStatusMessage = "Testando servidores DNS em paralelo (consultando google.com, microsoft.com, netflix.com)...";
        _dnsCts = new CancellationTokenSource();
        var ct = _dnsCts.Token;

        foreach (var item in DnsPresets)
        {
            item.IsTesting = true;
            item.HasFailed = false;
            item.IsFastest = false;
            item.StatusText = "Testando...";
            item.StatusBrush = new SolidColorBrush(Color.FromRgb(0x38, 0xBD, 0xF8)); // Sky blue
        }

        try
        {
            // Executa todos os testes de DNS simultaneamente com Task.WhenAll
            var benchmarkTasks = DnsPresets.Select(async item =>
            {
                if (ct.IsCancellationRequested) return;

                string targetIp = item.Primary;
                if (targetIp == "Automático (DHCP)" || string.IsNullOrWhiteSpace(targetIp))
                {
                    // Se for DHCP e não tem IP estático gravado, usa o gateway ou tenta resolver
                    item.ResponseTimeMs = 9999;
                    item.ResponseDisplay = "DHCP";
                    item.HasFailed = true;
                    item.StatusText = "Padrão DHCP";
                    item.StatusBrush = new SolidColorBrush(Color.FromArgb(140, 140, 140, 140));
                    item.IsTesting = false;
                    return;
                }

                var (avgMs, success, error) = await NetworkDiagnosticsService.BenchmarkSingleDnsAsync(targetIp, TestDomains, ct);

                App.Current?.Dispatcher.BeginInvoke(() =>
                {
                    item.IsTesting = false;
                    if (success && avgMs < 9000)
                    {
                        item.ResponseTimeMs = Math.Round(avgMs, 1);
                        item.ResponseDisplay = $"{item.ResponseTimeMs:0.0} ms";
                        item.HasFailed = false;

                        if (item.ResponseTimeMs <= 25)
                        {
                            item.StatusText = "Ultra Rápido";
                            item.StatusBrush = new SolidColorBrush(Color.FromRgb(0x10, 0xB9, 0x81)); // Verde neon
                        }
                        else if (item.ResponseTimeMs <= 50)
                        {
                            item.StatusText = "Excelente";
                            item.StatusBrush = new SolidColorBrush(Color.FromRgb(0x34, 0xD3, 0x99)); // Verde claro
                        }
                        else if (item.ResponseTimeMs <= 80)
                        {
                            item.StatusText = "Bom";
                            item.StatusBrush = new SolidColorBrush(Color.FromRgb(0xFA, 0xCC, 0x15)); // Amarelo
                        }
                        else
                        {
                            item.StatusText = "Moderado";
                            item.StatusBrush = new SolidColorBrush(Color.FromRgb(0xFB, 0x92, 0x3C)); // Laranja
                        }
                    }
                    else
                    {
                        item.ResponseTimeMs = 9999;
                        item.ResponseDisplay = "Sem resposta";
                        item.HasFailed = true;
                        item.StatusText = "Falha / Timeout";
                        item.StatusBrush = new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44)); // Vermelho
                    }
                });
            }).ToArray();

            await Task.WhenAll(benchmarkTasks);

            // Reordena a lista pelo tempo de resposta (menor para maior)
            var sorted = DnsPresets
                .OrderBy(d => d.HasFailed ? 1 : 0)
                .ThenBy(d => d.ResponseTimeMs)
                .ToList();

            double fastestMs = sorted.FirstOrDefault(s => !s.HasFailed)?.ResponseTimeMs ?? 10.0;
            double slowestMs = sorted.Where(s => !s.HasFailed).Select(s => s.ResponseTimeMs).DefaultIfEmpty(100.0).Max();

            DnsPresets.Clear();
            int rank = 1;
            foreach (var s in sorted)
            {
                s.Rank = rank++;
                if (s.Rank == 1 && !s.HasFailed)
                {
                    s.IsFastest = true;
                    s.RankBadgeBackground = new SolidColorBrush(Color.FromRgb(0x10, 0xB9, 0x81));
                    BestDnsItem = s;
                }
                else if (s.Rank == 2 && !s.HasFailed)
                {
                    s.RankBadgeBackground = new SolidColorBrush(Color.FromRgb(0x06, 0xB6, 0xD4));
                }
                else
                {
                    s.RankBadgeBackground = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255));
                }

                // Cálculo visual da barra de velocidade relativa
                if (!s.HasFailed && slowestMs > 0)
                {
                    // Mais rápido ganha barra cheia (1.0), mais lento ganha menor fração
                    s.SpeedBarRatio = Math.Clamp(1.0 - ((s.ResponseTimeMs - fastestMs) / Math.Max(1.0, slowestMs - fastestMs) * 0.7), 0.25, 1.0);
                }
                else
                {
                    s.SpeedBarRatio = 0.05;
                }

                DnsPresets.Add(s);
            }

            if (BestDnsItem != null)
            {
                DnsStatusMessage = $"Benchmark concluído! Servidor mais rápido: {BestDnsItem.Name} ({BestDnsItem.ResponseDisplay})";
            }
            else
            {
                DnsStatusMessage = "Benchmark concluído.";
            }
        }
        catch (OperationCanceledException)
        {
            DnsStatusMessage = "Benchmark cancelado pelo usuário.";
        }
        catch (Exception ex)
        {
            DnsStatusMessage = $"Erro durante o benchmark: {ex.Message}";
        }
        finally
        {
            IsDnsBenchmarking = false;
            _dnsCts?.Dispose();
            _dnsCts = null;
        }
    }

    public (bool Success, string Message) ApplySelectedDns(DnsBenchmarkItem item)
    {
        if (item.IsIspDefault && item.Primary == "Automático (DHCP)")
        {
            return RestoreDhcpDns();
        }

        bool ok = NetworkDiagnosticsService.ApplyDnsWmi(item.Primary, item.Secondary, out string err);
        if (ok)
        {
            RefreshActiveDnsInfo();
            return (true, $"DNS {item.Name} ({item.Primary}) aplicado com sucesso!");
        }

        return (false, $"Não foi possível aplicar o DNS: {err}");
    }

    public (bool Success, string Message) ApplyBestDns()
    {
        if (BestDnsItem == null)
        {
            var fastest = DnsPresets.FirstOrDefault(d => !d.HasFailed && d.ResponseTimeMs < 9000);
            if (fastest != null)
            {
                BestDnsItem = fastest;
            }
            else
            {
                return (false, "Execute o benchmark de DNS primeiro para identificar o melhor servidor.");
            }
        }

        return ApplySelectedDns(BestDnsItem);
    }

    public (bool Success, string Message) RestoreDhcpDns()
    {
        bool ok = NetworkDiagnosticsService.ApplyDnsWmi(null, null, out string err);
        if (ok)
        {
            RefreshActiveDnsInfo();
            return (true, "DNS restaurado para o padrão do Provedor de Internet (DHCP)!");
        }

        return (false, $"Erro ao restaurar DHCP: {err}");
    }

    #endregion

    public void Dispose()
    {
        _pingMonitor.SampleAdded -= OnPingSampleAdded;
        _pingMonitor.Dispose();
        _speedTestCts?.Cancel();
        _speedTestCts?.Dispose();
        _dnsCts?.Cancel();
        _dnsCts?.Dispose();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
