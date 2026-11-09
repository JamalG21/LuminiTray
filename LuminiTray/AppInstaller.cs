using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Win32;

namespace LuminiTray;

/// <summary>App do catálogo "Instalar apps" (pós-formatação), instalado pelo winget.</summary>
public sealed class InstallableApp : INotifyPropertyChanged
{
    public string Name { get; init; } = "";
    public string Category { get; init; } = "Utilitários";
    public string Description { get; init; } = "";
    public string Publisher { get; init; } = "";
    public string WingetId { get; init; } = "";
    /// <summary>Começo do nome em "Aplicativos instalados" (Painel de Controle), para saber se já está instalado.</summary>
    public string DisplayPrefix { get; init; } = "";
    public bool PreSelect { get; init; } = true;

    private bool _selected, _installed;
    private string _status = "";

    public bool Selected { get => _selected; set => Set(ref _selected, value); }
    public bool Installed
    {
        get => _installed;
        set { Set(ref _installed, value); OnChanged(nameof(CanSelect)); }
    }
    public bool CanSelect => !Installed;
    public string Status { get => _status; set => Set(ref _status, value); }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged([CallerMemberName] string? p = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
    private void Set<T>(ref T field, T value, [CallerMemberName] string? p = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        OnChanged(p);
    }
}

/// <summary>
/// Catálogo expandido de apps essenciais e instalação silenciosa pelo winget (Gerenciador de Pacotes do Windows), que baixa
/// sempre a versão oficial mais recente do site do fabricante e confere o hash do instalador.
/// </summary>
public static class AppInstaller
{
    public static List<InstallableApp> Catalog() => new()
    {
        // ==========================================
        // 🎮 JOGOS
        // ==========================================
        new()
        {
            Name = "Steam",
            Category = "Jogos",
            Publisher = "Valve Corporation",
            Description = "Loja e biblioteca de jogos da Valve, com matchmaking, comunidade e suporte a mods.",
            WingetId = "Valve.Steam",
            DisplayPrefix = "Steam",
            PreSelect = true
        },
        new()
        {
            Name = "Epic Games Launcher",
            Category = "Jogos",
            Publisher = "Epic Games, Inc.",
            Description = "Loja oficial da Epic Games com jogos gratuitos semanais, biblioteca Unreal e Fortnite.",
            WingetId = "EpicGames.EpicGamesLauncher",
            DisplayPrefix = "Epic Games",
            PreSelect = true
        },
        new()
        {
            Name = "EA App",
            Category = "Jogos",
            Publisher = "Electronic Arts",
            Description = "Plataforma oficial da EA para jogos como EA FC (FIFA), Battlefield, Apex Legends e The Sims.",
            WingetId = "ElectronicArts.EADesktop",
            DisplayPrefix = "EA app",
            PreSelect = false
        },
        new()
        {
            Name = "Ubisoft Connect",
            Category = "Jogos",
            Publisher = "Ubisoft",
            Description = "Cliente oficial de jogos e amigos da Ubisoft (Rainbow Six, Assassin's Creed, Far Cry).",
            WingetId = "Ubisoft.Connect",
            DisplayPrefix = "Ubisoft Connect",
            PreSelect = false
        },
        new()
        {
            Name = "GOG GALAXY",
            Category = "Jogos",
            Publisher = "GOG.com",
            Description = "Plataforma sem DRM da CD Projekt para jogos clássicos e modernos com biblioteca unificada.",
            WingetId = "GOG.Galaxy",
            DisplayPrefix = "GOG GALAXY",
            PreSelect = false
        },
        new()
        {
            Name = "Battle.net",
            Category = "Jogos",
            Publisher = "Blizzard Entertainment",
            Description = "Launcher da Blizzard para Call of Duty, Overwatch 2, Diablo, StarCraft e World of Warcraft.",
            WingetId = "Blizzard.BattleNet",
            DisplayPrefix = "Battle.net",
            PreSelect = false
        },

        // ==========================================
        // 💬 COMUNICAÇÃO
        // ==========================================
        new()
        {
            Name = "Discord",
            Category = "Comunicação",
            Publisher = "Discord Inc.",
            Description = "Chat de voz, vídeo e texto com canais dedicados e sobreposição em jogos para jogar com amigos.",
            WingetId = "Discord.Discord",
            DisplayPrefix = "Discord",
            PreSelect = true
        },
        new()
        {
            Name = "Telegram Desktop",
            Category = "Comunicação",
            Publisher = "Telegram FZ-LLC",
            Description = "Mensageiro ágil, sincronizado e seguro com suporte a canais, bots e transferência de grandes arquivos.",
            WingetId = "Telegram.TelegramDesktop",
            DisplayPrefix = "Telegram Desktop",
            PreSelect = true
        },
        new()
        {
            Name = "TeamSpeak 3 Client",
            Category = "Comunicação",
            Publisher = "TeamSpeak Systems GmbH",
            Description = "Comunicação por voz clássica com servidores dedicados e baixíssima latência.",
            WingetId = "TeamSpeakSystems.TeamSpeakClient",
            DisplayPrefix = "TeamSpeak 3 Client",
            PreSelect = false
        },
        new()
        {
            Name = "Zoom Workplace",
            Category = "Comunicação",
            Publisher = "Zoom",
            Description = "Plataforma de videoconferências, reuniões virtuais, webinars e compartilhamento de tela.",
            WingetId = "Zoom.Zoom",
            DisplayPrefix = "Zoom",
            PreSelect = false
        },
        new()
        {
            Name = "OBS Studio",
            Category = "Comunicação",
            Publisher = "OBS Project",
            Description = "Software aberto para captura de tela, gravação de gameplay e transmissões ao vivo (streaming).",
            WingetId = "OBSProject.OBSStudio",
            DisplayPrefix = "OBS Studio",
            PreSelect = false
        },
        new()
        {
            Name = "Spotify",
            Category = "Comunicação",
            Publisher = "Spotify AB",
            Description = "Streaming de músicas, podcasts e playlists para escutar durante partidas ou trabalho.",
            WingetId = "Spotify.Spotify",
            DisplayPrefix = "Spotify",
            PreSelect = true
        },

        // ==========================================
        // 🌐 NAVEGAÇÃO
        // ==========================================
        new()
        {
            Name = "Google Chrome",
            Category = "Navegação",
            Publisher = "Google LLC",
            Description = "Navegador de internet rápido, seguro e totalmente integrado ao ecossistema Google.",
            WingetId = "Google.Chrome",
            DisplayPrefix = "Google Chrome",
            PreSelect = true
        },
        new()
        {
            Name = "Mozilla Firefox",
            Category = "Navegação",
            Publisher = "Mozilla",
            Description = "Navegador de código aberto focado em privacidade, personalização e padrões livres da web.",
            WingetId = "Mozilla.Firefox",
            DisplayPrefix = "Mozilla Firefox",
            PreSelect = false
        },
        new()
        {
            Name = "Brave Browser",
            Category = "Navegação",
            Publisher = "Brave Software Inc",
            Description = "Navegador veloz baseado em Chromium com bloqueador de anúncios e rastreadores nativo.",
            WingetId = "Brave.Brave",
            DisplayPrefix = "Brave",
            PreSelect = false
        },
        new()
        {
            Name = "Opera GX",
            Category = "Navegação",
            Publisher = "Opera Software",
            Description = "Navegador gamer com controles de limite de uso de CPU, RAM e largura de banda da rede.",
            WingetId = "Opera.OperaGX",
            DisplayPrefix = "Opera GX",
            PreSelect = false
        },
        new()
        {
            Name = "Microsoft Edge",
            Category = "Navegação",
            Publisher = "Microsoft Corporation",
            Description = "Navegador da Microsoft otimizado para o Windows com modo de eficiência e integração com IA.",
            WingetId = "Microsoft.Edge",
            DisplayPrefix = "Microsoft Edge",
            PreSelect = false
        },

        // ==========================================
        // 🛠️ UTILITÁRIOS
        // ==========================================
        new()
        {
            Name = "7-Zip",
            Category = "Utilitários",
            Publisher = "Igor Pavlov",
            Description = "Compactador gratuito de alta compressão. Abre e cria arquivos zip, 7z, rar e tar.",
            WingetId = "7zip.7zip",
            DisplayPrefix = "7-Zip",
            PreSelect = true
        },
        new()
        {
            Name = "WinRAR",
            Category = "Utilitários",
            Publisher = "win.rar GmbH",
            Description = "Compactador tradicional e consagrado para criação e descompactação de arquivos rar e zip.",
            WingetId = "RARLab.WinRAR",
            DisplayPrefix = "WinRAR",
            PreSelect = false
        },
        new()
        {
            Name = "VLC media player",
            Category = "Utilitários",
            Publisher = "VideoLAN",
            Description = "Player multimídia versátil que reproduz vídeos, filmes e músicas sem necessidade de codecs extras.",
            WingetId = "VideoLAN.VLC",
            DisplayPrefix = "VLC media player",
            PreSelect = true
        },
        new()
        {
            Name = "Lightshot",
            Category = "Utilitários",
            Publisher = "Skillbrains",
            Description = "Captura de tela instantânea acionada pela tecla Print Screen com anotações rápidas.",
            WingetId = "Skillbrains.Lightshot",
            DisplayPrefix = "Lightshot",
            PreSelect = true
        },
        new()
        {
            Name = "ShareX",
            Category = "Utilitários",
            Publisher = "ShareX Team",
            Description = "Captura avançada de tela, gravação de vídeos/GIFs, reconhecimento OCR e envio automático.",
            WingetId = "ShareX.ShareX",
            DisplayPrefix = "ShareX",
            PreSelect = false
        },
        new()
        {
            Name = "Notepad++",
            Category = "Utilitários",
            Publisher = "Notepad++ Team",
            Description = "Editor de texto e código leve com realce de sintaxe para mais de 50 linguagens.",
            WingetId = "Notepad++.Notepad++",
            DisplayPrefix = "Notepad++",
            PreSelect = false
        },
        new()
        {
            Name = "qBittorrent",
            Category = "Utilitários",
            Publisher = "The qBittorrent project",
            Description = "Cliente torrent moderno, leve, de código aberto e totalmente livre de anúncios.",
            WingetId = "qBittorrent.qBittorrent",
            DisplayPrefix = "qBittorrent",
            PreSelect = false
        },
        new()
        {
            Name = "Visual Studio Code",
            Category = "Utilitários",
            Publisher = "Microsoft Corporation",
            Description = "Editor de código-fonte poderoso e extensível para programação, scripts e produtividade técnica.",
            WingetId = "Microsoft.VisualStudioCode",
            DisplayPrefix = "Microsoft Visual Studio Code",
            PreSelect = false
        }
    };

