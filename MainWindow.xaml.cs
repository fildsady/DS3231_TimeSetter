using System.Windows;
using System.Windows.Threading;
using HidSharp;

namespace DS3231_TimeSetter;

public partial class MainWindow : Window
{
    private const int VID = 0x2E8A;
    private const int PID = 0x4003;

    private const byte CMD_SET  = 0x01;
    private const byte CMD_GET  = 0x02;
    private const byte RSP_OK   = 0x01;
    private const byte RSP_TIME = 0x03;

    private HidStream? _stream;
    private readonly DispatcherTimer _clockTimer;
    private readonly DispatcherTimer _rtcTimer;
    private bool _busy;

    public MainWindow()
    {
        InitializeComponent();

        _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _clockTimer.Tick += (_, _) => txtPcTime.Text = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        _clockTimer.Start();

        _rtcTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _rtcTimer.Tick += (_, _) => ReadRtcTime();

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
            txtStatus.Text = $"Connected: {device.GetProductName()}";
            txtStatus.Foreground = System.Windows.Media.Brushes.Green;
            _rtcTimer.Start();
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
        try { _stream?.Close(); } catch { }
        _stream = null;
        _busy = false;
        btnConnect.Content = "Connect";
        btnSyncTime.IsEnabled = false;
        btnReadTime.IsEnabled = false;
        txtStatus.Text = "Disconnected";
        txtStatus.Foreground = System.Windows.Media.Brushes.Gray;
        txtRtcTime.Text = "--:--:--";
        txtTemp.Text = "";
    }

    private byte[]? SendReport(byte[] data)
    {
        if (_stream == null) return null;

        // HidSharp: byte[0] = report ID (0 = no report ID), then 64 bytes data
        var outBuf = new byte[65];
        outBuf[0] = 0x00; // report ID
        Array.Copy(data, 0, outBuf, 1, Math.Min(data.Length, 64));

        _stream.Write(outBuf);

        var inBuf = _stream.Read();
        // inBuf[0] = report ID, [1..] = data
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
        catch (Exception)
        {
            Disconnect();
        }
        finally
        {
            _busy = false;
        }
    }

    private void Log(string msg)
    {
        txtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}\r\n");
        txtLog.ScrollToEnd();
    }
}
