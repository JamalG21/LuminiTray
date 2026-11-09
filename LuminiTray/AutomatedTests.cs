using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace LuminiTray;

/// <summary>
/// Executa testes automatizados de unidade e integração para validar
/// os componentes principais do Lumini Tray, incluindo configurações,
/// inicialização com Windows, otimizador, ajustes e funções automáticas.
/// </summary>
public static class AutomatedTests
{
    private record TestCase(string Name, Action Action);

    public static int RunAll()
    {
        Console.WriteLine();
        Console.WriteLine("====================================================================");
        Console.WriteLine("                 LUMINI TRAY - TESTES AUTOMATIZADOS                  ");
        Console.WriteLine("====================================================================");
        Console.WriteLine($" Executando em: {DateTime.Now:dd/MM/yyyy HH:mm:ss}");
        Console.WriteLine($" Diretório base: {AppContext.BaseDirectory}");
        Console.WriteLine("--------------------------------------------------------------------");

        var tests = new List<TestCase>
        {
            new("Configurações: Padrões e Serialização JSON", Test_AppSettings),
            new("Agendador de Inicialização: Constantes e Resolução de Executável", Test_AutoStartManager),
            new("Integridade dos Ajustes do Windows (Tweaks)", Test_WindowsTweaks_Integrity),
            new("Otimizador: Leitura de Memória e Processos", Test_Optimizer_MemoryAndApps),
            new("Gerenciador de Inicialização: Parser de Comandos e Caminhos", Test_StartupManager_ExeExtraction),
            new("Funções Automáticas: Execução e Validação de Resultados", Test_AutoFeatures_Execution),
            new("Banco de Especificações e Modelos do Sistema", Test_SystemInfo_Models)
        };

        int passed = 0;
        int failed = 0;
        var swTotal = Stopwatch.StartNew();

        foreach (var t in tests)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                t.Action();
                sw.Stop();
                passed++;
                Console.ForegroundColor = ConsoleColor.Green;
                Console.Write(" [PASS] ");
                Console.ResetColor();
                Console.WriteLine($"{t.Name} ({sw.ElapsedMilliseconds} ms)");
            }
            catch (Exception ex)
            {
                sw.Stop();
                failed++;
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Write(" [FAIL] ");
                Console.ResetColor();
                Console.WriteLine($"{t.Name} ({sw.ElapsedMilliseconds} ms)");
                Console.ForegroundColor = ConsoleColor.DarkYellow;
                Console.WriteLine($"        Detalhes: {ex.Message}");
                Console.ResetColor();
            }
        }

        swTotal.Stop();
        Console.WriteLine("====================================================================");
        if (failed == 0)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($" SUCESSO: Todos os {passed} testes foram aprovados em {swTotal.ElapsedMilliseconds} ms!");
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($" FALHA: {passed} aprovados, {failed} falhas em {swTotal.ElapsedMilliseconds} ms.");
        }
        Console.ResetColor();
        Console.WriteLine("====================================================================");
        Console.WriteLine();

        return failed == 0 ? 0 : 1;
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException($"Asserção falhou: {message}");
    }

    private static void Test_AppSettings()
    {
        var settings = new AppSettings();
        Assert(settings.AutoApplyTweaks == true, "AutoApplyTweaks deve ser true por padrão");
        Assert(settings.AutoCleanRam == true, "AutoCleanRam deve ser true por padrão");
        Assert(settings.AutoStartMinimized == true, "AutoStartMinimized deve ser true por padrão");
        Assert(settings.AutoCloseBackgroundApps == false, "AutoCloseBackgroundApps deve ser false por padrão por segurança");

        // Testa serialização e deserialização JSON
        settings.AutoStartWithWindows = true;
        settings.LastAutoFeaturesSummary = "Teste de execução";
        string json = JsonSerializer.Serialize(settings);
        var restored = JsonSerializer.Deserialize<AppSettings>(json);

        Assert(restored != null, "Falha ao desserializar AppSettings");
        Assert(restored!.AutoStartWithWindows == true, "Propriedade AutoStartWithWindows não preservada");
        Assert(restored.AutoApplyTweaks == true, "Propriedade AutoApplyTweaks não preservada");
        Assert(restored.LastAutoFeaturesSummary == "Teste de execução", "Propriedade LastAutoFeaturesSummary não preservada");
    }

    private static void Test_AutoStartManager()
    {
        Assert(AutoStartManager.TaskName == "LuminiTrayAutoStart", "Nome da tarefa agendada inválido");
        Assert(AutoStartManager.RegistryValueName == "LuminiTray", "Nome do valor de registro inválido");

        string exePath = AutoStartManager.GetExecutablePath();
        Assert(!string.IsNullOrWhiteSpace(exePath), "Caminho do executável retornado está vazio");

        // Testa consulta de status (não deve lançar exceção)
        bool sched = AutoStartManager.IsScheduledTaskConfigured();
        bool reg = AutoStartManager.IsRegistryRunConfigured();
        bool isEnabled = AutoStartManager.IsAutoStartEnabled();
        Assert(isEnabled == (sched || reg), "Consistência de IsAutoStartEnabled incorreta");

        string status = AutoStartManager.GetStatusSummary();
        Assert(!string.IsNullOrEmpty(status), "Status do AutoStartManager não deve ser vazio");
    }

    private static void Test_WindowsTweaks_Integrity()
    {
        var tweaks = WindowsTweaks.All();
        Assert(tweaks.Count >= 5, "Deveria haver pelo menos 5 ajustes cadastrados");

        var ids = new HashSet<string>();
        foreach (var t in tweaks)
        {
            Assert(!string.IsNullOrWhiteSpace(t.Id), "Tweak com ID vazio encontrado");
            Assert(ids.Add(t.Id), $"ID de tweak duplicado: {t.Id}");
            Assert(!string.IsNullOrWhiteSpace(t.Title), $"Título do tweak {t.Id} está vazio");
            Assert(!string.IsNullOrWhiteSpace(t.Description), $"Descrição do tweak {t.Id} está vazia");
        }

        Assert(ids.Contains("power"), "Ajuste 'power' não encontrado");
        Assert(ids.Contains("gamemode"), "Ajuste 'gamemode' não encontrado");
        Assert(ids.Contains("gamedvr"), "Ajuste 'gamedvr' não encontrado");
    }

    private static void Test_Optimizer_MemoryAndApps()
    {
        var (total, avail) = Optimizer.MemoryStatus();
        Assert(total > 0, "Total de memória RAM retornado pelo Optimizer é inválido");
        Assert(avail > 0, "Memória RAM disponível retornada pelo Optimizer é inválida");
        Assert(avail <= total, "Memória disponível maior que o total");

        var apps = Optimizer.ListApps();
        Assert(apps != null, "Lista de apps do Optimizer retornou null");

        // Garante que processos protegidos não constam na lista
        if (apps != null)
        {
            foreach (var app in apps)
            {
                Assert(!app.ProcessName.Equals("System", StringComparison.OrdinalIgnoreCase), "Processo 'System' não deve ser listado para fechar");
                Assert(!app.ProcessName.Equals("explorer", StringComparison.OrdinalIgnoreCase), "Processo 'explorer' não deve ser listado para fechar");
            }
        }
    }

    private static void Test_StartupManager_ExeExtraction()
    {
        string? exe1 = StartupManager.ExeFromCommand("\"C:\\Program Files\\App\\app.exe\" --argumento");
        Assert(exe1 != null && exe1.EndsWith("app.exe", StringComparison.OrdinalIgnoreCase), "Falha ao extrair executável entre aspas");

        string? exe2 = StartupManager.ExeFromCommand("C:\\Windows\\notepad.exe");
        Assert(exe2 != null && exe2.EndsWith("notepad.exe", StringComparison.OrdinalIgnoreCase), "Falha ao extrair executável sem aspas");
    }

    private static void Test_AutoFeatures_Execution()
    {
        var testSettings = new AppSettings
        {
            AutoApplyTweaks = false,      // não altera tweaks do sistema durante o teste rápido
            AutoCleanRam = true,          // testa a chamada da limpeza de RAM
            AutoCloseBackgroundApps = false
        };

        var task = AutoFeaturesManager.ApplyAsync(testSettings);
        task.Wait(10000);

        Assert(task.IsCompletedSuccessfully, "AutoFeaturesManager.ApplyAsync não completou com sucesso");
        var result = task.Result;
        Assert(result != null && result.Success, "AutoFeaturesResult.Success deve ser true");
        Assert(testSettings.LastAutoFeaturesApplied.HasValue, "LastAutoFeaturesApplied deve ser preenchido");
        Assert(!string.IsNullOrEmpty(result?.Summary), "Sumário do resultado das funções automáticas está vazio");
    }

    private static void Test_SystemInfo_Models()
    {
        var benchRecord = new BenchRecord(DateTime.Now, "gpu", 1500, "150 FPS médio");
        Assert(benchRecord.Score == 1500, "Score do BenchRecord incorreto");
        Assert(benchRecord.Test == "gpu", "Test do BenchRecord incorreto");

        var gameProfile = new GameProfile { Priority = "high", WinMode = "borderless" };
        Assert(gameProfile.Priority == "high", "Prioridade do GameProfile incorreta");
        Assert(gameProfile.WinMode == "borderless", "WinMode do GameProfile incorreto");
    }
}
