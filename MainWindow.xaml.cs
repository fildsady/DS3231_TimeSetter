using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using HidSharp;
using ModernWpf;

namespace DS3231_TimeSetter;

public partial class MainWindow : Window
{
    private const int VID = 0x2E8A;
    private const int PID = 0x4003;

    private const byte CMD_SET   = 0x01;
    private const byte CMD_GET   = 0x02;
    private const byte CMD_LED   = 0x03;
    private const byte CMD_INPUT = 0x04;
    private const byte RSP_OK    = 0x01;
    private const byte RSP_TIME  = 0x03;
    private const byte RSP_INPUT = 0x04;

    private HidStream? _stream;
    private readonly DispatcherTimer _clockTimer;
    private readonly DispatcherTimer _rtcTimer;
    private readonly DispatcherTimer _inputTimer;
    private bool _busy;

    public MainWindow()
    {
        InitializeComponent();

        _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _clockTimer.Tick += (_, _) => txtPcTime.Text = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        _clockTimer.Start();

        _rtcTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _rtcTimer.Tick += (_, _) => ReadRtcTime();

        _inputTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _inputTimer.Tick += (_, _) => ReadInputs();

        Closed += (_, _) => Disconnect();
    }

    private void BtnConnect_Click(object sender, RoutedEventArgs e)
    {
        if (_stream != null)
        {
            Disconnect();
            return;
        }

        var device = DeviceList.Local.GetHidDeviceOrNull(VID, PID);
        if (device == null)
        {
            Log("Device not found (VID=2E8A, PID=4003) — check USB cable");
            return;
        }

        try
        {
            _stream = device.Open();
            _stream.ReadTimeout = 2000;
            _stream.WriteTimeout = 1000;

            btnConnect.Content = "Disconnect";
            btnSyncTime.IsEnabled = true;
            btnReadTime.IsEnabled = true;
            grpLed.IsEnabled = true;
            grpInput.IsEnabled = true;
            txtStatus.Text = $"Connected: {device.GetProductName()}";
            txtStatus.Foreground = Brushes.Green;
            _rtcTimer.Start();
            _inputTimer.Start();
            Log($"Connected via HID: {device.DevicePath}");
        }
        catch (Exception ex)
        {
            Log($"Error: {ex.Message}");
        }
    }

    private void Disconnect()
    {
        _rtcTimer.Stop();
        _inputTimer.Stop();
        try { _stream?.Close(); } catch { }
        _stream = null;
        _busy = false;
        btnConnect.Content = "Connect";
        btnSyncTime.IsEnabled = false;
        btnReadTime.IsEnabled = false;
        grpLed.IsEnabled = false;
        grpInput.IsEnabled = false;
        txtStatus.Text = "Disconnected";
        txtStatus.Foreground = Brushes.Gray;
        txtRtcTime.Text = "--:--:--";
        txtTemp.Text = "";
        indIn0.Background = indIn1.Background = indIn2.Background = Brushes.Gray;
    }

    private byte[]? SendReport(byte[] data)
    {
        if (_stream == null) return null;

        var outBuf = new byte[65];
        outBuf[0] = 0x00;
        Array.Copy(data, 0, outBuf, 1, Math.Min(data.Length, 64));

        _stream.Write(outBuf);

        var inBuf = _stream.Read();
        if (inBuf.Length > 1)
        {
            var result = new byte[inBuf.Length - 1];
            Array.Copy(inBuf, 1, result, 0, result.Length);
            return result;
        }
        return null;
    }

    private void BtnSyncTime_Click(object sender, RoutedEventArgs e)
    {
        if (_stream == null || _busy) return;

        _busy = true;
        try
        {
            var now = DateTime.Now;
            var cmd = new byte[64];
            cmd[0] = CMD_SET;
            cmd[1] = (byte)(now.Year - 2000);
            cmd[2] = (byte)now.Month;
            cmd[3] = (byte)now.Day;
            cmd[4] = (byte)now.DayOfWeek;
            cmd[5] = (byte)now.Hour;
            cmd[6] = (byte)now.Minute;
            cmd[7] = (byte)now.Second;

            Log($"TX: SET {now:yyyy-MM-dd HH:mm:ss}");
            var resp = SendReport(cmd);

            if (resp != null && resp[0] == RSP_OK)
                Log("Time synced successfully!");
            else
                Log("Sync failed!");
        }
        catch (Exception ex)
        {
            Log($"Error: {ex.Message}");
            Disconnect();
        }
        finally
        {
            _busy = false;
        }
    }

