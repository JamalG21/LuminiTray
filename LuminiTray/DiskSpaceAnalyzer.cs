using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Media;

namespace LuminiTray;

/// <summary>
/// Resultado detalhado da varredura de disco com métricas e contadores.
/// </summary>
public sealed class DiskScanResult
{
    public DiskSpaceItem Root { get; set; } = null!;
    public List<DiskSpaceItem> TopFiles { get; set; } = new();
    public List<DiskSpaceItem> TopFolders { get; set; } = new();
    public List<ExtensionStat> Extensions { get; set; } = new();
    public int AccessDeniedCount { get; set; }
    public int ReparseCount { get; set; }
    public TimeSpan Elapsed { get; set; }

    public void Deconstruct(out DiskSpaceItem root, out List<DiskSpaceItem> topFiles, out List<ExtensionStat> extensions)
    {
        root = Root;
        topFiles = TopFiles;
        extensions = Extensions;
    }
}

/// <summary>
/// Representa um item (pasta ou arquivo) analisado no espaço em disco.
/// </summary>
public sealed class DiskSpaceItem
{
    public string Name { get; set; } = "";
    public string FullPath { get; set; } = "";
    public long Size { get; set; }
    public int FileCount { get; set; }
    public int FolderCount { get; set; }
    public bool IsDirectory { get; set; }
    public double Percentage { get; set; }
    public string Category { get; set; } = "Outros";
    public List<DiskSpaceItem> Children { get; set; } = new();

    public string CategoryColorHex => TreemapLayoutEngine.CategoryColors.TryGetValue(Category, out var c) ? $"#{c.R:X2}{c.G:X2}{c.B:X2}" : "#64748B";
    public Color CategoryColor => TreemapLayoutEngine.GetCategoryColor(Category);
    public Brush CategoryBrush
    {
        get
        {
            var b = new SolidColorBrush(CategoryColor);
            b.Freeze();
            return b;
        }
    }
    public string FormattedSize => DiskSpaceAnalyzer.FormatSize(Size);
    public string FormattedPercentage => $"{Percentage:F1}%";
    public string Icon => IsDirectory ? "📁" : GetFileIcon(Name);
    public string Subtitle => IsDirectory ? $"{FileCount:N0} arquivos · mais comum: {Category}" : $"{FormattedSize} · {Category}";
    public double ProgressPercentage => Math.Clamp(Percentage, 0.5, 100.0);

    public static string GetFileIcon(string filename)
    {
        string ext = Path.GetExtension(filename).ToLowerInvariant();
        return ext switch
        {
            ".exe" or ".msi" or ".bat" or ".cmd" => "⚙️",
            ".iso" or ".vmdk" or ".vhd" => "💿",
            ".zip" or ".rar" or ".7z" or ".tar" or ".gz" => "📦",
            ".mp4" or ".mkv" or ".avi" or ".mov" => "🎬",
            ".mp3" or ".wav" or ".flac" or ".aac" => "🎵",
            ".jpg" or ".png" or ".gif" or ".webp" or ".bmp" => "🖼️",
            ".dll" or ".sys" or ".drv" => "🔧",
            ".log" or ".txt" or ".dmp" => "📄",
            _ => "📄"
        };
    }
}

/// <summary>
/// Estatística de extensões de arquivos no disco.
/// </summary>
public sealed record ExtensionStat(string Extension, long TotalSize, int Count)
{
    public string FormattedSize => DiskSpaceAnalyzer.FormatSize(TotalSize);
}

/// <summary>
/// Categoria de lixo ou arquivos temporários detectados para liberação rápida de espaço.
/// </summary>
public sealed class QuickCleanCategory
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Path { get; set; } = "";
    public List<string> Paths { get; set; } = new();
    public List<string>? ExplicitTargetFiles { get; set; }
    public long Size { get; set; }
    public bool Selected { get; set; } = true;
    public string FormattedSize => DiskSpaceAnalyzer.FormatSize(Size);
}

