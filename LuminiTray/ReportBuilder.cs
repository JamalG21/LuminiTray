using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;

namespace LuminiTray;

public sealed record ReportNode(string Name, string? Value, List<ReportNode>? Children);
public sealed record ReportSensor(string Hardware, string Category, string Name, string Value, string Min, string Max);
public sealed record ReportBench(DateTime Date, string Test, string Summary, string ReferenceApp = "");
public sealed record ReportDisk(string Name, string Details, string Verdict, string Reasons, List<KeyValuePair<string, string>> Metrics);
public sealed record ReportGpu(string Name, string Subtitle, List<ReportNode> Sections);

/// <summary>Tudo que entra no relatório exportado.</summary>
public sealed class ReportData
{
    public DateTime Date { get; init; } = DateTime.Now;
    public string Machine { get; init; } = Environment.MachineName;
    public string AppVersion { get; init; } = "";
    public string? LogoBase64 { get; init; }
    public List<ReportNode> System { get; init; } = new();
    public List<ReportGpu> Gpus { get; init; } = new();
    public List<ReportDisk> Disks { get; init; } = new();
    public List<ReportDisk> Power { get; init; } = new();
    public List<ReportDisk> Network { get; init; } = new();
    public List<ReportSensor> Sensors { get; init; } = new();
    public List<ReportBench> Benchmarks { get; init; } = new();
    public MonitoringSessionResult? Monitoring { get; init; }
    public bool PrivacyModeActive { get; init; }
}

/// <summary>Gera o relatório em HTML (arquivo único, abre em qualquer navegador) ou JSON. Números de série ficam de fora.</summary>
public static class ReportBuilder
{
    public static ReportNode? ToNode(InfoNode n) => ToNode(n, PrivacyModeService.IsEnabled);

    public static ReportNode? ToNode(InfoNode n, bool privacyMode)
    {
        string h = n.Header ?? "";
        // privacidade: o relatório costuma ser enviado a terceiros
        if (h.Contains("série", StringComparison.OrdinalIgnoreCase) || h.Contains("serial", StringComparison.OrdinalIgnoreCase)) return null;
        var kids = n.Children.Select(c => ToNode(c, privacyMode)).Where(k => k != null).Cast<ReportNode>().ToList();
        string? val = string.IsNullOrEmpty(n.Value) ? null : PrivacyModeService.MaskIfSensitiveField(h, n.Value, privacyMode);
        return new ReportNode(PrivacyModeService.MaskSensitivePatterns(h, privacyMode), val, kids.Count > 0 ? kids : null);
    }

    private static List<ReportDisk> SanitizeCards(List<ReportDisk> items, bool privacy)
    {
        if (!privacy || items.Count == 0) return items;
        return items.Select(x => new ReportDisk(
            PrivacyModeService.MaskSensitivePatterns(x.Name, true),
            PrivacyModeService.MaskSensitivePatterns(x.Details, true),
            x.Verdict,
            PrivacyModeService.MaskSensitivePatterns(x.Reasons, true),
            x.Metrics.Select(m => new KeyValuePair<string, string>(
                PrivacyModeService.MaskSensitivePatterns(m.Key, true),
                PrivacyModeService.MaskIfSensitiveField(m.Key, m.Value, true))).ToList()
        )).ToList();
    }

    private static List<ReportNode> SanitizeNodes(List<ReportNode> nodes, bool privacy)
    {
        if (!privacy || nodes.Count == 0) return nodes;
        return nodes.Select(n => new ReportNode(
            PrivacyModeService.MaskSensitivePatterns(n.Name, true),
            n.Value != null ? PrivacyModeService.MaskIfSensitiveField(n.Name, n.Value, true) : null,
            n.Children != null ? SanitizeNodes(n.Children, true) : null
        )).ToList();
    }

