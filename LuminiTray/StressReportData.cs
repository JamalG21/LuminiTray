namespace LuminiTray;

/// <summary>
/// Modelo de dados consolidado com especificações de hardware (CPU, GPU, RAM)
/// e resultados de temperatura e estabilidade do teste de estresse.
/// </summary>
public sealed class StressReportData
{
    public DateTime TestDate { get; set; } = DateTime.Now;
    public TimeSpan Duration { get; set; }
    public string StatusText { get; set; } = "Concluído com Sucesso";
    public bool WasAborted { get; set; }
    public string AbortReason { get; set; } = "";

    // Hardware - CPU
    public string CpuName { get; set; } = "";
    public string CpuCoresThreads { get; set; } = "";
    public float CpuMaxTemp { get; set; }
    public float CpuTempLimit { get; set; }
    public float CpuMaxPowerWatts { get; set; }
    public float CpuAvgLoad { get; set; } = 100;

    // Hardware - GPU
    public string GpuName { get; set; } = "";
    public string GpuVram { get; set; } = "";
    public float GpuMaxTemp { get; set; }
    public float GpuTempLimit { get; set; }
    public float GpuMaxPowerWatts { get; set; }
    public double GpuAvgFps { get; set; }
    public long GpuTotalFrames { get; set; }

    // Hardware - RAM
    public string RamTotalText { get; set; } = "";
    public string RamSpeedType { get; set; } = "";
    public long RamPasses { get; set; }
    public long RamErrors { get; set; }

    // Logs recentes
    public List<string> RecentLogs { get; set; } = new();
}
