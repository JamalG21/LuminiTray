using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Media;
using System.Runtime.InteropServices;
using System.Windows.Input;

namespace LuminiTray;

public sealed class KeyModel
{
    public Key Key { get; }
    public string DisplayText { get; }
    public double WidthUnits { get; }

    public KeyModel(Key key, string displayText, double widthUnits = 1.0)
    {
        Key = key;
        DisplayText = displayText;
        WidthUnits = widthUnits;
    }
}

public sealed class KeyboardTester
{
    private readonly HashSet<Key> _activeKeys = new();
    private readonly HashSet<Key> _testedKeys = new();
    private readonly Dictionary<Key, long> _pressTimestamps = new();
    private readonly Queue<long> _keystrokeHistory = new();
    private readonly List<string> _recentKeyLog = new();

    public int PeakRollover { get; private set; }
    public int CurrentRollover => _activeKeys.Count;
    public int TestedCount => _testedKeys.Count;
    public int TotalKeysInLayout { get; set; } = 104;
    public double TestedPercentage => TotalKeysInLayout > 0 ? Math.Round((double)TestedCount / TotalKeysInLayout * 100.0, 1) : 0;

    public double LastLatencyMs { get; private set; }
    public string LastKeyName { get; private set; } = "Nenhuma";
    public int LastKeyCode { get; private set; }
    public string LastKeyHex => LastKeyCode > 0 ? $"0x{LastKeyCode:X2}" : "--";

    public int TotalKeystrokes { get; private set; }
    public double CurrentKps { get; private set; }
    public double PeakKps { get; private set; }
    public double CurrentWpm => Math.Round(CurrentKps * 60.0 / 5.0, 1); // 5 caracteres por palavra padrão

    public int StuckKeysCount { get; private set; }
    public bool PlaySoundFeedback { get; set; }
    public KeySoundType SoundType { get; set; } = KeySoundType.Switch;

    public IReadOnlyList<string> RecentKeyLog => _recentKeyLog;

    public event Action? StateChanged;

    public bool IsKeyPressed(Key key) => _activeKeys.Contains(key);
    public bool IsKeyTested(Key key) => _testedKeys.Contains(key);

    public bool IsKeyStuck(Key key)
    {
        if (_pressTimestamps.TryGetValue(key, out long startTicks))
        {
            double heldSec = (double)(Stopwatch.GetTimestamp() - startTicks) / Stopwatch.Frequency;
            return heldSec > 3.0; // Presa por mais de 3 segundos
        }
        return false;
    }

    public void OnKeyDown(Key key)
    {
        Key realKey = key == Key.System ? Key.F10 : key;
        long now = Stopwatch.GetTimestamp();

        TotalKeystrokes++;
        _testedKeys.Add(realKey);
        _activeKeys.Add(realKey);

        if (!_pressTimestamps.ContainsKey(realKey))
            _pressTimestamps[realKey] = now;

        if (_activeKeys.Count > PeakRollover)
            PeakRollover = _activeKeys.Count;

        LastKeyName = realKey.ToString();
        LastKeyCode = KeyInterop.VirtualKeyFromKey(realKey);

        // Atualizar histórico recente (máximo 12 teclas)
        _recentKeyLog.Add(LastKeyName);
        if (_recentKeyLog.Count > 12)
            _recentKeyLog.RemoveAt(0);

        // KPS cálculo com janela de 2 segundos
        _keystrokeHistory.Enqueue(now);
        long windowTicks = (long)(2.0 * Stopwatch.Frequency);
        while (_keystrokeHistory.Count > 0 && (now - _keystrokeHistory.Peek()) > windowTicks)
            _keystrokeHistory.Dequeue();

        CurrentKps = Math.Round(_keystrokeHistory.Count / 2.0, 1);
        if (CurrentKps > PeakKps)
            PeakKps = CurrentKps;

        // Verificar teclas presas
        int stuck = 0;
        foreach (var kvp in _pressTimestamps)
        {
            if ((double)(now - kvp.Value) / Stopwatch.Frequency > 3.0)
                stuck++;
        }
        StuckKeysCount = stuck;

        if (PlaySoundFeedback)
        {
            try { KeySoundEngine.Play(SoundType); } catch { }
        }

        StateChanged?.Invoke();
    }

    public void OnKeyUp(Key key)
    {
        Key realKey = key == Key.System ? Key.F10 : key;

        _activeKeys.Remove(realKey);

        if (_pressTimestamps.TryGetValue(realKey, out long startTicks))
        {
            long elapsedTicks = Stopwatch.GetTimestamp() - startTicks;
            LastLatencyMs = Math.Round((double)elapsedTicks / Stopwatch.Frequency * 1000.0, 1);
            _pressTimestamps.Remove(realKey);
        }

        // Recalcular presas
        long now = Stopwatch.GetTimestamp();
        int stuck = 0;
        foreach (var kvp in _pressTimestamps)
        {
            if ((double)(now - kvp.Value) / Stopwatch.Frequency > 3.0)
                stuck++;
        }
        StuckKeysCount = stuck;

        StateChanged?.Invoke();
    }

    public void Reset()
    {
        _activeKeys.Clear();
        _testedKeys.Clear();
        _pressTimestamps.Clear();
        _keystrokeHistory.Clear();
        _recentKeyLog.Clear();
        PeakRollover = 0;
        LastLatencyMs = 0;
        LastKeyName = "Nenhuma";
        LastKeyCode = 0;
        TotalKeystrokes = 0;
        CurrentKps = 0;
        PeakKps = 0;
        StuckKeysCount = 0;
        StateChanged?.Invoke();
    }
}