    public static string WingetPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\WindowsApps\winget.exe");

    public static bool WingetAvailable => File.Exists(WingetPath);

    /// <summary>Nomes em "Aplicativos instalados" (máquina 64/32 bits e usuário atual).</summary>
    public static HashSet<string> InstalledNames()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        const string path = @"Software\Microsoft\Windows\CurrentVersion\Uninstall";
        foreach (var (hive, view) in new[] { (RegistryHive.LocalMachine, RegistryView.Registry64), (RegistryHive.LocalMachine, RegistryView.Registry32),
                                             (RegistryHive.CurrentUser, RegistryView.Default) })
        {
            try
            {
                using var root = RegistryKey.OpenBaseKey(hive, view).OpenSubKey(path);
                if (root == null) continue;
                foreach (var sub in root.GetSubKeyNames())
                {
                    using var k = root.OpenSubKey(sub);
                    if (k?.GetValue("DisplayName") is string n && !string.IsNullOrWhiteSpace(n)) names.Add(n.Trim());
                }
            }
            catch { }
        }
        return names;
    }

    public static void RefreshInstalled(IEnumerable<InstallableApp> apps)
    {
        var names = InstalledNames();
        foreach (var a in apps)
        {
            a.Installed = names.Any(n => n.StartsWith(a.DisplayPrefix, StringComparison.OrdinalIgnoreCase));
            if (a.Installed) a.Selected = false;
        }
    }

