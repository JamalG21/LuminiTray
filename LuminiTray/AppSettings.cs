using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LuminiTray;

public sealed record BenchRun(DateTime Date, double? Multi, double? Single, bool Sustained, string Cpu);

/// <summary>Resultado dos benchmarks de jogo (Test = "gpu", "sim" ou "mem"). Score é usado para a barra do histórico.</summary>
public sealed record BenchRecord(DateTime Date, string Test, double Score, string Summary);

/// <summary>Programa extra que abre antes do jogo (ex.: ferramenta de comando/monitoramento) e pode fechar junto com ele.</summary>
public sealed class HelperProgram
{
    public string Path { get; set; } = "";
    public string Args { get; set; } = "";
    public bool CloseWithGame { get; set; } = true;
    public string Display => System.IO.Path.GetFileName(Path) + (string.IsNullOrWhiteSpace(Args) ? "" : " " + Args);
}

/// <summary>Perfil de um jogo na aba LUMINI JOGOS: desempenho, argumentos de gráficos e programas auxiliares.</summary>
public sealed class GameProfile
{
    public string Priority { get; set; } = "normal";   // normal | above | high
    public bool HighPerfPlan { get; set; }
    public bool GpuHigh { get; set; }
    public bool DisableFso { get; set; }
    public string? ExePath { get; set; }
    public string WinMode { get; set; } = "default";   // default | full | borderless | window
    public string Res { get; set; } = "default";       // default | 1280x720 | 1920x1080 | 2560x1440 | 3840x2160
    public int Hz { get; set; }                        // 0 = não definir
    public string Style { get; set; } = "source";      // source | unity | unreal
    public string Extra { get; set; } = "";
    public List<HelperProgram> Helpers { get; set; } = new();
}

/// <summary>Jogo adicionado à mão (qualquer .exe).</summary>
public sealed class CustomGame
{
    public string Name { get; set; } = "";
    public string Exe { get; set; } = "";
    public string Args { get; set; } = "";
}

/// <summary>Snapshot das configurações ativas antes de ligar o Modo Baixo Consumo para restauração fiel.</summary>
public sealed class LowPowerSnapshot
{
    public bool IsSaved { get; set; } = false;
    public bool TaskbarVolumeScroll { get; set; }
    public bool PlaybackMute { get; set; }
    public bool MicMute { get; set; }
    public bool PlaybackCycle { get; set; }
    public bool RecordingCycle { get; set; }
    public bool SuppressorMaster { get; set; }
}

/// <summary>Configurações em %AppData%\LuminiTray. A chave da API é cifrada com DPAPI (só este usuário do Windows a lê).</summary>
public sealed class AppSettings
{
    public static string Folder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LuminiTray");
    private static string FilePath => Path.Combine(Folder, "settings.json");

    public string? HowManyFpsKeyProtected { get; set; }

    // Modo Baixo Consumo (Otimização Extrema de Recursos)
    public bool LowPowerMode { get; set; } = false;
    public LowPowerSnapshot LowPowerPreviousState { get; set; } = new();

    // Inicialização automática com o Windows e Funções Automáticas
    public bool AutoStartWithWindows { get; set; }
    public bool AutoStartMinimized { get; set; } = true;
    public bool AutoApplyTweaks { get; set; } = true;
    public bool AutoCleanRam { get; set; } = true;
    public bool AutoCloseBackgroundApps { get; set; } = false;
    public DateTime? LastAutoFeaturesApplied { get; set; }
    public string? LastAutoFeaturesSummary { get; set; }

    // Otimização Automática Programada (Ajustes de Desempenho / RAM / Processos)
    public bool AutoOptimizeEnabled { get; set; } = false;
    public int AutoOptimizeIntervalMinutes { get; set; } = 15;
    public bool AutoOptimizeRam { get; set; } = true;
    public bool AutoOptimizeProcesses { get; set; } = true;
    public bool AutoOptimizeCleanTemp { get; set; } = false;
    public List<string> AutoOptimizeExclusions { get; set; } = new()
    {
        "discord.exe", "steam.exe", "spotify.exe", "chrome.exe", "obs64.exe"
    };
    public DateTime? LastAutoOptimizeRun { get; set; }
    public string? LastAutoOptimizeSummary { get; set; }
    public int AutoOptimizeRunCount { get; set; }

