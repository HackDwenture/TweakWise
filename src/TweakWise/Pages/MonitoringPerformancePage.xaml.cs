using System;
using System.Collections.Generic;
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
using WindowsPoint = System.Windows.Point;
using WinForms = System.Windows.Forms;

namespace TweakWise.Pages
{
    public partial class MonitoringPerformancePage : Page
    {
        private HardwareTemperatureService _temperatureService;
        private readonly DispatcherTimer _diagnosticsTimer = new DispatcherTimer();
        private readonly Dictionary<string, BoardNode> _nodes;
        private readonly Dictionary<string, Border> _zones = new Dictionary<string, Border>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, FrameworkElement> _glows = new Dictionary<string, FrameworkElement>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Line> _routes = new Dictionary<string, Line>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _animatedNodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private List<BoardFinding> _findings = new List<BoardFinding>();
        private string _selectedNodeKey = "Cpu";
        private string _hoverNodeKey = string.Empty;
        private bool _isDetailsOpen;
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
            SelectNode(_selectedNodeKey, openDetails: false);
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
                EnsurePartTransforms(zone, out _, out _);
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
            Focus();

            if (_temperatureService == null)
                _temperatureService = CreateTemperatureService();

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
            StopAllNodeMicroAnimations();
            _temperatureService?.Dispose();
            _temperatureService = null;
        }