    /// <summary>Instala um app em silêncio. Retorna (ok, mensagem curta).</summary>
    public static async Task<(bool Ok, string Message)> InstallAsync(InstallableApp app, CancellationToken ct, bool upgrade = false)
    {
        var psi = new ProcessStartInfo(WingetPath,
            $"{(upgrade ? "upgrade" : "install")} --id {app.WingetId} -e --silent --accept-package-agreements --accept-source-agreements --disable-interactivity")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
        };
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("Não foi possível iniciar o winget.");
        var outTask = p.StandardOutput.ReadToEndAsync(ct);
        var errTask = p.StandardError.ReadToEndAsync(ct);
        try { await p.WaitForExitAsync(ct); }
        catch (OperationCanceledException) { try { p.Kill(true); } catch { } throw; }
        string output = await outTask + await errTask;

        return p.ExitCode switch
        {
            0 => (true, "Instalado."),
            unchecked((int)0x8A15002B) or unchecked((int)0x8A150061) => (true, "Já estava instalado."),
            unchecked((int)0x8A150014) => (false, "Pacote não encontrado no winget."),
            unchecked((int)0x8A150008) => (false, "Falha no download: confira a internet e tente de novo."),
            _ => (false, $"Falhou (código 0x{p.ExitCode:X8}). {LastLine(output)}".Trim()),
        };
    }

    private static string LastLine(string s) =>
        s.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? "";

    // =========================================================================
    // 🔄 ATUALIZADOR DE PROGRAMAS (WINGET UPGRADE - ESTILO HUB PRO)
    // =========================================================================

    public static async Task<List<UpgradableAppItem>> CheckUpgradesAsync(CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            var list = new List<UpgradableAppItem>();
            if (!WingetAvailable) return list;

            try
            {
                var psi = new ProcessStartInfo(WingetPath, "upgrade --include-unknown")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8
                };
                using var p = Process.Start(psi);
                if (p == null) return list;

                string output = p.StandardOutput.ReadToEnd();
                p.WaitForExit(35000);

                var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                bool headerFound = false;

                foreach (var line in lines)
                {
                    if (!headerFound)
                    {
                        if ((line.Contains("Id") || line.Contains("Identificador")) && (line.Contains("Vers") || line.Contains("Version")))
                        {
                            headerFound = true;
                        }
                        continue;
                    }

                    if (line.StartsWith("---") || line.StartsWith("===") ||
                        line.Contains("upgrades available") || line.Contains("atualizações disponíveis") ||
                        line.Contains("atualização disponível") || line.Contains("upgrade available"))
                        continue;

                    var parts = line.Split(new[] { "  " }, StringSplitOptions.RemoveEmptyEntries)
                                    .Select(s => s.Trim())
                                    .Where(s => !string.IsNullOrEmpty(s))
                                    .ToList();

                    if (parts.Count >= 3)
                    {
                        string name = parts[0];
                        string id = parts[1];
                        string installedVer = parts[2];
                        string availVer = parts.Count >= 4 ? parts[3] : "Mais recente";
                        string source = parts.Count >= 5 ? parts[4] : "winget";

                        list.Add(new UpgradableAppItem
                        {
                            Name = name,
                            Id = id,
                            InstalledVersion = installedVer,
                            AvailableVersion = availVer,
                            Source = source,
                            Selected = true
                        });
                    }
                }
            }
            catch { }

            return list;
        }, ct);
    }

    public static async Task<(bool Ok, string Message)> UpgradeAppAsync(UpgradableAppItem app, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(WingetPath,
            $"upgrade --id \"{app.Id}\" --silent --accept-package-agreements --accept-source-agreements --disable-interactivity")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
        };
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("Não foi possível iniciar o winget.");
        try { await p.WaitForExitAsync(ct); }
        catch (OperationCanceledException) { try { p.Kill(true); } catch { } throw; }

        return p.ExitCode == 0 ? (true, "Atualizado com sucesso.") : (false, $"Falhou (código 0x{p.ExitCode:X8}).");
    }

    public static async Task<(bool Ok, string Message)> UpgradeAllAsync(CancellationToken ct)
    {
        var psi = new ProcessStartInfo(WingetPath,
            "upgrade --all --silent --accept-package-agreements --accept-source-agreements --disable-interactivity")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
        };
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("Não foi possível iniciar o winget.");
        try { await p.WaitForExitAsync(ct); }
        catch (OperationCanceledException) { try { p.Kill(true); } catch { } throw; }

        return p.ExitCode == 0 ? (true, "Todos os programas foram atualizados.") : (false, $"Atualização finalizada com código 0x{p.ExitCode:X8}.");
    }
}

/// <summary>
/// Aplicativo detectado com atualização disponível no winget.
/// </summary>
public sealed class UpgradableAppItem : INotifyPropertyChanged
{
    private bool _selected = true;
    private string _status = "";

    public string Name { get; set; } = "";
    public string Id { get; set; } = "";
    public string InstalledVersion { get; set; } = "";
    public string AvailableVersion { get; set; } = "";
    public string Source { get; set; } = "winget";

    public bool Selected
    {
        get => _selected;
        set { _selected = value; OnChanged(); }
    }

    public string Status
    {
        get => _status;
        set { _status = value; OnChanged(); }
    }

    public string VersionComparison => $"{InstalledVersion} → {AvailableVersion}";
    public string Subtitle => $"{Source} · Versão instalada: {InstalledVersion} (Disponível: {AvailableVersion})";

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged([CallerMemberName] string? prop = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
}
