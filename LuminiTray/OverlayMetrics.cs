namespace LuminiTray;

/// <summary>Item que pode aparecer no overlay. Group: "FPS", "GPU", "VRAM", "CPU", "RAM" ou "Outros".</summary>
public sealed record OverlayMetric(string Key, string Group, string Label, bool Default);

/// <summary>Linha do overlay já pronta para mostrar (ex.: "GPU" · "98%  65 °C  2790 MHz").</summary>
public sealed record OverlayLine(string Label, string Value);

/// <summary>Catálogo do que o overlay pode mostrar, na ordem em que aparece.</summary>
public static class OverlayMetrics
{
    public static readonly OverlayMetric[] All =
    {
        new("fps", "FPS", "FPS", true),
        new("frametime", "FPS", "Frametime (ms)", true),
        new("low1", "FPS", "1% low", true),
        new("graph", "FPS", "Gráfico de frametime", true),

        new("gpu_load", "GPU", "Uso", true),
        new("gpu_temp", "GPU", "Temperatura", true),
        new("gpu_hotspot", "GPU", "Hotspot", false),
        new("gpu_clock", "GPU", "Clock do núcleo", true),
        new("gpu_power", "GPU", "Consumo", true),
        new("gpu_volt", "GPU", "Tensão do núcleo", false),
        new("gpu_fan", "GPU", "Ventoinha", false),
        new("gpu_perfcap", "GPU", "Limitador (NVIDIA)", false),

        new("vram_used", "VRAM", "Usada (GB)", true),
        new("vram_pct", "VRAM", "Uso (%)", false),
        new("vram_temp", "VRAM", "Temperatura", false),
        new("gpu_memclock", "VRAM", "Clock da memória", false),

        new("cpu_load", "CPU", "Uso total", true),
        new("cpu_maxcore", "CPU", "Núcleo mais ocupado", false),
        new("cpu_temp", "CPU", "Temperatura", true),
        new("cpu_clock", "CPU", "Clock médio", true),
        new("cpu_clockmax", "CPU", "Clock máximo (núcleo)", false),
        new("cpu_power", "CPU", "Consumo", true),

        new("ram_used", "RAM", "Usada (GB)", true),
        new("ram_pct", "RAM", "Uso (%)", false),
        new("ram_swap", "RAM", "Swap / Paginação de memória", true),
        new("proc_ram", "RAM", "RAM do jogo em foco", false),
        new("app_ram", "RAM", "RAM do Lumini Tray", false),

        new("game", "Outros", "Nome do jogo", true),
        new("clock", "Outros", "Hora", false),
    };

    public static HashSet<string> Defaults() => All.Where(m => m.Default).Select(m => m.Key).ToHashSet();

    /// <summary>Formata temperatura em °C, °F ou K.</summary>
    public static string FormatTemperature(float? celsius, string? unit)
    {
        string u = (unit ?? "C").Trim().ToUpperInvariant();
        if (celsius is not float c) return u switch { "F" => "— °F", "K" => "— K", _ => "— °C" };
        return u switch
        {
            "F" => $"{c * 9.0 / 5.0 + 32.0:0.0} °F",
            "K" => $"{c + 273.15:0.0} K",
            _ => $"{c:0.0} °C"
        };
    }

    /// <summary>Formata clock em MHz ou GHz (respeitando padrão do componente quando unit == "Auto").</summary>
    public static string FormatClock(double? mhz, string? unit, bool defaultGHz = false)
    {
        bool useGHz = string.Equals(unit, "GHz", StringComparison.OrdinalIgnoreCase)
            || (!string.Equals(unit, "MHz", StringComparison.OrdinalIgnoreCase) && defaultGHz);

        if (mhz is not double v || v <= 0)
            return useGHz ? "— GHz" : "— MHz";

        return useGHz ? $"{v / 1000.0:0.00} GHz" : $"{v:0} MHz";
    }
}
