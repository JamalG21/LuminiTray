using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;

namespace LuminiTray;

[Flags]
public enum RemapModifiers
{
    None = 0,
    Ctrl = 1,
    Alt = 2,
    Shift = 4,
    Win = 8
}

public sealed class KeyRemapRule
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public int FromVk { get; set; }
    public int ToVk { get; set; }
    public RemapModifiers ToModifiers { get; set; } = RemapModifiers.None;
    public bool IsDisabled { get; set; }
    public string TargetApp { get; set; } = "";
    public bool IsEnabled { get; set; } = true;
}

public sealed class ShortcutRemapRule
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public RemapModifiers FromModifiers { get; set; } = RemapModifiers.Ctrl;
    public int FromVk { get; set; }
    public RemapModifiers ToModifiers { get; set; } = RemapModifiers.None;
    public int ToVk { get; set; }
    public bool IsDisabled { get; set; }
    public string TargetApp { get; set; } = "";
    public bool IsEnabled { get; set; } = true;
}

public readonly record struct KeyOption(int Vk, string Name);

public readonly record struct RemapResolution(bool Matched, bool SuppressOnly, RemapModifiers TargetModifiers, int TargetVk);

/// <summary>
/// Motor de remapeamento de teclas e atalhos inspirado no PowerToys Keyboard Manager.
/// Usa hook global WH_KEYBOARD_LL + SendInput com dwExtraInfo exclusivo para evitar loops em trocas mútuas (A <-> B).
/// Consome 0% de CPU quando desativado ou sem regras ativas.
/// </summary>
public sealed class KeyboardRemapperService : IDisposable
{
    public static KeyboardRemapperService Instance { get; } = new();

    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;
    private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private static readonly UIntPtr RemapMagicTag = new(0x4C554D49); // "LUMI"

    private readonly object _lock = new();
    private List<KeyRemapRule> _keyRules = new();
    private List<ShortcutRemapRule> _shortcutRules = new();
    private HashSet<string> _excludedApps = new(StringComparer.OrdinalIgnoreCase);
    private bool _enabled;
    private volatile bool _pausedForCapture;

    private IntPtr _hookId = IntPtr.Zero;
    private LowLevelKeyboardProc? _proc;

    // Rastreia teclas físicas suprimidas no keydown para garantir que o keyup correspondente também seja tratado
    private readonly Dictionary<int, RemapResolution> _activeHeldRemaps = new();

    private KeyboardRemapperService() { }

    public bool IsHookActive => _hookId != IntPtr.Zero;

    public static IReadOnlyList<KeyOption> Catalog { get; } = BuildCatalog();

    private static List<KeyOption> BuildCatalog()
    {
        var list = new List<KeyOption>
        {
            new(0x1B, "Esc"),
            new(0x09, "Tab"),
            new(0x14, "Caps Lock"),
            new(0x20, "Espaço"),
            new(0x0D, "Enter"),
            new(0x08, "Backspace"),
            new(0x2E, "Delete"),
            new(0x2D, "Insert"),
            new(0x24, "Home"),
            new(0x23, "End"),
            new(0x21, "Page Up"),
            new(0x22, "Page Down"),
            new(0x2C, "Print Screen"),
            new(0x91, "Scroll Lock"),
            new(0x13, "Pause / Break"),
            new(0x5D, "Menu (Apps)"),
            new(0xA2, "Ctrl Esquerdo"),
            new(0xA3, "Ctrl Direito"),
            new(0xA0, "Shift Esquerdo"),
            new(0xA1, "Shift Direito"),
            new(0xA4, "Alt Esquerdo"),
            new(0xA5, "Alt Direito (AltGr)"),
            new(0x5B, "Win Esquerdo"),
            new(0x5C, "Win Direito"),
            new(0x26, "Seta Cima"),
            new(0x28, "Seta Baixo"),
            new(0x25, "Seta Esquerda"),
            new(0x27, "Seta Direita"),
            new(0xAD, "Mídia: Mudo"),
            new(0xAE, "Mídia: Volume -"),
            new(0xAF, "Mídia: Volume +"),
            new(0xB3, "Mídia: Play/Pause"),
            new(0xB0, "Mídia: Próxima"),
            new(0xB1, "Mídia: Anterior"),
        };

        for (int vk = 0x70; vk <= 0x87; vk++) // F1..F24
            list.Add(new(vk, $"F{vk - 0x6F}"));

        for (int vk = 0x30; vk <= 0x39; vk++) // 0..9
            list.Add(new(vk, ((char)vk).ToString()));

        for (int vk = 0x41; vk <= 0x5A; vk++) // A..Z
            list.Add(new(vk, ((char)vk).ToString()));

        for (int vk = 0x60; vk <= 0x69; vk++) // NumPad 0..9
            list.Add(new(vk, $"NumPad {vk - 0x60}"));

        list.Add(new(0xBA, "; / Ç"));
        list.Add(new(0xBB, "= / +"));
        list.Add(new(0xBC, ", / <"));
        list.Add(new(0xBD, "- / _"));
        list.Add(new(0xBE, ". / >"));
        list.Add(new(0xBF, "/ / ?"));
        list.Add(new(0xC0, "' / ~"));
        list.Add(new(0xDB, "[ / ´"));
        list.Add(new(0xDC, "\\ / |"));
        list.Add(new(0xDD, "] / ["));
        list.Add(new(0xDE, "~ / ^"));

        return list;
    }