/// <summary>
/// Analisador visual e hierárquico de espaço em disco.
/// Realiza varreduras rápidas, identifica maiores pastas e arquivos e monta árvore hierárquica.
/// </summary>
public static class DiskSpaceAnalyzer
{
    private static readonly EnumerationOptions SafeRecursiveEnum = new()
    {
        IgnoreInaccessible = true,
        RecurseSubdirectories = true,
        AttributesToSkip = FileAttributes.ReparsePoint
    };

    public static string FormatSize(long bytes)
    {
        if (bytes >= 1024L * 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024 * 1024 * 1024):F2} TB";
        if (bytes >= 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
        if (bytes >= 1024L * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
        if (bytes >= 1024L) return $"{bytes / 1024.0:F0} KB";
        return $"{bytes} B";
    }

    /// <summary>
    /// Escaneia uma unidade ou diretório e gera a árvore completa com porcentagens e maiores arquivos.
    /// </summary>
    public static async Task<DiskScanResult> ScanAsync(
        string rootPath,
        Action<string, int>? onProgress = null,
        CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            var sw = Stopwatch.StartNew();
            var topFiles = new List<DiskSpaceItem>();
            var allFolders = new List<DiskSpaceItem>();
            var extMap = new Dictionary<string, (long Size, int Count)>(StringComparer.OrdinalIgnoreCase);
            int scannedItems = 0;
            int accessDeniedCount = 0;
            int reparseCount = 0;

            var rootItem = ScanRecursive(new DirectoryInfo(rootPath), ref scannedItems, ref accessDeniedCount, ref reparseCount, topFiles, allFolders, extMap, onProgress, ct);

            // Calcula porcentagens dos filhos
            CalculatePercentages(rootItem, rootItem.Size);

            // Top maiores arquivos ordenados
            var sortedTopFiles = topFiles.OrderByDescending(f => f.Size).Take(50).ToList();

            // Top pastas ordenadas
            var sortedTopFolders = allFolders.OrderByDescending(f => f.Size).Take(50).ToList();

            // Estatísticas por extensão ordenadas
            var sortedExtensions = extMap
                .Select(kv => new ExtensionStat(string.IsNullOrEmpty(kv.Key) ? "(sem extensão)" : kv.Key, kv.Value.Size, kv.Value.Count))
                .OrderByDescending(e => e.TotalSize)
                .Take(15)
                .ToList();

            sw.Stop();
            return new DiskScanResult
            {
                Root = rootItem,
                TopFiles = sortedTopFiles,
                TopFolders = sortedTopFolders,
                Extensions = sortedExtensions,
                AccessDeniedCount = accessDeniedCount,
                ReparseCount = reparseCount,
                Elapsed = sw.Elapsed
            };
        }, ct);
    }

    private static DiskSpaceItem ScanRecursive(
        DirectoryInfo dir,
        ref int scannedItems,
        ref int accessDeniedCount,
        ref int reparseCount,
        List<DiskSpaceItem> topFiles,
        List<DiskSpaceItem> allFolders,
        Dictionary<string, (long Size, int Count)> extMap,
        Action<string, int>? onProgress,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        scannedItems++;
        if (scannedItems % 150 == 0)
        {
            onProgress?.Invoke(dir.FullName, scannedItems);
        }

        var node = new DiskSpaceItem
        {
            Name = dir.Name,
            FullPath = dir.FullName,
            IsDirectory = true,
            Category = "Pasta"
        };

        long totalSize = 0;
        int totalFiles = 0;
        int totalFolders = 0;

        // Arquivos no diretório
        try
        {
            foreach (var fi in dir.EnumerateFiles())
            {
                ct.ThrowIfCancellationRequested();
                long fSize = fi.Length;
                totalSize += fSize;
                totalFiles++;

                string ext = fi.Extension.ToLowerInvariant();
                if (extMap.TryGetValue(ext, out var stat))
                    extMap[ext] = (stat.Size + fSize, stat.Count + 1);
                else
                    extMap[ext] = (fSize, 1);

                var fileItem = new DiskSpaceItem
                {
                    Name = fi.Name,
                    FullPath = fi.FullName,
                    Size = fSize,
                    IsDirectory = false,
                    Category = ClassifyExtension(ext, fi.FullName)
                };

                node.Children.Add(fileItem);

                // Armazena para top arquivos
                if (topFiles.Count < 100 || fSize >= 10L * 1024 * 1024)
                {
                    topFiles.Add(fileItem);
                }
            }
        }
        catch { accessDeniedCount++; }

        // Subdiretórios
        try
        {
            foreach (var subDir in dir.EnumerateDirectories())
            {
                ct.ThrowIfCancellationRequested();

                // Ignora pontos de junção cíclicos
                if ((subDir.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    reparseCount++;
                    continue;
                }

                totalFolders++;
                var subNode = ScanRecursive(subDir, ref scannedItems, ref accessDeniedCount, ref reparseCount, topFiles, allFolders, extMap, onProgress, ct);
                totalSize += subNode.Size;
                totalFiles += subNode.FileCount;
                totalFolders += subNode.FolderCount;
                node.Children.Add(subNode);

                if (subNode.Size > 10L * 1024 * 1024)
                {
                    allFolders.Add(subNode);
                }
            }
        }
        catch { accessDeniedCount++; }

        node.Size = totalSize;
        node.FileCount = totalFiles;
        node.FolderCount = totalFolders;

        // Ordena filhos por tamanho decrescente
        node.Children.Sort((a, b) => b.Size.CompareTo(a.Size));

        // Determina a categoria predominante da pasta pelo tamanho acumulado dos filhos
        string pLower = node.FullPath.ToLowerInvariant();
        if (pLower.Contains(@"\windows") || pLower.Contains(@"system32") || pLower.Contains(@"winsxs") || node.Name.StartsWith("$Recycle.Bin", StringComparison.OrdinalIgnoreCase))
        {
            node.Category = "Windows e sistema";
        }
        else if (pLower.Contains(@"\program files") || pLower.Contains(@"\games") || pLower.Contains("steamlibrary"))
        {
            node.Category = "Programas e jogos";
        }
        else if (node.Children.Count > 0)
        {
            var dominant = node.Children
                .GroupBy(c => c.Category)
                .OrderByDescending(g => g.Sum(c => c.Size))
                .FirstOrDefault();
            node.Category = dominant?.Key ?? "Outros";
        }

        return node;
    }

    private static void CalculatePercentages(DiskSpaceItem item, long parentTotal)
    {
        if (parentTotal <= 0) return;
        foreach (var child in item.Children)
        {
            child.Percentage = (double)child.Size / parentTotal * 100.0;
            if (child.IsDirectory && child.Size > 0)
            {
                CalculatePercentages(child, child.Size);
            }
        }
    }

    public static Dictionary<string, long> ComputeCategoryDistribution(DiskSpaceItem item)
    {
        var result = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var cat in TreemapLayoutEngine.CategoryColors.Keys)
        {
            result[cat] = 0;
        }

        AccumulateCategorySizes(item, result);
        return result;
    }

    private static void AccumulateCategorySizes(DiskSpaceItem item, Dictionary<string, long> map)
    {
        if (!item.IsDirectory)
        {
            string cat = map.ContainsKey(item.Category) ? item.Category : "Outros";
            map[cat] += item.Size;
            return;
        }

        foreach (var child in item.Children)
        {
            AccumulateCategorySizes(child, map);
        }
    }

    public static string ClassifyExtension(string ext, string fullPath = "")
    {
        if (!string.IsNullOrEmpty(fullPath))
        {
            string pLower = fullPath.ToLowerInvariant();
            if (pLower.Contains(@"\windows") || pLower.Contains(@"\system32") || pLower.Contains(@"\winsxs") || pLower.EndsWith("hiberfil.sys") || pLower.EndsWith("pagefile.sys") || pLower.EndsWith("swapfile.sys"))
                return "Windows e sistema";
        }

        return ext.ToLowerInvariant() switch
        {
            ".mp4" or ".mkv" or ".avi" or ".mov" or ".wmv" or ".flv" or ".webm" or ".m4v" or ".mpg" or ".mpeg" => "Vídeos",
            ".jpg" or ".jpeg" or ".png" or ".gif" or ".webp" or ".bmp" or ".svg" or ".ico" or ".tiff" or ".tif" or ".psd" or ".raw" => "Imagens",
            ".mp3" or ".wav" or ".flac" or ".aac" or ".ogg" or ".wma" or ".m4a" or ".alac" or ".aiff" or ".opus" => "Músicas",
            ".pdf" or ".docx" or ".doc" or ".xlsx" or ".xls" or ".pptx" or ".ppt" or ".txt" or ".csv" or ".odt" or ".rtf" or ".md" or ".json" or ".xml" or ".html" or ".htm" => "Documentos",
            ".exe" or ".msi" or ".dll" or ".bat" or ".cmd" or ".ps1" or ".apk" or ".vmdk" or ".pak" or ".bundle" or ".vpk" or ".ba2" or ".bin" or ".appx" or ".msix" => "Programas e jogos",
            ".zip" or ".rar" or ".7z" or ".tar" or ".gz" or ".bz2" or ".xz" or ".iso" or ".img" or ".cab" => "Compactados",
            ".sys" or ".drv" or ".cat" or ".mui" or ".cur" or ".inf" or ".cpl" or ".ocx" => "Windows e sistema",
            _ => "Outros"
        };
    }

    /// <summary>
    /// Identifica áreas comuns de limpeza de disco e calcula o espaço passível de liberação.
    /// Para Caches de Sombras e Shaders, identifica apenas caches obsoletos (de drivers antigos ou jogos desinstalados),
    /// preservando integralmente os caches ativos de jogos instalados.
    /// </summary>
    public static async Task<List<QuickCleanCategory>> ScanQuickCleanAsync()
    {
        return await Task.Run(() =>
        {
            var list = new List<QuickCleanCategory>();

            // 1. Temp do Usuário
            string userTemp = Path.GetTempPath();
            list.Add(new QuickCleanCategory
            {
                Name = "Arquivos Temporários do Usuário",
                Description = "Caches e arquivos efêmeros de programas em execução.",
                Path = userTemp,
                Paths = new List<string> { userTemp },
                Size = MeasureDirectorySize(userTemp)
            });

            // 2. Temp do Windows
            string winTemp = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp");
            list.Add(new QuickCleanCategory
            {
                Name = "Arquivos Temporários do Sistema",
                Description = "Temporários criados pelo instalador e processos do Windows.",
                Path = winTemp,
                Paths = new List<string> { winTemp },
                Size = MeasureDirectorySize(winTemp)
            });

            // 3. Cache do Windows Update
            string wuDownload = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SoftwareDistribution", "Download");
            list.Add(new QuickCleanCategory
            {
                Name = "Cache de Downloads do Windows Update",
                Description = "Pacotes de atualização antigos que já foram instalados.",
                Path = wuDownload,
                Paths = new List<string> { wuDownload },
                Size = MeasureDirectorySize(wuDownload)
            });

            // 4. Crash Dumps
            string dumps = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CrashDumps");
            list.Add(new QuickCleanCategory
            {
                Name = "Despejos de Falha de Aplicativos (Crash Dumps)",
                Description = "Arquivos de despejo gerados quando algum jogo ou app trava.",
                Path = dumps,
                Paths = new List<string> { dumps },
                Size = MeasureDirectorySize(dumps)
            });

            // 5. Shader Cache DirectX & GPU (Preservando caches ativos de jogos instalados)
            var (cleanableShaderFiles, shaderBytes, shaderDirs) = GetCleanableShaderCacheFiles();
            string displayShaderPath = shaderDirs.Count > 0
                ? string.Join(" · ", shaderDirs)
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "D3DSCache");

            list.Add(new QuickCleanCategory
            {
                Name = "Caches de Sombras e Shaders (DirectX/GPU)",
                Description = "Caches obsoletos de drivers antigos e de jogos desinstalados (preserva caches ativos de jogos instalados).",
                Path = displayShaderPath,
                Paths = shaderDirs,
                ExplicitTargetFiles = cleanableShaderFiles,
                Size = shaderBytes
            });

            return list;
        });
    }

    private static long MeasureDirectorySize(string path)
    {
        if (!Directory.Exists(path)) return 0;
        try
        {
            long size = 0;
            var di = new DirectoryInfo(path);
            foreach (var fi in di.EnumerateFiles("*", SafeRecursiveEnum))
            {
                try { size += fi.Length; } catch { }
            }
            return size;
        }
        catch { return 0; }
    }

    /// <summary>
    /// Coleta todos os executáveis e pacotes de jogos atualmente instalados no PC (Steam, Epic, XboxGames, Riot, EA, Ubisoft, GOG, UWP).
    /// Pastas vazias ou residuais sem nenhum executável (.exe) são ignoradas para que jogos desinstalados sejam reconhecidos.
    /// </summary>
    private static (HashSet<string> InstalledExes, HashSet<string> UwpPackages, HashSet<string> NormalizedGameNames) GetInstalledGamesIndex()
    {
        var installedExes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var uwpPackages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var normalizedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            string pkgDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Packages");
            if (Directory.Exists(pkgDir))
            {
                foreach (var d in Directory.EnumerateDirectories(pkgDir))
                {
                    uwpPackages.Add(Path.GetFileName(d));
                }
            }
        }
        catch { }

        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (!drive.IsReady || drive.DriveType != DriveType.Fixed) continue;
                string root = drive.RootDirectory.FullName;

                string[] gameLibRoots =
                {
                    Path.Combine(root, "SteamLibrary", "steamapps", "common"),
                    Path.Combine(root, "Program Files (x86)", "Steam", "steamapps", "common"),
                    Path.Combine(root, "Program Files", "Steam", "steamapps", "common"),
                    Path.Combine(root, "Program Files", "Epic Games"),
                    Path.Combine(root, "Games"),
                    Path.Combine(root, "Riot Games"),
                    Path.Combine(root, "XboxGames"),
                    Path.Combine(root, "EA Games"),
                    Path.Combine(root, "Program Files", "EA Games"),
                    Path.Combine(root, "Program Files (x86)", "Ubisoft", "Ubisoft Game Launcher", "games"),
                    Path.Combine(root, "GOG Games")
                };

                foreach (var lib in gameLibRoots)
                {
                    if (!Directory.Exists(lib)) continue;
                    try
                    {
                        foreach (var gameDir in Directory.EnumerateDirectories(lib))
                        {
                            bool hasExe = false;
                            try
                            {
                                foreach (var exePath in Directory.EnumerateFiles(gameDir, "*.exe", SafeRecursiveEnum))
                                {
                                    hasExe = true;
                                    installedExes.Add(Path.GetFileName(exePath));
                                }
                            }
                            catch { }

                            if (hasExe)
                            {
                                string norm = NormalizeAlphaNum(Path.GetFileName(gameDir));
                                if (norm.Length >= 3) normalizedNames.Add(norm);
                            }
                        }
                    }
                    catch { }
                }
            }
        }
        catch { }

        return (installedExes, uwpPackages, normalizedNames);
    }

    private static string NormalizeAlphaNum(string input)
    {
        if (string.IsNullOrEmpty(input)) return "";
        var sb = new StringBuilder(input.Length);
        foreach (char c in input)
        {
            if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }

    /// <summary>
    /// Lê o cabeçalho binário de um arquivo .nvph da NVIDIA para extrair o nome do executável/pacote do jogo no offset 0x11.
    /// Retorna também se o arquivo está bloqueado em uso exclusivo por algum processo ativo.
    /// </summary>
    private static (bool IsLocked, string AppName) InspectNvphHeader(string filePath)
    {
        try
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            Span<byte> buf = stackalloc byte[256];
            int read = fs.Read(buf);
            if (read > 17 && buf[0] == 0x6E && buf[1] == 0x76 && buf[2] == 0x70 && buf[3] == 0x68) // "nvph"
            {
                int len = 0;
                while (17 + len < read && buf[17 + len] != 0) len++;
                if (len > 0)
                {
                    string app = Encoding.ASCII.GetString(buf.Slice(17, len)).Trim();
                    return (false, app);
                }
            }
            return (false, "");
        }
        catch
        {
            return (true, "");
        }
    }

    private static bool CanDeleteFile(string filePath)
    {
        try
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Identifica todos os arquivos de cache de sombras e shaders que podem ser apagados com segurança,
    /// preservando os caches ativos dos jogos atualmente instalados no computador.
    /// </summary>
    public static (List<string> CleanableFiles, long TotalBytes, List<string> ExistingDirs) GetCleanableShaderCacheFiles()
    {
        var cleanableFiles = new List<string>();
        long totalBytes = 0;
        var existingDirs = new List<string>();

        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

        var (installedExes, uwpPackages, normalizedGameNames) = GetInstalledGamesIndex();

        // 1. Diretórios NVIDIA DXCache (.nvph com identificação de app e versão de driver no cabeçalho/nome)
        string[] nvDxCacheDirs =
        {
            Path.Combine(localAppData, "NVIDIA", "DXCache"),
            Path.Combine(userProfile, "AppData", "LocalLow", "NVIDIA", "PerDriverVersion", "DXCache")
        };

        foreach (var nvDir in nvDxCacheDirs)
        {
            if (!Directory.Exists(nvDir)) continue;
            existingDirs.Add(nvDir);

            try
            {
                var files = new DirectoryInfo(nvDir).EnumerateFiles("*", SafeRecursiveEnum).ToList();
                if (files.Count == 0) continue;

                // Identifica os prefixos do driver ativo (os mais recentes diferentes do bucket compartilhado 0002a938)
                var nonShared = files
                    .Where(f => Path.GetFileNameWithoutExtension(f.Name).Length >= 16 &&
                                !f.Name.StartsWith("0002a938", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                DateTime latestDriverWrite = nonShared.Count > 0
                    ? nonShared.Max(f => f.LastWriteTimeUtc)
                    : DateTime.UtcNow;

                var activePrefixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "0002a938" };
                foreach (var grp in nonShared.GroupBy(f => f.Name.Substring(0, 8), StringComparer.OrdinalIgnoreCase))
                {
                    DateTime grpLatest = grp.Max(f => f.LastWriteTimeUtc);
                    if ((latestDriverWrite - grpLatest).TotalDays <= 3.0)
                    {
                        activePrefixes.Add(grp.Key);
                    }
                }

                // Agrupa por sufixo de 8 caracteres (hash do jogo/aplicativo)
                var byAppSuffix = files.GroupBy(f =>
                {
                    string baseName = Path.GetFileNameWithoutExtension(f.Name);
                    return baseName.Length >= 16 ? baseName.Substring(8, 8) : baseName;
                }, StringComparer.OrdinalIgnoreCase);

                foreach (var appGroup in byAppSuffix)
                {
                    var appFiles = appGroup.ToList();
                    string appName = "";
                    bool anyLocked = false;

                    foreach (var fi in appFiles)
                    {
                        var (locked, headerApp) = InspectNvphHeader(fi.FullName);
                        if (locked)
                        {
                            anyLocked = true;
                            break;
                        }
                        if (string.IsNullOrEmpty(appName) && !string.IsNullOrEmpty(headerApp))
                        {
                            appName = headerApp;
                        }
                    }

                    // Se qualquer arquivo deste app está em uso exclusivo agora (jogo ou app aberto), protege todos
                    if (anyLocked) continue;

                    bool isInstalledGame = false;
                    if (!string.IsNullOrWhiteSpace(appName))
                    {
                        if (appName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                        {
                            isInstalledGame = installedExes.Contains(appName);
                        }
                        else
                        {
                            if (uwpPackages.Contains(appName))
                            {
                                isInstalledGame = true;
                            }
                            else
                            {
                                string normApp = NormalizeAlphaNum(appName);
                                foreach (var gNorm in normalizedGameNames)
                                {
                                    if (normApp.Contains(gNorm, StringComparison.OrdinalIgnoreCase))
                                    {
                                        isInstalledGame = true;
                                        break;
                                    }
                                }
                            }
                        }
                    }

                    if (isInstalledGame)
                    {
                        // Jogo instalado: preserva o cache compartilhado (0002a938) + o cache do driver mais recente deste jogo;
                        // apaga apenas caches duplicados de drivers antigos que ficaram obsoletos!
                        var driverSpecific = appFiles
                            .Where(f => !f.Name.StartsWith("0002a938", StringComparison.OrdinalIgnoreCase))
                            .OrderByDescending(f => f.LastWriteTimeUtc)
                            .ToList();

                        DateTime newestGameDriverWrite = driverSpecific.Count > 0
                            ? driverSpecific[0].LastWriteTimeUtc
                            : DateTime.MinValue;

                        foreach (var fi in appFiles)
                        {
                            bool isSharedBucket = fi.Name.StartsWith("0002a938", StringComparison.OrdinalIgnoreCase);
                            bool isCurrentForGame = (newestGameDriverWrite - fi.LastWriteTimeUtc).TotalMinutes <= 5.0;

                            if (!isSharedBucket && !isCurrentForGame && CanDeleteFile(fi.FullName))
                            {
                                cleanableFiles.Add(fi.FullName);
                                totalBytes += fi.Length;
                            }
                        }
                    }
                    else
                    {
                        // Jogo desinstalado ou aplicativo comum:
                        // Preserva pequenos caches de sistema ativos no driver atual (< 32 MB e modificados nos últimos 3 dias);
                        // remove caches de jogos desinstalados e todos os caches de drivers antigos!
                        foreach (var fi in appFiles)
                        {
                            string prefix = fi.Name.Length >= 8 ? fi.Name.Substring(0, 8) : "";
                            bool isActiveDrv = activePrefixes.Contains(prefix);
                            bool isRecentSmallSystemCache = isActiveDrv &&
                                                            (DateTime.UtcNow - fi.LastWriteTimeUtc).TotalDays <= 3.0 &&
                                                            fi.Length < 32L * 1024 * 1024;

                            if (!isRecentSmallSystemCache && CanDeleteFile(fi.FullName))
                            {
                                cleanableFiles.Add(fi.FullName);
                                totalBytes += fi.Length;
                            }
                        }
                    }
                }
            }
            catch { }
        }

        // 2. Outros diretórios de cache de shaders (D3DSCache, GLCache, NV_Cache, AMD DxCache/GLCache/VkCache)
        string[] otherShaderDirs =
        {
            Path.Combine(localAppData, "D3DSCache"),
            Path.Combine(localAppData, "NVIDIA", "GLCache"),
            Path.Combine(programData, "NVIDIA Corporation", "NV_Cache"),
            Path.Combine(localAppData, "AMD", "DxCache"),
            Path.Combine(localAppData, "AMD", "DxcCache"),
            Path.Combine(localAppData, "AMD", "GLCache"),
            Path.Combine(localAppData, "AMD", "VkCache")
        };

        foreach (var dir in otherShaderDirs)
        {
            if (!Directory.Exists(dir)) continue;
            existingDirs.Add(dir);

            try
            {
                foreach (var fi in new DirectoryInfo(dir).EnumerateFiles("*", SafeRecursiveEnum))
                {
                    try
                    {
                        // Preserva caches ativos modificados recentemente (< 7 dias) e apaga caches antigos/órfãos desbloqueados
                        if ((DateTime.UtcNow - fi.LastWriteTimeUtc).TotalDays > 7.0 && CanDeleteFile(fi.FullName))
                        {
                            cleanableFiles.Add(fi.FullName);
                            totalBytes += fi.Length;
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        return (cleanableFiles, totalBytes, existingDirs);
    }

    /// <summary>
    /// Limpa os itens de uma categoria selecionada com tratamento de arquivos em uso e preservação de caches ativos.
    /// </summary>
    public static async Task<(int CleanedCount, long CleanedBytes)> CleanCategoryAsync(QuickCleanCategory cat)
    {
        return await Task.Run(() =>
        {
            int count = 0;
            long bytes = 0;

            // Se a categoria possui uma lista explícita de arquivos seguros para exclusão (ex.: Caches de Shaders Obsoletos)
            if (cat.ExplicitTargetFiles != null)
            {
                foreach (var filePath in cat.ExplicitTargetFiles)
                {
                    try
                    {
                        if (File.Exists(filePath))
                        {
                            long len = new FileInfo(filePath).Length;
                            File.Delete(filePath);
                            count++;
                            bytes += len;
                        }
                    }
                    catch { /* Arquivo entrou em uso */ }
                }
                return (count, bytes);
            }

            var targetPaths = cat.Paths != null && cat.Paths.Count > 0
                ? cat.Paths
                : (!string.IsNullOrWhiteSpace(cat.Path) ? new List<string> { cat.Path } : new List<string>());

            foreach (var dirPath in targetPaths)
            {
                if (!Directory.Exists(dirPath)) continue;
                var di = new DirectoryInfo(dirPath);

                foreach (var fi in di.EnumerateFiles("*", SafeRecursiveEnum))
                {
                    try
                    {
                        long len = fi.Length;
                        fi.Delete();
                        count++;
                        bytes += len;
                    }
                    catch { /* Arquivo em uso pelo sistema */ }
                }

                try
                {
                    foreach (var sub in di.EnumerateDirectories())
                    {
                        try { sub.Delete(true); } catch { }
                    }
                }
                catch { }
            }

            return (count, bytes);
        });
    }

    /// <summary>
    /// Abre o item no Windows Explorer e o seleciona.
    /// </summary>
    public static void OpenInExplorer(string fullPath)
    {
        try
        {
            if (File.Exists(fullPath))
                Process.Start("explorer.exe", $"/select,\"{fullPath}\"");
            else if (Directory.Exists(fullPath))
                Process.Start("explorer.exe", $"\"{fullPath}\"");
        }
        catch { }
    }

    /// <summary>
    /// Envia o item para a Lixeira do Windows via Shell32 SHFileOperation.
    /// </summary>
    public static bool MoveToRecycleBin(string path)
    {
        try
        {
            var sh = new SHFILEOPSTRUCT
            {
                wFunc = FO_DELETE,
                pFrom = path + '\0' + '\0',
                fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT
            };
            return SHFileOperation(ref sh) == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Exclui o item permanentemente do disco.
    /// </summary>
    public static bool DeletePermanently(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
                return true;
            }
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
                return true;
            }
            return false;
        }
        catch { return false; }
    }

    #region Win32 Shell Recycle
    private const int FO_DELETE = 0x0003;
    private const ushort FOF_ALLOWUNDO = 0x0040;
    private const ushort FOF_NOCONFIRMATION = 0x0010;
    private const ushort FOF_SILENT = 0x0004;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public int wFunc;
        [MarshalAs(UnmanagedType.LPTStr)]
        public string pFrom;
        [MarshalAs(UnmanagedType.LPTStr)]
        public string? pTo;
        public ushort fFlags;
        public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        [MarshalAs(UnmanagedType.LPTStr)]
        public string? lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    private static extern int SHFileOperation(ref SHFILEOPSTRUCT FileOp);
    #endregion
}