    private void BtnReadTime_Click(object sender, RoutedEventArgs e) => ReadRtcTime();

    private void ReadRtcTime()
    {
        if (_stream == null || _busy) return;

        _busy = true;
        try
        {
            var cmd = new byte[64];
            cmd[0] = CMD_GET;

            var resp = SendReport(cmd);
            if (resp == null || resp[0] != RSP_TIME) return;

            int year = 2000 + resp[1];
            int month = resp[2];
            int day = resp[3];
            int hour = resp[5];
            int min = resp[6];
            int sec = resp[7];
            int tempInt = (sbyte)resp[8];
            int tempFrac = resp[9];

            txtRtcTime.Text = $"{year}-{month:D2}-{day:D2} {hour:D2}:{min:D2}:{sec:D2}";
            txtTemp.Text = $"{tempInt}.{tempFrac}°C";
        }
        catch (TimeoutException) { }
        catch (Exception) { Disconnect(); }
        finally { _busy = false; }
    }

    private void Led_Click(object sender, RoutedEventArgs e)
    {
        if (_stream == null || _busy) return;

        _busy = true;
        try
        {
            byte mask = 0;
            if (chkLed0.IsChecked == true) mask |= 0x01;
            if (chkLed1.IsChecked == true) mask |= 0x02;
            if (chkLed2.IsChecked == true) mask |= 0x04;
            if (chkLed3.IsChecked == true) mask |= 0x08;

            var cmd = new byte[64];
            cmd[0] = CMD_LED;
            cmd[1] = mask;

            var resp = SendReport(cmd);
            if (resp != null && resp[0] == RSP_OK)
                Log($"LED set: 0b{Convert.ToString(mask, 2).PadLeft(4, '0')}");
            else
                Log("LED command failed");
        }
        catch (Exception ex)
        {
            Log($"Error: {ex.Message}");
            Disconnect();
        }
        finally { _busy = false; }
    }

    private void ReadInputs()
    {
        if (_stream == null || _busy) return;

        _busy = true;
        try
        {
            var cmd = new byte[64];
            cmd[0] = CMD_INPUT;

            var resp = SendReport(cmd);
            if (resp == null || resp[0] != RSP_INPUT) return;

            byte inputs = resp[1];
            indIn0.Background = (inputs & 0x01) != 0 ? Brushes.LimeGreen : Brushes.Gray;
            indIn1.Background = (inputs & 0x02) != 0 ? Brushes.LimeGreen : Brushes.Gray;
            indIn2.Background = (inputs & 0x04) != 0 ? Brushes.LimeGreen : Brushes.Gray;

            byte leds = resp[2];
            chkLed0.IsChecked = (leds & 0x01) != 0;
            chkLed1.IsChecked = (leds & 0x02) != 0;
            chkLed2.IsChecked = (leds & 0x04) != 0;
            chkLed3.IsChecked = (leds & 0x08) != 0;
        }
        catch (TimeoutException) { }
        catch (Exception) { Disconnect(); }
        finally { _busy = false; }
    }

    private void CmbTheme_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (cmbTheme.SelectedIndex == 0)
            ThemeManager.Current.ApplicationTheme = null;
        else if (cmbTheme.SelectedIndex == 1)
            ThemeManager.Current.ApplicationTheme = ApplicationTheme.Light;
        else
            ThemeManager.Current.ApplicationTheme = ApplicationTheme.Dark;
    }

    private void Log(string msg)
    {
        txtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}\r\n");
        txtLog.ScrollToEnd();
    }
}
