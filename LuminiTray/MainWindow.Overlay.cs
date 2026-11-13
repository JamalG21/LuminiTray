using System.Windows;
using System.Windows.Controls;
using LibreHardwareMonitor.Hardware;

namespace LuminiTray;

/// <summary>Overlay: o que mostrar (escolhido na aba Overlay) e a montagem das linhas de sensores.</summary>
public partial class MainWindow
{
    private HashSet<string>? _ovShow;
    private HashSet<string> OvShow => _ovShow ??= _settings.OverlayItems is { } l ? l.ToHashSet() : OverlayMetrics.Defaults();

    /// <summary>Cria as caixas de seleção da aba Overlay, agrupadas por componente.</summary>
    private void BuildOverlayPicker()
    {
        OvItemsPanel.Children.Clear();
        foreach (var group in OverlayMetrics.All.GroupBy(m => m.Group))
        {
            OvItemsPanel.Children.Add(new TextBlock
            {
                Text = group.Key.ToUpperInvariant(), Style = (Style)FindResource("Label"), Margin = new Thickness(0, 10, 0, 4),
            });
            var wrap = new WrapPanel();
            foreach (var m in group)
            {
                var cb = new CheckBox { Content = m.Label, Tag = m.Key, IsChecked = OvShow.Contains(m.Key), Margin = new Thickness(0, 0, 18, 6) };
                cb.Click += (_, _) =>
                {
                    if (cb.IsChecked == true) OvShow.Add(m.Key); else OvShow.Remove(m.Key);
                    SaveOverlayItems();
                };
                wrap.Children.Add(cb);
            }
            OvItemsPanel.Children.Add(wrap);
        }
    }

    private void SaveOverlayItems()
    {
        _settings.OverlayItems = OverlayMetrics.All.Select(m => m.Key).Where(OvShow.Contains).ToList();
        _settings.Save();
        UpdateOverlay();
    }

    private void OvPreset_Click(object sender, RoutedEventArgs e)
    {
        var keys = (string)((FrameworkElement)sender).Tag switch
        {
            "all" => OverlayMetrics.All.Select(m => m.Key).ToHashSet(),
            "min" => new HashSet<string> { "fps", "frametime", "low1" },
            _ => OverlayMetrics.Defaults(),
        };
        _ovShow = keys;
        SaveOverlayItems();
        BuildOverlayPicker();
    }

    private void InitOverlayOutlineUi()
    {
        if (ChkOverlayTextOutline == null) return;
        ChkOverlayTextOutline.IsChecked = _settings.OverlayTextOutlineEnabled;
        ChkOverlayTextOutlineAutoOnly.IsChecked = _settings.OverlayTextOutlineAutoOnly;

        SliderOverlayOutlineThickness.Value = _settings.OverlayTextOutlineThickness;
        TxtOverlayOutlineThicknessVal.Text = $"{_settings.OverlayTextOutlineThickness:F1} px";

        SliderOverlayOutlineOpacity.Value = Math.Round(_settings.OverlayTextOutlineOpacity * 100);
        TxtOverlayOutlineOpacityVal.Text = $"{Math.Round(_settings.OverlayTextOutlineOpacity * 100):F0}%";
    }

    private void OverlayOutlineOption_Changed(object sender, RoutedEventArgs e)
    {
        if (!_overlayReady || ChkOverlayTextOutline == null) return;
        _settings.OverlayTextOutlineEnabled = ChkOverlayTextOutline.IsChecked == true;
        _settings.OverlayTextOutlineAutoOnly = ChkOverlayTextOutlineAutoOnly.IsChecked == true;
        _settings.Save();
        SyncOverlayOutline();
    }

