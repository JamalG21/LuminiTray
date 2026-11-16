using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32;

namespace LuminiTray;

public enum TaskbarBellHideMode
{
    Never = 0,
    WhenInactive = 1,
    WhenInactiveAndNoDnd = 2,
    Always = 3
}

public sealed record TaskbarTrayTweaksState
{
    public bool HideVolume { get; init; }
    public bool HideNetwork { get; init; }
    public bool HideBattery { get; init; }
    public bool GrayscaleBattery { get; init; }
    public bool HideMicrophone { get; init; }
    public bool HideGeolocation { get; init; }
    public bool HideStudioEffects { get; init; }
    public bool HideRecall { get; init; }
    public bool HideLanguageBar { get; init; }
    public bool HideLanguageSupplementary { get; init; }
    public TaskbarBellHideMode HideBellMode { get; init; } = TaskbarBellHideMode.Never;
    public int ShowDesktopWidth { get; init; } = -1; // -1 = padrão do Windows, 0..800 = largura customizada (0 oculta)
    public bool HideTaskView { get; init; }
    public bool HideWidgets { get; init; }

    public bool HasAnyActiveXamlTweak =>
        HideVolume || HideNetwork || HideBattery || GrayscaleBattery ||
        HideMicrophone || HideGeolocation || HideStudioEffects || HideRecall ||
        HideLanguageBar || HideLanguageSupplementary ||
        HideBellMode != TaskbarBellHideMode.Never || ShowDesktopWidth >= 0;
}

public sealed record TaskbarRegistryOp(
    string SubKey,
    string ValueName,
    int? SetDwordValue,
    bool DeleteWhenNull,
    bool RequiresExplorerRestart);

public sealed record TaskbarApplyResult(int ChangedCount, bool HookConnected, bool ExplorerRestarted, IReadOnlyList<string>? FailedValues = null);

/// <summary>
/// Controla os ícones da bandeja e barra de tarefas do Windows 11 usando:
/// 1. Injeção oficial via InitializeXamlDiagnosticsEx (Windows.UI.Xaml.dll) com LuminiTray.TaskbarHook.dll embutida
///    para manipular a árvore WinUI XAML do SystemTray.dll dentro do explorer.exe (sem PDBs nem dependências externas).
/// 2. Políticas nativas do Registro + SendMessageTimeout(WM_SETTINGCHANGE) para Visão de Tarefas e Widgets.
/// </summary>
public static class TaskbarTrayTweaksService
{
    private const string TweaksRegKey = @"Software\LuminiTray\TaskbarTweaks";
    private const string ExplorerAdvancedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
    private const string RefreshEventName = @"Local\LuminiTrayTaskbarTweaksRefresh_v3";
    public static readonly string[] DiagEndpointNames = { "VisualDiagConnection1", "VisualDiagConnection2" };
    private const string HookDllFileName = "LuminiTray.TaskbarHook.v3.dll";
    private static readonly Guid ClsidLuminiTaskbarDiag = new("7B8D4E21-9C4A-4B8F-A312-90D4E5F61234");

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(
        IntPtr hWnd, uint Msg, UIntPtr wParam, string lParam, uint fuFlags, uint uTimeout, out UIntPtr lpdwResult);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibraryW(string lpLibFileName);

