using System.Windows.Media;

namespace LuminiTray.Tests;

[TestClass]
public class TemperatureAlertTests
{
    [TestMethod]
    public void Evaluate_RespectsDefaultAndCustomCriticalThresholds()
    {
        var cfg = new FanControlSaveSettings
        {
            TempAlertEnabled = true,
            TempAlertCriticalC = 80.0,
            TempAlertOrangeOffsetC = 7.0,
            TempAlertYellowOffsetC = 15.0
        };

        Assert.AreEqual(TempAlertLevel.Normal, TemperatureAlertHelper.Evaluate(64.9, cfg));
        Assert.AreEqual(TempAlertLevel.Yellow, TemperatureAlertHelper.Evaluate(65.0, cfg));
        Assert.AreEqual(TempAlertLevel.Yellow, TemperatureAlertHelper.Evaluate(72.9, cfg));
        Assert.AreEqual(TempAlertLevel.Orange, TemperatureAlertHelper.Evaluate(73.0, cfg));
        Assert.AreEqual(TempAlertLevel.Orange, TemperatureAlertHelper.Evaluate(79.9, cfg));
        Assert.AreEqual(TempAlertLevel.Red, TemperatureAlertHelper.Evaluate(80.0, cfg));
        Assert.AreEqual(TempAlertLevel.Red, TemperatureAlertHelper.Evaluate(95.0, cfg));
    }

    [DataTestMethod]
    [DataRow(70.0, 55.0, 63.0, 70.0)]
    [DataRow(80.0, 65.0, 73.0, 80.0)]
    [DataRow(90.0, 75.0, 83.0, 90.0)]
    public void GetThresholds_ComputesPresetsCorrectly(double critical, double expectedYellow, double expectedOrange, double expectedCrit)
    {
        var cfg = new FanControlSaveSettings { TempAlertCriticalC = critical };
        var (y, o, c) = TemperatureAlertHelper.GetThresholds(cfg);
        Assert.AreEqual(expectedYellow, y);
        Assert.AreEqual(expectedOrange, o);
        Assert.AreEqual(expectedCrit, c);
    }

    [TestMethod]
    public void Evaluate_WhenDisabled_AlwaysReturnsNormal()
    {
        var cfg = new FanControlSaveSettings { TempAlertEnabled = false, TempAlertCriticalC = 70.0 };
        Assert.AreEqual(TempAlertLevel.Normal, TemperatureAlertHelper.Evaluate(95.0, cfg));
        Assert.AreEqual(Brushes.White, TemperatureAlertHelper.GetBrush(95.0, cfg, Brushes.White, isDarkTheme: true));
    }
}
