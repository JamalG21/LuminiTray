using System;
using System.Windows.Media;

namespace LuminiTray;

/// <summary>Níveis progressivos de alerta térmico.</summary>
public enum TempAlertLevel
{
    Normal = 0,
    Yellow = 1,
    Orange = 2,
    Red = 3
}

/// <summary>Avalia limites de temperatura e fornece pincéis consistentes (Amarelo, Laranja, Vermelho) para todos os visores.</summary>
public static class TemperatureAlertHelper
{
    // Cores para fundo escuro / overlay
    private static readonly SolidColorBrush YellowDarkBrush = Freeze(Color.FromRgb(0xFA, 0xCC, 0x15));
    private static readonly SolidColorBrush OrangeDarkBrush = Freeze(Color.FromRgb(0xFB, 0x92, 0x3C));
    private static readonly SolidColorBrush RedDarkBrush = Freeze(Color.FromRgb(0xEF, 0x44, 0x44));

    // Cores com contraste reforçado para tema claro
    private static readonly SolidColorBrush YellowLightBrush = Freeze(Color.FromRgb(0xCA, 0x8A, 0x04));
    private static readonly SolidColorBrush OrangeLightBrush = Freeze(Color.FromRgb(0xEA, 0x58, 0x0C));
    private static readonly SolidColorBrush RedLightBrush = Freeze(Color.FromRgb(0xDC, 0x26, 0x26));

    private static SolidColorBrush Freeze(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    public static (double YellowC, double OrangeC, double CriticalC) GetThresholds(FanControlSaveSettings? cfg)
    {
        double critical = cfg?.TempAlertCriticalC ?? 80.0;
        critical = Math.Clamp(critical, 45.0, 110.0);
        double orangeOffset = Math.Clamp(cfg?.TempAlertOrangeOffsetC ?? 7.0, 2.0, 25.0);
        double yellowOffset = Math.Clamp(cfg?.TempAlertYellowOffsetC ?? 15.0, orangeOffset + 2.0, 40.0);
        return (Math.Max(20.0, critical - yellowOffset), Math.Max(25.0, critical - orangeOffset), critical);
    }

    public static TempAlertLevel Evaluate(double? tempC, FanControlSaveSettings? cfg)
    {
        if (tempC is not double t || double.IsNaN(t) || double.IsInfinity(t) || t <= 0)
            return TempAlertLevel.Normal;
        if (cfg is { TempAlertEnabled: false })
            return TempAlertLevel.Normal;

        var (yellowC, orangeC, criticalC) = GetThresholds(cfg);
        if (t >= criticalC) return TempAlertLevel.Red;
        if (t >= orangeC) return TempAlertLevel.Orange;
        if (t >= yellowC) return TempAlertLevel.Yellow;
        return TempAlertLevel.Normal;
    }

    public static Brush GetBrush(double? tempC, FanControlSaveSettings? cfg, Brush defaultBrush, bool isDarkTheme = true)
    {
        return Evaluate(tempC, cfg) switch
        {
            TempAlertLevel.Yellow => isDarkTheme ? YellowDarkBrush : YellowLightBrush,
            TempAlertLevel.Orange => isDarkTheme ? OrangeDarkBrush : OrangeLightBrush,
            TempAlertLevel.Red => isDarkTheme ? RedDarkBrush : RedLightBrush,
            _ => defaultBrush
        };
    }

    public static Brush? GetOverlayBrush(double? tempC, FanControlSaveSettings? cfg)
    {
        return Evaluate(tempC, cfg) switch
        {
            TempAlertLevel.Yellow => YellowDarkBrush,
            TempAlertLevel.Orange => OrangeDarkBrush,
            TempAlertLevel.Red => RedDarkBrush,
            _ => null
        };
    }
}
