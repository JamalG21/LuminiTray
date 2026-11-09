using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace LuminiTray;

/// <summary>
/// Módulo de Diagnóstico S.M.A.R.T. (smartmontools 7.5), Verificação de Superfície
/// e Resolução de Bad Blocks em dispositivos de armazenamento.
/// </summary>
public partial class MainWindow
{
    private CancellationTokenSource? _badBlockCts;
    private bool _isBadBlockScanning;
    private List<DiskScanTarget> _badBlockTargets = new();
    private SmartSnapshot? _currentSmartSnapshot;
    private readonly ObservableCollection<BadLbaEntry> _badLbaList = new();

    private static readonly SolidColorBrush BrushExcellent = new(Color.FromRgb(46, 204, 113));  // #2ECC71
    private static readonly SolidColorBrush BrushGood = new(Color.FromRgb(52, 152, 219));       // #3498DB
    private static readonly SolidColorBrush BrushAttention = new(Color.FromRgb(241, 196, 15));  // #F1C40F
    private static readonly SolidColorBrush BrushCritical = new(Color.FromRgb(230, 126, 34));   // #E67E22
    private static readonly SolidColorBrush BrushBadBlock = new(Color.FromRgb(231, 76, 60));    // #E74C3C
    private static readonly SolidColorBrush BrushPending = new(Color.FromArgb(40, 255, 255, 255));

    private void InitBadBlocks()
    {
        if (CmbBadBlockTargets == null) return;
        LstBadLbas.ItemsSource = _badLbaList;

        _badBlockTargets = DiskSurfaceScanner.GetPhysicalTargets();

        CmbBadBlockTargets.Items.Clear();
        foreach (var t in _badBlockTargets)
        {
            CmbBadBlockTargets.Items.Add(new ComboBoxItem
            {
                Content = t.DisplayName,
                Tag = t
            });
        }

        if (CmbBadBlockTargets.Items.Count > 0)
        {
            CmbBadBlockTargets.SelectedIndex = 0;
        }
    }

