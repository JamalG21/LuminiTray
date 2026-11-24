using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using LibreHardwareMonitor.Hardware;

namespace LuminiTray;

/// <summary>Linha do painel de sensores, com notificação de mudança para a UI.</summary>
public class SensorRow : INotifyPropertyChanged
{
    public string Id { get; init; } = "";
    public string Hardware { get; init; } = "";
    public string Category { get; init; } = "";
    public string Name { get; init; } = "";
    public SensorType Type { get; init; }
    public HardwareType HardwareType { get; init; }

    private float? _value, _min, _max;
    private bool _isSelectedForMonitoring;

    public float? RawValue => _value;
    public float? RawMin => _min;
    public float? RawMax => _max;
    public DateTime? MinTime { get; private set; }
    public DateTime? MaxTime { get; private set; }

    public bool IsSelectedForMonitoring
    {
        get => _isSelectedForMonitoring;
        set
        {
            if (_isSelectedForMonitoring == value) return;
            _isSelectedForMonitoring = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelectedForMonitoring)));
        }
    }

    /// <summary>Ordem de exibição por tipo (clocks e temperaturas primeiro, como no GPU-Z).</summary>
    public int Order => Type switch
    {
        SensorType.Clock => 0, SensorType.Temperature => 1, SensorType.Load => 2, SensorType.Fan => 3,
        SensorType.Control => 4, SensorType.Power => 5, SensorType.Voltage => 6, SensorType.Current => 7,
        SensorType.SmallData => 8, SensorType.Data => 9, SensorType.Throughput => 10, _ => 11
    };

    public string UnitLabel => Type switch
    {
        SensorType.Temperature => "°C",
        SensorType.Load or SensorType.Control or SensorType.Level or SensorType.Humidity => "%",
        SensorType.Clock => "MHz",
        SensorType.Power => "W",
        SensorType.Voltage => "V",
        SensorType.Current => "A",
        SensorType.Fan => "RPM",
        SensorType.Flow => "L/h",
        SensorType.Frequency => "Hz",
        SensorType.Data => "GB",
        SensorType.SmallData => "MB",
        SensorType.Throughput => "MB/s",
        SensorType.Energy => "mWh",
        _ => ""
    };

    public string Value => Format(_value);
    public Brush ValueBrush => Type == SensorType.Temperature && ResolveTemperatureBrush != null
        ? ResolveTemperatureBrush(_value)
        : (Application.Current?.TryFindResource("Fg") as Brush ?? Brushes.White);
    public static Func<float?, Brush>? ResolveTemperatureBrush { get; set; }
    public string Min => Format(_min);
    public string Max => Format(_max);
    public string MinTimeText => MinTime?.ToString("HH:mm:ss") ?? "—";
    public string MaxTimeText => MaxTime?.ToString("HH:mm:ss") ?? "—";
    public string MinWithTime => _min == null ? "—" : $"{Min} ({MinTimeText})";
    public string MaxWithTime => _max == null ? "—" : $"{Max} ({MaxTimeText})";

    public string FormatValue(float? v) => Format(v);

    private void TrackMinMaxTimes(float? min, float? max, DateTime now)
    {
        if (min != null && (_min == null || min.Value < _min.Value || MinTime == null))
            MinTime = now;
        if (max != null && (_max == null || max.Value > _max.Value || MaxTime == null))
            MaxTime = now;
    }

    public void Update(float? value, float? min, float? max, DateTime? timestamp = null)
    {
        DateTime now = timestamp ?? DateTime.Now;
        bool valChanged = _value != value;
        bool minChanged = _min != min;
        bool maxChanged = _max != max;
        if (!valChanged && !minChanged && !maxChanged) return;

        TrackMinMaxTimes(min, max, now);
        _value = value; _min = min; _max = max;

        if (valChanged)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
            if (Type == SensorType.Temperature)
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ValueBrush)));
        }
        if (minChanged)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Min)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(MinTimeText)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(MinWithTime)));
        }
        if (maxChanged)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Max)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(MaxTimeText)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(MaxWithTime)));
        }
    }

    /// <summary>Atualiza o valor numérico em background sem disparar eventos de interface WPF.</summary>
    public void UpdateRawOnly(float? value, float? min, float? max, DateTime? timestamp = null)
    {
        TrackMinMaxTimes(min, max, timestamp ?? DateTime.Now);
        _value = value; _min = min; _max = max;
    }

    public void ResetMinMax(DateTime? timestamp = null)
    {
        DateTime? now = _value != null ? (timestamp ?? DateTime.Now) : null;
        _min = _value;
        _max = _value;
        MinTime = now;
        MaxTime = now;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Min)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Max)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(MinTimeText)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(MaxTimeText)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(MinWithTime)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(MaxWithTime)));
    }

    /// <summary>Notifica a UI para rebindar os valores já atualizados em segundo plano.</summary>
    public void NotifyUiRefresh() =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));

    private string Format(float? v)
    {
        if (v == null) return "—";
        return Type switch
        {
            SensorType.Temperature => $"{v:0.0} °C",
            SensorType.Load or SensorType.Control or SensorType.Level or SensorType.Humidity => $"{v:0.#} %",
            SensorType.Clock => $"{v:0} MHz",
            SensorType.Power => $"{v:0.#} W",
            SensorType.Voltage => $"{v:0.###} V",
            SensorType.Current => $"{v:0.###} A",
            SensorType.Fan => $"{v:0} RPM",
            SensorType.Flow => $"{v:0.#} L/h",
            SensorType.Frequency => $"{v:0.#} Hz",
            SensorType.Data => $"{v:0.##} GB",
            SensorType.SmallData => $"{v:0.#} MB",
            SensorType.Throughput => $"{v / 1048576f:0.##} MB/s",
            SensorType.Energy => $"{v:0.#} mWh",
            _ => $"{v:0.##}"
        };
    }

    public static string CategoryName(SensorType t) => t switch
    {
        SensorType.Temperature => "Temperatura",
        SensorType.Load => "Carga",
        SensorType.Clock => "Clock",
        SensorType.Power => "Consumo",
        SensorType.Voltage => "Tensão",
        SensorType.Current => "Corrente",
        SensorType.Fan => "Ventoinhas",
        SensorType.Control => "Controle",
        SensorType.Level => "Nível",
        SensorType.Data or SensorType.SmallData => "Dados",
        SensorType.Throughput => "Taxa",
        SensorType.Frequency => "Frequência",
        SensorType.Energy => "Energia",
        SensorType.Flow => "Fluxo",
        SensorType.Humidity => "Umidade",
        SensorType.Factor => "Fator",
        _ => t.ToString()
    };

    public event PropertyChangedEventHandler? PropertyChanged;
}
