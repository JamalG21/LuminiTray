using System.Windows;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using LuminiTray;

namespace LuminiTray.Tests;

[TestClass]
public class TreemapAndUndoTests
{
    [TestMethod]
    public void TestTreemapLayout_GeneratesValidTilesWithinBounds()
    {
        var root = new DiskSpaceItem
        {
            Name = "C:\\",
            FullPath = "C:\\",
            IsDirectory = true,
            Size = 100L * 1024 * 1024 * 1024 // 100 GB
        };

        root.Children.Add(new DiskSpaceItem
        {
            Name = "Users",
            FullPath = "C:\\Users",
            IsDirectory = true,
            Size = 40L * 1024 * 1024 * 1024,
            Category = "Outros"
        });

        root.Children.Add(new DiskSpaceItem
        {
            Name = "Windows",
            FullPath = "C:\\Windows",
            IsDirectory = true,
            Size = 30L * 1024 * 1024 * 1024,
            Category = "Windows e sistema"
        });

        root.Children.Add(new DiskSpaceItem
        {
            Name = "Program Files",
            FullPath = "C:\\Program Files",
            IsDirectory = true,
            Size = 20L * 1024 * 1024 * 1024,
            Category = "Programas e jogos"
        });

        root.Children.Add(new DiskSpaceItem
        {
            Name = "hiberfil.sys",
            FullPath = "C:\\hiberfil.sys",
            IsDirectory = false,
            Size = 10L * 1024 * 1024 * 1024,
            Category = "Windows e sistema"
        });

        var canvasBounds = new Rect(0, 0, 800, 500);
        var tiles = TreemapLayoutEngine.CalculateLayout(root, canvasBounds, maxDepth: 2);

        Assert.IsNotNull(tiles);
        Assert.AreEqual(4, tiles.Count, "Deveria ter gerado 4 blocos de primeiro nível correspondentes aos 4 itens.");

        foreach (var tile in tiles)
        {
            Assert.IsTrue(tile.Bounds.Width > 0, "A largura do bloco deve ser positiva.");
            Assert.IsTrue(tile.Bounds.Height > 0, "A altura do bloco deve ser positiva.");
            Assert.IsTrue(tile.Bounds.Left >= 0 && tile.Bounds.Right <= 800.01, "O bloco deve estar dentro dos limites horizontais do canvas.");
            Assert.IsTrue(tile.Bounds.Top >= 0 && tile.Bounds.Bottom <= 500.01, "O bloco deve estar dentro dos limites verticais do canvas.");
            Assert.IsNotNull(tile.FillBrush, "O bloco deve ter um pincel de preenchimento atribuído.");
        }
    }

    [TestMethod]
    public void TestCategoryColorsAndDistribution_All8CategoriesPresent()
    {
        string[] expectedCategories =
        {
            "Vídeos", "Imagens", "Músicas", "Documentos",
            "Programas e jogos", "Compactados", "Windows e sistema", "Outros"
        };

        foreach (var cat in expectedCategories)
        {
            Assert.IsTrue(TreemapLayoutEngine.CategoryColors.ContainsKey(cat), $"Categoria '{cat}' deve estar registrada nas cores oficiais.");
            var color = TreemapLayoutEngine.GetCategoryColor(cat);
            Assert.AreNotEqual(0, color.A, "O canal alpha deve ser opaco.");
        }

        var folder = new DiskSpaceItem
        {
            Name = "TestFolder",
            FullPath = "D:\\TestFolder",
            IsDirectory = true,
            Size = 1000
        };

        folder.Children.Add(new DiskSpaceItem { Name = "vid.mp4", Category = "Vídeos", Size = 400 });
        folder.Children.Add(new DiskSpaceItem { Name = "img.png", Category = "Imagens", Size = 300 });
        folder.Children.Add(new DiskSpaceItem { Name = "doc.pdf", Category = "Documentos", Size = 200 });
        folder.Children.Add(new DiskSpaceItem { Name = "other.dat", Category = "Outros", Size = 100 });

        var dist = DiskSpaceAnalyzer.ComputeCategoryDistribution(folder);

        Assert.AreEqual(400, dist["Vídeos"]);
        Assert.AreEqual(300, dist["Imagens"]);
        Assert.AreEqual(200, dist["Documentos"]);
        Assert.AreEqual(100, dist["Outros"]);
        Assert.AreEqual(0, dist["Músicas"]);
    }

    [TestMethod]
    public void TestWindowsDebloat_NewHubProTweaksRegistered()
    {
        var tweaks = WindowsDebloatService.GetAllTweaks();
        Assert.IsNotNull(tweaks);
        Assert.IsTrue(tweaks.Count >= 35, $"Deveriam haver ao menos 35 ajustes disponíveis (encontrados: {tweaks.Count}).");

        string[] requiredIds =
        {
            "taskbar_task_view", "taskbar_seconds_clock", "taskbar_align_left", "taskbar_hide_widgets",
            "taskbar_hide_chat", "taskbar_search_icon", "sys_silent_app_install",
            "explorer_show_extensions", "explorer_show_hidden", "explorer_open_this_pc",
            "explorer_disable_recent", "search_disable_bing",
            "privacy_disable_ad_id", "privacy_disable_app_tracking", "privacy_disable_activity_feed",
            "privacy_disable_typing_telemetry", "privacy_disable_feedback",
            "perf_disable_hibernation", "perf_app_kill_timeout", "perf_disable_transparency"
        };

        foreach (var reqId in requiredIds)
        {
            var tweak = tweaks.FirstOrDefault(t => t.Id == reqId);
            Assert.IsNotNull(tweak, $"O ajuste obrigatório '{reqId}' do HUB Pro deve estar presente no catálogo.");
            Assert.IsFalse(string.IsNullOrWhiteSpace(tweak.Title), $"O ajuste '{reqId}' deve ter um título legível.");
            Assert.IsFalse(string.IsNullOrWhiteSpace(tweak.Category), $"O ajuste '{reqId}' deve possuir uma categoria.");
            Assert.IsFalse(string.IsNullOrWhiteSpace(tweak.Description), $"O ajuste '{reqId}' deve possuir uma descrição explicativa.");
            Assert.IsNotNull(tweak.Read, $"O ajuste '{reqId}' deve ter função Read configurada.");
            Assert.IsNotNull(tweak.Write, $"O ajuste '{reqId}' deve ter função Write configurada.");
            Assert.IsNotNull(tweak.Restore, $"O ajuste '{reqId}' deve ter função Restore configurada.");
        }
    }
}
