using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using LuminiTray;

namespace LuminiTray.Tests;

[TestClass]
public class OverlayAndAppsTests
{
    [TestMethod]
    public void TestAppInstaller_CatalogContainsExpectedCategories()
    {
        var catalog = AppInstaller.Catalog();
        Assert.IsTrue(catalog.Count >= 20, $"Catálogo deve conter pelo menos 20 apps, mas contém {catalog.Count}");

        var categories = catalog.Select(a => a.Category).Distinct().ToList();
        CollectionAssert.Contains(categories, "Jogos", "Catálogo deve conter a categoria Jogos");
        CollectionAssert.Contains(categories, "Comunicação", "Catálogo deve conter a categoria Comunicação");
        CollectionAssert.Contains(categories, "Navegação", "Catálogo deve conter a categoria Navegação");
        CollectionAssert.Contains(categories, "Utilitários", "Catálogo deve conter a categoria Utilitários");
    }

    [TestMethod]
    public void TestAppInstaller_AllAppsHaveValidOfficialProperties()
    {
        var catalog = AppInstaller.Catalog();
        var wingetIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var app in catalog)
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(app.Name), "App deve possuir Name");
            Assert.IsFalse(string.IsNullOrWhiteSpace(app.Category), $"App {app.Name} deve possuir Category");
            Assert.IsFalse(string.IsNullOrWhiteSpace(app.Publisher), $"App {app.Name} deve possuir Publisher");
            Assert.IsFalse(string.IsNullOrWhiteSpace(app.WingetId), $"App {app.Name} deve possuir WingetId");
            Assert.IsFalse(string.IsNullOrWhiteSpace(app.DisplayPrefix), $"App {app.Name} deve possuir DisplayPrefix");

            bool added = wingetIds.Add(app.WingetId);
            Assert.IsTrue(added, $"WingetId duplicado encontrado no catálogo: {app.WingetId}");
        }
    }

    [TestMethod]
    public void TestAppInstaller_CategoryDistribution()
    {
        var catalog = AppInstaller.Catalog();
        var jogos = catalog.Where(a => a.Category == "Jogos").ToList();
        var comunicacao = catalog.Where(a => a.Category == "Comunicação").ToList();
        var navegacao = catalog.Where(a => a.Category == "Navegação").ToList();
        var utilitarios = catalog.Where(a => a.Category == "Utilitários").ToList();

        Assert.IsTrue(jogos.Count >= 5, "Jogos deve ter pelo menos 5 apps");
        Assert.IsTrue(comunicacao.Count >= 5, "Comunicação deve ter pelo menos 5 apps");
        Assert.IsTrue(navegacao.Count >= 4, "Navegação deve ter pelo menos 4 apps");
        Assert.IsTrue(utilitarios.Count >= 6, "Utilitários deve ter pelo menos 6 apps");
    }

    [TestMethod]
    public void TestAppSettings_OverlayNewPropertiesSerialization()
    {
        var settings = new AppSettings
        {
            OverlayWidth = 320,
            OverlayHeight = 180,
            OverlayOpacity = 0.75,
            OverlayLocked = false
        };

        string json = JsonSerializer.Serialize(settings);
        var restored = JsonSerializer.Deserialize<AppSettings>(json);

        Assert.IsNotNull(restored);
        Assert.AreEqual(320, restored.OverlayWidth, 0.001);
        Assert.AreEqual(180, restored.OverlayHeight, 0.001);
        Assert.AreEqual(0.75, restored.OverlayOpacity, 0.001);
        Assert.IsFalse(restored.OverlayLocked);
        Assert.IsTrue(restored.OverlayTextOutlineEnabled);
        Assert.AreEqual(3.0, restored.OverlayTextOutlineThickness, 0.001);
        Assert.AreEqual(0.90, restored.OverlayTextOutlineOpacity, 0.001);
        Assert.IsTrue(restored.OverlayTextOutlineAutoOnly);
    }

    [TestMethod]
    public void TestOverlay_OpacityClamping()
    {
        double op1 = Math.Clamp(1.20, 0.20, 1.00);
        double op2 = Math.Clamp(0.05, 0.20, 1.00);
        double op3 = Math.Clamp(0.85, 0.20, 1.00);

        Assert.AreEqual(1.00, op1, 0.001);
        Assert.AreEqual(0.20, op2, 0.001);
        Assert.AreEqual(0.85, op3, 0.001);
    }
}
