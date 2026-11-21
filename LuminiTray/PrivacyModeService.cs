using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace LuminiTray;

/// <summary>
/// Modo Privacidade global: quando ativado (botão de olho na barra superior),
/// mascara dados sensíveis (IP, Gateway, DNS, MAC, Serial, UUID, SSID Wi-Fi, Nome do PC e Usuário)
/// em todas as abas e nos relatórios HTML/JSON exportados.
/// </summary>
public static class PrivacyModeService
{
    public const string MaskToken = "••••";

    private static bool _isEnabled;
    public static event Action<bool>? Changed;

    private static readonly Regex RxMac = new(
        @"\b(?:[0-9A-Fa-f]{2}[:-]){5}[0-9A-Fa-f]{2}\b",
        RegexOptions.Compiled);

    private static readonly Regex RxIpv4 = new(
        @"(?<![\d.vV])(?:(?:25[0-5]|2[0-4]\d|1?\d?\d)\.){3}(?:25[0-5]|2[0-4]\d|1?\d?\d)(?:/\d{1,2})?(?![\d.])",
        RegexOptions.Compiled);

    private static readonly Regex RxIpv6 = new(
        @"\b(?:[0-9a-fA-F]{1,4}:){2,7}[0-9a-fA-F]{1,4}(?:/\d{1,3})?\b|\bfe80::[0-9a-fA-F:%]+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly string[] SensitiveFieldKeywords =
    {
        "ip", "ipv4", "ipv6", "mac", "endereço físico", "endereco fisico",
        "physical address", "gateway", "dns", "dhcp", "série", "serie",
        "serial", "uuid", "guid", "ssid", "bssid", "wi-fi", "wifi",
        "hostname", "nome do computador", "nome do pc", "machine",
        "usuário", "usuario", "user", "domínio", "dominio",
        "product id", "id do produto", "chave"
    };

    public static bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled == value) return;
            _isEnabled = value;
            Changed?.Invoke(value);
        }
    }

    public static bool Toggle()
    {
        IsEnabled = !IsEnabled;
        return IsEnabled;
    }

    public static bool IsSensitiveFieldName(string? label)
    {
        if (string.IsNullOrWhiteSpace(label)) return false;
        string lower = label.Trim().ToLowerInvariant();
        foreach (string kw in SensitiveFieldKeywords)
        {
            // Evita falsos positivos como "chip" ou "equipamento" contendo "ip"
            if (kw == "ip")
            {
                if (Regex.IsMatch(lower, @"\bip\b")) return true;
                continue;
            }
            if (lower.Contains(kw, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    public static string MaskIfSensitiveField(string? label, string? value, bool? forceEnabled = null)
    {
        string raw = value ?? "";
        bool enabled = forceEnabled ?? _isEnabled;
        if (!enabled || string.IsNullOrWhiteSpace(raw) || raw == "—")
            return raw;

        if (IsSensitiveFieldName(label))
            return MaskToken;

        return MaskSensitivePatterns(raw, enabled);
    }

    public static string MaskSensitivePatterns(string? text, bool? forceEnabled = null)
    {
        string raw = text ?? "";
        bool enabled = forceEnabled ?? _isEnabled;
        if (!enabled || string.IsNullOrEmpty(raw))
            return raw;

        string masked = RxMac.Replace(raw, MaskToken);
        masked = RxIpv6.Replace(masked, MaskToken);
        masked = RxIpv4.Replace(masked, MaskToken);

        string machine = Environment.MachineName;
        if (!string.IsNullOrWhiteSpace(machine) && machine.Length >= 3)
            masked = Regex.Replace(masked, @"\b" + Regex.Escape(machine) + @"\b", MaskToken, RegexOptions.IgnoreCase);

        string user = Environment.UserName;
        if (!string.IsNullOrWhiteSpace(user) && user.Length >= 3)
            masked = Regex.Replace(masked, @"\b" + Regex.Escape(user) + @"\b", MaskToken, RegexOptions.IgnoreCase);

        return masked;
    }

    public static DiskHealthCard MaskCard(DiskHealthCard card, bool? forceEnabled = null)
    {
        bool enabled = forceEnabled ?? _isEnabled;
        if (!enabled) return card;

        return new DiskHealthCard
        {
            Index = card.Index,
            Title = MaskSensitivePatterns(card.Title, enabled),
            Meta = MaskSensitivePatterns(card.Meta, enabled),
            Serial = string.IsNullOrWhiteSpace(card.Serial) ? "" : MaskToken,
            Verdict = card.Verdict,
            Label = card.Label,
            Reasons = MaskSensitivePatterns(card.Reasons, enabled),
            Metrics = card.Metrics
                .Select(m => new KeyValuePair<string, string>(
                    MaskSensitivePatterns(m.Key, enabled),
                    MaskIfSensitiveField(m.Key, m.Value, enabled)))
                .ToList()
        };
    }

    public static List<DiskHealthCard> MaskCards(IEnumerable<DiskHealthCard> cards, bool? forceEnabled = null)
    {
        bool enabled = forceEnabled ?? _isEnabled;
        return enabled ? cards.Select(c => MaskCard(c, enabled)).ToList() : cards.ToList();
    }
}
