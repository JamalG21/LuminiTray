using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace LuminiTray;

public sealed record MonitoringPoint(DateTime Timestamp, float Value);

public sealed class MonitoringSensorSummary
{
    public string SensorId { get; init; } = "";
    public string Hardware { get; init; } = "";
    public string SensorName { get; init; } = "";
    public string Category { get; init; } = "";
    public string Unit { get; init; } = "";
    public float Min { get; init; }
    public DateTime MinTime { get; init; }
    public float Max { get; init; }
    public DateTime MaxTime { get; init; }
    public float Average { get; init; }
    public int SampleCount { get; init; }
    public List<MonitoringPoint> Points { get; init; } = new();

    public string DisplayLabel => string.IsNullOrEmpty(Unit)
        ? $"{Hardware} · {SensorName}"
        : $"{Hardware} · {SensorName} ({Unit})";

    public string FormatStat(float v) => Unit switch
    {
        "°C" => $"{v:0.0} °C",
        "%" => $"{v:0.#} %",
        "MHz" => $"{v:0} MHz",
        "W" => $"{v:0.#} W",
        "V" => $"{v:0.###} V",
        "A" => $"{v:0.###} A",
        "RPM" => $"{v:0} RPM",
        _ => string.IsNullOrEmpty(Unit) ? $"{v:0.##}" : $"{v:0.##} {Unit}"
    };
}

public sealed class MonitoringSessionResult
{
    public DateTime StartedAt { get; init; }
    public DateTime EndedAt { get; init; }
    public int IntervalSeconds { get; init; }
    public int DurationSeconds { get; init; }
    public bool CompletedAutomatically { get; init; }
    public List<MonitoringSensorSummary> Sensors { get; init; } = new();
}

/// <summary>
/// Serviço de Monitoramento Registrado temporizado:
/// grava em disco apenas durante a sessão programada (%LocalAppData%\LuminiTray\monitoring_session_active.json),
/// gera um resumo breve (Mín + horário, Máx + horário, Média) e série compactada para o gráfico ao finalizar,
/// apagando o arquivo bruto temporário do disco ao término.
/// </summary>
public sealed class MonitoringLogService
{
    private sealed class TrackedSensorState
    {
        public string SensorId { get; init; } = "";
        public string Hardware { get; init; } = "";
        public string SensorName { get; init; } = "";
        public string Category { get; init; } = "";
        public string Unit { get; init; } = "";
        public float Min { get; set; } = float.MaxValue;
        public DateTime MinTime { get; set; }
        public float Max { get; set; } = float.MinValue;
        public DateTime MaxTime { get; set; }
        public double Sum { get; set; }
        public int Count { get; set; }
        public List<MonitoringPoint> Points { get; } = new();
    }

    private readonly string _storageDir;
    private readonly object _lock = new();
    private readonly Dictionary<string, TrackedSensorState> _tracked = new(StringComparer.OrdinalIgnoreCase);
    private DateTime _lastSampleAt;

    public MonitoringLogService(string? storageDir = null)
    {
        _storageDir = string.IsNullOrWhiteSpace(storageDir)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LuminiTray")
            : storageDir;
    }

    public string ActiveTempFilePath => Path.Combine(_storageDir, "monitoring_session_active.json");
    public bool IsRunning { get; private set; }
    public DateTime StartedAt { get; private set; }
    public DateTime EndsAt { get; private set; }
    public int IntervalSeconds { get; private set; }
    public int DurationSeconds { get; private set; }
    public int RecordedIntervalsCount { get; private set; }
    public MonitoringSessionResult? LastResult { get; private set; }

    public TimeSpan GetRemainingTime(DateTime? nowOverride = null)
    {
        if (!IsRunning) return TimeSpan.Zero;
        DateTime now = nowOverride ?? DateTime.Now;
        return EndsAt > now ? EndsAt - now : TimeSpan.Zero;
    }

    public double GetProgressFraction(DateTime? nowOverride = null)
    {
        if (!IsRunning || DurationSeconds <= 0) return 0;
        DateTime now = nowOverride ?? DateTime.Now;
        double elapsed = Math.Max(0, (now - StartedAt).TotalSeconds);
        return Math.Clamp(elapsed / DurationSeconds, 0.0, 1.0);
    }