    public static string FormatKeyName(int vk)
    {
        if (vk <= 0) return "Desativada";
        foreach (var opt in Catalog)
        {
            if (opt.Vk == vk) return opt.Name;
        }
        return $"VK 0x{vk:X2}";
    }

    public static string FormatModifiers(RemapModifiers mods)
    {
        if (mods == RemapModifiers.None) return "";
        var parts = new List<string>(4);
        if (mods.HasFlag(RemapModifiers.Ctrl)) parts.Add("Ctrl");
        if (mods.HasFlag(RemapModifiers.Alt)) parts.Add("Alt");
        if (mods.HasFlag(RemapModifiers.Shift)) parts.Add("Shift");
        if (mods.HasFlag(RemapModifiers.Win)) parts.Add("Win");
        return string.Join(" + ", parts);
    }

    public static string FormatShortcut(RemapModifiers mods, int vk, bool isDisabled = false)
    {
        if (isDisabled) return "Desativada";
        string m = FormatModifiers(mods);
        string k = FormatKeyName(vk);
        return string.IsNullOrEmpty(m) ? k : $"{m} + {k}";
    }

    public void PauseForCapture(bool paused) => _pausedForCapture = paused;

    public static string NormalizeProcessName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "";
        string clean = name.Trim();
        if (clean.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            clean = clean[..^4];
        return clean;
    }

    public static bool IsTargetAppMatch(string? targetApp, string? activeProcessName)
    {
        if (string.IsNullOrWhiteSpace(targetApp)) return true;
        string target = NormalizeProcessName(targetApp);
        string proc = NormalizeProcessName(activeProcessName);
        return !string.IsNullOrEmpty(proc) && proc.Equals(target, StringComparison.OrdinalIgnoreCase);
    }

    public void UpdateRules(
        bool enabled,
        IEnumerable<KeyRemapRule>? keyRules,
        IEnumerable<ShortcutRemapRule>? shortcutRules,
        IEnumerable<string>? excludedApps = null)
    {
        lock (_lock)
        {
            _enabled = enabled;
            _keyRules = keyRules?.Where(r => r != null).ToList() ?? new List<KeyRemapRule>();
            _shortcutRules = shortcutRules?.Where(r => r != null).ToList() ?? new List<ShortcutRemapRule>();
            _excludedApps = new HashSet<string>(
                (excludedApps ?? Enumerable.Empty<string>())
                .Select(NormalizeProcessName)
                .Where(n => !string.IsNullOrEmpty(n)),
                StringComparer.OrdinalIgnoreCase);

            bool shouldRun = _enabled && (_keyRules.Any(r => r.IsEnabled) || _shortcutRules.Any(r => r.IsEnabled));
            if (shouldRun)
            {
                EnsureHookStarted();
            }
            else
            {
                StopHook();
            }
        }
    }