    private async void CmbBadBlockTargets_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var target = (CmbBadBlockTargets.SelectedItem as ComboBoxItem)?.Tag as DiskScanTarget;
        if (target == null) return;
        await LoadTargetDiagnosticsAsync(target);
    }

    private async void BtnRefreshSmartBadBlocks_Click(object sender, RoutedEventArgs e)
    {
        var target = (CmbBadBlockTargets.SelectedItem as ComboBoxItem)?.Tag as DiskScanTarget;
        if (target != null)
        {
            await LoadTargetDiagnosticsAsync(target);
        }
    }

    private async Task LoadTargetDiagnosticsAsync(DiskScanTarget target)
    {
        _badLbaList.Clear();
        TxtBadLbaCount.Text = "(0 detectados)";

        if (target.PhysicalDisk == null)
        {
            TxtSmartHealthBadge.Text = "S.M.A.R.T.: INDISPONÍVEL";
            TxtSmartReallocated.Text = "—";
            TxtSmartPending.Text = "—";
            TxtSmartOfflineUnc.Text = "—";
            TxtSmartMediaErrors.Text = "—";
            TxtSmartCrcErrors.Text = "—";
            TxtSelfTestExecStatus.Text = "Alvo selecionado é um volume lógico sem vínculo direto com disco físico.";
            return;
        }

        var disk = target.PhysicalDisk;
        TxtSelfTestExecStatus.Text = "Lendo informações do firmware via smartmontools...";

        SmartSnapshot snap = await Task.Run(() => SmartDiagnostics.Read(disk));
        _currentSmartSnapshot = snap;

        // Atualiza Badge S.M.A.R.T.
        if (!snap.Available)
        {
            TxtSmartHealthBadge.Text = "S.M.A.R.T.: INDISPONÍVEL";
            BrdSmartHealthBadge.Background = new SolidColorBrush(Color.FromRgb(100, 100, 100));
        }
        else if (snap.HealthPassed == true)
        {
            TxtSmartHealthBadge.Text = "S.M.A.R.T.: PASSED (APROVADO)";
            BrdSmartHealthBadge.Background = new SolidColorBrush(Color.FromRgb(46, 204, 113));
        }
        else if (snap.HealthPassed == false)
        {
            TxtSmartHealthBadge.Text = "S.M.A.R.T.: FAILED (FALHANDO)";
            BrdSmartHealthBadge.Background = new SolidColorBrush(Color.FromRgb(231, 76, 60));
        }
        else
        {
            TxtSmartHealthBadge.Text = "S.M.A.R.T.: DESCONHECIDO";
            BrdSmartHealthBadge.Background = new SolidColorBrush(Color.FromRgb(241, 196, 15));
        }

        // Atualiza contadores
        TxtSmartReallocated.Text = snap.Reallocated >= 0 ? snap.Reallocated.ToString("N0") : "—";
        TxtSmartReallocated.Foreground = snap.Reallocated > 0 ? (snap.Reallocated >= 50 ? BrushBadBlock : BrushAttention) : (Brush)FindResource("Fg");

        TxtSmartPending.Text = snap.Pending >= 0 ? snap.Pending.ToString("N0") : "—";
        TxtSmartPending.Foreground = snap.Pending > 0 ? BrushBadBlock : (Brush)FindResource("Fg");

        TxtSmartOfflineUnc.Text = snap.OfflineUncorrectable >= 0 ? snap.OfflineUncorrectable.ToString("N0") : "—";
        TxtSmartOfflineUnc.Foreground = snap.OfflineUncorrectable > 0 ? BrushBadBlock : (Brush)FindResource("Fg");

        TxtSmartMediaErrors.Text = snap.MediaErrors.HasValue ? snap.MediaErrors.Value.ToString("N0") : "—";
        TxtSmartMediaErrors.Foreground = (snap.MediaErrors ?? 0) > 0 ? BrushBadBlock : (Brush)FindResource("Fg");

        TxtSmartCrcErrors.Text = snap.CrcErrors >= 0 ? snap.CrcErrors.ToString("N0") : "—";
        TxtSmartCrcErrors.Foreground = snap.CrcErrors > 0 ? BrushAttention : (Brush)FindResource("Fg");

        // Status de Auto-Teste do Firmware
        TxtSelfTestExecStatus.Text = snap.SelfTestExecText;
        BtnSelfTestShort.IsEnabled = snap.SelfTestSupported && !snap.SelfTestRunning;
        BtnSelfTestExt.IsEnabled = snap.SelfTestSupported && !snap.SelfTestRunning;
        BtnSelfTestAbort.IsEnabled = snap.SelfTestRunning;

        // Preenche LBAs suspeitos encontrados no firmware
        foreach (var lba in snap.SuspectLbas.OrderBy(x => x))
        {
            var loc = SmartDiagnostics.Locate(disk, lba);
            _badLbaList.Add(new BadLbaEntry
            {
                Lba = lba,
                Source = "S.M.A.R.T. (Firmware)",
                Location = loc.Region,
                AffectedFile = "Clique em MAPEAR ARQUIVOS",
                Status = "Suspeito / Defeituoso"
            });
        }
        TxtBadLbaCount.Text = $"({_badLbaList.Count} detectados)";

        // Atualiza card de veredito
        UpdateVerdictUi(snap);
    }

    private void UpdateVerdictUi(SmartSnapshot snap)
    {
        TxtBadBlockVerdictTitle.Text = snap.VerdictTitle;
        TxtBadBlockVerdictDesc.Text = snap.VerdictText;

        if (snap.Recommendations.Count > 0)
        {
            TxtBadBlockRecommendations.Text = "Ações recomendadas:\n• " + string.Join("\n• ", snap.Recommendations);
        }
        else
        {
            TxtBadBlockRecommendations.Text = "";
        }

        switch (snap.Level)
        {
            case BadBlockLevel.Healthy:
                TxtBadBlockVerdictIcon.Text = "🛡️";
                BrdBadBlockVerdictIconBg.Background = new SolidColorBrush(Color.FromArgb(40, 46, 204, 113));
                break;
            case BadBlockLevel.Attention:
                TxtBadBlockVerdictIcon.Text = "⚠️";
                BrdBadBlockVerdictIconBg.Background = new SolidColorBrush(Color.FromArgb(40, 241, 196, 15));
                break;
            case BadBlockLevel.Critical:
                TxtBadBlockVerdictIcon.Text = "🚨";
                BrdBadBlockVerdictIconBg.Background = new SolidColorBrush(Color.FromArgb(40, 231, 76, 60));
                break;
            default:
                TxtBadBlockVerdictIcon.Text = "ℹ️";
                BrdBadBlockVerdictIconBg.Background = (Brush)FindResource("Line");
                break;
        }
    }

    private async void BtnSelfTestShort_Click(object sender, RoutedEventArgs e)
    {
        var target = (CmbBadBlockTargets.SelectedItem as ComboBoxItem)?.Tag as DiskScanTarget;
        if (target?.PhysicalDisk == null) return;

        var (ok, msg) = SmartDiagnostics.StartSelfTest(target.PhysicalDisk, SmartDiagnostics.SelfTestKind.Short);
        MessageBox.Show(msg, "Auto-Teste de Firmware (smartmontools)", MessageBoxButton.OK,
            ok ? MessageBoxImage.Information : MessageBoxImage.Warning);

        if (ok) await LoadTargetDiagnosticsAsync(target);
    }

    private async void BtnSelfTestExt_Click(object sender, RoutedEventArgs e)
    {
        var target = (CmbBadBlockTargets.SelectedItem as ComboBoxItem)?.Tag as DiskScanTarget;
        if (target?.PhysicalDisk == null) return;

        var (ok, msg) = SmartDiagnostics.StartSelfTest(target.PhysicalDisk, SmartDiagnostics.SelfTestKind.Extended);
        MessageBox.Show(msg, "Auto-Teste Estendido (smartmontools)", MessageBoxButton.OK,
            ok ? MessageBoxImage.Information : MessageBoxImage.Warning);

        if (ok) await LoadTargetDiagnosticsAsync(target);
    }

    private async void BtnSelfTestAbort_Click(object sender, RoutedEventArgs e)
    {
        var target = (CmbBadBlockTargets.SelectedItem as ComboBoxItem)?.Tag as DiskScanTarget;
        if (target?.PhysicalDisk == null) return;

        var (ok, msg) = SmartDiagnostics.StartSelfTest(target.PhysicalDisk, SmartDiagnostics.SelfTestKind.Abort);
        MessageBox.Show(msg, "Auto-Teste de Firmware", MessageBoxButton.OK, MessageBoxImage.Information);

        if (ok) await LoadTargetDiagnosticsAsync(target);
    }

    private async void BtnLocateBadLbaFiles_Click(object sender, RoutedEventArgs e)
    {
        var target = (CmbBadBlockTargets.SelectedItem as ComboBoxItem)?.Tag as DiskScanTarget;
        if (target?.PhysicalDisk == null)
        {
            MessageBox.Show("Selecione um disco com partições para localizar os arquivos afetados.", "Localização de Arquivos",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (_badLbaList.Count == 0)
        {
            MessageBox.Show("Nenhum LBA suspeito ou com erro foi detectado para mapeamento.", "Localização de Arquivos",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        BtnLocateBadLbaFiles.IsEnabled = false;
        try
        {
            var disk = target.PhysicalDisk;
            foreach (var entry in _badLbaList)
            {
                var loc = SmartDiagnostics.Locate(disk, entry.Lba);
                entry.Location = loc.Region;

                if (!string.IsNullOrEmpty(loc.Letter) && loc.Cluster.HasValue)
                {
                    entry.AffectedFile = "Consultando sistema de arquivos...";
                    string file = await SmartDiagnostics.QueryFileAtClusterAsync(loc.Letter, loc.Cluster.Value);
                    entry.AffectedFile = file;
                }
                else
                {
                    entry.AffectedFile = "Fora de volumes montados / setor de partição";
                }
            }

            MessageBox.Show("Mapeamento de arquivos concluído!", "Localização de Arquivos", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        finally
        {
            BtnLocateBadLbaFiles.IsEnabled = true;
        }
    }

    private async void BtnRunSelectiveTest_Click(object sender, RoutedEventArgs e)
    {
        var target = (CmbBadBlockTargets.SelectedItem as ComboBoxItem)?.Tag as DiskScanTarget;
        if (target?.PhysicalDisk == null) return;

        var disk = target.PhysicalDisk;
        if (disk.IsNvme)
        {
            MessageBox.Show("O auto-teste seletivo por faixas de LBAs é específico da especificação ATA/SATA (smartmontools). " +
                            "Para NVMe, utilize o Auto-Teste Estendido.", "Auto-Teste Seletivo", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var lbas = _badLbaList.Select(x => x.Lba).ToList();
        if (lbas.Count == 0)
        {
            MessageBox.Show("Não há LBAs defeituosos identificados para compor o auto-teste seletivo.", "Auto-Teste Seletivo",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var (ok, msg) = SmartDiagnostics.StartSelectiveSelfTest(disk, lbas);
        MessageBox.Show(msg, "Auto-Teste Seletivo SMART", MessageBoxButton.OK, ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
        if (ok) await LoadTargetDiagnosticsAsync(target);
    }

    private void BtnRepairChkdsk_Click(object sender, RoutedEventArgs e)
    {
        var target = (CmbBadBlockTargets.SelectedItem as ComboBoxItem)?.Tag as DiskScanTarget;
        string? volumeLetter = target?.PhysicalDisk?.Volumes.FirstOrDefault()?.Letter
                              ?? (!string.IsNullOrEmpty(target?.DriveLetter) && target.DriveLetter.Contains(":") ? target.DriveLetter : null);

        if (string.IsNullOrEmpty(volumeLetter))
        {
            MessageBox.Show("Não foi possível identificar a letra da unidade para executar o CHKDSK.", "Reparo de Setores",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var ans = MessageBox.Show(
            $"Deseja iniciar o reparo de superfície via CHKDSK /R para a unidade {volumeLetter}?\n\n" +
            "O Windows examinará cada cluster, isolará os setores defeituosos no $BadClus para impedir gravação " +
            "e forçará a controladora/disco a realocar setores pendentes.",
            "Reparo de Bad Blocks com CHKDSK /R",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (ans == MessageBoxResult.Yes)
        {
            SmartDiagnostics.LaunchChkdskRepair(volumeLetter, out string msg);
            MessageBox.Show(msg, "Reparo de Bad Blocks", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private async void BtnStartBadBlockScan_Click(object sender, RoutedEventArgs e)
    {
        if (_isBadBlockScanning) return;

        var selectedTarget = (CmbBadBlockTargets.SelectedItem as ComboBoxItem)?.Tag as DiskScanTarget;
        if (selectedTarget == null)
        {
            MessageBox.Show("Por favor, selecione uma unidade de disco para realizar o teste.", "Teste de Bad Blocks",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        bool isQuick = RadBadBlockQuick.IsChecked == true;

        _isBadBlockScanning = true;
        _badBlockCts = new CancellationTokenSource();
        BtnStartBadBlockScan.IsEnabled = false;
        BtnStopBadBlockScan.IsEnabled = true;
        CmbBadBlockTargets.IsEnabled = false;
        RadBadBlockQuick.IsEnabled = false;
        RadBadBlockFull.IsEnabled = false;

        // Limpa entradas anteriores de varredura de superfície e prepara contadores
        for (int i = _badLbaList.Count - 1; i >= 0; i--)
        {
            if (_badLbaList[i].Source == "Varredura de Superfície")
            {
                _badLbaList.RemoveAt(i);
            }
        }
        TxtBadLbaCount.Text = $"({_badLbaList.Count} detectados)";

        TxtCountExcellent.Text = "0";
        TxtCountGood.Text = "0";
        TxtCountAttention.Text = "0";
        TxtCountCritical.Text = "0";
        TxtCountBadBlocks.Text = "0";
        TxtBadBlockNotice.Text = "Verificando...";
        PgbBadBlockProgress.Value = 0;
        TxtBadBlockPercent.Text = "0%";
        TxtBadBlockSpeed.Text = "0 MB/s";
        TxtBadBlockStatus.Text = $"Iniciando varredura {(isQuick ? "rápida (amostragem)" : "completa")} em {selectedTarget.DisplayName}...";

        // Prepara grade visual de blocos
        PnlBadBlocksGrid.Children.Clear();
        int expectedBlocks = isQuick ? 400 : (int)Math.Min(selectedTarget.TotalBytes / (512 * 1024), 2000);
        if (expectedBlocks <= 0) expectedBlocks = 200;

        var rectangles = new Rectangle[expectedBlocks];
        for (int i = 0; i < expectedBlocks; i++)
        {
            var r = new Rectangle
            {
                Width = 8,
                Height = 8,
                RadiusX = 1.5,
                RadiusY = 1.5,
                Margin = new Thickness(1.5),
                Fill = BrushPending
            };
            rectangles[i] = r;
            PnlBadBlocksGrid.Children.Add(r);
        }

        try
        {
            var result = await DiskSurfaceScanner.ScanSurfaceAsync(
                selectedTarget,
                isQuick,
                (progress, status, index) =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        PgbBadBlockProgress.Value = progress.Percent;
                        TxtBadBlockPercent.Text = $"{progress.Percent:0}%";
                        TxtBadBlockSpeed.Text = $"{progress.CurrentSpeedMBps:0.#} MB/s";
                        TxtBadBlockStatus.Text = progress.StatusMessage;

                        TxtCountExcellent.Text = progress.ExcellentCount.ToString();
                        TxtCountGood.Text = progress.GoodCount.ToString();
                        TxtCountAttention.Text = progress.AttentionCount.ToString();
                        TxtCountCritical.Text = progress.CriticalCount.ToString();
                        TxtCountBadBlocks.Text = progress.BadBlockCount.ToString();

                        if (index < rectangles.Length)
                        {
                            rectangles[index].Fill = status switch
                            {
                                SurfaceBlockStatus.Excellent => BrushExcellent,
                                SurfaceBlockStatus.Good => BrushGood,
                                SurfaceBlockStatus.Attention => BrushAttention,
                                SurfaceBlockStatus.Critical => BrushCritical,
                                SurfaceBlockStatus.BadBlock => BrushBadBlock,
                                _ => BrushPending
                            };
                        }
                    });
                },
                _badBlockCts.Token);

            // Adiciona LBAs defeituosos encontrados pela varredura à tabela
            if (result.DetectedBadLbas.Count > 0 && selectedTarget.PhysicalDisk != null)
            {
                var disk = selectedTarget.PhysicalDisk;
                foreach (var lba in result.DetectedBadLbas)
                {
                    if (!_badLbaList.Any(x => x.Lba == lba))
                    {
                        var loc = SmartDiagnostics.Locate(disk, lba);
                        _badLbaList.Add(new BadLbaEntry
                        {
                            Lba = lba,
                            Source = "Varredura de Superfície",
                            Location = loc.Region,
                            AffectedFile = "Clique em MAPEAR ARQUIVOS",
                            Status = "Setor Defeituoso"
                        });
                    }
                }
                TxtBadLbaCount.Text = $"({_badLbaList.Count} detectados)";
            }

            // Veredito
            if (result.IsAborted)
            {
                TxtBadBlockStatus.Text = "Varredura interrompida pelo usuário.";
                TxtBadBlockVerdictIcon.Text = "⏹️";
                TxtBadBlockVerdictTitle.Text = "Varredura Interrompida";
                TxtBadBlockVerdictDesc.Text = $"O teste foi pausado após analisar {result.BlockIndex} blocos. Nenhum dado foi alterado no disco.";
            }
            else if (result.BadBlockCount == 0)
            {
                TxtBadBlockStatus.Text = "Varredura concluída! Nenhum setor com erro físico encontrado.";
                TxtBadBlockVerdictIcon.Text = "🛡️";
                TxtBadBlockVerdictTitle.Text = "SUPERFÍCIE 100% ÍNTEGRA · NENHUM BAD BLOCK DETECTADO";
                TxtBadBlockVerdictDesc.Text = $"Varredura concluída com êxito em {selectedTarget.DisplayName}. Todos os blocos responderam com integridade e sem falhas físicas de leitura.";
                TxtBadBlockNotice.Text = "Nenhum erro";
            }
            else
            {
                TxtBadBlockStatus.Text = $"ATENÇÃO: {result.BadBlockCount} bad blocks detectados!";
                TxtBadBlockVerdictIcon.Text = "⚠️";
                TxtBadBlockVerdictTitle.Text = $"ALERTA CRÍTICO: {result.BadBlockCount} SETORES DEFEITUOSOS ENCONTRADOS!";
                TxtBadBlockVerdictDesc.Text = "Foram encontradas falhas graves de leitura física ou erros de redundância cíclica (CRC). " +
                                              "Recomendamos fazer BACKUP IMEDIATO dos seus dados e utilizar a opção 'Mapear Arquivos Afetados' para resgatar arquivos.";
                TxtBadBlockNotice.Text = $"{result.BadBlockCount} defeitos";
            }
        }
        catch (Exception ex)
        {
            TxtBadBlockStatus.Text = $"Erro durante o teste: {ex.Message}";
        }
        finally
        {
            _isBadBlockScanning = false;
            BtnStartBadBlockScan.IsEnabled = true;
            BtnStopBadBlockScan.IsEnabled = false;
            CmbBadBlockTargets.IsEnabled = true;
            RadBadBlockQuick.IsEnabled = true;
            RadBadBlockFull.IsEnabled = true;
        }
    }

    private void BtnStopBadBlockScan_Click(object sender, RoutedEventArgs e)
    {
        _badBlockCts?.Cancel();
    }
}