    [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode)]
    private delegate int InitializeXamlDiagnosticsExDelegate(
        string endPointName,
        uint pid,
        string? wszDllXamlDiagnostics,
        string wszTAPDllName,
        [In] ref Guid tapClsid,
        string? wszInitializationData);

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
    private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

    private static readonly IntPtr HWND_BROADCAST = new(0xFFFF);
    private const uint WM_SETTINGCHANGE = 0x001A;
    private const uint SMTO_ABORTIFHUNG = 0x0002;

    public static TaskbarTrayTweaksState LoadCurrentFromRegistry()
    {
        int bell = ReadDword(TweaksRegKey, "HideBellMode") ?? 0;
        int width = ReadDword(TweaksRegKey, "ShowDesktopWidth") ?? -1;
        return new TaskbarTrayTweaksState
        {
            HideVolume = ReadDword(TweaksRegKey, "HideVolume") == 1,
            HideNetwork = ReadDword(TweaksRegKey, "HideNetwork") == 1,
            HideBattery = ReadDword(TweaksRegKey, "HideBattery") == 1,
            GrayscaleBattery = ReadDword(TweaksRegKey, "GrayscaleBattery") == 1,
            HideMicrophone = ReadDword(TweaksRegKey, "HideMicrophone") == 1,
            HideGeolocation = ReadDword(TweaksRegKey, "HideGeolocation") == 1,
            HideStudioEffects = ReadDword(TweaksRegKey, "HideStudioEffects") == 1,
            HideRecall = ReadDword(TweaksRegKey, "HideRecall") == 1,
            HideLanguageBar = ReadDword(TweaksRegKey, "HideLanguageBar") == 1,
            HideLanguageSupplementary = ReadDword(TweaksRegKey, "HideLanguageSupplementary") == 1,
            HideBellMode = bell is >= 0 and <= 3 ? (TaskbarBellHideMode)bell : TaskbarBellHideMode.Never,
            ShowDesktopWidth = width is >= 0 and <= 800 ? width : -1,
            HideTaskView = ReadDword(ExplorerAdvancedKey, "ShowTaskViewButton") == 0,
            HideWidgets = ReadDword(ExplorerAdvancedKey, "TaskbarDa") == 0
        };
    }

    public static List<TaskbarRegistryOp> BuildOperations(TaskbarTrayTweaksState current, TaskbarTrayTweaksState desired)
    {
        var ops = new List<TaskbarRegistryOp>();

        void AddBool(bool c, bool d, string name)
        {
            if (c != d) ops.Add(new(TweaksRegKey, name, d ? 1 : 0, false, false));
        }

        AddBool(current.HideVolume, desired.HideVolume, "HideVolume");
        AddBool(current.HideNetwork, desired.HideNetwork, "HideNetwork");
        AddBool(current.HideBattery, desired.HideBattery, "HideBattery");
        AddBool(current.GrayscaleBattery, desired.GrayscaleBattery, "GrayscaleBattery");
        AddBool(current.HideMicrophone, desired.HideMicrophone, "HideMicrophone");
        AddBool(current.HideGeolocation, desired.HideGeolocation, "HideGeolocation");
        AddBool(current.HideStudioEffects, desired.HideStudioEffects, "HideStudioEffects");
        AddBool(current.HideRecall, desired.HideRecall, "HideRecall");
        AddBool(current.HideLanguageBar, desired.HideLanguageBar, "HideLanguageBar");
        AddBool(current.HideLanguageSupplementary, desired.HideLanguageSupplementary, "HideLanguageSupplementary");

        if (current.HideBellMode != desired.HideBellMode)
            ops.Add(new(TweaksRegKey, "HideBellMode", (int)desired.HideBellMode, false, false));

        if (current.ShowDesktopWidth != desired.ShowDesktopWidth)
            ops.Add(new(TweaksRegKey, "ShowDesktopWidth", desired.ShowDesktopWidth >= 0 ? desired.ShowDesktopWidth : -1, false, false));

        if (current.HideTaskView != desired.HideTaskView)
            ops.Add(new(ExplorerAdvancedKey, "ShowTaskViewButton", desired.HideTaskView ? 0 : 1, false, false));

        if (current.HideWidgets != desired.HideWidgets)
            ops.Add(new(ExplorerAdvancedKey, "TaskbarDa", desired.HideWidgets ? 0 : 1, false, false));

        return ops;
    }

    public static TaskbarApplyResult Apply(TaskbarTrayTweaksState desired)
    {
        var current = LoadCurrentFromRegistry();
        var ops = BuildOperations(current, desired);

        // Cada valor é gravado isoladamente: chaves protegidas (ex.: TaskbarDa bloqueada pelo driver UCPD
        // no Windows 11 24H2+) não podem abortar o restante nem impedir a sinalização do hook.
        var failed = new List<string>();
        int applied = 0;
        foreach (var op in ops)
        {
            try
            {
                if (op.SetDwordValue is int val)
                {
                    using var key = Registry.CurrentUser.CreateSubKey(op.SubKey);
                    key?.SetValue(op.ValueName, val, RegistryValueKind.DWord);
                }
                applied++;
            }
            catch
            {
                failed.Add(op.ValueName);
            }
        }

        if (current.HideTaskView != desired.HideTaskView || current.HideWidgets != desired.HideWidgets)
        {
            try
            {
                SendMessageTimeout(HWND_BROADCAST, WM_SETTINGCHANGE, UIntPtr.Zero, "TraySettings", SMTO_ABORTIFHUNG, 1000, out _);
            }
            catch { }
        }

        bool hooked = false;
        try { hooked = EnsureHookAndSignalRefresh(); } catch { }
        return new TaskbarApplyResult(applied, hooked, false, failed);
    }

    public static void ApplyOnStartupIfActive()
    {
        var st = LoadCurrentFromRegistry();
        if (st.HasAnyActiveXamlTweak)
            EnsureHookAndSignalRefresh();
    }

    public static TaskbarApplyResult RestoreDefaults() => Apply(new TaskbarTrayTweaksState());

    public static bool EnsureHookAndSignalRefresh()
    {
        if (TrySignalRefresh())
            return true;

        string? dllPath = EnsureHookDllExtracted();
        if (string.IsNullOrEmpty(dllPath))
            return false;

        try
        {
            IntPtr hXaml = LoadLibraryW("Windows.UI.Xaml.dll");
            if (hXaml == IntPtr.Zero) return false;

            IntPtr pInit = GetProcAddress(hXaml, "InitializeXamlDiagnosticsEx");
            if (pInit == IntPtr.Zero) return false;

            var initFn = Marshal.GetDelegateForFunctionPointer<InitializeXamlDiagnosticsExDelegate>(pInit);
            var clsid = ClsidLuminiTaskbarDiag;

            bool anyOk = false;
            foreach (var proc in Process.GetProcessesByName("explorer"))
            {
                try
                {
                    foreach (var endpoint in DiagEndpointNames)
                    {
                        int hr = initFn(endpoint, (uint)proc.Id, null, dllPath, ref clsid, null);
                        if (hr >= 0)
                        {
                            anyOk = true;
                            break;
                        }
                    }
                }
                catch { }
                finally { proc.Dispose(); }
            }

            if (anyOk)
            {
                Thread.Sleep(250);
                TrySignalRefresh();
            }
            return anyOk || TrySignalRefresh();
        }
        catch
        {
            return false;
        }
    }

    private static bool TrySignalRefresh()
    {
        try
        {
            using var ev = EventWaitHandle.OpenExisting(RefreshEventName);
            ev.Set();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string? EnsureHookDllExtracted()
    {
        try
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LuminiTray");
            Directory.CreateDirectory(dir);
            string dest = Path.Combine(dir, HookDllFileName);

            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("LuminiTray.TaskbarHook.dll");
            if (stream == null)
            {
                string fallback = Path.Combine(AppContext.BaseDirectory, "Native", "LuminiTray.TaskbarHook.dll");
                return File.Exists(fallback) ? fallback : null;
            }

            bool needWrite = !File.Exists(dest) || new FileInfo(dest).Length != stream.Length;
            if (needWrite)
            {
                try
                {
                    using var fs = File.Create(dest);
                    stream.CopyTo(fs);
                }
                catch
                {
                    return File.Exists(dest) ? dest : null;
                }
            }
            return dest;
        }
        catch
        {
            return null;
        }
    }

    private static int? ReadDword(string subKey, string valueName)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(subKey, writable: false);
            object? raw = key?.GetValue(valueName);
            return raw is int i ? i : null;
        }
        catch
        {
            return null;
        }
    }
}
