using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace SystemMonitor
{
    public partial class MainWindow : Window
    {
        private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };

        // ---- CPU tracking (via GetSystemTimes) ----
        private ulong _prevIdleTime, _prevKernelTime, _prevUserTime;
        private bool _cpuInitialized;

        // ---- Network tracking ----
        private long _prevBytesReceived;
        private long _prevBytesSent;
        private DateTime _prevSampleTime;

        // ---- Disk tracking ----
        private PerformanceCounter? _diskReadCounter;
        private PerformanceCounter? _diskWriteCounter;
        private const double DiskBarMaxMBps = 200.0; // visual scaling reference for the bar, not a hard limit

        // ---- Tray icon ----
        private System.Windows.Forms.NotifyIcon? _trayIcon;

        // ---- History graph (last ~40 seconds of CPU/RAM %) ----
        private readonly List<double> _cpuHistory = new();
        private readonly List<double> _ramHistory = new();
        private const int MaxHistoryPoints = 40;

        public MainWindow()
        {
            InitializeComponent();
            _timer.Tick += Timer_Tick;

            try
            {
                _diskReadCounter = new PerformanceCounter("PhysicalDisk", "Disk Read Bytes/sec", "_Total");
                _diskWriteCounter = new PerformanceCounter("PhysicalDisk", "Disk Write Bytes/sec", "_Total");
                _diskReadCounter.NextValue(); // first call always returns 0 - warm it up
                _diskWriteCounter.NextValue();
            }
            catch
            {
                // Performance counters can be unavailable/disabled on some systems; degrade gracefully.
                _diskReadCounter = null;
                _diskWriteCounter = null;
            }
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // Position in the top-right corner of the primary monitor's work area.
            var workArea = SystemParameters.WorkArea;
            Left = workArea.Right - Width - 20;
            Top = workArea.Top + 20;

            _prevSampleTime = DateTime.UtcNow;
            (_prevBytesReceived, _prevBytesSent) = GetTotalBytes();

            InitializeTrayIcon();

            _timer.Start();
        }

        private void Timer_Tick(object? sender, EventArgs e)
        {
            double cpu = UpdateCpu();
            double ram = UpdateRam();
            UpdateDisk();
            UpdateNetwork();
            UpdateGraph(cpu, ram);
        }

        // ------------------- CPU -------------------
        private double UpdateCpu()
        {
            if (!GetSystemTimes(out var idle, out var kernel, out var user))
                return CpuBar.Value;

            ulong idleTime = ToUInt64(idle);
            ulong kernelTime = ToUInt64(kernel);
            ulong userTime = ToUInt64(user);

            if (!_cpuInitialized)
            {
                _prevIdleTime = idleTime;
                _prevKernelTime = kernelTime;
                _prevUserTime = userTime;
                _cpuInitialized = true;
                return 0;
            }

            ulong idleDiff = idleTime - _prevIdleTime;
            ulong kernelDiff = kernelTime - _prevKernelTime; // kernel time includes idle time
            ulong userDiff = userTime - _prevUserTime;

            _prevIdleTime = idleTime;
            _prevKernelTime = kernelTime;
            _prevUserTime = userTime;

            ulong total = kernelDiff + userDiff;
            double cpuUsage = total > 0 ? (double)(total - idleDiff) / total * 100.0 : 0;
            cpuUsage = Math.Clamp(cpuUsage, 0, 100);

            CpuBar.Value = cpuUsage;
            CpuValueText.Text = $"{cpuUsage:0}%";
            return cpuUsage;
        }

        // ------------------- RAM -------------------
        private double UpdateRam()
        {
            var status = new MEMORYSTATUSEX();
            if (!GlobalMemoryStatusEx(status))
                return RamBar.Value;

            double usedPercent = status.dwMemoryLoad;
            double totalGb = status.ullTotalPhys / 1024.0 / 1024.0 / 1024.0;
            double usedGb = totalGb - (status.ullAvailPhys / 1024.0 / 1024.0 / 1024.0);

            RamBar.Value = usedPercent;
            RamValueText.Text = $"{usedPercent:0}%  ({usedGb:0.0}/{totalGb:0.0} GB)";
            return usedPercent;
        }

        // ------------------- Disk -------------------
        private void UpdateDisk()
        {
            if (_diskReadCounter == null || _diskWriteCounter == null)
            {
                DiskValueText.Text = "N/A";
                return;
            }

            try
            {
                double readBytesPerSec = _diskReadCounter.NextValue();
                double writeBytesPerSec = _diskWriteCounter.NextValue();
                double totalMBps = (readBytesPerSec + writeBytesPerSec) / 1024.0 / 1024.0;

                double percent = Math.Clamp(totalMBps / DiskBarMaxMBps * 100.0, 0, 100);
                DiskBar.Value = percent;
                DiskValueText.Text = $"{totalMBps:0.0} MB/s";
            }
            catch
            {
                DiskValueText.Text = "N/A";
            }
        }

        // ------------------- History graph -------------------
        private void UpdateGraph(double cpuPercent, double ramPercent)
        {
            _cpuHistory.Add(cpuPercent);
            _ramHistory.Add(ramPercent);

            if (_cpuHistory.Count > MaxHistoryPoints) _cpuHistory.RemoveAt(0);
            if (_ramHistory.Count > MaxHistoryPoints) _ramHistory.RemoveAt(0);

            RedrawGraphLine(CpuGraphLine, _cpuHistory);
            RedrawGraphLine(RamGraphLine, _ramHistory);
        }

        private void RedrawGraphLine(System.Windows.Shapes.Polyline line, List<double> history)
        {
            double width = GraphCanvas.ActualWidth > 0 ? GraphCanvas.ActualWidth : 200;
            double height = GraphCanvas.ActualHeight > 0 ? GraphCanvas.ActualHeight : 40;

            int n = history.Count;
            if (n < 2)
            {
                line.Points = new System.Windows.Media.PointCollection();
                return;
            }

            var points = new System.Windows.Media.PointCollection(n);
            for (int i = 0; i < n; i++)
            {
                double x = (double)i / (n - 1) * width;
                double v = Math.Clamp(history[i], 0, 100);
                double y = height - (v / 100.0 * height);
                points.Add(new System.Windows.Point(x, y));
            }
            line.Points = points;
        }

        // ------------------- Network -------------------
        private void UpdateNetwork()
        {
            var now = DateTime.UtcNow;
            double elapsedSeconds = (now - _prevSampleTime).TotalSeconds;
            if (elapsedSeconds <= 0) elapsedSeconds = 1;

            var (bytesReceived, bytesSent) = GetTotalBytes();

            long deltaReceived = bytesReceived - _prevBytesReceived;
            long deltaSent = bytesSent - _prevBytesSent;

            _prevBytesReceived = bytesReceived;
            _prevBytesSent = bytesSent;
            _prevSampleTime = now;

            double downBitsPerSec = Math.Max(0, deltaReceived) * 8.0 / elapsedSeconds;
            double upBitsPerSec = Math.Max(0, deltaSent) * 8.0 / elapsedSeconds;

            NetDownText.Text = FormatBitsPerSecond(downBitsPerSec);
            NetUpText.Text = FormatBitsPerSecond(upBitsPerSec);
        }

        private static (long received, long sent) GetTotalBytes()
        {
            long received = 0, sent = 0;

            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up)
                    continue;
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                    continue;
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Tunnel)
                    continue;

                var stats = nic.GetIPv4Statistics();
                received += stats.BytesReceived;
                sent += stats.BytesSent;
            }

            return (received, sent);
        }

        private static string FormatBitsPerSecond(double bits)
        {
            if (bits >= 1_000_000)
                return $"{bits / 1_000_000:0.0} Mbps";
            if (bits >= 1_000)
                return $"{bits / 1_000:0} Kbps";
            return $"{bits:0} bps";
        }

        // ------------------- Window chrome -------------------
        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
                DragMove();
        }

        private void CloseButton_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // The ✕ hides the overlay to the tray rather than fully quitting.
            // Use "Exit" from the tray icon's right-click menu to actually close the app.
            Hide();
        }

        // ------------------- Tray icon -------------------
        private void InitializeTrayIcon()
        {
            _trayIcon = new System.Windows.Forms.NotifyIcon
            {
                Icon = System.Drawing.SystemIcons.Application,
                Visible = true,
                Text = "System Monitor"
            };

            var menu = new System.Windows.Forms.ContextMenuStrip();
            menu.Items.Add("Show/Hide", null, (_, _) => ToggleVisibility());
            menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            menu.Items.Add("Exit", null, (_, _) => ExitApplication());
            _trayIcon.ContextMenuStrip = menu;

            _trayIcon.DoubleClick += (_, _) => ToggleVisibility();
        }

        private void ToggleVisibility()
        {
            if (Visibility == Visibility.Visible)
                Hide();
            else
                Show();
        }

        private void ExitApplication()
        {
            if (_trayIcon != null)
            {
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
                _trayIcon = null;
            }
            _diskReadCounter?.Dispose();
            _diskWriteCounter?.Dispose();
           System.Windows.Application.Current.Shutdown();
        }

        // ------------------- Win32 interop -------------------
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetSystemTimes(out FILETIME lpIdleTime, out FILETIME lpKernelTime, out FILETIME lpUserTime);

        [StructLayout(LayoutKind.Sequential)]
        private struct FILETIME
        {
            public uint dwLowDateTime;
            public uint dwHighDateTime;
        }

        private static ulong ToUInt64(FILETIME time) =>
            ((ulong)time.dwHighDateTime << 32) | time.dwLowDateTime;

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);

        [StructLayout(LayoutKind.Sequential)]
        private class MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;

            public MEMORYSTATUSEX()
            {
                dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            }
        }
    }
}