    public bool Start(IEnumerable<SensorRow> selectedSensors, int intervalSeconds, int durationSeconds, DateTime? nowOverride = null)
    {
        var list = selectedSensors?.Where(s => s != null && !string.IsNullOrWhiteSpace(s.Id)).ToList()
                   ?? new List<SensorRow>();
        if (list.Count == 0) return false;

        intervalSeconds = Math.Max(1, intervalSeconds);
        durationSeconds = Math.Max(intervalSeconds, durationSeconds);
        DateTime now = nowOverride ?? DateTime.Now;

        lock (_lock)
        {
            _tracked.Clear();
            foreach (var s in list)
            {
                if (_tracked.ContainsKey(s.Id)) continue;
                var st = new TrackedSensorState
                {
                    SensorId = s.Id,
                    Hardware = s.Hardware,
                    SensorName = s.Name,
                    Category = s.Category,
                    Unit = s.UnitLabel
                };
                if (s.RawValue is float val && !float.IsNaN(val) && !float.IsInfinity(val))
                    AppendValue(st, val, now);
                _tracked[s.Id] = st;
            }

            if (_tracked.Count == 0) return false;

            StartedAt = now;
            EndsAt = now.AddSeconds(durationSeconds);
            IntervalSeconds = intervalSeconds;
            DurationSeconds = durationSeconds;
            _lastSampleAt = now;
            RecordedIntervalsCount = _tracked.Values.Any(v => v.Count > 0) ? 1 : 0;
            IsRunning = true;

            FlushActiveSessionToDisk(now);
            return true;
        }
    }

    public bool Tick(IReadOnlyList<SensorRow> allRows, DateTime? nowOverride = null) =>
        Tick(allRows, nowOverride, out _);

    public bool Tick(IReadOnlyList<SensorRow> allRows, DateTime? nowOverride, out bool sampledNow)
    {
        sampledNow = false;
        if (!IsRunning) return false;

        DateTime now = nowOverride ?? DateTime.Now;
        bool completed = false;

        lock (_lock)
        {
            if (!IsRunning) return false;

            if ((now - _lastSampleAt).TotalSeconds >= IntervalSeconds - 0.05)
            {
                var byId = new Dictionary<string, SensorRow>(StringComparer.OrdinalIgnoreCase);
                foreach (var r in allRows)
                {
                    if (!string.IsNullOrWhiteSpace(r.Id) && !byId.ContainsKey(r.Id))
                        byId[r.Id] = r;
                }

                bool anyAdded = false;
                foreach (var kv in _tracked)
                {
                    if (byId.TryGetValue(kv.Key, out var row) &&
                        row.RawValue is float val && !float.IsNaN(val) && !float.IsInfinity(val))
                    {
                        AppendValue(kv.Value, val, now);
                        anyAdded = true;
                    }
                }

                _lastSampleAt = now;
                if (anyAdded)
                {
                    RecordedIntervalsCount++;
                    sampledNow = true;
                }
                FlushActiveSessionToDisk(now);
            }

            if (now >= EndsAt)
            {
                FinalizeLocked(completedAutomatically: true, now);
                completed = true;
            }
        }

        return completed;
    }

    public MonitoringSessionResult? StopAndFinalize(bool completedAutomatically = false, DateTime? nowOverride = null)
    {
        lock (_lock)
        {
            if (!IsRunning && _tracked.Count == 0)
                return LastResult;
            return FinalizeLocked(completedAutomatically, nowOverride ?? DateTime.Now);
        }
    }

    public MonitoringSessionResult? GetLiveSnapshot(DateTime? nowOverride = null)
    {
        lock (_lock)
        {
            if (!IsRunning || _tracked.Count == 0) return LastResult;
            return BuildResultLocked(completedAutomatically: false, nowOverride ?? DateTime.Now);
        }
    }

    public void ClearLastResult()
    {
        lock (_lock)
        {
            LastResult = null;
            DeleteActiveTempFile();
        }
    }