    public static RemapResolution TryResolveRemap(
        int vk,
        RemapModifiers currentModifiers,
        string? activeProcessName,
        IReadOnlyList<KeyRemapRule> keyRules,
        IReadOnlyList<ShortcutRemapRule> shortcutRules,
        IReadOnlyCollection<string>? excludedApps = null)
    {
        if (excludedApps != null && excludedApps.Count > 0)
        {
            string proc = NormalizeProcessName(activeProcessName);
            if (!string.IsNullOrEmpty(proc))
            {
                foreach (var ex in excludedApps)
                {
                    if (proc.Equals(NormalizeProcessName(ex), StringComparison.OrdinalIgnoreCase))
                        return new RemapResolution(false, false, RemapModifiers.None, 0);
                }
            }
        }

        int normalizedVk = NormalizeSideModifier(vk);

        // 1. Atalhos têm precedência sobre remapeamento de tecla única
        if (currentModifiers != RemapModifiers.None)
        {
            foreach (var sr in shortcutRules)
            {
                if (!sr.IsEnabled) continue;
                if (sr.FromModifiers != currentModifiers) continue;
                if (sr.FromVk != vk && sr.FromVk != normalizedVk) continue;
                if (!IsTargetAppMatch(sr.TargetApp, activeProcessName)) continue;

                return new RemapResolution(true, sr.IsDisabled || sr.ToVk <= 0, sr.ToModifiers, sr.ToVk);
            }
        }

        // 2. Remapeamento de tecla única
        foreach (var kr in keyRules)
        {
            if (!kr.IsEnabled) continue;
            if (kr.FromVk != vk) continue;
            if (!IsTargetAppMatch(kr.TargetApp, activeProcessName)) continue;

            return new RemapResolution(true, kr.IsDisabled || kr.ToVk <= 0, kr.ToModifiers, kr.ToVk);
        }

        return new RemapResolution(false, false, RemapModifiers.None, 0);
    }

    private static int NormalizeSideModifier(int vk) => vk switch
    {
        0xA2 or 0xA3 => 0x11, // LCtrl/RCtrl -> VK_CONTROL
        0xA0 or 0xA1 => 0x10, // LShift/RShift -> VK_SHIFT
        0xA4 or 0xA5 => 0x12, // LAlt/RAlt -> VK_MENU
        _ => vk
    };