    public static string Json(ReportData d)
    {
        bool privacy = d.PrivacyModeActive || PrivacyModeService.IsEnabled;
        string machine = privacy ? PrivacyModeService.MaskToken : d.Machine;
        var sys = SanitizeNodes(d.System, privacy);
        var disks = SanitizeCards(d.Disks, privacy);
        var power = SanitizeCards(d.Power, privacy);
        var net = SanitizeCards(d.Network, privacy);

        return JsonSerializer.Serialize(new
        {
            gerado_em = d.Date.ToString("s"),
            computador = machine,
            modo_privacidade = privacy,
            versao_do_programa = d.AppVersion,
            sistema = sys,
            placas_de_video = d.Gpus,
            discos = disks.Select(x => new
            {
                x.Name, x.Details, saude = x.Verdict, x.Reasons,
                metricas = x.Metrics.ToDictionary(m => m.Key, m => m.Value)
            }),
            energia = power.Select(x => new { x.Name, x.Details, estado = x.Verdict, x.Reasons, metricas = x.Metrics.ToDictionary(m => m.Key, m => m.Value) }),
            rede = net.Select(x => new { x.Name, x.Details, estado = x.Verdict, x.Reasons, metricas = x.Metrics.ToDictionary(m => m.Key, m => m.Value) }),
            sensores = d.Sensors,
            monitoramento_registrado = d.Monitoring == null ? null : new
            {
                iniciado_em = d.Monitoring.StartedAt.ToString("s"),
                encerrado_em = d.Monitoring.EndedAt.ToString("s"),
                intervalo_segundos = d.Monitoring.IntervalSeconds,
                duracao_segundos = d.Monitoring.DurationSeconds,
                concluido_automaticamente = d.Monitoring.CompletedAutomatically,
                sensores = d.Monitoring.Sensors.Select(s => new
                {
                    hardware = s.Hardware,
                    sensor = s.SensorName,
                    categoria = s.Category,
                    unidade = s.Unit,
                    min = s.Min,
                    horario_min = s.MinTime.ToString("HH:mm:ss"),
                    max = s.Max,
                    horario_max = s.MaxTime.ToString("HH:mm:ss"),
                    media = Math.Round(s.Average, 2),
                    leituras = s.SampleCount,
                    pontos = s.Points.Select(p => new { horario = p.Timestamp.ToString("HH:mm:ss"), valor = p.Value })
                })
            },
            benchmarks = d.Benchmarks.Select(b => new
            {
                data = b.Date.ToString("s"),
                teste = b.Test,
                app_referencia = b.ReferenceApp,
                resultado = b.Summary
            }),
        }, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    }

    private static void Section(StringBuilder sb, string title, List<ReportDisk> items)
    {
        if (items.Count == 0) return;
        sb.Append($"<section><h2>{E(title)}</h2>");
        foreach (var disk in items)
        {
            string cls = disk.Verdict switch { "SAUDÁVEL" or "ESTÁVEL" or "EM USO" => "ok", "SEM DADOS" => "na", _ => "bad" };
            sb.Append($"<div class=\"card\"><div class=\"row\"><b>{E(disk.Name)}</b><span class=\"badge {cls}\">{E(disk.Verdict)}</span></div>");
            if (disk.Details.Length > 0) sb.Append($"<p class=\"muted\">{E(disk.Details)}</p>");
            sb.Append($"<p>{E(disk.Reasons)}</p>");
            if (disk.Metrics.Count > 0)
            {
                sb.Append("<table>");
                foreach (var m in disk.Metrics) sb.Append($"<tr><td class=\"k\">{E(m.Key)}</td><td>{E(m.Value)}</td></tr>");
                sb.Append("</table>");
            }
            sb.Append("</div>");
        }
        sb.Append("</section>");
    }

    private static string E(string? s) => WebUtility.HtmlEncode(s ?? "");

    public static string Html(ReportData d)
    {
        bool privacy = d.PrivacyModeActive || PrivacyModeService.IsEnabled;
        string machine = privacy ? PrivacyModeService.MaskToken : d.Machine;
        var sys = SanitizeNodes(d.System, privacy);
        var disks = SanitizeCards(d.Disks, privacy);
        var power = SanitizeCards(d.Power, privacy);
        var net = SanitizeCards(d.Network, privacy);

        var sb = new StringBuilder(64 * 1024);
        sb.Append("<!doctype html><html lang=\"pt-BR\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">");
        sb.Append($"<title>Lumini Tray · Relatório · {E(machine)}</title><style>{Css}</style></head><body><div class=\"wrap\">");

        sb.Append("<header>");
        if (!string.IsNullOrEmpty(d.LogoBase64)) sb.Append($"<img class=\"logo\" alt=\"\" src=\"data:image/png;base64,{d.LogoBase64}\">");
        sb.Append($"<div><h1>LUMINI TRAY</h1><p class=\"sub\">Relatório de hardware · {E(machine)} · {d.Date:dd/MM/yyyy HH:mm}");
        if (d.AppVersion.Length > 0) sb.Append($" · v{E(d.AppVersion)}");
        sb.Append("</p></div></header>");
        sb.Append(privacy
            ? "<p class=\"note\">Números de série foram omitidos e dados sensíveis (IP, MAC, hostname, usuário) foram ocultados pelo Modo Privacidade.</p>"
            : "<p class=\"note\">Números de série foram omitidos deste relatório.</p>");

        Section(sb, "Saúde dos discos", disks);
        Section(sb, "Bateria e energia", power);
        Section(sb, "Teste de rede", net);

        if (d.Monitoring is { Sensors.Count: > 0 } mon)
        {
            AppendMonitoringHtmlSection(sb, mon);
        }

        if (sys.Count > 0)
        {
            sb.Append("<section><h2>Sistema</h2>");
            foreach (var n in sys) { sb.Append($"<div class=\"card\"><h3>{E(n.Name)}</h3>"); Tree(sb, n.Children, 0); sb.Append("</div>"); }
            sb.Append("</section>");
        }

        foreach (var g in d.Gpus)
        {
            sb.Append($"<section><h2>Placa de vídeo · {E(g.Name)}</h2>");
            if (g.Subtitle.Length > 0) sb.Append($"<p class=\"muted\">{E(g.Subtitle)}</p>");
            foreach (var s in g.Sections) { sb.Append($"<div class=\"card\"><h3>{E(s.Name)}</h3>"); Tree(sb, s.Children, 0); sb.Append("</div>"); }
            sb.Append("</section>");
        }

        if (d.Sensors.Count > 0)
        {
            sb.Append("<section><h2>Sensores (leitura no momento da exportação)</h2>");
            foreach (var grp in d.Sensors.GroupBy(s => s.Hardware))
            {
                sb.Append($"<div class=\"card\"><h3>{E(grp.Key)}</h3><table><tr><th>Categoria</th><th>Sensor</th><th>Valor</th><th>Mín.</th><th>Máx.</th></tr>");
                foreach (var s in grp)
                    sb.Append($"<tr><td class=\"muted\">{E(s.Category)}</td><td>{E(s.Name)}</td><td class=\"num\">{E(s.Value)}</td><td class=\"num muted\">{E(s.Min)}</td><td class=\"num muted\">{E(s.Max)}</td></tr>");
                sb.Append("</table></div>");
            }
            sb.Append("</section>");
        }

        if (d.Benchmarks.Count > 0)
        {
            sb.Append("<section><h2>Benchmarks e testes recentes</h2><div class=\"card\"><table><tr><th>Data</th><th>Teste</th><th>App / Metodologia de Referência</th><th>Resultado</th></tr>");
            foreach (var b in d.Benchmarks)
            {
                string refBadge = !string.IsNullOrEmpty(b.ReferenceApp) ? $"<span class=\"badge ok\">{E(b.ReferenceApp)}</span>" : "—";
                sb.Append($"<tr><td class=\"muted\">{b.Date:dd/MM/yyyy HH:mm}</td><td><b>{E(b.Test)}</b></td><td>{refBadge}</td><td>{E(b.Summary)}</td></tr>");
            }
            sb.Append("</table></div></section>");
        }

        sb.Append("<footer>Gerado pelo Lumini Tray. Dados lidos do Windows, do driver da placa de vídeo e dos sensores da placa-mãe; podem variar conforme o hardware.</footer>");
        sb.Append("</div></body></html>");
        return sb.ToString();
    }

    private static void AppendMonitoringHtmlSection(StringBuilder sb, MonitoringSessionResult mon)
    {
        sb.Append("<section><h2>Monitoramento registrado</h2>");
        sb.Append($"<p class=\"muted\">Período: {mon.StartedAt:dd/MM HH:mm:ss} até {mon.EndedAt:HH:mm:ss} · Intervalo: {mon.IntervalSeconds}s · {mon.Sensors.Count} sensor(es)</p>");

        sb.Append("<div class=\"card\"><table><tr><th>Hardware</th><th>Sensor</th><th>Mín. (Horário)</th><th>Média</th><th>Máx. (Horário)</th><th>Leituras</th></tr>");
        foreach (var s in mon.Sensors)
        {
            sb.Append($"<tr><td>{E(s.Hardware)}</td><td>{E(s.SensorName)}</td>");
            sb.Append($"<td class=\"num\">{E(s.FormatStat(s.Min))} <span class=\"muted\">({s.MinTime:HH:mm:ss})</span></td>");
            sb.Append($"<td class=\"num\">{E(s.FormatStat(s.Average))}</td>");
            sb.Append($"<td class=\"num\">{E(s.FormatStat(s.Max))} <span class=\"muted\">({s.MaxTime:HH:mm:ss})</span></td>");
            sb.Append($"<td class=\"num muted\">{s.SampleCount}</td></tr>");
        }
        sb.Append("</table></div>");

        foreach (var s in mon.Sensors.Where(x => x.Points.Count > 0))
        {
            sb.Append($"<div class=\"card\"><h3>{E(s.DisplayLabel)}</h3>");
            AppendSvgChart(sb, s);
            sb.Append("</div>");
        }

        sb.Append("</section>");
    }

    private static void AppendSvgChart(StringBuilder sb, MonitoringSensorSummary s)
    {
        const double w = 640, h = 135, padL = 52, padR = 12, padT = 14, padB = 22;
        double plotW = w - padL - padR;
        double plotH = h - padT - padB;
        double minVal = s.Points.Min(p => (double)p.Value);
        double maxVal = s.Points.Max(p => (double)p.Value);
        double span = Math.Max(0.1, maxVal - minVal);
        double yMin = minVal - span * 0.12;
        double yMax = maxVal + span * 0.12;
        if (Math.Abs(yMax - yMin) < 0.01) { yMin -= 1; yMax += 1; }

        var pts = new StringBuilder();
        for (int i = 0; i < s.Points.Count; i++)
        {
            double x = s.Points.Count == 1 ? padL + plotW * 0.5 : padL + (plotW * i / (s.Points.Count - 1));
            double y = padT + plotH * (1.0 - (s.Points[i].Value - yMin) / (yMax - yMin));
            pts.Append(FormattableString.Invariant($"{x:0.#},{y:0.#} "));
        }

        string startTime = s.Points[0].Timestamp.ToString("HH:mm:ss");
        string endTime = s.Points[^1].Timestamp.ToString("HH:mm:ss");
        sb.Append("<svg viewBox=\"0 0 640 135\" style=\"width:100%;height:auto;display:block\">");
        sb.Append(FormattableString.Invariant($"<line x1=\"{padL}\" y1=\"{padT}\" x2=\"{padL + plotW}\" y2=\"{padT}\" stroke=\"#444\" stroke-dasharray=\"3 3\" stroke-width=\"0.8\"/>"));
        sb.Append(FormattableString.Invariant($"<line x1=\"{padL}\" y1=\"{padT + plotH}\" x2=\"{padL + plotW}\" y2=\"{padT + plotH}\" stroke=\"#444\" stroke-width=\"0.8\"/>"));
        sb.Append(FormattableString.Invariant($"<text x=\"{padL - 6}\" y=\"{padT + 4}\" text-anchor=\"end\" font-size=\"10\" fill=\"#888\">{E(s.FormatStat(s.Max))}</text>"));
        sb.Append(FormattableString.Invariant($"<text x=\"{padL - 6}\" y=\"{padT + plotH}\" text-anchor=\"end\" font-size=\"10\" fill=\"#888\">{E(s.FormatStat(s.Min))}</text>"));
        sb.Append(FormattableString.Invariant($"<text x=\"{padL}\" y=\"{h - 4}\" font-size=\"10\" fill=\"#888\">{E(startTime)}</text>"));
        sb.Append(FormattableString.Invariant($"<text x=\"{padL + plotW}\" y=\"{h - 4}\" text-anchor=\"end\" font-size=\"10\" fill=\"#888\">{E(endTime)}</text>"));
        sb.Append($"<polyline fill=\"none\" stroke=\"#00D2FF\" stroke-width=\"2\" points=\"{pts.ToString().TrimEnd()}\"/>");
        sb.Append("</svg>");
    }

    private static void Tree(StringBuilder sb, List<ReportNode>? nodes, int depth)
    {
        if (nodes == null || nodes.Count == 0) return;
        sb.Append(depth == 0 ? "<table>" : "<table class=\"nested\">");
        foreach (var n in nodes)
        {
            if (n.Children != null)
            {
                sb.Append($"<tr><td colspan=\"2\" class=\"grp\">{E(n.Name)}{(n.Value != null ? " · " + E(n.Value) : "")}</td></tr><tr><td colspan=\"2\" class=\"inner\">");
                Tree(sb, n.Children, depth + 1);
                sb.Append("</td></tr>");
            }
            else sb.Append($"<tr><td class=\"k\">{E(n.Name)}</td><td>{E(n.Value)}</td></tr>");
        }
        sb.Append("</table>");
    }

    private const string Css = @"
:root{--bg:#fff;--fg:#111;--muted:#666;--line:#e3e3e3;--card:#fafafa;--ok:#111;--badbg:#111;--badfg:#fff}
@media (prefers-color-scheme:dark){:root{--bg:#000;--fg:#f2f2f2;--muted:#8c8c8c;--line:#222;--card:#0c0c0c;--ok:#f2f2f2;--badbg:#f2f2f2;--badfg:#000}}
*{box-sizing:border-box}body{margin:0;background:var(--bg);color:var(--fg);font:14px/1.5 'Segoe UI',system-ui,sans-serif}
.wrap{max-width:980px;margin:0 auto;padding:32px 20px 60px}
header{display:flex;gap:16px;align-items:center;margin-bottom:6px}.logo{width:44px;height:44px}
h1{margin:0;font-size:26px;letter-spacing:.5px}h2{margin:34px 0 12px;font-size:17px;letter-spacing:.4px}h3{margin:0 0 8px;font-size:14px}
.sub,.muted{color:var(--muted)}.sub{margin:2px 0 0}.note{color:var(--muted);font-size:12px;margin:0 0 4px}
.card{background:var(--card);border:1px solid var(--line);border-radius:6px;padding:14px 16px;margin:0 0 12px}
.row{display:flex;justify-content:space-between;gap:12px;align-items:center}
.badge{font-size:11px;font-weight:600;letter-spacing:.6px;padding:3px 10px;border-radius:3px;border:1px solid var(--ok);color:var(--ok)}
.badge.bad,.badge.na{background:var(--badbg);color:var(--badfg);border-color:var(--badbg)}.badge.na{opacity:.6}
table{width:100%;border-collapse:collapse}td,th{padding:5px 8px;border-bottom:1px solid var(--line);vertical-align:top;text-align:left}
th{color:var(--muted);font-weight:600;font-size:12px}.k{color:var(--muted);width:38%}.num{font-family:Consolas,monospace;text-align:right;white-space:nowrap}
.grp{font-weight:600;padding-top:12px}.inner{padding:0 0 0 14px;border-bottom:0}.nested td{font-size:13px}
footer{margin-top:40px;color:var(--muted);font-size:12px}
@media print{body{background:#fff;color:#000}.card{break-inside:avoid}}
";
}