    private void SliderOverlayOutline_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_overlayReady || SliderOverlayOutlineThickness == null || SliderOverlayOutlineOpacity == null) return;
        double thick = Math.Round(SliderOverlayOutlineThickness.Value, 1);
        double op = Math.Round(SliderOverlayOutlineOpacity.Value) / 100.0;

        _settings.OverlayTextOutlineThickness = thick;
        _settings.OverlayTextOutlineOpacity = op;
        _settings.Save();

        if (TxtOverlayOutlineThicknessVal != null) TxtOverlayOutlineThicknessVal.Text = $"{thick:F1} px";
        if (TxtOverlayOutlineOpacityVal != null) TxtOverlayOutlineOpacityVal.Text = $"{Math.Round(op * 100):F0}%";

        SyncOverlayOutline();
    }

    private void SyncOverlayOutline()
    {
        _overlay?.UpdateTextOutline(
            _settings.OverlayTextOutlineEnabled,
            _settings.OverlayTextOutlineThickness,
            _settings.OverlayTextOutlineOpacity,
            _settings.OverlayTextOutlineAutoOnly);

        if (MiniTaskManagerWindow.IsWindowVisible)
        {
            MiniTaskManagerWindow.Instance.UpdateTextOutline(
                _settings.OverlayTextOutlineEnabled,
                _settings.OverlayTextOutlineThickness,
                _settings.OverlayTextOutlineOpacity,
                _settings.OverlayTextOutlineAutoOnly);
        }
    }

    // ------------------------------------------------------------------ leitura dos sensores

    private SensorRow? GpuRow(SensorType type, string name, bool startsWith = false) =>
        _gpus.Count == 0 ? null : _monitor.Rows.FirstOrDefault(r => r.Hardware == _gpus[0].Name && r.Type == type &&
                                                                   (startsWith ? r.Name.StartsWith(name) : r.Name == name) && r.RawValue != null);

    private uint? MainGpuClock(int nvmlType, string lhmName)
    {
        if (_gpus.Count > 0 && _gpus[0].NvmlHandle != IntPtr.Zero && Nvml.Clock(_gpus[0].NvmlHandle, nvmlType) is uint c) return c;
        return GpuRow(SensorType.Clock, lhmName)?.RawValue is float v && v > 0 ? (uint)v : null;
    }

    /// <summary>VRAM usada e total em GB: NVML quando disponível, senão LibreHardwareMonitor (MB).</summary>
    private (double Used, double Total)? MainGpuVram()
    {
        if (_gpus.Count == 0) return null;
        if (_gpus[0].NvmlHandle != IntPtr.Zero && Nvml.MemoryInfo(_gpus[0].NvmlHandle) is Nvml.Memory m && m.Total > 0)
            return (m.Used / 1073741824.0, m.Total / 1073741824.0);
        if (GpuRow(SensorType.SmallData, "GPU Memory Used")?.RawValue is float u && GpuRow(SensorType.SmallData, "GPU Memory Total")?.RawValue is float t && t > 0)
            return (u / 1024.0, t / 1024.0);
        return null;
    }

    private (float? Avg, float? Max) CpuClocks()
    {
        var cores = _monitor.Rows.Where(r => r.HardwareType == HardwareType.Cpu && r.Type == SensorType.Clock &&
                                             r.Name.StartsWith("Core #") && !r.Name.Contains("Effective") && r.RawValue > 0)
                                 .Select(r => r.RawValue!.Value).ToList();
        return cores.Count > 0 ? (cores.Average(), cores.Max()) : (null, null);
    }

    /// <summary>Monta as linhas GPU / VRAM / CPU / RAM só com o que foi escolhido; linha vazia não aparece.</summary>
    private List<OverlayLine> BuildOverlayLines(FrameStats? f)
    {
        var show = OvShow;
        var s = BottleneckAnalyzer.Sample(_monitor, _gpus.FirstOrDefault(), f);
        var lines = new List<OverlayLine>();
        string T(float? v) => v is float x ? $"{x:0.0} °C" : "—";
        string P(double? v) => v is double x ? $"{x:0}%" : "—";
        void Line(string label, List<string> parts) { if (parts.Count > 0) lines.Add(new OverlayLine(label, string.Join("  ", parts))); }

        var gpu = new List<string>();
        if (show.Contains("gpu_load")) gpu.Add(P(s.GpuLoad));
        if (show.Contains("gpu_temp")) gpu.Add(T(s.GpuTemp));
        if (show.Contains("gpu_hotspot")) gpu.Add("HS " + T(GpuRow(SensorType.Temperature, "GPU Hot Spot")?.RawValue));
        if (show.Contains("gpu_clock")) gpu.Add(MainGpuClock(Nvml.ClockGraphics, "GPU Core") is uint c ? $"{c} MHz" : "— MHz");
        if (show.Contains("gpu_memclock")) gpu.Add(MainGpuClock(Nvml.ClockMem, "GPU Memory") is uint mc ? $"mem {mc} MHz" : "mem —");
        if (show.Contains("gpu_power")) gpu.Add(MainGpuPower() is float w ? $"{w:0} W" : "— W");
        if (show.Contains("gpu_volt")) gpu.Add(GpuRow(SensorType.Voltage, "GPU Core")?.RawValue is float v ? $"{v:0.000} V" : "— V");
        if (show.Contains("gpu_fan"))
            gpu.Add(GpuRow(SensorType.Fan, "GPU Fan", true)?.RawValue is float rpm && rpm > 0 ? $"{rpm:0} RPM"
                  : GpuRow(SensorType.Control, "GPU Fan", true)?.RawValue is float pct ? $"fan {pct:0}%" : "fan —");
        if (show.Contains("gpu_perfcap") && MainGpuPerfCap() is { Length: > 0 } cap) gpu.Add(cap);
        Line("GPU", gpu);

        var vram = new List<string>();
        var vr = MainGpuVram();
        if (show.Contains("vram_used")) vram.Add(vr is { } a ? $"{a.Used:0.0} GB" : "—");
        if (show.Contains("vram_pct")) vram.Add(P(s.VramPct));
        if (show.Contains("vram_temp")) vram.Add(T(GpuRow(SensorType.Temperature, "GPU Memory")?.RawValue));
        Line("VRAM", vram);

        var cpu = new List<string>();
        var (avg, max) = CpuClocks();
        if (show.Contains("cpu_load")) cpu.Add(P(s.CpuTotal));
        if (show.Contains("cpu_maxcore")) cpu.Add(s.CpuMaxCore is float mx ? $"núcleo {mx:0}%" : "núcleo —");
        if (show.Contains("cpu_temp")) cpu.Add(T(s.CpuTemp));
        if (show.Contains("cpu_clock")) cpu.Add(avg is float ca ? $"{ca / 1000:0.00} GHz" : "— GHz");
        if (show.Contains("cpu_clockmax")) cpu.Add(max is float cm ? $"pico {cm / 1000:0.00} GHz" : "pico —");
        if (show.Contains("cpu_power")) cpu.Add(_monitor.CpuPower is float cw && cw > 0 ? $"{cw:0} W" : "— W"); // 0 = sem leitura (precisa de administrador)
        Line("CPU", cpu);

        var ram = new List<string>();
        var (total, availB) = Optimizer.MemoryStatus();
        if (show.Contains("ram_used")) ram.Add(total > 0 ? $"{(total - availB) / 1073741824.0:0.0} GB" : "—");
        if (show.Contains("ram_pct")) ram.Add(P(s.RamPct));
        if (show.Contains("ram_swap"))
        {
            var (swapUsed, _) = Optimizer.GetSwapUsage();
            ram.Add(swapUsed > 0 ? $"swap {FormatMemory(swapUsed)}" : "swap 0 MB");
        }
        if (show.Contains("proc_ram"))
            ram.Add(f != null && ProcessMemoryText(f.ProcessId) is { } pm ? $"jogo {pm}" : "jogo —");
        if (show.Contains("app_ram"))
            ram.Add($"app {FormatMemory(GetTotalLuminiAppWorkingSet())}");
        Line("RAM", ram);
        return lines;
    }

    /// <summary>Formata bytes como "512 MB" ou "3.4 GB".</summary>
    internal static string FormatMemory(long bytes) =>
        bytes >= 1L << 30 ? $"{bytes / 1073741824.0:0.0} GB" : $"{bytes / 1048576.0:0} MB";

    /// <summary>Memória (working set) do processo do jogo, ou null se não puder ser lida.</summary>
        /// <summary>
    /// Retorna a soma do WorkingSet (memória física) de todos os processos do ecossistema Lumini Tray
    /// (processo principal + player de papel de parede animado + subtarefas ativas).
    /// </summary>
    internal static long GetTotalLuminiAppWorkingSet()
    {
        long total = 0;
        try
        {
            total = ProcessTree.GetTreeWorkingSet(Environment.ProcessId);
        }
        catch
        {
            total = Environment.WorkingSet;
        }

        // Consolida memória do player de papel de parede caso executado fora da árvore direta
        try
        {
            long wallpaperWs = LivelyWallpaperService.Instance.GetTotalProcessesWorkingSet();
            if (wallpaperWs > 0)
            {
                var descendants = ProcessTree.GetDescendantPids(Environment.ProcessId);
                if (!descendants.Any(pid => LivelyWallpaperService.Instance.ContainsProcess(pid)))
                {
                    total += wallpaperWs;
                }
            }
        }
        catch { }

        // Consolida memória das subtarefas ativas (caso Toolhelp32Snapshot tenha falhado)
        try
        {
            long subtasksWs = SubtaskProcessManager.GetSubtasksWorkingSet();
            if (subtasksWs > 0)
            {
                var descendants = ProcessTree.GetDescendantPids(Environment.ProcessId);
                if (descendants.Count == 0)
                {
                    total += subtasksWs;
                }
            }
        }
        catch { }

        return Math.Max(total, Environment.WorkingSet);
    }

    private static string? ProcessMemoryText(int pid)
    {
        try
        {
            using var p = System.Diagnostics.Process.GetProcessById(pid);
            return FormatMemory(p.WorkingSet64);
        }
        catch { return null; }
    }
}
