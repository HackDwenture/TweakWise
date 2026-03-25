using System;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using LibreHardwareMonitor.Hardware;
using Application = System.Windows.Application;

namespace TweakWise.Managers
{
    public sealed class TrayTemperatureManager : IDisposable
    {
        private readonly Computer _computer;
        private readonly DispatcherTimer _timer;
        private readonly Forms.NotifyIcon _notifyIcon;
        private bool _enabled;
        private bool _showTemperature;
        private Icon _currentIcon;

        public TrayTemperatureManager()
        {
            _computer = new Computer
            {
                IsCpuEnabled = true,
                IsGpuEnabled = true,
                IsMotherboardEnabled = true,
                IsStorageEnabled = true
            };
            _computer.Open();

            var menu = new Forms.ContextMenuStrip();
            menu.Items.Add("Открыть", null, (_, _) => RestoreMainWindow());
            menu.Items.Add("Выход", null, (_, _) => ExitApplication());

            _notifyIcon = new Forms.NotifyIcon
            {
                Visible = false,
                ContextMenuStrip = menu
            };
            _notifyIcon.DoubleClick += (_, _) => RestoreMainWindow();

            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(10)
            };
            _timer.Tick += (_, _) => Refresh();
        }

        public void ApplyPreferences(bool enabled, bool showTemperature)
        {
            _enabled = enabled;
            _showTemperature = showTemperature;

            if (!_enabled)
            {
                _timer.Stop();
                _notifyIcon.Visible = false;
                return;
            }

            Refresh();
            _notifyIcon.Visible = true;
            _timer.IsEnabled = _showTemperature;
            if (!_showTemperature)
                _timer.Stop();
        }

        public void RestoreMainWindow()
        {
            if (Application.Current.MainWindow is not Window window)
                return;

            if (!window.IsVisible)
                window.Show();

            window.ShowInTaskbar = true;

            if (window.WindowState == WindowState.Minimized)
                window.WindowState = WindowState.Normal;

            window.Activate();
        }

        private static void ExitApplication()
        {
            if (Application.Current.MainWindow is MainWindow mainWindow)
            {
                mainWindow.AllowCloseAndShutdown();
                return;
            }

            Application.Current.Shutdown();
        }

        private void Refresh()
        {
            if (!_enabled)
                return;

            if (!_showTemperature)
            {
                UpdateIcon("TW");
                _notifyIcon.Text = "TweakWise работает в трее";
                _notifyIcon.Visible = true;
                return;
            }

            foreach (var hardware in _computer.Hardware)
                UpdateHardwareRecursive(hardware);

            var sensors = _computer.Hardware
                .SelectMany(FlattenHardware)
                .SelectMany(GetSensors)
                .Where(sensor => sensor.SensorType == SensorType.Temperature && sensor.Value.HasValue)
                .ToList();

            string cpuText = BuildPreferredTemperatureText(sensors, "CPU", "Package", "Tctl/Tdie", "CCD", "Core");
            string gpuText = BuildPreferredTemperatureText(sensors, "GPU", "Hot Spot", "GPU Core", "Core", "Memory");
            string display = cpuText != "--" ? cpuText : gpuText;

            UpdateIcon(display);
            _notifyIcon.Text = $"TweakWise | CPU: {cpuText}°C | GPU: {gpuText}°C";
            _notifyIcon.Visible = true;
        }

        private static string BuildPreferredTemperatureText(
            System.Collections.Generic.List<ISensor> sensors,
            string hardwareToken,
            params string[] preferredNames)
        {
            var filtered = sensors.Where(sensor =>
                    sensor.Hardware?.Name?.IndexOf(hardwareToken, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    sensor.Hardware?.HardwareType.ToString().IndexOf(hardwareToken, StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();

            foreach (var name in preferredNames)
            {
                var match = filtered.FirstOrDefault(sensor => sensor.Name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0);
                if (match?.Value != null)
                    return $"{Math.Round(match.Value.Value):0}";
            }

            var fallback = filtered.FirstOrDefault(sensor => sensor.Value != null);
            return fallback?.Value != null ? $"{Math.Round(fallback.Value.Value):0}" : "--";
        }

        private void UpdateIcon(string text)
        {
            _currentIcon?.Dispose();
            _currentIcon = CreateIcon(text);
            _notifyIcon.Icon = _currentIcon;
        }

        private static Icon CreateIcon(string text)
        {
            using var bitmap = new Bitmap(16, 16);
            using var graphics = Graphics.FromImage(bitmap);
            graphics.Clear(Color.Transparent);
            graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.SingleBitPerPixelGridFit;

            using var backgroundBrush = new SolidBrush(Color.FromArgb(35, 35, 35));
            using var foregroundBrush = new SolidBrush(Color.White);
            using var font = new System.Drawing.Font("Segoe UI", text.Length > 2 ? 5.5f : 7f, System.Drawing.FontStyle.Bold, GraphicsUnit.Pixel);

            graphics.FillRoundedRectangle(backgroundBrush, new RectangleF(0, 0, 16, 16), 3);
            var rect = new RectangleF(0, 2, 16, 12);
            var format = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            };
            graphics.DrawString(text, font, foregroundBrush, rect, format);

            IntPtr hIcon = bitmap.GetHicon();
            try
            {
                return (Icon)Icon.FromHandle(hIcon).Clone();
            }
            finally
            {
                DestroyIcon(hIcon);
            }
        }

        private static System.Collections.Generic.IEnumerable<IHardware> FlattenHardware(IHardware hardware)
        {
            yield return hardware;

            foreach (var child in hardware.SubHardware)
            {
                foreach (var nested in FlattenHardware(child))
                    yield return nested;
            }
        }

        private static System.Collections.Generic.IEnumerable<ISensor> GetSensors(IHardware hardware)
        {
            return hardware.Sensors.Concat(hardware.SubHardware.SelectMany(GetSensors));
        }

        private static void UpdateHardwareRecursive(IHardware hardware)
        {
            hardware.Update();
            foreach (var child in hardware.SubHardware)
                UpdateHardwareRecursive(child);
        }

        public void Dispose()
        {
            _timer.Stop();
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _currentIcon?.Dispose();
            _computer.Close();
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr hIcon);
    }

    internal static class GraphicsExtensions
    {
        public static void FillRoundedRectangle(this Graphics graphics, Brush brush, RectangleF bounds, float radius)
        {
            using var path = new System.Drawing.Drawing2D.GraphicsPath();
            path.AddArc(bounds.X, bounds.Y, radius, radius, 180, 90);
            path.AddArc(bounds.Right - radius, bounds.Y, radius, radius, 270, 90);
            path.AddArc(bounds.Right - radius, bounds.Bottom - radius, radius, radius, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - radius, radius, radius, 90, 90);
            path.CloseFigure();
            graphics.FillPath(brush, path);
        }
    }
}
