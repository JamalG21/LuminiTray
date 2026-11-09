using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LuminiTray;

public enum AudioMicMode
{
    Toggle = 0,
    PushToTalk = 1,
    PushToMute = 2,
    HybridPtt = 3,
    SeparateHotkeys = 4
}

public sealed class AudioHotkeyModel
{
    public int VirtualKey { get; set; } = 0;
    public bool Ctrl { get; set; } = false;
    public bool Alt { get; set; } = false;
    public bool Shift { get; set; } = false;
    public bool Win { get; set; } = false;
    public bool IsEnabled { get; set; } = true;

    [JsonIgnore]
    public bool IsEmpty => VirtualKey == 0;

    public AudioHotkeyModel() { }

    public AudioHotkeyModel(int vk, bool ctrl = false, bool alt = false, bool shift = false, bool win = false)
    {
        VirtualKey = vk;
        Ctrl = ctrl;
        Alt = alt;
        Shift = shift;
        Win = win;
        IsEnabled = true;
    }

    public override string ToString()
    {
        if (IsEmpty) return "Nenhum";

        var parts = new List<string>();
        if (Ctrl) parts.Add("Ctrl");
        if (Alt) parts.Add("Alt");
        if (Shift) parts.Add("Shift");
        if (Win) parts.Add("Win");

        parts.Add(FormatVk(VirtualKey));
        return string.Join(" + ", parts);
    }

    public static string FormatVk(int vk)
    {
        if (vk >= 0x70 && vk <= 0x87) // F1..F24
            return $"F{vk - 0x70 + 1}";

        return vk switch
        {
            0x08 => "Backspace",
            0x09 => "Tab",
            0x0D => "Enter",
            0x13 => "Pause",
            0x14 => "CapsLock",
            0x1B => "Esc",
            0x20 => "Space",
            0x21 => "PageUp",
            0x22 => "PageDown",
            0x23 => "End",
            0x24 => "Home",
            0x25 => "Left",
            0x26 => "Up",
            0x27 => "Right",
            0x28 => "Down",
            0x2C => "PrintScreen",
            0x2D => "Insert",
            0x2E => "Delete",
            0x5B or 0x5C => "Win",
            0x60 => "Num 0",
            0x61 => "Num 1",
            0x62 => "Num 2",
            0x63 => "Num 3",
            0x64 => "Num 4",
            0x65 => "Num 5",
            0x66 => "Num 6",
            0x67 => "Num 7",
            0x68 => "Num 8",
            0x69 => "Num 9",
            0x6A => "Num *",
            0x6B => "Num +",
            0x6D => "Num -",
            0x6E => "Num .",
            0x6F => "Num /",
            0x90 => "NumLock",
            0x91 => "ScrollLock",
            0xBA => ";",
            0xBB => "=",
            0xBC => ",",
            0xBD => "-",
            0xBE => ".",
            0xBF => "/",
            0xC0 => "`",
            0xDB => "[",
            0xDC => "\\",
            0xDD => "]",
            0xDE => "'",
            _ => (vk >= 0x30 && vk <= 0x39) || (vk >= 0x41 && vk <= 0x5A)
                ? ((char)vk).ToString()
                : $"0x{vk:X2}"
        };
    }
}

public sealed class AudioSettings
{
    private static readonly string SettingsFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LuminiTray");
    private static readonly string SettingsFilePath = Path.Combine(SettingsFolder, "audio_settings.json");

    public List<string> PlaybackCycleDeviceIds { get; set; } = new();
    public List<string> RecordingCycleDeviceIds { get; set; } = new();
    public List<string> DisabledPlaybackDeviceIds { get; set; } = new();
    public List<string> DisabledRecordingDeviceIds { get; set; } = new();

    public bool ChangeCommunications { get; set; } = true;
    public bool KeepVolumeEnabled { get; set; } = false;

    public AudioMicMode MicMode { get; set; } = AudioMicMode.Toggle;
    public int PttDelayMs { get; set; } = 200;
    public int AfkTimeoutMinutes { get; set; } = 0; // 0 = desativado
    public bool VolumeLockEnabled { get; set; } = false;
    public int VolumeLockPercent { get; set; } = 100;

    public bool OverlayEnabled { get; set; } = false;
    public bool OverlayOnlyWhenMuted { get; set; } = false;
    public double OverlayPositionX { get; set; } = 60;
    public double OverlayPositionY { get; set; } = 60;
    public bool MicOverlayDefaultMigratedV21 { get; set; } = false;

    public bool SoundFeedback { get; set; } = true;
    public int SoundFeedbackVolume { get; set; } = 50; // 0 a 100%
    public bool OsdFeedback { get; set; } = true;
    public bool Passthrough { get; set; } = true;