        private void Page_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Escape && _isDetailsOpen)
            {
                HideNodeDetails();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.BrowserBack || e.Key == Key.Back)
            {
                if (Application.Current.MainWindow is MainWindow mainWindow)
                    mainWindow.NavigateToCoreHome();

                e.Handled = true;
            }
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

            SelectNode(key, openDetails: true);
            e.Handled = true;
        }

        private void DetailsScrim_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            HideNodeDetails();
            e.Handled = true;
        }

        private void SelectNode(string key, bool openDetails)
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

            UpdateSelectedFindings();
            UpdateHighlights();

            if (openDetails)
                ShowNodeDetails();
        }

        private void ShowNodeDetails()
        {
            _isDetailsOpen = true;

            if (NodeDetailsLayer.Visibility != Visibility.Visible)
            {
                NodeDetailsLayer.Visibility = Visibility.Visible;
                NodeDetailsLayer.Opacity = 0;
            }

            NodeDetailsTranslate.X = 44;
            AnimateOpacity(NodeDetailsLayer, 1, 210);
            NodeDetailsTranslate.BeginAnimation(
                TranslateTransform.XProperty,
                new DoubleAnimation(0, TimeSpan.FromMilliseconds(240))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                });

            StopRouteAnimations();
            UpdateHighlights();
        }

        private void HideNodeDetails()
        {
            if (!_isDetailsOpen)
                return;

            _isDetailsOpen = false;

            var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(170))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };

            fade.Completed += (sender, args) =>
            {
                if (!_isDetailsOpen)
                    NodeDetailsLayer.Visibility = Visibility.Collapsed;
            };

            NodeDetailsLayer.BeginAnimation(UIElement.OpacityProperty, fade);
            NodeDetailsTranslate.BeginAnimation(
                TranslateTransform.XProperty,
                new DoubleAnimation(44, TimeSpan.FromMilliseconds(170))
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                });

            StopRouteAnimations();
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
                    Title = "Система сильно нагревается",
                    Description = $"Самый горячий датчик показывает {HardwareTemperatureService.FormatTemperature(hottestPerformanceTemp)}. Проверьте вентиляцию, пыль в корпусе и текущий режим питания."
                });
            }
            else if (hottestPerformanceTemp >= 78)
            {
                findings.Add(new BoardFinding
                {
                    NodeKey = "Cooling",
                    Level = HealthLevel.Normal,
                    Title = "Охлаждение близко к высокой нагрузке",
                    Description = $"Пик по датчикам: {HardwareTemperatureService.FormatTemperature(hottestPerformanceTemp)}. Перед тяжёлыми задачами стоит убедиться, что вентиляторы работают нормально."
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
                        Title = "Оперативная память почти заполнена",
                        Description = $"Занято {memory.dwMemoryLoad}% ОЗУ. Система может медленнее реагировать, особенно при запуске игр, браузера или тяжёлых программ."
                    });
                }
                else if (memory.dwMemoryLoad >= 78)
                {
                    findings.Add(new BoardFinding
                    {
                        NodeKey = "Ram",
                        Level = HealthLevel.Normal,
                        Title = "Оперативная память сильно загружена",
                        Description = $"Занято {memory.dwMemoryLoad}% ОЗУ. Перед включением производительных настроек лучше закрыть лишние тяжёлые приложения."
                    });
                }
            }
            catch
            {
            }
        }

        private void ApplyCallouts()
        {
            if (CalloutLayer == null)
                return;

            CalloutLayer.Children.Clear();

            var nodeCounters = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var finding in _findings)
            {
                nodeCounters.TryGetValue(finding.NodeKey, out int index);
                nodeCounters[finding.NodeKey] = index + 1;
                AddCallout(finding, index);
            }
        }

        private void AddCallout(BoardFinding finding, int index)
        {
            var layout = GetCalloutLayout(finding.NodeKey, index);
            var lineEnd = GetCalloutLineEnd(layout);
            string brushKey = GetStatusBrushKey(finding.Level);

            var line = new Line
            {
                X1 = layout.Source.X,
                Y1 = layout.Source.Y,
                X2 = lineEnd.X,
                Y2 = lineEnd.Y,
                StrokeThickness = 1.35,
                Opacity = 0,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round
            };
            line.SetResourceReference(Shape.StrokeProperty, brushKey);

            var dot = new Ellipse
            {
                Width = 10,
                Height = 10,
                Opacity = 0
            };
            dot.SetResourceReference(Shape.FillProperty, brushKey);
            Canvas.SetLeft(dot, layout.Source.X - 5);
            Canvas.SetTop(dot, layout.Source.Y - 5);

            var card = new Border
            {
                Width = layout.CardWidth,
                MinHeight = 74,
                Style = FindResource("DiagnosticCardStyle") as Style,
                Opacity = 0,
                RenderTransform = new TranslateTransform(layout.EntranceOffset.X, layout.EntranceOffset.Y)
            };
            card.SetResourceReference(Border.BorderBrushProperty, brushKey);

            var panel = new StackPanel();
            var header = new TextBlock
            {
                Text = GetFindingKindText(finding.Level),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Opacity = 0.72
            };
            header.SetResourceReference(TextBlock.ForegroundProperty, brushKey);

            var title = new TextBlock
            {
                Text = finding.Title,
                Margin = new Thickness(0, 5, 0, 0),
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap
            };

            var description = new TextBlock
            {
                Text = finding.Description,
                Margin = new Thickness(0, 6, 0, 0),
                FontSize = 12,
                LineHeight = 18,
                Opacity = 0.78,
                TextWrapping = TextWrapping.Wrap
            };

            panel.Children.Add(header);
            panel.Children.Add(title);
            panel.Children.Add(description);
            card.Child = panel;

            Canvas.SetLeft(card, layout.Card.X);
            Canvas.SetTop(card, layout.Card.Y);

            CalloutLayer.Children.Add(line);
            CalloutLayer.Children.Add(dot);
            CalloutLayer.Children.Add(card);

            AnimateOpacity(line, 0.88, 170 + index * 40);
            AnimateOpacity(dot, 1, 180 + index * 40);
            AnimateOpacity(card, 1, 210 + index * 40);

            if (card.RenderTransform is TranslateTransform translate)
            {
                translate.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(260))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                });
                translate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(260))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                });
            }
        }

        private static BoardCalloutLayout GetCalloutLayout(string nodeKey, int index)
        {
            int safeIndex = Math.Min(index, 3);

            return nodeKey switch
            {
                "Power" => new BoardCalloutLayout(
                    new WindowsPoint(395, 226),
                    new WindowsPoint(62, 120 + safeIndex * 108),
                    238,
                    new Vector(-18, -8)),

                "Ram" => new BoardCalloutLayout(
                    new WindowsPoint(854, 326),
                    new WindowsPoint(966, 112 + safeIndex * 116),
                    238,
                    new Vector(18, -8)),

                "Gpu" => new BoardCalloutLayout(
                    new WindowsPoint(505, 552),
                    new WindowsPoint(62, 390 - safeIndex * 106),
                    292,
                    new Vector(-18, 8)),

                "Cooling" => new BoardCalloutLayout(
                    new WindowsPoint(905, 552),
                    new WindowsPoint(970, 360 + safeIndex * 112),
                    238,
                    new Vector(18, 8)),

                _ => new BoardCalloutLayout(
                    new WindowsPoint(610, 340),
                    new WindowsPoint(450 + (safeIndex % 2) * 324, 586 - (safeIndex / 2) * 102),
                    310,
                    new Vector(0, 18))
            };
        }

        private static WindowsPoint GetCalloutLineEnd(BoardCalloutLayout layout)
        {
            double x = layout.Card.X > layout.Source.X
                ? layout.Card.X
                : layout.Card.X + layout.CardWidth;

            double y = layout.Card.Y + 28;
            return new WindowsPoint(x, y);
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

            if (SelectedFindingSummaryTextBlock == null)
                return;

            if (selected.Count == 0)
            {
                SelectedFindingSummaryTextBlock.Text = "Активных сигналов нет";
                SelectedFindingSummaryTextBlock.SetResourceReference(TextBlock.ForegroundProperty, "CoreGoodBrush");
                return;
            }

            int problemCount = selected.Count(item => item.Level == HealthLevel.Warning || item.Level == HealthLevel.Critical);
            int recommendationCount = selected.Count - problemCount;
            var highestLevel = selected.OrderByDescending(item => GetSeverity(item.Level)).First().Level;

            SelectedFindingSummaryTextBlock.Text = problemCount > 0
                ? $"{problemCount} проблем · {recommendationCount} рекомендаций"
                : $"{recommendationCount} рекомендаций";

            SelectedFindingSummaryTextBlock.SetResourceReference(TextBlock.ForegroundProperty, GetStatusBrushKey(highestLevel));
        }

        private void UpdateHighlights()
        {
            foreach (var pair in _glows)
            {
                bool isHover = string.Equals(pair.Key, _hoverNodeKey, StringComparison.OrdinalIgnoreCase);
                bool isSelected = string.Equals(pair.Key, _selectedNodeKey, StringComparison.OrdinalIgnoreCase);
                double targetOpacity = isHover ? 0.18 : isSelected && _isDetailsOpen ? 0.14 : isSelected ? 0.08 : 0;
                AnimateOpacity(pair.Value, targetOpacity, 170);
            }

            foreach (var pair in _zones)
            {
                bool isHover = string.Equals(pair.Key, _hoverNodeKey, StringComparison.OrdinalIgnoreCase);
                bool isSelected = string.Equals(pair.Key, _selectedNodeKey, StringComparison.OrdinalIgnoreCase);
                double targetScale = isHover ? 1.035 : isSelected && _isDetailsOpen ? 1.022 : isSelected ? 1.01 : 1;
                double targetLift = isHover ? -7 : isSelected && _isDetailsOpen ? -4 : 0;
                AnimatePart(pair.Value, targetScale, targetLift);
            }

            UpdateNodeMicroAnimations();
        }

        private void UpdateNodeMicroAnimations()
        {
            string activeNode = !string.IsNullOrWhiteSpace(_hoverNodeKey)
                ? _hoverNodeKey
                : _isDetailsOpen ? _selectedNodeKey : string.Empty;

            foreach (string key in _nodes.Keys)
                SetNodeMicroAnimation(key, string.Equals(key, activeNode, StringComparison.OrdinalIgnoreCase));
        }

        private void SetNodeMicroAnimation(string key, bool active)
        {
            bool alreadyActive = _animatedNodes.Contains(key);

            if (active && alreadyActive)
                return;

            if (!active && !alreadyActive)
                return;

            if (active)
            {
                _animatedNodes.Add(key);
                StartNodeMicroAnimation(key);
            }
            else
            {
                _animatedNodes.Remove(key);
                StopNodeMicroAnimation(key);
            }
        }

        private void StartNodeMicroAnimation(string key)
        {
            switch (key)
            {
                case "Power":
                    StartPowerAnimation();
                    break;
                case "Cpu":
                    StartCpuAnimation();
                    break;
                case "Gpu":
                    StartGpuAnimation();
                    break;
                case "Ram":
                    StartRamAnimation();
                    break;
                case "Cooling":
                    StartCoolingAnimation();
                    break;
            }
        }

        private void StopNodeMicroAnimation(string key)
        {
            switch (key)
            {
                case "Power":
                    StopPowerAnimation();
                    break;
                case "Cpu":
                    StopCpuAnimation();
                    break;
                case "Gpu":
                    StopGpuAnimation();
                    break;
                case "Ram":
                    StopRamAnimation();
                    break;
                case "Cooling":
                    StopCoolingAnimation();
                    break;
            }
        }

        private void StopAllNodeMicroAnimations()
        {
            foreach (string key in _animatedNodes.ToList())
                StopNodeMicroAnimation(key);

            _animatedNodes.Clear();
        }

        private void StartPowerAnimation()
        {
            BeginOpacityPulse(PowerPhaseA, 0.30, 0.68, 0);
            BeginOpacityPulse(PowerPhaseB, 0.42, 0.82, 110);
            BeginOpacityPulse(PowerPhaseC, 0.30, 0.68, 220);
        }

        private void StopPowerAnimation()
        {
            ResetOpacity(PowerPhaseA, 0.30);
            ResetOpacity(PowerPhaseB, 0.42);
            ResetOpacity(PowerPhaseC, 0.30);
        }

        private void StartCpuAnimation()
        {
            BeginOpacityPulse(CpuPackageGlow, 0.78, 1.0, 0, 620);
            BeginOpacityPulse(CpuActivityGrid, 0.62, 1.0, 80, 540);
        }

        private void StopCpuAnimation()
        {
            ResetOpacity(CpuPackageGlow, 0.78);
            ResetOpacity(CpuActivityGrid, 0.72);
        }

        private void StartGpuAnimation()
        {
            GpuSignalTranslate.X = 0;
            GpuSignalPulse.BeginAnimation(
                UIElement.OpacityProperty,
                new DoubleAnimation(0.16, 0.88, TimeSpan.FromMilliseconds(420))
                {
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                });

            GpuSignalTranslate.BeginAnimation(
                TranslateTransform.XProperty,
                new DoubleAnimation(0, 144, TimeSpan.FromMilliseconds(860))
                {
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
                });
        }

        private void StopGpuAnimation()
        {
            GpuSignalPulse.BeginAnimation(UIElement.OpacityProperty, null);
            GpuSignalPulse.Opacity = 0;
            GpuSignalTranslate.BeginAnimation(TranslateTransform.XProperty, null);
            GpuSignalTranslate.X = 0;
        }

        private void StartRamAnimation()
        {
            BeginOpacityPulse(RamSlotA, 0.76, 1.0, 0, 520);
            BeginOpacityPulse(RamSlotB, 0.66, 0.96, 120, 520);
            BeginOpacityPulse(RamSlotC, 0.76, 1.0, 240, 520);
            BeginOpacityPulse(RamSlotD, 0.66, 0.96, 360, 520);
        }

        private void StopRamAnimation()
        {
            ResetOpacity(RamSlotA, 0.76);
            ResetOpacity(RamSlotB, 0.66);
            ResetOpacity(RamSlotC, 0.76);
            ResetOpacity(RamSlotD, 0.66);
        }

        private void StartCoolingAnimation()
        {
            CoolingFanRotate.BeginAnimation(
                RotateTransform.AngleProperty,
                new DoubleAnimation(0, 360, TimeSpan.FromMilliseconds(720))
                {
                    RepeatBehavior = RepeatBehavior.Forever
                });
        }

        private void StopCoolingAnimation()
        {
            CoolingFanRotate.BeginAnimation(RotateTransform.AngleProperty, null);
            CoolingFanRotate.Angle = 0;
        }

        private static void BeginOpacityPulse(
            UIElement element,
            double from,
            double to,
            int beginDelayMilliseconds,
            int durationMilliseconds = 480)
        {
            if (element == null)
                return;

            element.BeginAnimation(
                UIElement.OpacityProperty,
                new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(durationMilliseconds))
                {
                    BeginTime = TimeSpan.FromMilliseconds(beginDelayMilliseconds),
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
                });
        }

        private static void ResetOpacity(UIElement element, double opacity)
        {
            if (element == null)
                return;

            element.BeginAnimation(UIElement.OpacityProperty, null);
            element.Opacity = opacity;
        }

        private void AnimateRoutesForHover(string key)
        {
            StopRouteAnimations(clearSelected: true);

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
            if (line == null)
                return;

            line.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.90, TimeSpan.FromMilliseconds(120)));
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

        private void StopRouteAnimations(bool clearSelected = false)
        {
            foreach (var pair in _routes)
            {
                var line = pair.Value;
                line.BeginAnimation(Shape.StrokeDashOffsetProperty, null);

                bool keepSelected = !clearSelected &&
                                    _isDetailsOpen &&
                                    string.Equals(pair.Key, _selectedNodeKey, StringComparison.OrdinalIgnoreCase);

                line.BeginAnimation(
                    UIElement.OpacityProperty,
                    new DoubleAnimation(keepSelected ? 0.48 : 0, TimeSpan.FromMilliseconds(150)));
            }
        }

        private static void AnimateOpacity(UIElement element, double opacity, int milliseconds)
        {
            if (element == null)
                return;

            element.BeginAnimation(
                UIElement.OpacityProperty,
                new DoubleAnimation(opacity, TimeSpan.FromMilliseconds(milliseconds))
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                });
        }

        private static void AnimatePart(Border border, double scale, double yOffset)
        {
            if (border == null)
                return;

            EnsurePartTransforms(border, out var scaleTransform, out var translateTransform);
            var duration = TimeSpan.FromMilliseconds(170);
            var easing = new QuadraticEase { EasingMode = EasingMode.EaseOut };
            scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(scale, duration) { EasingFunction = easing });
            scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(scale, duration) { EasingFunction = easing });
            translateTransform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(yOffset, duration) { EasingFunction = easing });
        }

        private static void EnsurePartTransforms(
            Border border,
            out ScaleTransform scaleTransform,
            out TranslateTransform translateTransform)
        {
            if (border.RenderTransform is TransformGroup group &&
                group.Children.OfType<ScaleTransform>().FirstOrDefault() is ScaleTransform existingScale &&
                group.Children.OfType<TranslateTransform>().FirstOrDefault() is TranslateTransform existingTranslate)
            {
                scaleTransform = existingScale;
                translateTransform = existingTranslate;
                return;
            }

            scaleTransform = new ScaleTransform(1, 1);
            translateTransform = new TranslateTransform(0, 0);
            group = new TransformGroup();
            group.Children.Add(scaleTransform);
            group.Children.Add(translateTransform);
            border.RenderTransform = group;
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

        private static string GetFindingKindText(HealthLevel status)
        {
            return status switch
            {
                HealthLevel.Critical => "КРИТИЧНО",
                HealthLevel.Warning => "ПРОБЛЕМА",
                HealthLevel.Attention => "ВНИМАНИЕ",
                _ => "РЕКОМЕНДАЦИЯ"
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

        private static int GetSeverity(HealthLevel level)
        {
            return level switch
            {
                HealthLevel.Critical => 5,
                HealthLevel.Warning => 4,
                HealthLevel.Attention => 3,
                HealthLevel.Normal => 2,
                HealthLevel.Good => 1,
                _ => 0
            };
        }

        private static Dictionary<string, BoardNode> BuildNodes()
        {
            return new Dictionary<string, BoardNode>(StringComparer.OrdinalIgnoreCase)
            {
                ["Power"] = new BoardNode
                {
                    Title = "Питание",
                    Description = "Режим питания Windows и работа устройства от сети или батареи.",
                    Actions = new List<BoardAction>
                    {
                        new() { Title = "План питания", Channel = "powercfg", Description = "Профиль Windows влияет на частоты, нагрев и скорость реакции системы под нагрузкой." },
                        new() { Title = "Экономия энергии в фоне", Channel = "реестр", Description = "Если Windows слишком активно экономит энергию, приложения могут медленнее реагировать под нагрузкой." },
                        new() { Title = "Питание ноутбука", Channel = "Windows", Description = "При работе от батареи карта показывает рекомендацию только если система реально отключена от сети." }
                    }
                },
                ["Cpu"] = new BoardNode
                {
                    Title = "CPU",
                    Description = "Частоты процессора, автоматическое ускорение и тепловой запас под нагрузкой.",
                    Actions = new List<BoardAction>
                    {
                        new() { Title = "Приоритет нагрузки", Channel = "реестр", Description = "Параметры планировщика относятся к процессору, а не к общему разделу системы." },
                        new() { Title = "Ускорение процессора", Channel = "powercfg", Description = "Настройки CPU влияют на скорость работы, температуру и шум охлаждения." },
                        new() { Title = "Температура CPU", Channel = "датчики", Description = "Если датчики показывают высокий нагрев, табличка появляется прямо от CPU без наведения." }
                    }
                },
                ["Gpu"] = new BoardNode
                {
                    Title = "GPU",
                    Description = "Драйвер, графический профиль приложений и нагрев видеокарты.",
                    Actions = new List<BoardAction>
                    {
                        new() { Title = "Планирование графики", Channel = "реестр", Description = "Параметр относится к видеокарте и может потребовать перезапуск для применения." },
                        new() { Title = "Графический профиль приложений", Channel = "Windows", Description = "Параметры графики должны редактироваться прямо в TweakWise; игровые функции Windows остаются в другом разделе." },
                        new() { Title = "Температура GPU", Channel = "датчики", Description = "Высокая температура или hot spot выводятся отдельной табличкой у видеокарты." }
                    }
                },
                ["Ram"] = new BoardNode
                {
                    Title = "Оперативная память",
                    Description = "Загрузка ОЗУ, каналы памяти и стабильность под тяжёлыми задачами.",
                    Actions = new List<BoardAction>
                    {
                        new() { Title = "Объём и загрузка", Channel = "диагностика", Description = "Показывает, хватает ли оперативной памяти для текущих задач." },
                        new() { Title = "Настройки памяти", Channel = "BIOS/UEFI", Description = "Профили памяти меняются в BIOS/UEFI, поэтому приложение показывает только понятную рекомендацию." },
                        new() { Title = "Стабильность", Channel = "проверка", Description = "Проблемы памяти выводятся отдельной табличкой от планок RAM." }
                    }
                },
                ["Cooling"] = new BoardNode
                {
                    Title = "Охлаждение",
                    Description = "Вентиляторы, датчики температуры и запас охлаждения для производительных режимов.",
                    Actions = new List<BoardAction>
                    {
                        new() { Title = "Температуры", Channel = "датчики", Description = "Высокий нагрев показывает постоянную табличку у контура охлаждения." },
                        new() { Title = "Работа вентиляторов", Channel = "безопасный режим", Description = "Если прямое управление недоступно, приложение показывает диагностику и ручную рекомендацию." },
                        new() { Title = "Запас охлаждения", Channel = "анализ", Description = "Карта связывает питание, CPU и GPU с охлаждением, чтобы рекомендации были понятны по месту." }
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

        private readonly struct BoardCalloutLayout
        {
            public BoardCalloutLayout(WindowsPoint source, WindowsPoint card, double cardWidth, Vector entranceOffset)
            {
                Source = source;
                Card = card;
                CardWidth = cardWidth;
                EntranceOffset = entranceOffset;
            }

            public WindowsPoint Source { get; }
            public WindowsPoint Card { get; }
            public double CardWidth { get; }
            public Vector EntranceOffset { get; }
        }
    }
}