    private MonitoringSessionResult FinalizeLocked(bool completedAutomatically, DateTime endedAt)
    {
        IsRunning = false;
        var result = BuildResultLocked(completedAutomatically, endedAt);
        LastResult = result;
        _tracked.Clear();

        // Após gerar o resumo breve e os dados para o gráfico, remove o arquivo bruto temporário do disco.
        DeleteActiveTempFile();
        return result;
    }

    private MonitoringSessionResult BuildResultLocked(bool completedAutomatically, DateTime endedAt)
    {
        var summaries = new List<MonitoringSensorSummary>();
        foreach (var st in _tracked.Values)
        {
            if (st.Count == 0) continue;
            float avg = (float)(st.Sum / st.Count);
            summaries.Add(new MonitoringSensorSummary
            {
                SensorId = st.SensorId,
                Hardware = st.Hardware,
                SensorName = st.SensorName,
                Category = st.Category,
                Unit = st.Unit,
                Min = st.Min,
                MinTime = st.MinTime,
                Max = st.Max,
                MaxTime = st.MaxTime,
                Average = avg,
                SampleCount = st.Count,
                Points = DownsamplePreservingExtrema(st.Points, maxPoints: 240, st.MinTime, st.MaxTime)
            });
        }

        return new MonitoringSessionResult
        {
            StartedAt = StartedAt,
            EndedAt = endedAt,
            IntervalSeconds = IntervalSeconds,
            DurationSeconds = DurationSeconds,
            CompletedAutomatically = completedAutomatically,
            Sensors = summaries
        };
    }

    private static void AppendValue(TrackedSensorState st, float val, DateTime now)
    {
        if (st.Count == 0 || val < st.Min)
        {
            st.Min = val;
            st.MinTime = now;
        }
        if (st.Count == 0 || val > st.Max)
        {
            st.Max = val;
            st.MaxTime = now;
        }
        st.Sum += val;
        st.Count++;
        st.Points.Add(new MonitoringPoint(now, val));
    }

    private static List<MonitoringPoint> DownsamplePreservingExtrema(
        List<MonitoringPoint> raw, int maxPoints, DateTime minTime, DateTime maxTime)
    {
        if (raw.Count <= maxPoints)
            return new List<MonitoringPoint>(raw);

        var indices = new SortedSet<int> { 0, raw.Count - 1 };
        int minIdx = raw.FindIndex(p => p.Timestamp == minTime);
        int maxIdx = raw.FindIndex(p => p.Timestamp == maxTime);
        if (minIdx >= 0) indices.Add(minIdx);
        if (maxIdx >= 0) indices.Add(maxIdx);

        double step = (double)(raw.Count - 1) / (maxPoints - 1);
        for (int i = 1; i < maxPoints - 1; i++)
            indices.Add((int)Math.Round(i * step));

        return indices.Select(idx => raw[idx]).ToList();
    }

    private void FlushActiveSessionToDisk(DateTime updatedAt)
    {
        try
        {
            Directory.CreateDirectory(_storageDir);
            var payload = new
            {
                started_at = StartedAt.ToString("s"),
                ends_at = EndsAt.ToString("s"),
                updated_at = updatedAt.ToString("s"),
                interval_seconds = IntervalSeconds,
                duration_seconds = DurationSeconds,
                sensors = _tracked.Values.Select(v => new
                {
                    v.SensorId,
                    v.Hardware,
                    v.SensorName,
                    v.Category,
                    v.Unit,
                    min = v.Count > 0 ? (float?)v.Min : null,
                    min_time = v.Count > 0 ? v.MinTime.ToString("s") : null,
                    max = v.Count > 0 ? (float?)v.Max : null,
                    max_time = v.Count > 0 ? v.MaxTime.ToString("s") : null,
                    average = v.Count > 0 ? (double?)(v.Sum / v.Count) : null,
                    samples = v.Count,
                    points = v.Points
                })
            };
            string json = JsonSerializer.Serialize(payload);
            File.WriteAllText(ActiveTempFilePath, json, new UTF8Encoding(false));
        }
        catch { }
    }

    private void DeleteActiveTempFile()
    {
        try
        {
            if (File.Exists(ActiveTempFilePath))
                File.Delete(ActiveTempFilePath);
        }
        catch { }
    }
}
