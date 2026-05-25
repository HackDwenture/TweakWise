using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using TweakWise.Models;
using TweakWise.Services;
using Application = System.Windows.Application;
using Border = System.Windows.Controls.Border;
using WinForms = System.Windows.Forms;

namespace TweakWise.Pages
{
    public partial class MonitoringPerformancePage : Page
    {
        private HardwareTemperatureService _temperatureService;
        private readonly DispatcherTimer _diagnosticsTimer = new DispatcherTimer();
        private readonly Dictionary<string, BoardNode> _nodes;
        private Dictionary<string, Border> _zones = new Dictionary<string, Border>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, FrameworkElement> _glows = new Dictionary<string, FrameworkElement>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, Line> _routes = new Dictionary<string, Line>(StringComparer.OrdinalIgnoreCase);
        private List<BoardFinding> _findings = new List<BoardFinding>();
        private string _selectedNodeKey = "Cpu";
        private string _hoverNodeKey = string.Empty;
        private bool _isInitialized;

        public MonitoringPerformancePage()
        {
            InitializeComponent();

            _nodes = BuildNodes();
            _diagnosticsTimer.Interval = TimeSpan.FromSeconds(12);
            _diagnosticsTimer.Tick += (sender, args) => RefreshDiagnostics();

            InitializeMaps();
            _temperatureService = CreateTemperatureService();
            _isInitialized = true;
            SelectNode(_selectedNodeKey);
            UpdateModuleStatus();
            RefreshDiagnostics();
        }

        private void InitializeMaps()
        {
            AddElement(_zones, "Power", PowerZone);
            AddElement(_zones, "Cpu", CpuZone);
            AddElement(_zones, "Gpu", GpuZone);
            AddElement(_zones, "Ram", RamZone);
            AddElement(_zones, "Cooling", CoolingZone);

            AddElement(_glows, "Power", PowerGlow);
            AddElement(_glows, "Cpu", CpuGlow);
            AddElement(_glows, "Gpu", GpuGlow);
            AddElement(_glows, "Ram", RamGlow);
            AddElement(_glows, "Cooling", CoolingGlow);

            AddElement(_routes, "Power", PowerRouteLine);
            AddElement(_routes, "Gpu", GpuRouteLine);
            AddElement(_routes, "Ram", RamRouteLine);
            AddElement(_routes, "Cooling", CoolingRouteLine);

            foreach (var zone in _zones.Values)
                EnsureScaleTransform(zone);
        }

        private static void AddElement<T>(Dictionary<string, T> map, string key, T element)
            where T : class
        {
            if (element != null)
                map[key] = element;
        }

        private static HardwareTemperatureService CreateTemperatureService()
        {
            try
            {
                return new HardwareTemperatureService();
            }
            catch
            {
                return null;
            }
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            if (App.ComputerHealthService != null)
                App.ComputerHealthService.HealthStatusChanged += HealthService_HealthStatusChanged;

            _diagnosticsTimer.Start();
            RefreshDiagnostics();
        }

        private void Page_Unloaded(object sender, RoutedEventArgs e)
        {
            if (App.ComputerHealthService != null)
                App.ComputerHealthService.HealthStatusChanged -= HealthService_HealthStatusChanged;

            _diagnosticsTimer.Stop();
            _temperatureService?.Dispose();
        }

        private void HealthService_HealthStatusChanged(object sender, EventArgs e)
        {
            Dispatcher.Invoke(UpdateModuleStatus);
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            if (Application.Current.MainWindow is MainWindow mainWindow)
                mainWindow.NavigateToCoreHome();
        }

        private void Component_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
        {
            string key = GetNodeKey(sender);
            if (string.IsNullOrWhiteSpace(key))
                return;

            _hoverNodeKey = key;
            UpdateHighlights();
            AnimateRoutesForHover(key);
        }

        private void Component_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
        {
            _hoverNodeKey = string.Empty;
            StopRouteAnimations();
            UpdateHighlights();
        }

        private void Component_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            string key = GetNodeKey(sender);
            if (string.IsNullOrWhiteSpace(key))
                return;

            SelectNode(key);
            e.Handled = true;
        }

