using System;
using System.Collections.Generic;

namespace LuminiTray;

/// <summary>
/// Gerencia o histórico de navegação entre páginas do Lumini Tray (Voltar e Avançar).
/// Suporta histórico encadeado, truncamento em novas ramificações e nomes amigáveis para tooltips.
/// </summary>
public sealed class NavigationHistoryManager
{
    private readonly List<string> _history = new();
    private int _currentIndex = -1;
    private readonly int _maxHistoryCount;

    public const int DefaultMaxHistoryCount = 50;

    public NavigationHistoryManager(int maxCapacity = DefaultMaxHistoryCount)
    {
        _maxHistoryCount = maxCapacity > 0 ? maxCapacity : DefaultMaxHistoryCount;
    }

    public bool CanGoBack => _currentIndex > 0;
    public bool CanGoForward => _currentIndex >= 0 && _currentIndex < _history.Count - 1;

    public string? CurrentPage => (_currentIndex >= 0 && _currentIndex < _history.Count)
        ? _history[_currentIndex]
        : null;

    public int Count => _history.Count;
    public int CurrentIndex => _currentIndex;

    /// <summary>
    /// Adiciona uma página ao histórico.
    /// Se o usuário estava no meio do histórico e navegou para uma nova página,
    /// o histórico futuro é descartado (estilo navegador/IDE).
    /// </summary>
    public bool Push(string pageName)
    {
        if (string.IsNullOrWhiteSpace(pageName)) return false;

        // Evita duplicar páginas idênticas consecutivas
        if (_currentIndex >= 0 && _currentIndex < _history.Count && _history[_currentIndex] == pageName)
            return false;

        // Descarta histórico à frente se estávamos navegando para trás
        if (_currentIndex >= 0 && _currentIndex < _history.Count - 1)
        {
            _history.RemoveRange(_currentIndex + 1, _history.Count - (_currentIndex + 1));
        }

        _history.Add(pageName);

        // Limita capacidade para não acumular memória indefinidamente
        while (_history.Count > _maxHistoryCount)
        {
            _history.RemoveAt(0);
        }

        _currentIndex = _history.Count - 1;
        return true;
    }

    /// <summary>
    /// Retrocede para a página anterior no histórico.
    /// Retorna o nome da página para onde navegar, ou null se não for possível.
    /// </summary>
    public string? GoBack()
    {
        if (!CanGoBack) return null;
        _currentIndex--;
        return _history[_currentIndex];
    }

    /// <summary>
    /// Avança para a próxima página no histórico.
    /// Retorna o nome da página para onde navegar, ou null se não for possível.
    /// </summary>
    public string? GoForward()
    {
        if (!CanGoForward) return null;
        _currentIndex++;
        return _history[_currentIndex];
    }

    /// <summary>
    /// Retorna o nome da página anterior sem mover o cursor do histórico (para tooltips).
    /// </summary>
    public string? PeekBack()
    {
        if (!CanGoBack) return null;
        return _history[_currentIndex - 1];
    }

    /// <summary>
    /// Retorna o nome da próxima página sem mover o cursor do histórico (para tooltips).
    /// </summary>
    public string? PeekForward()
    {
        if (!CanGoForward) return null;
        return _history[_currentIndex + 1];
    }

    /// <summary>
    /// Limpa o histórico de navegação.
    /// </summary>
    public void Clear()
    {
        _history.Clear();
        _currentIndex = -1;
    }

    /// <summary>
    /// Converte a tag da página no nome amigável visível na UI.
    /// </summary>
    public static string GetFriendlyPageTitle(string? pageTag)
    {
        if (string.IsNullOrWhiteSpace(pageTag)) return "Página";

        return pageTag switch
        {
            "PageDash" => "Visão Geral",
            "PageSystem" => "Sistema & Hardware",
            "PageSensors" => "Sensores em Tempo Real",
            "PageGpu" => "Placa de Vídeo (GPU)",
            "PageDisks" => "Discos & Armazenamento",
            "PageDiskTools" => "Ferramentas de Disco",
            "PageEnergy" => "Energia & Bateria",
            "PageNet" => "Rede & Diagnóstico",
            "PageLauncher" => "Launcher de Jogos",
            "PageGames" => "Biblioteca de Jogos",
            "PageBottleneck" => "Calculadora de Gargalo",
            "PageOverlay" => "Overlay Flutuante",
            "PageBench" => "Benchmark de Desempenho",
            "PageStress" => "Teste de Estresse de Hardware",
            "PageOc" => "Overclocking & Estabilidade",
            "PageOptimize" => "Otimizações de Sistema",
            "PageApps" => "Instalador de Aplicativos",
            "PageUninstaller" => "Desinstalador de Programas",
            "PageDriver" => "Drivers & DDU",
            "PageMakerPorts" => "Monitor de Portas USB",
            "PageMakerDrv" => "Drivers Maker & Arduino",
            "PageRestore" => "Pontos de Restauração",
            "PageBackup" => "Central de Backup",
            "PageFanControl" => "Controle de Ventoinhas",
            "PageWindowsDebloat" => "Windows & Debloat",
            "PageTweaks" => "Tweaks Exclusivos",
            "PageSuppressor" => "Supressor de Ruído",
            "PageSettings" => "Configurações",
            "PageWallpapers" => "Wallpapers & Visual",
            "PageStartup" => "Programas de Inicialização",
            "PageInteg" => "Integração & Atalhos",
            "PageAudio" => "Central de Áudio",
            "PagePerifericos" => "Central de Periféricos",
            "PagePerifTeclado" => "Periféricos · Teclado",
            "PagePerifMouse" => "Periféricos · Mouse",
            "PagePerifMonitor" => "Periféricos · Monitor",
            "PagePerifAudio" => "Periféricos · Áudio",
            "PagePerifMic" => "Periféricos · Microfone",
            "PagePerifGamepad" => "Periféricos · Controle",
            "PagePerifDevices" => "Periféricos · Dispositivos",
            _ => pageTag.StartsWith("Page") ? pageTag.Substring(4) : pageTag
        };
    }
}
