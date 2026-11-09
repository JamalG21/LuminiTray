using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace LuminiTray;

public sealed class MouseTester
{
    public int LeftClicks { get; private set; }
    public int RightClicks { get; private set; }
    public int MiddleClicks { get; private set; }
    public int XButton1Clicks { get; private set; }
    public int XButton2Clicks { get; private set; }
    public int TotalClicks => LeftClicks + RightClicks + MiddleClicks + XButton1Clicks + XButton2Clicks;

    public bool IsLeftDown { get; private set; }
    public bool IsRightDown { get; private set; }
    public bool IsMiddleDown { get; private set; }
    public bool IsXButton1Down { get; private set; }
    public bool IsXButton2Down { get; private set; }

    // Detector de Duplo Clique Acidental (Falha de microswitch / Bounce)
    public int DefectiveDoubleClicksCount { get; private set; }
    public double LastDoubleClickIntervalMs { get; private set; }
    public bool IsLastClickDefective { get; private set; }
    private readonly Dictionary<MouseButton, long> _lastButtonUpTimestamp = new();

    // Scroll
    public int ScrollUpCount { get; private set; }
    public int ScrollDownCount { get; private set; }
    public int LastScrollDelta { get; private set; }
    public int ScrollJitterCount { get; private set; }
    private int _lastScrollDirection; // +1 = up, -1 = down
    private long _lastScrollTimestamp;

    // Métricas de Desempenho
    public double LastClickLatencyMs { get; private set; }
    public double TotalDistancePx { get; private set; }
    public double MaxSpeedPxPerSec { get; private set; }

    // Polling rate calculation (Hz)
    private readonly Queue<long> _moveTimestamps = new();
    public int CurrentPollingRateHz { get; private set; }
    public int PeakPollingRateHz { get; private set; }
    public int AveragePollingRateHz { get; private set; }
    public double CurrentLatencyMs => CurrentPollingRateHz > 0 ? Math.Round(1000.0 / CurrentPollingRateHz, 1) : 0;
    private long _totalPollSamples;
    private long _accumulatedHz;

    private Point? _lastMousePos;
    private long _lastMoveTimestamp;
    private long _lastUiNotifyTimestamp;
    private readonly Dictionary<MouseButton, long> _buttonPressTimestamps = new();

    // CPS Teste cronometrado
    public bool IsCpsTestRunning { get; private set; }
    public int CpsTestClicks { get; private set; }
    public double CpsScore { get; private set; }
    public double CpsPeak { get; private set; }
    public int CpsTimeRemainingSeconds { get; private set; }
    public string CpsRank => CpsScore switch
    {
        <= 0 => "--",
        < 5 => "Tartaruga (< 5 CPS)",
        < 8 => "Normal (5 - 8 CPS)",
        < 12 => "Gamer Veloz (8 - 12 CPS)",
        _ => "Pro / Butterfly Click (12+ CPS)"
    };
    private CancellationTokenSource? _cpsCts;

    public event Action? StateChanged;
    public event Action? CpsTestFinished;

    public void OnMouseDown(MouseButton button)
    {
        long now = Stopwatch.GetTimestamp();

        // 1. Detecção de Duplo Clique Acidental (Debounce Fail)
        if (_lastButtonUpTimestamp.TryGetValue(button, out long prevUpTicks))
        {
            double intervalMs = (double)(now - prevUpTicks) / Stopwatch.Frequency * 1000.0;
            // Cliques entre 5ms e 80ms são tipicamente repiques mecânicos defeituosos do switch
            if (intervalMs >= 5.0 && intervalMs < 80.0)
            {
                DefectiveDoubleClicksCount++;
                LastDoubleClickIntervalMs = Math.Round(intervalMs, 1);
                IsLastClickDefective = true;
            }
            else
            {
                IsLastClickDefective = false;
            }
        }

        switch (button)
        {
            case MouseButton.Left: LeftClicks++; IsLeftDown = true; break;
            case MouseButton.Right: RightClicks++; IsRightDown = true; break;
            case MouseButton.Middle: MiddleClicks++; IsMiddleDown = true; break;
            case MouseButton.XButton1: XButton1Clicks++; IsXButton1Down = true; break;
            case MouseButton.XButton2: XButton2Clicks++; IsXButton2Down = true; break;
        }

        if (IsCpsTestRunning)
        {
            CpsTestClicks++;
        }

        _buttonPressTimestamps[button] = now;
        StateChanged?.Invoke();
    }

    public void OnMouseUp(MouseButton button)
    {
        long now = Stopwatch.GetTimestamp();

        switch (button)
        {
            case MouseButton.Left: IsLeftDown = false; break;
            case MouseButton.Right: IsRightDown = false; break;
            case MouseButton.Middle: IsMiddleDown = false; break;
            case MouseButton.XButton1: IsXButton1Down = false; break;
            case MouseButton.XButton2: IsXButton2Down = false; break;
        }

        if (_buttonPressTimestamps.TryGetValue(button, out long startTicks))
        {
            long elapsedTicks = now - startTicks;
            LastClickLatencyMs = Math.Round((double)elapsedTicks / Stopwatch.Frequency * 1000.0, 1);
            _buttonPressTimestamps.Remove(button);
        }

        _lastButtonUpTimestamp[button] = now;
        StateChanged?.Invoke();
    }