    // Wallpapers
    public string? WallpaperFolder { get; set; }
    public string WallpaperStyle { get; set; } = "fill"; // fill | fit | stretch | center
    public int WallpaperIntervalMinutes { get; set; } = 0; // 0 = desligado, 5, 15, 30, 60...
    public bool AutoChangeWallpaperOnStartup { get; set; }
    public string? LastWallpaperPath { get; set; }
    public string SelectedWallpaperMonitorId { get; set; } = "ALL";
    public Dictionary<string, string> StaticWallpaperMonitorPaths { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    // Lively Wallpaper (Motor 2.2.1.0)
    public bool LivelyWallpaperEnabled { get; set; } = false; // Padrão DESATIVADO (Mínimo Consumo)
    public string? LivelyWallpaperCurrentPath { get; set; }
    public Dictionary<string, string> LivelyWallpaperMonitorPaths { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public bool LivelyWallpaperAutoPauseGames { get; set; } = true;
    public bool LivelyWallpaperPauseOnFocusedApp { get; set; } = false; // Se true, pausa mesmo em modo janela quando outro app tiver foco
    public List<string> LivelyWallpaperPlaylist { get; set; } = new(); // Lista persistente de vídeos para rotação
    public int LivelyWallpaperVolume { get; set; } = 0; // Padrão MUDO (0%)
    public int LivelyWallpaperIntervalMinutes { get; set; } = 0; // 0 = desligado
    public int LivelyWallpaperSettingsVersion { get; set; } = 0; // Versao para migracao automatica de ajustes

    // LUMINI BENCH: últimos resultados (mais recente primeiro)
    public List<BenchRun> BenchRuns { get; set; } = new();
    public List<BenchRecord> BenchRecords { get; set; } = new();

    // LUMINI OTIMIZA: valores originais dos ajustes do Windows antes da primeira alteração ("<ausente>" = não existia)
    public Dictionary<string, string?> TweakBackup { get; set; } = new();
    /// <summary>ComDB original (reservas de números COM) antes da primeira "liberação".</summary>
    public byte[]? ComDbBackup { get; set; }
    /// <summary>Programas de inicialização removidos, para "Restaurar removidos".</summary>
    public List<StartupBackup> StartupRemoved { get; set; } = new();

    // LUMINI JOGOS
    public Dictionary<string, GameProfile> GameProfiles { get; set; } = new();
    public List<CustomGame> CustomGames { get; set; } = new();

    // Aparência e Cores
    public bool LightTheme { get; set; }
    public string AccentColorTheme { get; set; } = "Monochrome";

    // Comportamento & Bandeja do Sistema
    public bool MinimizeToTrayOnClose { get; set; } = true;
    public bool StartMinimizedToTray { get; set; } = false;
    public bool ShowMicTrayIcon { get; set; } = true;
    public bool ShowAudioTrayIcon { get; set; } = true;
    public bool ShowMiniTaskTrayIcon { get; set; } = true;
    public int SensorIntervalMs { get; set; } = 1000;

    // Supressor de Ruído
    public bool SuppressorMasterEnabled { get; set; } = false;
    public float SuppressorIntensity { get; set; } = 1.0f;
    public float SuppressorGainDb { get; set; } = 0.0f;
    public float SuppressorGateThresholdDb { get; set; } = -52.0f;
    public float SuppressorGateReleaseMs { get; set; } = 120.0f;
    public float SuppressorLowEqDb { get; set; } = 0.0f;
    public float SuppressorMidEqDb { get; set; } = 0.0f;
    public float SuppressorHighEqDb { get; set; } = 0.0f;
    public string? SuppressorSelectedInputId { get; set; }
    public string? SuppressorSelectedOutputId { get; set; }
    public string? SuppressorVirtualMicDeviceId { get; set; }
    public bool SuppressorListenSelf { get; set; } = false;
    public int SuppressorMonitorMode { get; set; } = 0; // 0 = Processed, 1 = Raw
    public float SuppressorVadThreshold { get; set; } = 0.35f;   // probabilidade de voz (RNNoise) que mantém o gate aberto
    public float SuppressorVadHoldMs { get; set; } = 180.0f;     // "VAD grace" estilo EasyEffects
    public bool SuppressorLimiterEnabled { get; set; } = true;   // limitador suave anti-estouro
    public bool SuppressorDeesserEnabled { get; set; } = true;   // atenuador de sibilância

    // Overlay & Atalhos Globais
    public string OverlayHotkeyStr { get; set; } = "Ctrl + Shift + O";
    public uint OverlayHotkeyModifiers { get; set; } = 0x0002 | 0x0004 | 0x4000; // Ctrl + Shift + NoRepeat
    public uint OverlayHotkeyVk { get; set; } = 0x4F; // VK_O

    public string MiniTaskHotkeyStr { get; set; } = "Ctrl + Shift + P";
    public uint MiniTaskHotkeyModifiers { get; set; } = 0x0002 | 0x0004 | 0x4000; // Ctrl + Shift + NoRepeat
    public uint MiniTaskHotkeyVk { get; set; } = 0x50; // VK_P
    public bool HotkeyMigratedV21 { get; set; } = false;

    public string OverlayCorner { get; set; } = "TL";
    public double OverlayScale { get; set; } = 1.0;
    public double OverlayWidth { get; set; } = 240;
    public double OverlayHeight { get; set; } = 0;
    public double OverlayOpacity { get; set; } = 1.0;
    public double OverlayBackgroundOpacity { get; set; } = 0.85; // Opacidade do fundo (Box.Background) sem alterar o texto
    /// <summary>Itens escolhidos para o overlay (chaves de OverlayMetrics); null = padrão.</summary>
    public List<string>? OverlayItems { get; set; }
    public bool OverlayCustomPosition { get; set; }
    public double OverlayCustomX { get; set; } = 20;
    public double OverlayCustomY { get; set; } = 20;
    public bool OverlayLocked { get; set; } = false; // Padrão livre para arrastar

    // Mini Gerenciador de Tarefas (Mesmas funcionalidades do Overlay)
    public bool MiniTaskVisible { get; set; } = false;
    public string MiniTaskCorner { get; set; } = "BR";
    public double MiniTaskScale { get; set; } = 1.0;
    public double MiniTaskWidth { get; set; } = 380;
    public double MiniTaskHeight { get; set; } = 240;
    public double MiniTaskOpacity { get; set; } = 1.0;
    public double MiniTaskBackgroundOpacity { get; set; } = 0.90;
    public bool MiniTaskCustomPosition { get; set; } = false;
    public double MiniTaskCustomX { get; set; } = 0;
    public double MiniTaskCustomY { get; set; } = 0;
    public bool MiniTaskLocked { get; set; } = false;

    // LUMINI FANS: persistência de curvas e travas de canais (ex: bomba de watercooler 100%)
    public FanControlSaveSettings FanControl { get; set; } = new();

    [System.Text.Json.Serialization.JsonIgnore] // nunca gravar a chave em texto puro
    public string? HowManyFpsKey
    {
        get
        {
            if (string.IsNullOrEmpty(HowManyFpsKeyProtected)) return null;
            try
            {
                var bytes = ProtectedData.Unprotect(Convert.FromBase64String(HowManyFpsKeyProtected), null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(bytes);
            }
            catch { return null; }
        }
        set
        {
            HowManyFpsKeyProtected = string.IsNullOrWhiteSpace(value)
                ? null
                : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value.Trim()), null, DataProtectionScope.CurrentUser));
        }
    }

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
                if (!loaded.HotkeyMigratedV21)
                {
                    loaded.MiniTaskHotkeyStr = "Ctrl + Shift + P";
                    loaded.MiniTaskHotkeyModifiers = 0x0002 | 0x0004 | 0x4000;
                    loaded.MiniTaskHotkeyVk = 0x50;
                    loaded.HotkeyMigratedV21 = true;
                    loaded.Save();
                }
                return loaded;
            }
        }
        catch { }
        return new AppSettings();
    }

    public void Save()
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}

public class FanControlSaveSettings
{
    public bool IsControlActive { get; set; } = true;
    public Dictionary<string, FanChannelSaveState> Channels { get; set; } = new();
    public FanCurveSaveConfig? CpuCurve { get; set; }
    public FanCurveSaveConfig? GpuCurve { get; set; }
    public List<FanCurveSaveConfig> SavedCpuPresets { get; set; } = new();
    public List<FanCurveSaveConfig> SavedGpuPresets { get; set; } = new();
}

public class FanChannelSaveState
{
    public bool IsLocked100 { get; set; }
    public bool IsManual { get; set; }
    public float TargetPwm { get; set; }
}

public class FanCurveSaveConfig
{
    public string Name { get; set; } = "Personalizada";
    public string TempSource { get; set; } = "Cpu";
    public double RampUpSeconds { get; set; } = 4.0;
    public double RampDownSeconds { get; set; } = 8.0;
    public List<FanCurvePointSave> Points { get; set; } = new();
}

public class FanCurvePointSave
{
    public double Temp { get; set; }
    public double Pwm { get; set; }

    public FanCurvePointSave() { }
    public FanCurvePointSave(double temp, double pwm)
    {
        Temp = temp;
        Pwm = pwm;
    }
}
