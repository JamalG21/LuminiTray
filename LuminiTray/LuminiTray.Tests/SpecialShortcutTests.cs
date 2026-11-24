namespace LuminiTray.Tests;

[TestClass]
public class SpecialShortcutTests
{
    [DataTestMethod]
    [DataRow("Meu App Legal", "AtalhoEspecial_Meu_App_Legal")]
    [DataRow("MSI Afterburner!", "AtalhoEspecial_MSI_Afterburner")]
    [DataRow("AtalhoEspecial_Teste", "AtalhoEspecial_Teste")]
    [DataRow("   ", "AtalhoEspecial_App")]
    public void BuildTaskName_SanitizesAndPrefixesCorrectly(string input, string expected)
    {
        Assert.AreEqual(expected, SpecialShortcutService.BuildTaskName(input));
    }

    [TestMethod]
    public void BuildSchtasksArguments_UsesExpectedRunFormat()
    {
        string args = SpecialShortcutService.BuildSchtasksArguments("AtalhoEspecial_CpuZ");
        Assert.AreEqual("/run /tn \"AtalhoEspecial_CpuZ\"", args);
    }

    [TestMethod]
    public void BuildRegisterTaskPowerShellScript_ContainsRequiredFlagsAndSettings()
    {
        string script = SpecialShortcutService.BuildRegisterTaskPowerShellScript(
            @"C:\Program Files\App\App's.exe",
            "AtalhoEspecial_App");

        StringAssert.Contains(script, "New-ScheduledTaskAction -Execute 'C:\\Program Files\\App\\App''s.exe'");
        StringAssert.Contains(script, "-LogonType Interactive");
        StringAssert.Contains(script, "-RunLevel Highest");
        StringAssert.Contains(script, "-AllowStartIfOnBatteries");
        StringAssert.Contains(script, "-DontStopIfGoingOnBatteries");
        StringAssert.Contains(script, "-ExecutionTimeLimit (New-TimeSpan -Seconds 0)");
        StringAssert.Contains(script, "Register-ScheduledTask -TaskName 'AtalhoEspecial_App'");
        StringAssert.Contains(script, "-Force");
    }
}
