using System;
using System.Collections.Generic;
using System.Diagnostics;
using DrawingBitmap = System.Drawing.Bitmap;
using DrawingBrush = System.Drawing.Brush;
using DrawingColor = System.Drawing.Color;
using DrawingFont = System.Drawing.Font;
using DrawingFontStyle = System.Drawing.FontStyle;
using DrawingGraphics = System.Drawing.Graphics;
using DrawingGraphicsUnit = System.Drawing.GraphicsUnit;
using DrawingIcon = System.Drawing.Icon;
using DrawingRectangleF = System.Drawing.RectangleF;
using DrawingSolidBrush = System.Drawing.SolidBrush;
using DrawingStringAlignment = System.Drawing.StringAlignment;
using DrawingStringFormat = System.Drawing.StringFormat;
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
        private readonly DispatcherTimer _timer;
        private readonly Forms.NotifyIcon _notifyIcon;
        private readonly HashSet<string> _faultedHardwareKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly bool _suppressHardwareBackend;
        private readonly bool _skipUnsafeHardwareUpdates;
        private Computer _computer;
        private bool _enabled;
        private bool _showTemperature;
        private bool _isOpen;
        private DrawingIcon _currentIcon;

        public TrayTemperatureManager()
        {
            _suppressHardwareBackend = ShouldSuppressLibreHardwareMonitor();
            _skipUnsafeHardwareUpdates = ShouldSkipUnsafeHardwareUpdates();

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
                CloseComputer();
                return;
            }

            if (!_showTemperature)
            {
                _timer.Stop();
                CloseComputer();
                Refresh();
                _notifyIcon.Visible = true;
                return;
            }

            Refresh();
            _notifyIcon.Visible = true;
            if (!_timer.IsEnabled)
                _timer.Start();
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

            if (!_showTemperature || _suppressHardwareBackend || !EnsureComputerOpened())
            {
                UpdateIcon("TW");
                _notifyIcon.Text = _showTemperature
                    ? "TweakWise работает в трее | датчики недоступны"
                    : "TweakWise работает в трее";
                _notifyIcon.Visible = true;
                return;
            }

            var hardwareItems = GetRootHardware();

            foreach (var hardware in hardwareItems)
                UpdateHardwareRecursive(hardware);

            var sensors = hardwareItems
                .SelectMany(FlattenHardware)
                .SelectMany(GetSensors)
                .Where(sensor => sensor != null && TryGetSensorType(sensor, out var type) && type == SensorType.Temperature)
                .Where(sensor => TryGetSensorValue(sensor, out _))
                .ToList();

            string cpuText = BuildPreferredTemperatureText(sensors, "CPU", "Package", "Tctl/Tdie", "CCD", "Core");
            string gpuText = BuildPreferredTemperatureText(sensors, "GPU", "Hot Spot", "GPU Core", "Core", "Memory");
            string display = cpuText != "--" ? cpuText : gpuText;

            UpdateIcon(display);
            _notifyIcon.Text = $"TweakWise | CPU: {cpuText}°C | GPU: {gpuText}°C";
            _notifyIcon.Visible = true;
        }

        private bool EnsureComputerOpened()
        {
            if (_isOpen)
                return true;

            if (_suppressHardwareBackend)
                return false;

            try
            {
                _computer ??= CreateComputer();
                _computer.Open();
                _isOpen = true;
                return true;
            }
            catch
            {
                _isOpen = false;
                return false;
            }
        }

        private static Computer CreateComputer()
        {
            return new Computer
            {
                IsCpuEnabled = true,
                IsGpuEnabled = true,
                IsMotherboardEnabled = false,
                IsStorageEnabled = false,
                IsMemoryEnabled = false,
                IsControllerEnabled = false,
                IsNetworkEnabled = false,
                IsPsuEnabled = false,
                IsBatteryEnabled = false
            };
        }

        private static bool ShouldSuppressLibreHardwareMonitor()
        {
            string suppressHardware = Environment.GetEnvironmentVariable("TW_SUPPRESS_HARDWARE_MONITORING") ?? string.Empty;
            return string.Equals(suppressHardware, "1", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(suppressHardware, "true", StringComparison.OrdinalIgnoreCase);
        }

        private static bool ShouldSkipUnsafeHardwareUpdates()
        {
            string allowUnsafeUpdate = Environment.GetEnvironmentVariable("TW_ALLOW_UNSAFE_HARDWARE_UPDATE") ?? string.Empty;
            string allowDebugTelemetry = Environment.GetEnvironmentVariable("TW_ALLOW_HARDWARE_DEBUG") ?? string.Empty;

            if (string.Equals(allowUnsafeUpdate, "1", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(allowUnsafeUpdate, "true", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(allowDebugTelemetry, "1", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(allowDebugTelemetry, "true", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

#if DEBUG
            return Debugger.IsAttached;
#else
            return false;
#endif
        }

        private static string BuildPreferredTemperatureText(
            List<ISensor> sensors,
            string hardwareToken,
            params string[] preferredNames)
        {
            var filtered = sensors
                .Where(sensor => sensor != null)
                .Where(sensor =>
                {
                    var hardware = GetSensorHardware(sensor);
                    string source = $"{GetHardwareName(hardware)} {GetHardwareTypeName(hardware)}";
                    return source.IndexOf(hardwareToken, StringComparison.OrdinalIgnoreCase) >= 0;
                })
                .ToList();

            foreach (var name in preferredNames)
            {
                var match = filtered.FirstOrDefault(sensor => GetSensorName(sensor).IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0);
                if (match != null && TryGetSensorValue(match, out float value))
                    return $"{Math.Round(value):0}";
            }

            var fallback = filtered.FirstOrDefault(sensor => TryGetSensorValue(sensor, out _));
            return fallback != null && TryGetSensorValue(fallback, out float fallbackValue) ? $"{Math.Round(fallbackValue):0}" : "--";
        }

        private void UpdateIcon(string text)
        {
            _currentIcon?.Dispose();
            _currentIcon = CreateIcon(text);
            _notifyIcon.Icon = _currentIcon;
        }

        private static DrawingIcon CreateIcon(string text)
        {
            using var bitmap = new DrawingBitmap(16, 16);
            using var graphics = DrawingGraphics.FromImage(bitmap);
            graphics.Clear(DrawingColor.Transparent);
            graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.SingleBitPerPixelGridFit;

            using var backgroundBrush = new DrawingSolidBrush(DrawingColor.FromArgb(35, 35, 35));
            using var foregroundBrush = new DrawingSolidBrush(DrawingColor.White);
            using var font = new DrawingFont("Segoe UI", text.Length > 2 ? 5.5f : 7f, DrawingFontStyle.Bold, DrawingGraphicsUnit.Pixel);

            graphics.FillRoundedRectangle(backgroundBrush, new DrawingRectangleF(0, 0, 16, 16), 3);
            var rect = new DrawingRectangleF(0, 2, 16, 12);
            var format = new DrawingStringFormat
            {
                Alignment = DrawingStringAlignment.Center,
                LineAlignment = DrawingStringAlignment.Center
            };
            graphics.DrawString(text, font, foregroundBrush, rect, format);

            IntPtr hIcon = bitmap.GetHicon();
            try
            {
                return (DrawingIcon)DrawingIcon.FromHandle(hIcon).Clone();
            }
            finally
            {
                DestroyIcon(hIcon);
            }
        }

        private IReadOnlyList<IHardware> GetRootHardware()
        {
            if (_computer == null)
                return Array.Empty<IHardware>();

            try
            {
                return (_computer.Hardware ?? Array.Empty<IHardware>())
                    .Where(hardware => hardware != null)
                    .ToArray();
            }
            catch
            {
                return Array.Empty<IHardware>();
            }
        }

        private IEnumerable<IHardware> FlattenHardware(IHardware hardware)
        {
            if (hardware == null)
                yield break;

            yield return hardware;

            foreach (var child in GetSafeSubHardware(hardware))
            {
                foreach (var nested in FlattenHardware(child))
                    yield return nested;
            }
        }

        private static IEnumerable<ISensor> GetSensors(IHardware hardware)
        {
            if (hardware == null)
                return Enumerable.Empty<ISensor>();

            return GetSafeSensors(hardware)
                .Concat(GetSafeSubHardware(hardware).SelectMany(GetSensors));
        }

        [DebuggerNonUserCode]
        [DebuggerStepThrough]
        private void UpdateHardwareRecursive(IHardware hardware)
        {
            if (hardware == null)
                return;

            if (_skipUnsafeHardwareUpdates)
            {
                foreach (var child in GetSafeSubHardware(hardware))
                    UpdateHardwareRecursive(child);

                return;
            }

            string hardwareKey = GetHardwareKey(hardware);

            if (ShouldUpdateHardware(hardware) && !_faultedHardwareKeys.Contains(hardwareKey))
            {
                try
                {
                    hardware.Update();
                }
                catch
                {
                    _faultedHardwareKeys.Add(hardwareKey);
                    return;
                }
            }

            foreach (var child in GetSafeSubHardware(hardware))
                UpdateHardwareRecursive(child);
        }

        private static bool ShouldUpdateHardware(IHardware hardware)
        {
            if (!TryGetHardwareType(hardware, out var hardwareType))
                return false;

            return hardwareType == HardwareType.Cpu ||
                   hardwareType == HardwareType.GpuNvidia ||
                   hardwareType == HardwareType.GpuAmd ||
                   hardwareType == HardwareType.GpuIntel;
        }

        private static IReadOnlyList<IHardware> GetSafeSubHardware(IHardware hardware)
        {
            if (hardware == null)
                return Array.Empty<IHardware>();

            try
            {
                return (hardware.SubHardware ?? Array.Empty<IHardware>())
                    .Where(child => child != null)
                    .ToArray();
            }
            catch
            {
                return Array.Empty<IHardware>();
            }
        }

        private static IReadOnlyList<ISensor> GetSafeSensors(IHardware hardware)
        {
            if (hardware == null)
                return Array.Empty<ISensor>();

            try
            {
                return (hardware.Sensors ?? Array.Empty<ISensor>())
                    .Where(sensor => sensor != null)
                    .ToArray();
            }
            catch
            {
                return Array.Empty<ISensor>();
            }
        }

        private static bool TryGetHardwareType(IHardware hardware, out HardwareType hardwareType)
        {
            hardwareType = default;
            if (hardware == null)
                return false;

            try
            {
                hardwareType = hardware.HardwareType;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryGetSensorType(ISensor sensor, out SensorType sensorType)
        {
            sensorType = default;
            if (sensor == null)
                return false;

            try
            {
                sensorType = sensor.SensorType;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryGetSensorValue(ISensor sensor, out float value)
        {
            value = 0;
            if (sensor == null)
                return false;

            try
            {
                if (!sensor.Value.HasValue)
                    return false;

                value = sensor.Value.Value;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static IHardware GetSensorHardware(ISensor sensor)
        {
            if (sensor == null)
                return null;

            try
            {
                return sensor.Hardware;
            }
            catch
            {
                return null;
            }
        }

        private static string GetHardwareName(IHardware hardware)
        {
            if (hardware == null)
                return string.Empty;

            try
            {
                return hardware.Name ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string GetHardwareTypeName(IHardware hardware)
        {
            return TryGetHardwareType(hardware, out var hardwareType) ? hardwareType.ToString() : string.Empty;
        }

        private static string GetSensorName(ISensor sensor)
        {
            if (sensor == null)
                return string.Empty;

            try
            {
                return sensor.Name ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string GetHardwareKey(IHardware hardware)
        {
            return $"{GetHardwareTypeName(hardware)}:{GetHardwareName(hardware)}";
        }

        private void CloseComputer()
        {
            try
            {
                if (_isOpen)
                    _computer?.Close();
            }
            catch
            {
            }
            finally
            {
                _isOpen = false;
                _computer = null;
                _faultedHardwareKeys.Clear();
            }
        }

        public void Dispose()
        {
            try { _timer.Stop(); } catch { }
            try { _notifyIcon.Visible = false; } catch { }
            try { _notifyIcon.ContextMenuStrip?.Dispose(); } catch { }
            try { _notifyIcon.Dispose(); } catch { }
            try { _currentIcon?.Dispose(); } catch { }
            CloseComputer();
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr hIcon);
    }

    internal static class GraphicsExtensions
    {
        public static void FillRoundedRectangle(this DrawingGraphics graphics, DrawingBrush brush, DrawingRectangleF bounds, float radius)
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