        private void OpenNativeSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            App.DialogManager?.Show(
                Application.Current.MainWindow,
                "Производительность и охлаждение",
                "Настройки открываются внутри TweakWise",
                "Этот раздел не переводит пользователя во внешние окна Windows. Для каждого узла будут добавляться собственные переключатели, риск, предпросмотр и откат.",
                AppDialogKind.Info);
        }

        private void SelectNode(string key)
        {
            if (!_isInitialized)
                return;

            if (!_nodes.TryGetValue(key, out var node))
                return;

            _selectedNodeKey = key;
            if (SelectedTitleTextBlock != null)
                SelectedTitleTextBlock.Text = node.Title;
            if (SelectedDescriptionTextBlock != null)
                SelectedDescriptionTextBlock.Text = node.Description;
            if (ActionItemsControl != null)
                ActionItemsControl.ItemsSource = node.Actions;

            if (OpenNativeSettingsButton != null)
            {
                OpenNativeSettingsButton.Visibility = Visibility.Collapsed;
                OpenNativeSettingsButton.Content = "Внутренние настройки";
            }

            UpdateSelectedFindings();
            UpdateHighlights();
        }

        private void UpdateModuleStatus()
        {
            if (!_isInitialized)
                return;

            var module = App.ComputerHealthService?.GetModule(CoreModuleId.Resources);
            if (module?.Status == null || ModuleStatusTextBlock == null || ModuleStatusIndicator == null)
                return;

            ModuleStatusTextBlock.Text = GetModuleStatusText(module.Status.Status, module.Status.ProblemCount, module.Status.RecommendationCount);
            ModuleStatusIndicator.SetResourceReference(Shape.FillProperty, GetStatusBrushKey(module.Status.Status));
        }

        private void RefreshDiagnostics()
        {
            if (!_isInitialized)
                return;

            try
            {
                var findings = new List<BoardFinding>();
                AddTemperatureFindings(findings);
                AddPowerFindings(findings);
                AddRamFindings(findings);

                _findings = findings;
                ApplyCallouts();
                UpdateSelectedFindings();
            }
            catch
            {
                _findings = new List<BoardFinding>();
                ApplyCallouts();
                UpdateSelectedFindings();
            }
        }

        private void AddTemperatureFindings(List<BoardFinding> findings)
        {
            if (_temperatureService == null)
                return;

            var readings = _temperatureService.GetTemperatures() ?? Array.Empty<TemperatureSensorReading>();
            if (readings.Count == 0)
                return;

            AddThermalFinding(findings, readings, "Cpu", "CPU нагревается", 90, 82);
            AddThermalFinding(findings, readings, "Gpu", "GPU нагревается", 87, 80);

            float hottestPerformanceTemp = readings
                .Where(item => item.Group == "Cpu" || item.Group == "Gpu" || item.Group == "Motherboard" || item.Group == "Other")
                .Select(item => item.ValueCelsius)
                .DefaultIfEmpty(0)
                .Max();

            if (hottestPerformanceTemp >= 86)
            {
                findings.Add(new BoardFinding
                {
                    NodeKey = "Cooling",
                    Level = HealthLevel.Warning,
                    Title = "Термоконтур перегружен",
                    Description = $"Самый горячий датчик показывает {HardwareTemperatureService.FormatTemperature(hottestPerformanceTemp)}. Стоит проверить кривую вентиляторов, пыль и режим питания."
                });
            }
            else if (hottestPerformanceTemp >= 78)
            {
                findings.Add(new BoardFinding
                {
                    NodeKey = "Cooling",
                    Level = HealthLevel.Normal,
                    Title = "Есть запас для настройки охлаждения",
                    Description = $"Пик по датчикам: {HardwareTemperatureService.FormatTemperature(hottestPerformanceTemp)}. Можно поднять вентиляцию до включения тяжёлых профилей."
                });
            }
        }

        private static void AddThermalFinding(
            List<BoardFinding> findings,
            IReadOnlyList<TemperatureSensorReading> readings,
            string group,
            string title,
            float warningThreshold,
            float recommendationThreshold)
        {
            var hottest = readings
                .Where(item => string.Equals(item.Group, group, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(item => item.ValueCelsius)
                .FirstOrDefault();

            if (hottest == null || hottest.ValueCelsius < recommendationThreshold)
                return;

            findings.Add(new BoardFinding
            {
                NodeKey = group,
                Level = hottest.ValueCelsius >= warningThreshold ? HealthLevel.Warning : HealthLevel.Normal,
                Title = title,
                Description = $"{hottest.Title}: {HardwareTemperatureService.FormatTemperature(hottest.ValueCelsius)}."
            });
        }

        private static void AddPowerFindings(List<BoardFinding> findings)
        {
            try
            {
                var power = WinForms.SystemInformation.PowerStatus;
                if (power.PowerLineStatus != WinForms.PowerLineStatus.Offline)
                    return;

                findings.Add(new BoardFinding
                {
                    NodeKey = "Power",
                    Level = HealthLevel.Normal,
                    Title = "Питание от батареи",
                    Description = "Windows может ограничивать частоты и охлаждение. Для тяжёлых задач лучше включить питание от сети или производительный профиль."
                });
            }
            catch
            {
            }
        }

        private static void AddRamFindings(List<BoardFinding> findings)
        {
            try
            {
                var memory = new MemoryStatusEx();
                if (!GlobalMemoryStatusEx(memory) || memory.ullTotalPhys == 0)
                    return;

                if (memory.dwMemoryLoad >= 90)
                {
                    findings.Add(new BoardFinding
                    {
                        NodeKey = "Ram",
                        Level = HealthLevel.Warning,
                        Title = "RAM почти заполнена",
                        Description = $"Занято {memory.dwMemoryLoad}% оперативной памяти. Это может снижать отзывчивость и вызывать сброс частот в тяжёлых задачах."
                    });
                }
                else if (memory.dwMemoryLoad >= 78)
                {
                    findings.Add(new BoardFinding
                    {
                        NodeKey = "Ram",
                        Level = HealthLevel.Normal,
                        Title = "Высокая нагрузка на RAM",
                        Description = $"Занято {memory.dwMemoryLoad}% ОЗУ. Стоит проверить профиль памяти и тяжёлые процессы перед включением производительных твиков."
                    });
                }
            }
            catch
            {
            }
        }

        private void ApplyCallouts()
        {
            ApplyCallout(_findings.FirstOrDefault(item => item.NodeKey == "Power"), PowerCalloutCard, PowerCalloutLine, PowerCalloutDot, PowerCalloutTitleTextBlock, PowerCalloutDescriptionTextBlock);
            ApplyCallout(_findings.FirstOrDefault(item => item.NodeKey == "Cooling"), CoolingCalloutCard, CoolingCalloutLine, CoolingCalloutDot, CoolingCalloutTitleTextBlock, CoolingCalloutDescriptionTextBlock);
            ApplyCallout(_findings.FirstOrDefault(item => item.NodeKey == "Cpu"), CpuCalloutCard, CpuCalloutLine, CpuCalloutDot, CpuCalloutTitleTextBlock, CpuCalloutDescriptionTextBlock);
            ApplyCallout(_findings.FirstOrDefault(item => item.NodeKey == "Gpu"), GpuCalloutCard, GpuCalloutLine, GpuCalloutDot, GpuCalloutTitleTextBlock, GpuCalloutDescriptionTextBlock);
            ApplyCallout(_findings.FirstOrDefault(item => item.NodeKey == "Ram"), RamCalloutCard, RamCalloutLine, RamCalloutDot, RamCalloutTitleTextBlock, RamCalloutDescriptionTextBlock);

            if (NoFindingsBadge != null)
                NoFindingsBadge.Visibility = _findings.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private static void ApplyCallout(BoardFinding finding, Border card, Line line, Ellipse dot, TextBlock title, TextBlock description)
        {
            if (card == null || line == null || dot == null || title == null || description == null)
                return;

            bool visible = finding != null;
            var visibility = visible ? Visibility.Visible : Visibility.Collapsed;

            card.Visibility = visibility;
            line.Visibility = visibility;
            dot.Visibility = visibility;

            if (!visible)
                return;

            title.Text = finding.Title;
            description.Text = finding.Description;

            string brushKey = GetStatusBrushKey(finding.Level);
            card.SetResourceReference(Border.BorderBrushProperty, brushKey);
            line.SetResourceReference(Shape.StrokeProperty, brushKey);
            dot.SetResourceReference(Shape.FillProperty, brushKey);
        }

        private void UpdateSelectedFindings()
        {
            if (SelectedFindingsItemsControl == null || SelectedFindingsEmptyText == null)
                return;

            var selected = _findings
                .Where(item => string.Equals(item.NodeKey, _selectedNodeKey, StringComparison.OrdinalIgnoreCase))
                .ToList();

            SelectedFindingsItemsControl.ItemsSource = selected;
            SelectedFindingsEmptyText.Visibility = selected.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void UpdateHighlights()
        {
            foreach (var pair in _glows)
            {
                bool isHover = string.Equals(pair.Key, _hoverNodeKey, StringComparison.OrdinalIgnoreCase);
                bool isSelected = string.Equals(pair.Key, _selectedNodeKey, StringComparison.OrdinalIgnoreCase);
                double targetOpacity = isHover ? 0.86 : isSelected ? 0.35 : 0;
                AnimateOpacity(pair.Value, targetOpacity, 170);
            }

            foreach (var pair in _zones)
            {
                bool isHover = string.Equals(pair.Key, _hoverNodeKey, StringComparison.OrdinalIgnoreCase);
                bool isSelected = string.Equals(pair.Key, _selectedNodeKey, StringComparison.OrdinalIgnoreCase);
                AnimateScale(pair.Value, isHover ? 1.035 : isSelected ? 1.015 : 1);
            }
        }

        private void AnimateRoutesForHover(string key)
        {
            StopRouteAnimations();

            if (string.Equals(key, "Cpu", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var route in _routes.Values)
                    AnimateRoute(route, false);
                return;
            }

            if (_routes.TryGetValue(key, out var line))
                AnimateRoute(line, true);
        }

        private static void AnimateRoute(Line line, bool fromCore)
        {
            line.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.92, TimeSpan.FromMilliseconds(120)));
            line.BeginAnimation(
                Shape.StrokeDashOffsetProperty,
                new DoubleAnimation
                {
                    From = fromCore ? 28 : -28,
                    To = 0,
                    Duration = TimeSpan.FromMilliseconds(620),
                    RepeatBehavior = RepeatBehavior.Forever
                });
        }

        private void StopRouteAnimations()
        {
            foreach (var line in _routes.Values)
            {
                line.BeginAnimation(Shape.StrokeDashOffsetProperty, null);
                line.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(150)));
            }
        }

        private static void AnimateOpacity(UIElement element, double opacity, int milliseconds)
        {
            element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(opacity, TimeSpan.FromMilliseconds(milliseconds)) { EasingFunction = new QuadraticEase() });
        }

        private static void AnimateScale(Border border, double scale)
        {
            var transform = EnsureScaleTransform(border);

            var duration = TimeSpan.FromMilliseconds(170);
            var easing = new QuadraticEase();
            transform.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(scale, duration) { EasingFunction = easing });
            transform.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(scale, duration) { EasingFunction = easing });
        }

        private static ScaleTransform EnsureScaleTransform(Border border)
        {
            if (border.RenderTransform is ScaleTransform transform && !transform.IsFrozen)
                return transform;

            transform = new ScaleTransform(1, 1);
            border.RenderTransform = transform;
            return transform;
        }

        private static string GetNodeKey(object sender)
        {
            return sender is FrameworkElement element ? element.Tag?.ToString() ?? string.Empty : string.Empty;
        }

        private static string GetModuleStatusText(HealthLevel status, int problemCount, int recommendationCount)
        {
            if (status == HealthLevel.Checking)
                return "Проверка состояния";

            if (problemCount > 0)
                return $"{problemCount} проблем";

            if (recommendationCount > 0)
                return $"{recommendationCount} рекомендаций";

            return status switch
            {
                HealthLevel.Good => "В норме",
                HealthLevel.Normal => "Есть рекомендации",
                HealthLevel.Attention => "Требуется внимание",
                HealthLevel.Warning => "Требуется внимание",
                HealthLevel.Critical => "Критично",
                _ => "Нет данных"
            };
        }

        private static string GetStatusBrushKey(HealthLevel status)
        {
            return status switch
            {
                HealthLevel.Good => "CoreGoodBrush",
                HealthLevel.Normal => "CoreNormalBrush",
                HealthLevel.Attention => "CoreAttentionBrush",
                HealthLevel.Warning => "CoreWarningBrush",
                HealthLevel.Critical => "CoreCriticalBrush",
                _ => "CoreUnknownBrush"
            };
        }

        private static Dictionary<string, BoardNode> BuildNodes()
        {
            return new Dictionary<string, BoardNode>(StringComparer.OrdinalIgnoreCase)
            {
                ["Power"] = new BoardNode
                {
                    Title = "Питание и лимиты",
                    Description = "Схема питания, ограничения Windows, VRM и поведение устройства от сети или батареи.",
                    Actions = new List<BoardAction>
                    {
                        new() { Title = "Схема питания", Channel = "powercfg", Description = "Внутренний переключатель профиля питания с оценкой риска и откатом, без открытия внешних настроек." },
                        new() { Title = "Power Throttling", Channel = "реестр", Description = "Ограничения фоновой экономии энергии напрямую влияют на частоты и отзывчивость под нагрузкой." },
                        new() { Title = "Питание ноутбука", Channel = "Windows", Description = "При работе от батареи карта показывает рекомендацию только если система реально отключена от сети." }
                    }
                },
                ["Cpu"] = new BoardNode
                {
                    Title = "CPU",
                    Description = "Планировщик, boost-поведение, тепловой запас и настройки, влияющие на частоты процессора.",
                    Actions = new List<BoardAction>
                    {
                        new() { Title = "Приоритет нагрузки", Channel = "реестр", Description = "Параметры планировщика относятся к процессору, а не к общему разделу системы." },
                        new() { Title = "Boost и лимиты", Channel = "powercfg", Description = "Максимальное состояние CPU и boost-режим управляют частотами, нагревом и шумом." },
                        new() { Title = "Температура CPU", Channel = "датчики", Description = "Если датчики показывают высокий нагрев, табличка появляется прямо от CPU без наведения." }
                    }
                },
                ["Gpu"] = new BoardNode
                {
                    Title = "GPU",
                    Description = "Графический стек, драйверные настройки и тепловое состояние видеоядра.",
                    Actions = new List<BoardAction>
                    {
                        new() { Title = "Аппаратное планирование GPU", Channel = "реестр", Description = "Параметр относится к видеокарте и требует понятного предупреждения о перезапуске." },
                        new() { Title = "Графический профиль приложений", Channel = "Windows", Description = "Параметры графики должны редактироваться прямо в TweakWise; игровые функции Windows остаются в другом разделе." },
                        new() { Title = "Температура GPU", Channel = "датчики", Description = "Высокая температура или hot spot выводятся отдельной табличкой у видеокарты." }
                    }
                },
                ["Ram"] = new BoardNode
                {
                    Title = "Оперативная память",
                    Description = "RAM как часть производительности: каналы, стабильность под нагрузкой и рекомендации без смешивания с дисками.",
                    Actions = new List<BoardAction>
                    {
                        new() { Title = "Каналы и доступный объём", Channel = "диагностика", Description = "На карте RAM отвечает только за оперативную память, без дисков, файла подкачки и VRAM отдельным пунктом." },
                        new() { Title = "Профили памяти", Channel = "BIOS/UEFI", Description = "EXPO/XMP не меняются из приложения напрямую, но могут быть показаны как ручная рекомендация." },
                        new() { Title = "Стабильность", Channel = "проверка", Description = "Проблемы памяти выводятся отдельной табличкой от планок RAM." }
                    }
                },
                ["Cooling"] = new BoardNode
                {
                    Title = "Охлаждение",
                    Description = "Температуры CPU/GPU/платы, вентиляторы и тепловой запас для производительных режимов.",
                    Actions = new List<BoardAction>
                    {
                        new() { Title = "Температурные пороги", Channel = "датчики", Description = "Высокий нагрев показывает постоянную табличку у контура охлаждения." },
                        new() { Title = "Кривая вентиляторов", Channel = "безопасный режим", Description = "Если прямое управление недоступно, показываем диагностику и ручную рекомендацию, не обещая невозможную запись." },
                        new() { Title = "Тепловой запас", Channel = "анализ", Description = "Карта связывает питание, CPU и GPU с охлаждением, чтобы рекомендации не висели отдельно от железа." }
                    }
                }
            };
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool GlobalMemoryStatusEx([In, Out] MemoryStatusEx buffer);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private sealed class MemoryStatusEx
        {
            public uint dwLength = (uint)Marshal.SizeOf<MemoryStatusEx>();
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }

        private sealed class BoardNode
        {
            public string Title { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
            public string NativeSettingsUri { get; set; } = string.Empty;
            public string NativeSettingsButtonText { get; set; } = string.Empty;
            public List<BoardAction> Actions { get; set; } = new List<BoardAction>();
        }

        private sealed class BoardAction
        {
            public string Title { get; set; } = string.Empty;
            public string Channel { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
        }

        private sealed class BoardFinding
        {
            public string NodeKey { get; set; } = string.Empty;
            public HealthLevel Level { get; set; } = HealthLevel.Normal;
            public string Title { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
        }
    }
}