    private void EnsureHookStarted()
    {
        if (_hookId != IntPtr.Zero) return;
        _proc = HookCallback;
        using var curProcess = Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule;
        IntPtr hMod = curModule != null ? GetModuleHandle(curModule.ModuleName) : IntPtr.Zero;
        _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, hMod, 0);
    }

    private void StopHook()
    {
        _activeHeldRemaps.Clear();
        if (_hookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && !_pausedForCapture && _enabled)
        {
            var kb = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);

            // Ignora teclas injetadas pelo próprio LuminiTray (anti-loop em trocas A <-> B)
            if (kb.dwExtraInfo == RemapMagicTag)
                return CallNextHookEx(_hookId, nCode, wParam, lParam);

            int msg = wParam.ToInt32();
            bool isKeyDown = msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN;
            bool isKeyUp = msg == WM_KEYUP || msg == WM_SYSKEYUP;
            int vk = (int)kb.vkCode;

            if (isKeyDown)
            {
                if (_activeHeldRemaps.TryGetValue(vk, out var held))
                {
                    if (!held.SuppressOnly)
                        SendRemappedKey(held.TargetModifiers, held.TargetVk, isUp: false);
                    return (IntPtr)1;
                }

                var mods = GetCurrentModifiers(vk);
                bool needsFgProc;
                lock (_lock)
                {
                    needsFgProc = _excludedApps.Count > 0
                        || _keyRules.Any(k => k.IsEnabled && !string.IsNullOrWhiteSpace(k.TargetApp))
                        || _shortcutRules.Any(s => s.IsEnabled && !string.IsNullOrWhiteSpace(s.TargetApp));
                }
                string? fgProc = needsFgProc ? GetForegroundProcessName() : null;

                RemapResolution res;
                lock (_lock)
                {
                    res = TryResolveRemap(vk, mods, fgProc, _keyRules, _shortcutRules, _excludedApps);
                }

                if (res.Matched)
                {
                    _activeHeldRemaps[vk] = res;
                    if (!res.SuppressOnly)
                    {
                        SendRemappedKey(res.TargetModifiers, res.TargetVk, isUp: false);
                    }
                    return (IntPtr)1;
                }
            }
            else if (isKeyUp)
            {
                if (_activeHeldRemaps.TryGetValue(vk, out var held))
                {
                    _activeHeldRemaps.Remove(vk);
                    if (!held.SuppressOnly)
                    {
                        SendRemappedKey(held.TargetModifiers, held.TargetVk, isUp: true);
                    }
                    return (IntPtr)1;
                }
            }
        }

        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    private static RemapModifiers GetCurrentModifiers(int currentVk)
    {
        RemapModifiers mods = RemapModifiers.None;
        if (currentVk is not (0xA2 or 0xA3 or 0x11) && ((GetAsyncKeyState(0x11) & 0x8000) != 0))
            mods |= RemapModifiers.Ctrl;
        if (currentVk is not (0xA4 or 0xA5 or 0x12) && ((GetAsyncKeyState(0x12) & 0x8000) != 0))
            mods |= RemapModifiers.Alt;
        if (currentVk is not (0xA0 or 0xA1 or 0x10) && ((GetAsyncKeyState(0x10) & 0x8000) != 0))
            mods |= RemapModifiers.Shift;
        if (currentVk is not (0x5B or 0x5C) && (((GetAsyncKeyState(0x5B) & 0x8000) != 0) || ((GetAsyncKeyState(0x5C) & 0x8000) != 0)))
            mods |= RemapModifiers.Win;
        return mods;
    }

    private static string? GetForegroundProcessName()
    {
        try
        {
            IntPtr hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return null;
            GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == 0) return null;
            using var proc = Process.GetProcessById((int)pid);
            return proc.ProcessName;
        }
        catch
        {
            return null;
        }
    }

    private static void SendRemappedKey(RemapModifiers targetMods, int targetVk, bool isUp)
    {
        var inputs = new List<INPUT>(6);

        if (!isUp)
        {
            AppendModInputs(inputs, targetMods, keyUp: false);
            inputs.Add(CreateKeyInput(targetVk, keyUp: false));
        }
        else
        {
            inputs.Add(CreateKeyInput(targetVk, keyUp: true));
            AppendModInputs(inputs, targetMods, keyUp: true);
        }

        if (inputs.Count > 0)
        {
            SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<INPUT>());
        }
    }

    private static void AppendModInputs(List<INPUT> inputs, RemapModifiers mods, bool keyUp)
    {
        if (mods.HasFlag(RemapModifiers.Ctrl)) inputs.Add(CreateKeyInput(0xA2, keyUp));
        if (mods.HasFlag(RemapModifiers.Alt)) inputs.Add(CreateKeyInput(0xA4, keyUp));
        if (mods.HasFlag(RemapModifiers.Shift)) inputs.Add(CreateKeyInput(0xA0, keyUp));
        if (mods.HasFlag(RemapModifiers.Win)) inputs.Add(CreateKeyInput(0x5B, keyUp));
    }

    private static INPUT CreateKeyInput(int vk, bool keyUp)
    {
        uint flags = keyUp ? KEYEVENTF_KEYUP : 0u;
        if (IsExtendedKey(vk)) flags |= KEYEVENTF_EXTENDEDKEY;

        return new INPUT
        {
            type = 1, // INPUT_KEYBOARD
            u = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = (ushort)vk,
                    wScan = 0,
                    dwFlags = flags,
                    time = 0,
                    dwExtraInfo = RemapMagicTag
                }
            }
        };
    }

    private static bool IsExtendedKey(int vk) => vk is
        0xA3 or 0xA5 or 0x5B or 0x5C or 0x5D or
        0x21 or 0x22 or 0x23 or 0x24 or
        0x25 or 0x26 or 0x27 or 0x28 or
        0x2D or 0x2E or 0x2C or 0x6F;

    public void Dispose()
    {
        lock (_lock)
        {
            StopHook();
        }
    }

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion u;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx, dy;
        public uint mouseData, dwFlags, time;
        public UIntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HARDWAREINPUT
    {
        public uint uMsg;
        public ushort wParamL, wParamH;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);
}