    public void OnMouseWheel(int delta)
    {
        long now = Stopwatch.GetTimestamp();
        LastScrollDelta = delta;

        int currentDir = delta > 0 ? 1 : -1;
        if (delta > 0) ScrollUpCount++;
        else if (delta < 0) ScrollDownCount++;

        // Detecção de salto invertido (encoder sujo/danificado saltando na direção oposta em < 60ms)
        if (_lastScrollDirection != 0 && currentDir != _lastScrollDirection)
        {
            double elapsedMs = (double)(now - _lastScrollTimestamp) / Stopwatch.Frequency * 1000.0;
            if (elapsedMs < 60.0)
            {
                ScrollJitterCount++;
            }
        }

        _lastScrollDirection = currentDir;
        _lastScrollTimestamp = now;

        StateChanged?.Invoke();
    }

    public void OnMouseMove(Point pos)
    {
        long now = Stopwatch.GetTimestamp();

        if (_lastMousePos.HasValue)
        {
            double dx = pos.X - _lastMousePos.Value.X;
            double dy = pos.Y - _lastMousePos.Value.Y;
            double dist = Math.Sqrt(dx * dx + dy * dy);
            TotalDistancePx += dist;

            double elapsedSec = (double)(now - _lastMoveTimestamp) / Stopwatch.Frequency;
            if (elapsedSec > 0.0001 && elapsedSec < 0.2)
            {
                double speed = dist / elapsedSec;
                if (speed > MaxSpeedPxPerSec)
                    MaxSpeedPxPerSec = Math.Round(speed, 0);
            }
        }
        _lastMousePos = pos;
        _lastMoveTimestamp = now;

        // Cálculo de polling rate (Hz)
        _moveTimestamps.Enqueue(now);

        long windowTicks = (long)(0.5 * Stopwatch.Frequency);
        while (_moveTimestamps.Count > 0 && (now - _moveTimestamps.Peek()) > windowTicks)
            _moveTimestamps.Dequeue();

        if (_moveTimestamps.Count > 1)
        {
            int hz = (int)Math.Round(_moveTimestamps.Count / 0.5);
            CurrentPollingRateHz = hz;

            if (hz > PeakPollingRateHz)
                PeakPollingRateHz = hz;

            _totalPollSamples++;
            _accumulatedHz += hz;
            AveragePollingRateHz = (int)(_accumulatedHz / _totalPollSamples);
        }

        // Throttle para notificação de UI a ~40 Hz (25ms), evitando sobrecarregar o Dispatcher do WPF em mouses gamers (1000Hz-8000Hz)
        double sinceLastUiSec = (double)(now - _lastUiNotifyTimestamp) / Stopwatch.Frequency;
        if (sinceLastUiSec >= 0.025)
        {
            _lastUiNotifyTimestamp = now;
            StateChanged?.Invoke();
        }
    }

    public void StartCpsTest(int durationSec = 5)
    {
        if (IsCpsTestRunning) return;
        IsCpsTestRunning = true;
        CpsTestClicks = 0;
        CpsScore = 0;
        CpsPeak = 0;
        CpsTimeRemainingSeconds = durationSec;

        _cpsCts?.Cancel();
        _cpsCts = new CancellationTokenSource();
        var token = _cpsCts.Token;

        Task.Run(async () =>
        {
            var sw = Stopwatch.StartNew();
            while (sw.Elapsed.TotalSeconds < durationSec && !token.IsCancellationRequested)
            {
                await Task.Delay(100, token);
                double elapsed = sw.Elapsed.TotalSeconds;
                if (elapsed > 0.1)
                {
                    double curCps = Math.Round(CpsTestClicks / elapsed, 1);
                    CpsScore = curCps;
                    if (curCps > CpsPeak) CpsPeak = curCps;
                }
                CpsTimeRemainingSeconds = Math.Max(0, durationSec - (int)elapsed);
                StateChanged?.Invoke();
            }

            IsCpsTestRunning = false;
            if (durationSec > 0)
                CpsScore = Math.Round((double)CpsTestClicks / durationSec, 1);

            StateChanged?.Invoke();
            CpsTestFinished?.Invoke();
        }, token);
    }

    public void StopCpsTest()
    {
        try { _cpsCts?.Cancel(); } catch { }
        IsCpsTestRunning = false;
        StateChanged?.Invoke();
    }

    public void Reset()
    {
        StopCpsTest();
        LeftClicks = 0; RightClicks = 0; MiddleClicks = 0;
        XButton1Clicks = 0; XButton2Clicks = 0;
        IsLeftDown = false; IsRightDown = false; IsMiddleDown = false;
        IsXButton1Down = false; IsXButton2Down = false;
        DefectiveDoubleClicksCount = 0; LastDoubleClickIntervalMs = 0; IsLastClickDefective = false;
        ScrollUpCount = 0; ScrollDownCount = 0; LastScrollDelta = 0; ScrollJitterCount = 0;
        _lastScrollDirection = 0;
        LastClickLatencyMs = 0; TotalDistancePx = 0; MaxSpeedPxPerSec = 0;
        CurrentPollingRateHz = 0; PeakPollingRateHz = 0; AveragePollingRateHz = 0;
        _totalPollSamples = 0; _accumulatedHz = 0;
        _moveTimestamps.Clear();
        _lastMousePos = null;
        _lastButtonUpTimestamp.Clear();
        CpsTestClicks = 0; CpsScore = 0; CpsPeak = 0; CpsTimeRemainingSeconds = 0;
        StateChanged?.Invoke();
    }
}
