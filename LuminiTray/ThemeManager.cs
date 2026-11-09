using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace LuminiTray;

/// <summary>
/// Troca entre modo escuro e claro substituindo os pincéis da paleta (Theme.xaml). O XAML os referencia com DynamicResource,
/// então a troca vale na hora, sem recarregar janelas. Suporta temas de destaque e cores personalizadas.
/// </summary>
public static class ThemeManager
{
    public static bool IsLight { get; private set; }
    public static string CurrentAccentTheme { get; private set; } = "Monochrome";
    public static event Action? ThemeChanged;

    public static readonly Dictionary<string, (string Hex, string HexHover, string Name, string Description)> AccentThemes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Monochrome"] = ("#F5F5F5", "#FFFFFF", "Monocromático Clássico", "Estilo original minimalista (Preto e Branco)"),
        ["CyberBlue"] = ("#00D2FF", "#33DDFF", "Azul Cyberpunk", "Ciano esportivo de alto contraste"),
        ["Emerald"] = ("#10B981", "#34D399", "Verde Esmeralda", "Visual limpo inspirado em setups Razer/Matrix"),
        ["NeonPurple"] = ("#8B5CF6", "#A78BFA", "Roxo Neon", "Tons violetas modernos para setups gamers"),
        ["CrimsonRed"] = ("#EF4444", "#F87171", "Vermelho Carmesim", "Estilo agressivo e enérgico"),
        ["SunsetOrange"] = ("#F97316", "#FB923C", "Laranja Sunset", "Cores quentes e vibrantes"),
        ["AmberGold"] = ("#EAB308", "#FACC15", "Dourado Cyber", "Destaque dourado futurista"),
        ["DeepRose"] = ("#EC4899", "#F4F5F6", "Rosa Shock", "Visual moderno e arrojado")
    };

    private static readonly (object Key, string Dark, string Light)[] Palette =
    {
        ("Bg", "#000000", "#F4F5F7"),
        ("Panel", "#080808", "#FFFFFF"),
        ("Card", "#0E0E0E", "#FFFFFF"),
        ("Hover", "#181818", "#E8ECF0"),
        ("Line", "#1F1F1F", "#E2E8F0"),
        ("LineStrong", "#3A3A3A", "#CBD5E1"),
        ("Fg", "#F5F5F5", "#0F172A"),
        ("Muted", "#8C8C8C", "#475569"),
        ("Faint", "#4A4A4A", "#64748B"),
        ("Ink", "#000000", "#FFFFFF"),
        ("ThumbBrush", "#2E2E2E", "#CBD5E1"),
        ("ThumbHover", "#6A6A6A", "#94A3B8"),
        ("ThumbDrag", "#9A9A9A", "#64748B"),
        ("CardBorderColor", "#262626", "#E2E8F0"),
        ("CardSubtleBg", "#121212", "#F8FAFC"),
        ("Danger", "#EF4444", "#DC2626"),
        ("ChipBg", "#1E1E1E", "#E2E8F0"),
        ("ChipText", "#F5F5F5", "#0F172A"),
        (SystemColors.ControlBrushKey, "#000000", "#F4F5F7"),
        (SystemColors.HighlightBrushKey, "#181818", "#E8ECF0"),
        (SystemColors.InactiveSelectionHighlightBrushKey, "#181818", "#E8ECF0"),
    };

    public static void Apply(bool light, string? accentKey = null)
    {
        IsLight = light;
        if (!string.IsNullOrWhiteSpace(accentKey))
        {
            CurrentAccentTheme = accentKey;
        }

        var res = Application.Current.Resources;
        foreach (var (key, dark, lightHex) in Palette)
        {
            var color = (Color)ColorConverter.ConvertFromString(light ? lightHex : dark);
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            res[key] = brush; // o XAML usa DynamicResource: trocar o pincel atualiza todos os controles na hora
        }

        // Aplica o tema de cor de destaque
        if (AccentThemes.TryGetValue(CurrentAccentTheme, out var acc))
        {
            string hexMain = acc.Hex;
            string hexHover = acc.HexHover;
            string hexFg = "#FFFFFF";

            if (CurrentAccentTheme.Equals("Monochrome", StringComparison.OrdinalIgnoreCase))
            {
                hexMain = light ? "#000000" : "#F5F5F5";
                hexHover = light ? "#222222" : "#FFFFFF";
                hexFg = light ? "#FFFFFF" : "#000000";
            }
            else if (CurrentAccentTheme.Equals("CyberBlue", StringComparison.OrdinalIgnoreCase) ||
                     CurrentAccentTheme.Equals("AmberGold", StringComparison.OrdinalIgnoreCase))
            {
                hexFg = "#000000";
            }

            var brushAccent = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hexMain));
            brushAccent.Freeze();
            res["Accent"] = brushAccent;

            var brushHover = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hexHover));
            brushHover.Freeze();
            res["AccentHover"] = brushHover;

            var brushFg = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hexFg));
            brushFg.Freeze();
            res["AccentFg"] = brushFg;

            var brushChipBg = new SolidColorBrush((Color)ColorConverter.ConvertFromString(light ? "#E2E8F0" : hexMain));
            brushChipBg.Freeze();
            res["ChipBg"] = brushChipBg;

            var brushChipText = new SolidColorBrush((Color)ColorConverter.ConvertFromString(light ? "#0F172A" : hexFg));
            brushChipText.Freeze();
            res["ChipText"] = brushChipText;
        }

        ThemeChanged?.Invoke();
    }
}