    // Controle de Atalhos Globais e Mod Windhawk Taskbar Volume
    public bool EnableGlobalHotkeys { get; set; } = true;
    public bool EnableMicMuteFeature { get; set; } = true; // Habilita ou desabilita o atalho e funcionalidade global de mic mute
    public bool EnablePlaybackMuteFeature { get; set; } = true; // Habilita ou desabilita atalho/ação de mutar som
    public bool EnablePlaybackCycleFeature { get; set; } = true; // Habilita ou desabilita alternar saídas de áudio
    public bool EnableRecordingCycleFeature { get; set; } = true; // Habilita ou desabilita alternar microfones
    public bool EnableTaskbarVolumeScroll { get; set; } = true;

    [JsonIgnore]
    public bool HasAnyActiveHotkey =>
        EnableGlobalHotkeys && (
            (EnablePlaybackCycleFeature && HotkeyPlaybackCycle != null && HotkeyPlaybackCycle.IsEnabled && !HotkeyPlaybackCycle.IsEmpty) ||
            (EnableRecordingCycleFeature && HotkeyRecordingCycle != null && HotkeyRecordingCycle.IsEnabled && !HotkeyRecordingCycle.IsEmpty) ||
            (EnablePlaybackMuteFeature && HotkeyPlaybackMute != null && HotkeyPlaybackMute.IsEnabled && !HotkeyPlaybackMute.IsEmpty) ||
            (EnableMicMuteFeature && HotkeyMicMute != null && HotkeyMicMute.IsEnabled && !HotkeyMicMute.IsEmpty)
        );
    public int TaskbarVolumeScrollStep { get; set; } = 2; // 1% a 10%
    public string TaskbarPerAppModifierKey { get; set; } = "Ctrl Esquerdo"; // "Ctrl Esquerdo", "Ctrl Direito", "Ctrl", "Shift", "Alt", "Win", "Nenhum"
    public string TaskbarPerAppTargetMode { get; set; } = "Player Atual em Execução"; // "Player Atual em Execução", "Janela Ativa em Primeiro Plano", "App Específico Configurado"
    public string TaskbarSpecificAppName { get; set; } = "";
    public bool InvertTaskbarModifier { get; set; } = false;
    public bool MiddleClickToMute { get; set; } = true;
    public bool ScrollAnywhereWithModifier { get; set; } = false;
    public bool DisableTaskbarVolumeInFullscreen { get; set; } = true; // Proteção contra ativação acidental em jogos (Valorant, CS2, etc.)
    public List<string> ExcludedPlayerApps { get; set; } = new();

    public bool IsAppExcluded(string? processOrAppName)
    {
        if (string.IsNullOrWhiteSpace(processOrAppName) || ExcludedPlayerApps == null || ExcludedPlayerApps.Count == 0)
            return false;

        string clean = processOrAppName.Trim();
        if (clean.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            clean = clean[..^4];

        foreach (var exc in ExcludedPlayerApps)
        {
            if (string.IsNullOrWhiteSpace(exc)) continue;
            string cleanExc = exc.Trim();
            if (cleanExc.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                cleanExc = cleanExc[..^4];

            if (string.Equals(clean, cleanExc, StringComparison.OrdinalIgnoreCase))
                return true;
            if (string.Equals(processOrAppName.Trim(), exc.Trim(), StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    // Atalhos Padrão (compatíveis com SoundSwitch e AHK_MicMute)
    // Playback Cycle: Ctrl + Alt + F11 (0x7A)
    public AudioHotkeyModel HotkeyPlaybackCycle { get; set; } = new(0x7A, ctrl: true, alt: true);

    // Recording Cycle: Ctrl + Alt + F7 (0x76)
    public AudioHotkeyModel HotkeyRecordingCycle { get; set; } = new(0x76, ctrl: true, alt: true);

    // Playback Mute: Ctrl + Alt + O (0x4F)
    public AudioHotkeyModel HotkeyPlaybackMute { get; set; } = new(0x4F, ctrl: true, alt: true);

    // Mic Action (Toggle / PTT / PTM / HybridPTT / Mute Separado): Ctrl + Alt + M (0x4D)
    public AudioHotkeyModel HotkeyMicMute { get; set; } = new(0x4D, ctrl: true, alt: true);

    // Mic Unmute Separado (opcional quando MicMode == SeparateHotkeys)
    public AudioHotkeyModel HotkeyMicUnmute { get; set; } = new(0);

    public static AudioSettings Load()
    {
        try
        {
            if (File.Exists(SettingsFilePath))
            {
                var json = File.ReadAllText(SettingsFilePath);
                var settings = JsonSerializer.Deserialize<AudioSettings>(json);
                if (settings != null)
                {
                    if (!settings.MicOverlayDefaultMigratedV21)
                    {
                        settings.OverlayEnabled = false;
                        settings.MicOverlayDefaultMigratedV21 = true;
                        settings.Save();
                    }
                    return settings;
                }
            }
        }
        catch
        {
            // Fallback seguro em caso de arquivo corrompido
        }

        var def = new AudioSettings { OverlayEnabled = false, MicOverlayDefaultMigratedV21 = true };
        def.Save();
        return def;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(SettingsFolder);
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsFilePath, json);
        }
        catch
        {
            // Silencioso em caso de permissão restrita
        }
    }
}
