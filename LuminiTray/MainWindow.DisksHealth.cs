using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace LuminiTray;

public partial class MainWindow
{
    private readonly ObservableCollection<UsbDriveTarget> _usbDriveTargets = new();
    private UsbDriveTarget? _selectedUsbDrive;
    private CancellationTokenSource? _fakeDriveCts;

    private void InitDisksHealthView()
    {
        InitBadBlocks();
        RefreshUsbDrivesList();
    }

    private void TabDiskHealthMode_Checked(object sender, RoutedEventArgs e)
    {
        if (PnlDiskHealthGeneral == null || PnlDiskHealthFakeCheck == null) return;

        bool isFakeCheck = RadTabDiskFakeCheck?.IsChecked == true;
        PnlDiskHealthGeneral.Visibility = isFakeCheck ? Visibility.Collapsed : Visibility.Visible;
        PnlDiskHealthFakeCheck.Visibility = isFakeCheck ? Visibility.Visible : Visibility.Collapsed;

        if (isFakeCheck)
        {
            RefreshUsbDrivesList();
        }
    }

    private void RefreshUsbDrivesList()
    {
        _usbDriveTargets.Clear();
        var drives = UsbFakeDriveScanner.GetRemovableDrives();
        foreach (var d in drives) _usbDriveTargets.Add(d);

        if (PnlUsbDrivePills != null)
        {
            PnlUsbDrivePills.Children.Clear();
            if (_usbDriveTargets.Count == 0)
            {
                var txtNone = new TextBlock
                {
                    Text = "Nenhum pendrive, cartão de memória ou HD externo conectado.",
                    Foreground = (Brush)FindResource("Muted"),
                    FontSize = 12,
                    VerticalAlignment = VerticalAlignment.Center
                };
                PnlUsbDrivePills.Children.Add(txtNone);
                _selectedUsbDrive = null;
                UpdateUsbDriveDetailsUi();
                return;
            }

            foreach (var drive in _usbDriveTargets)
            {
                var rb = new RadioButton
                {
                    Style = (Style)FindResource("Chip"),
                    GroupName = "UsbDrivePills",
                    Content = drive.DisplayName,
                    Tag = drive,
                    Margin = new Thickness(0, 0, 8, 8)
                };

                rb.Checked += (s, e) =>
                {
                    _selectedUsbDrive = (s as RadioButton)?.Tag as UsbDriveTarget;
                    UpdateUsbDriveDetailsUi();
                };

                PnlUsbDrivePills.Children.Add(rb);
            }

            if (PnlUsbDrivePills.Children.OfType<RadioButton>().FirstOrDefault() is RadioButton firstRb)
            {
                firstRb.IsChecked = true;
            }
        }
    }

    private void UpdateUsbDriveDetailsUi()
    {
        if (_selectedUsbDrive == null)
        {
            if (TxtUsbDriveName != null) TxtUsbDriveName.Text = "—";
            if (TxtUsbVidPid != null) TxtUsbVidPid.Text = "—";
            if (TxtUsbVendor != null) TxtUsbVendor.Text = "—";
            if (TxtUsbProtocol != null) TxtUsbProtocol.Text = "—";
            if (TxtUsbBus != null) TxtUsbBus.Text = "—";
            if (TxtUsbSerial != null) TxtUsbSerial.Text = "—";
            if (TxtUsbAdvertisedCapacity != null) TxtUsbAdvertisedCapacity.Text = "—";
            if (BtnStartQuickFakeCheck != null) BtnStartQuickFakeCheck.IsEnabled = false;
            return;
        }

        if (TxtUsbDriveName != null) TxtUsbDriveName.Text = _selectedUsbDrive.Model;
        if (TxtUsbVidPid != null) TxtUsbVidPid.Text = $"{_selectedUsbDrive.Vid} / {_selectedUsbDrive.Pid}";
        if (TxtUsbVendor != null) TxtUsbVendor.Text = _selectedUsbDrive.VendorName;
        if (TxtUsbProtocol != null) TxtUsbProtocol.Text = _selectedUsbDrive.Protocol;
        if (TxtUsbBus != null) TxtUsbBus.Text = _selectedUsbDrive.UsbBusVersion;
        if (TxtUsbSerial != null) TxtUsbSerial.Text = _selectedUsbDrive.SerialNumber;
        if (TxtUsbAdvertisedCapacity != null) TxtUsbAdvertisedCapacity.Text = _selectedUsbDrive.TotalFormatted;

        if (BtnStartQuickFakeCheck != null) BtnStartQuickFakeCheck.IsEnabled = true;

        if (_selectedUsbDrive.IsSuspiciousController && BrdUsbSuspiciousWarning != null && TxtUsbSuspiciousWarning != null)
        {
            BrdUsbSuspiciousWarning.Visibility = Visibility.Visible;
            TxtUsbSuspiciousWarning.Text = _selectedUsbDrive.SuspiciousReason;
        }
        else if (BrdUsbSuspiciousWarning != null)
        {
            BrdUsbSuspiciousWarning.Visibility = Visibility.Collapsed;
        }
    }

    private void BtnRefreshUsbDrives_Click(object sender, RoutedEventArgs e)
    {
        RefreshUsbDrivesList();
    }

    private void BtnCopyUsbInfo_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedUsbDrive == null) return;

        string info = $"Nome: {_selectedUsbDrive.Model}\r\n" +
                      $"VID/PID: {_selectedUsbDrive.Vid} / {_selectedUsbDrive.Pid}\r\n" +
                      $"Fabricante: {_selectedUsbDrive.VendorName}\r\n" +
                      $"Capacidade Informada: {_selectedUsbDrive.TotalFormatted}\r\n" +
                      $"Barramento: {_selectedUsbDrive.UsbBusVersion}\r\n" +
                      $"Série: {_selectedUsbDrive.SerialNumber}";

        try
        {
            Clipboard.SetText(info);
            MessageBox.Show("Informações do hardware USB copiadas para a Área de Transferência!",
                "Copiado", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch { }
    }

    private async void BtnStartQuickFakeCheck_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedUsbDrive == null) return;

        _fakeDriveCts?.Cancel();
        _fakeDriveCts = new CancellationTokenSource();

        if (BtnStartQuickFakeCheck != null) BtnStartQuickFakeCheck.IsEnabled = false;
        if (BtnStopQuickFakeCheck != null) BtnStopQuickFakeCheck.IsEnabled = true;
        if (BrdUsbTestResult != null) BrdUsbTestResult.Visibility = Visibility.Collapsed;

        var progress = new Progress<UsbTestProgress>(p =>
        {
            if (PgbUsbTestProgress != null) PgbUsbTestProgress.Value = p.Percent;
            if (TxtUsbTestProgressStatus != null) TxtUsbTestProgressStatus.Text = $"{p.StatusMessage} ({p.SpeedMbPerSec:0.0} MB/s)";
        });

        bool highPrecision = ChkUsbHighPrecision?.IsChecked == true;

        try
        {
            var res = await UsbFakeDriveScanner.RunQuickSampleTestAsync(
                _selectedUsbDrive, highPrecision, progress, _fakeDriveCts.Token);

            if (BrdUsbTestResult != null)
            {
                BrdUsbTestResult.Visibility = Visibility.Visible;
                BrdUsbTestResult.Background = new SolidColorBrush(res.IsCounterfeit ? Color.FromArgb(40, 239, 68, 68) : Color.FromArgb(40, 16, 185, 129));
                BrdUsbTestResult.BorderBrush = new SolidColorBrush(res.IsCounterfeit ? Color.FromRgb(239, 68, 68) : Color.FromRgb(16, 185, 129));
            }

            if (TxtUsbTestResultSummary != null)
            {
                TxtUsbTestResultSummary.Text = res.SummaryMessage;
            }
        }
        catch (OperationCanceledException)
        {
            if (TxtUsbTestProgressStatus != null) TxtUsbTestProgressStatus.Text = "Teste interrompido pelo usuário.";
        }
        catch (Exception ex)
        {
            MessageBox.Show("Erro ao executar teste de integridade: " + ex.Message, "Teste de Pendrive", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            if (BtnStartQuickFakeCheck != null) BtnStartQuickFakeCheck.IsEnabled = true;
            if (BtnStopQuickFakeCheck != null) BtnStopQuickFakeCheck.IsEnabled = false;
            _fakeDriveCts = null;
        }
    }

    private void BtnStopQuickFakeCheck_Click(object sender, RoutedEventArgs e)
    {
        if (_fakeDriveCts != null && !_fakeDriveCts.IsCancellationRequested)
        {
            _fakeDriveCts.Cancel();
            BtnStopQuickFakeCheck.IsEnabled = false;
        }
    }
}
