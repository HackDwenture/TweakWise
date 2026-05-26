using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
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
        private PerformanceTuningService _performanceTuningService;
        private readonly DispatcherTimer _diagnosticsTimer = new DispatcherTimer();
        private readonly Dictionary<string, BoardNode> _nodes;
        private readonly Dictionary<string, Border> _zones = new Dictionary<string, Border>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, FrameworkElement> _glows = new Dictionary<string, FrameworkElement>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Line> _routes = new Dictionary<string, Line>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _animatedNodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, PerformanceSettingsCacheEntry> _settingsItemsCache = new Dictionary<string, PerformanceSettingsCacheEntry>(StringComparer.OrdinalIgnoreCase);
        private readonly SemaphoreSlim _settingsLoadGate = new SemaphoreSlim(1, 1);
        private readonly object _temperatureSync = new object();
        private static readonly TimeSpan SettingsItemsCacheLifetime = TimeSpan.FromSeconds(40);
        private CancellationTokenSource _settingsLoadCts;
        private int _settingsLoadVersion;
        private IReadOnlyList<PerformanceTuningItem> _currentPerformanceItems = Array.Empty<PerformanceTuningItem>();
        private string _pendingSearchTargetSettingId = string.Empty;
        private List<BoardFinding> _findings = new List<BoardFinding>();
        private string _selectedNodeKey = "Cpu";
        private string _hoverNodeKey = string.Empty;
        private bool _isDetailsOpen;
        private bool _isInitialized;
        private bool _isPageActive;
        private bool _diagnosticsRefreshRunning;

        public MonitoringPerformancePage()
        {
            InitializeComponent();

            _nodes = BuildNodes();
            _diagnosticsTimer.Interval = TimeSpan.FromSeconds(12);
            _diagnosticsTimer.Tick += (sender, args) => RefreshDiagnostics();

            InitializeMaps();
            _performanceTuningService = new PerformanceTuningService(App.SettingsManager, ReadCurrentTemperatures);
            _isInitialized = true;
            SelectNode(_selectedNodeKey, openDetails: false);
            UpdateModuleStatus();
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

        private IReadOnlyList<TemperatureSensorReading> ReadCurrentTemperatures()
        {
            lock (_temperatureSync)
            {
                if (_temperatureService == null && _isPageActive)
                    _temperatureService = CreateTemperatureService();

                try
                {
                    return _temperatureService?.GetTemperatures() ?? Array.Empty<TemperatureSensorReading>();
                }
                catch
                {
                    return Array.Empty<TemperatureSensorReading>();
                }
            }
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            _isPageActive = true;
            Focus();

            lock (_temperatureSync)
            {
                if (_temperatureService == null)
                    _temperatureService = CreateTemperatureService();
            }

            if (App.ComputerHealthService != null)
                App.ComputerHealthService.HealthStatusChanged += HealthService_HealthStatusChanged;

            _diagnosticsTimer.Start();
            RefreshDiagnostics();
        }

        private void Page_Unloaded(object sender, RoutedEventArgs e)
        {
            _isPageActive = false;
            CancelPerformanceSettingsLoad();

            if (App.ComputerHealthService != null)
                App.ComputerHealthService.HealthStatusChanged -= HealthService_HealthStatusChanged;

            _diagnosticsTimer.Stop();
            StopAllNodeMicroAnimations();

            lock (_temperatureSync)
            {
                _temperatureService?.Dispose();
                _temperatureService = null;
            }
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

        private async void AnalyzePerformanceSetting_Click(object sender, RoutedEventArgs e)
        {
            if (!TryGetPerformanceItem(sender, out var item))
                return;

            await RunPerformanceOperationAsync(
                item,
                operationItem => _performanceTuningService?.Analyze(operationItem),
                "Проверяю параметр...");
        }

        private async void ApplyPerformanceSetting_Click(object sender, RoutedEventArgs e)
        {
            if (!TryGetPerformanceItem(sender, out var item))
                return;

            var result = await RunPerformanceOperationAsync(
                item,
                operationItem => _performanceTuningService?.Apply(operationItem),
                "Сначала проверяю риск и сохраняю бэкап...");

            if (result == null)
                return;

            if (result.Success)
                InvalidatePerformanceSettingsCache(_selectedNodeKey);

            if (result.RequiresRestart && App.ComputerHealthService != null)
                _ = App.ComputerHealthService.RefreshStatusAsync();
        }

        private async void RollbackPerformanceSetting_Click(object sender, RoutedEventArgs e)
        {
            if (!TryGetPerformanceItem(sender, out var item))
                return;

            var result = await RunPerformanceOperationAsync(
                item,
                operationItem => _performanceTuningService?.Rollback(operationItem),
                "Возвращаю сохранённое значение...");

            if (result == null)
                return;

            if (result.Success)
                InvalidatePerformanceSettingsCache(_selectedNodeKey);

            if (result.RequiresRestart && App.ComputerHealthService != null)
                _ = App.ComputerHealthService.RefreshStatusAsync();
        }

        private void PerformanceSettingCard_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement element)
                return;

            var translate = new TranslateTransform(0, 10);
            element.RenderTransform = translate;

            element.Opacity = 0;

            element.BeginAnimation(
                UIElement.OpacityProperty,
                new DoubleAnimation(1, TimeSpan.FromMilliseconds(180))
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                });

            translate.BeginAnimation(
                TranslateTransform.YProperty,
                new DoubleAnimation(0, TimeSpan.FromMilliseconds(220))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                });

            if (element.DataContext is PerformanceTuningItem item &&
                !string.IsNullOrWhiteSpace(_pendingSearchTargetSettingId) &&
                string.Equals(item.SettingId, _pendingSearchTargetSettingId, StringComparison.OrdinalIgnoreCase))
            {
                _pendingSearchTargetSettingId = string.Empty;
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    element.BringIntoView();
                    if (element is Border border)
                    {
                        border.BeginAnimation(
                            Border.OpacityProperty,
                            new DoubleAnimation(0.72, 1, TimeSpan.FromMilliseconds(520))
                            {
                                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                            });
                    }
                }), DispatcherPriority.Background);
            }
        }

        private void PerformanceSearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyPerformanceSearchFilter();
        }

        private void PerformanceSearchClearButton_Click(object sender, RoutedEventArgs e)
        {
            if (PerformanceSearchTextBox == null)
                return;

            PerformanceSearchTextBox.Text = string.Empty;
            PerformanceSearchTextBox.Focus();
        }

        private void PerformanceSearchSuggestion_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement element ||
                element.DataContext is not PerformanceSearchSuggestion suggestion)
            {
                return;
            }

            _pendingSearchTargetSettingId = suggestion.SettingId;
            if (PerformanceSearchTextBox != null)
                PerformanceSearchTextBox.Text = suggestion.Title;

            ApplyPerformanceSearchFilter();
        }

        private static bool TryGetPerformanceItem(object sender, out PerformanceTuningItem item)
        {
            item = null;

            if (sender is FrameworkElement element && element.DataContext is PerformanceTuningItem tuningItem)
            {
                item = tuningItem;
                return true;
            }

            return false;
        }

        private async Task<PerformanceTuningResult> RunPerformanceOperationAsync(
            PerformanceTuningItem item,
            Func<PerformanceTuningItem, PerformanceTuningResult> operation,
            string pendingMessage)
        {
            if (item == null || operation == null)
                return PerformanceTuningResult.Fail("Не удалось выполнить действие для выбранного параметра.");

            bool previousCanApply = item.CanApply;
            var operationItem = ClonePerformanceItem(item);

            item.CanApply = false;
            item.SetStatus(pendingMessage, isWarning: false);

            await _settingsLoadGate.WaitAsync();
            try
            {
                var result = await Task.Run(() => operation(operationItem));
                CopyPerformanceItemState(operationItem, item);

                if (result != null)
                    item.SetStatus(result.Message, !result.Success);

                return result;
            }
            catch (Exception ex)
            {
                string message = $"Действие не выполнено: {ex.Message}";
                item.SetStatus(message, isWarning: true);
                item.CanApply = previousCanApply;
                return PerformanceTuningResult.Fail(message);
            }
            finally
            {
                _settingsLoadGate.Release();
            }
        }

        private static PerformanceTuningItem ClonePerformanceItem(PerformanceTuningItem source)
        {
            var clone = new PerformanceTuningItem
            {
                SettingId = source.SettingId,
                Title = source.Title,
                Description = source.Description,
                ChannelLabel = source.ChannelLabel,
                SectionTitle = source.SectionTitle,
                SectionDescription = source.SectionDescription,
                SearchKeywords = source.SearchKeywords,
                Recommendation = source.Recommendation,
                RiskLabel = source.RiskLabel,
                ApplyButtonText = source.ApplyButtonText,
                SensorGroup = source.SensorGroup,
                ReadOnlyKind = source.ReadOnlyKind,
                OperationKind = source.OperationKind,
                PowerSubgroupAlias = source.PowerSubgroupAlias,
                PowerSettingAlias = source.PowerSettingAlias,
                PowerValueScale = source.PowerValueScale,
                ValueUnit = source.ValueUnit,
                RegistryHive = source.RegistryHive,
                RegistryHiveName = source.RegistryHiveName,
                RegistryPath = source.RegistryPath,
                RegistryValueName = source.RegistryValueName,
                EnabledValue = source.EnabledValue,
                DisabledValue = source.DisabledValue,
                DefaultDwordValue = source.DefaultDwordValue,
                RegistryDeleteWhenDisabled = source.RegistryDeleteWhenDisabled,
                EnabledText = source.EnabledText,
                DisabledText = source.DisabledText,
                RestartReason = source.RestartReason,
                Order = source.Order,
                Minimum = source.Minimum,
                Maximum = source.Maximum,
                NumericStep = source.NumericStep,
                RequiresElevation = source.RequiresElevation,
                RequiresRestart = source.RequiresRestart,
                ShowApplyAction = source.ShowApplyAction,
                ControlKind = source.ControlKind,
                CurrentValue = source.CurrentValue,
                StatusMessage = source.StatusMessage,
                CanApply = source.CanApply,
                CanRollback = source.CanRollback,
                IsSupported = source.IsSupported,
                IsPriority = source.IsPriority,
                ToggleValue = source.ToggleValue,
                NumericValue = source.NumericValue
            };

            foreach (var option in source.Options)
                clone.Options.Add(new PerformanceTuningOption(option.Label, option.Value, option.Hint));

            if (source.SelectedOption != null)
            {
                clone.SelectedOption = clone.Options.FirstOrDefault(option =>
                    string.Equals(option.Value, source.SelectedOption.Value, StringComparison.OrdinalIgnoreCase));
            }

            return clone;
        }

        private static void CopyPerformanceItemState(PerformanceTuningItem source, PerformanceTuningItem target)
        {
            target.Recommendation = source.Recommendation;
            target.RiskLabel = source.RiskLabel;
            target.ApplyButtonText = source.ApplyButtonText;
            target.SectionTitle = source.SectionTitle;
            target.SectionDescription = source.SectionDescription;
            target.SearchKeywords = source.SearchKeywords;
            target.Minimum = source.Minimum;
            target.Maximum = source.Maximum;
            target.NumericStep = source.NumericStep;
            target.RequiresElevation = source.RequiresElevation;
            target.RequiresRestart = source.RequiresRestart;
            target.ShowApplyAction = source.ShowApplyAction;
            target.IsSupported = source.IsSupported;
            target.IsPriority = source.IsPriority;
            target.ToggleValue = source.ToggleValue;
            target.NumericValue = source.NumericValue;
            target.CurrentValue = source.CurrentValue;
            target.StatusMessage = source.StatusMessage;
            target.CanRollback = source.CanRollback;

            string selectedValue = source.SelectedOption?.Value;
            target.Options.Clear();
            foreach (var option in source.Options)
                target.Options.Add(new PerformanceTuningOption(option.Label, option.Value, option.Hint));

            target.SelectedOption = string.IsNullOrWhiteSpace(selectedValue)
                ? null
                : target.Options.FirstOrDefault(option => string.Equals(option.Value, selectedValue, StringComparison.OrdinalIgnoreCase));

            target.CanApply = source.CanApply;
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

            UpdateSelectedFindings();
            if (openDetails || _isDetailsOpen)
                BeginPerformanceSettingsLoad();
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

        private async void RefreshDiagnostics()
        {
            if (!_isInitialized || _diagnosticsRefreshRunning)
                return;

            _diagnosticsRefreshRunning = true;
            try
            {
                var findings = await Task.Run(BuildDiagnosticFindings);

                if (!_isInitialized || !_isPageActive)
                    return;

                _findings = findings;
                ApplyCallouts();
                UpdateSelectedFindings();
            }
            catch
            {
                if (!_isInitialized || !_isPageActive)
                    return;

                _findings = new List<BoardFinding>();
                ApplyCallouts();
                UpdateSelectedFindings();
            }
            finally
            {
                _diagnosticsRefreshRunning = false;
            }
        }

        private List<BoardFinding> BuildDiagnosticFindings()
        {
            var findings = new List<BoardFinding>();
            AddTemperatureFindings(findings);
            AddPowerFindings(findings);
            AddRamFindings(findings);
            return findings;
        }

        private void AddTemperatureFindings(List<BoardFinding> findings)
        {
            var readings = ReadCurrentTemperatures();
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

        private async void BeginPerformanceSettingsLoad(bool forceRefresh = false)
        {
            if (PerformanceSettingsItemsControl == null ||
                PerformanceSettingsEmptyText == null ||
                _performanceTuningService == null)
            {
                return;
            }

            string nodeKey = _selectedNodeKey;
            int loadVersion = ResetPerformanceSettingsLoad();

            if (!forceRefresh && TryApplyCachedPerformanceSettings(nodeKey))
            {
                CancelPerformanceSettingsLoad();
                return;
            }

            SetPerformanceSettingsLoading(true);

            var token = _settingsLoadCts.Token;
            await _settingsLoadGate.WaitAsync();
            try
            {
                if (token.IsCancellationRequested)
                    return;

                var items = await Task.Run(() => _performanceTuningService.BuildItemsForNode(nodeKey), token);
                if (token.IsCancellationRequested ||
                    loadVersion != _settingsLoadVersion ||
                    !string.Equals(nodeKey, _selectedNodeKey, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                _settingsItemsCache[nodeKey] = new PerformanceSettingsCacheEntry(items, DateTime.UtcNow);
                ApplyPerformanceSettingsItems(items);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                if (loadVersion == _settingsLoadVersion)
                    ShowPerformanceSettingsLoadError(ex.Message);
            }
            finally
            {
                _settingsLoadGate.Release();

                if (loadVersion == _settingsLoadVersion)
                    SetPerformanceSettingsLoading(false);
            }
        }

        private int ResetPerformanceSettingsLoad()
        {
            CancelPerformanceSettingsLoad();
            _settingsLoadCts = new CancellationTokenSource();
            return ++_settingsLoadVersion;
        }

        private void CancelPerformanceSettingsLoad()
        {
            _settingsLoadVersion++;

            if (_settingsLoadCts == null)
                return;

            try
            {
                _settingsLoadCts.Cancel();
                _settingsLoadCts.Dispose();
            }
            catch
            {
            }

            _settingsLoadCts = null;
        }

        private bool TryApplyCachedPerformanceSettings(string nodeKey)
        {
            if (!_settingsItemsCache.TryGetValue(nodeKey, out var entry))
                return false;

            if (DateTime.UtcNow - entry.CreatedAtUtc > SettingsItemsCacheLifetime)
            {
                _settingsItemsCache.Remove(nodeKey);
                return false;
            }

            ApplyPerformanceSettingsItems(entry.Items);
            SetPerformanceSettingsLoading(false);
            return true;
        }

        private void ApplyPerformanceSettingsItems(IReadOnlyList<PerformanceTuningItem> items)
        {
            PerformanceSettingsEmptyText.Text = "Для этого узла пока нет доступных действий.";
            _currentPerformanceItems = items ?? Array.Empty<PerformanceTuningItem>();
            ApplyPerformanceSearchFilter();
        }

        private void ShowPerformanceSettingsLoadError(string message)
        {
            _currentPerformanceItems = Array.Empty<PerformanceTuningItem>();
            PerformanceSettingsItemsControl.ItemsSource = null;
            PerformanceSettingsEmptyText.Text = string.IsNullOrWhiteSpace(message)
                ? "Не удалось загрузить параметры узла. Попробуйте открыть его ещё раз."
                : $"Не удалось загрузить параметры узла: {message}";
            PerformanceSettingsEmptyText.Visibility = Visibility.Visible;
            PerformanceSearchEmptyText.Visibility = Visibility.Collapsed;
            PerformanceSearchSuggestionsItemsControl.Visibility = Visibility.Collapsed;
        }

        private void InvalidatePerformanceSettingsCache(string nodeKey)
        {
            if (!string.IsNullOrWhiteSpace(nodeKey))
                _settingsItemsCache.Remove(nodeKey);
        }

        private void SetPerformanceSettingsLoading(bool isLoading)
        {
            if (PerformanceSettingsLoadingPanel == null)
                return;

            PerformanceSettingsLoadingPanel.Visibility = isLoading ? Visibility.Visible : Visibility.Collapsed;

            if (isLoading)
            {
                _currentPerformanceItems = Array.Empty<PerformanceTuningItem>();
                PerformanceSettingsItemsControl.ItemsSource = null;
                PerformanceSettingsEmptyText.Visibility = Visibility.Collapsed;
                PerformanceSearchEmptyText.Visibility = Visibility.Collapsed;
                PerformanceSearchSuggestionsItemsControl.Visibility = Visibility.Collapsed;
                StartLoadingSquares();
            }
            else
            {
                StopLoadingSquares();
            }
        }

        private void ApplyPerformanceSearchFilter()
        {
            if (PerformanceSettingsItemsControl == null ||
                PerformanceSettingsEmptyText == null ||
                PerformanceSearchTextBox == null)
            {
                return;
            }

            string query = PerformanceSearchTextBox.Text?.Trim() ?? string.Empty;
            bool hasQuery = !string.IsNullOrWhiteSpace(query);

            if (PerformanceSearchPlaceholderTextBlock != null)
                PerformanceSearchPlaceholderTextBlock.Visibility = hasQuery ? Visibility.Collapsed : Visibility.Visible;

            if (PerformanceSearchClearButton != null)
                PerformanceSearchClearButton.Visibility = hasQuery ? Visibility.Visible : Visibility.Collapsed;

            var items = _currentPerformanceItems ?? Array.Empty<PerformanceTuningItem>();
            var filtered = hasQuery
                ? items.Where(item => MatchesPerformanceSearch(item, query)).ToList()
                : items.ToList();

            PerformanceSettingsItemsControl.ItemsSource = BuildPerformanceSections(filtered);

            bool noItems = items.Count == 0;
            bool noSearchResults = !noItems && filtered.Count == 0 && hasQuery;
            PerformanceSettingsEmptyText.Visibility = noItems ? Visibility.Visible : Visibility.Collapsed;

            if (PerformanceSearchEmptyText != null)
                PerformanceSearchEmptyText.Visibility = noSearchResults ? Visibility.Visible : Visibility.Collapsed;

            UpdatePerformanceSearchSuggestions(items, query);
        }

        private void UpdatePerformanceSearchSuggestions(IReadOnlyList<PerformanceTuningItem> items, string query)
        {
            if (PerformanceSearchSuggestionsItemsControl == null)
                return;

            if (items == null || string.IsNullOrWhiteSpace(query))
            {
                PerformanceSearchSuggestionsItemsControl.ItemsSource = null;
                PerformanceSearchSuggestionsItemsControl.Visibility = Visibility.Collapsed;
                return;
            }

            var suggestions = items
                .Where(item => MatchesPerformanceSearch(item, query))
                .Take(5)
                .Select(item => new PerformanceSearchSuggestion
                {
                    SettingId = item.SettingId,
                    Title = item.Title,
                    SectionTitle = string.IsNullOrWhiteSpace(item.SectionTitle) ? "Параметры" : item.SectionTitle
                })
                .ToList();

            PerformanceSearchSuggestionsItemsControl.ItemsSource = suggestions;
            PerformanceSearchSuggestionsItemsControl.Visibility = suggestions.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private static IReadOnlyList<PerformanceSettingSectionViewModel> BuildPerformanceSections(IReadOnlyList<PerformanceTuningItem> items)
        {
            if (items == null || items.Count == 0)
                return Array.Empty<PerformanceSettingSectionViewModel>();

            return items
                .GroupBy(item => string.IsNullOrWhiteSpace(item.SectionTitle) ? "Параметры" : item.SectionTitle)
                .Select(group =>
                {
                    var groupItems = group.OrderByDescending(item => item.IsPriority).ThenBy(item => item.Order).ToList();
                    string description = groupItems
                        .Select(item => item.SectionDescription)
                        .FirstOrDefault(text => !string.IsNullOrWhiteSpace(text)) ?? string.Empty;

                    return new PerformanceSettingSectionViewModel
                    {
                        Title = group.Key,
                        Description = description,
                        Items = groupItems,
                        FirstOrder = groupItems.Min(item => item.Order)
                    };
                })
                .OrderBy(section => section.FirstOrder)
                .ToList();
        }

        private static bool MatchesPerformanceSearch(PerformanceTuningItem item, string query)
        {
            if (item == null || string.IsNullOrWhiteSpace(query))
                return true;

            string source = string.Join(" ", new[]
            {
                item.Title,
                item.Description,
                item.SectionTitle,
                item.SectionDescription,
                item.ChannelLabel,
                item.CurrentValue,
                item.Recommendation,
                item.StatusMessage,
                item.RiskLabel,
                item.SearchKeywords,
                string.Join(" ", item.Options.Select(option => $"{option.Label} {option.Hint}"))
            });

            return source.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0;
        }

        private void StartLoadingSquares()
        {
            FrameworkElement[] squares = { LoadingSquareA, LoadingSquareB, LoadingSquareC, LoadingSquareD };

            for (int index = 0; index < squares.Length; index++)
            {
                var square = squares[index];
                if (square == null)
                    continue;

                var scale = new ScaleTransform(0.86, 0.86);
                square.RenderTransform = scale;
                square.Opacity = 0.32;

                var beginTime = TimeSpan.FromMilliseconds(index * 130);
                var opacity = new DoubleAnimation(0.32, 1, TimeSpan.FromMilliseconds(360))
                {
                    BeginTime = beginTime,
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
                };

                var size = new DoubleAnimation(0.86, 1.08, TimeSpan.FromMilliseconds(360))
                {
                    BeginTime = beginTime,
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
                };

                square.BeginAnimation(UIElement.OpacityProperty, opacity);
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, size);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, size.Clone());
            }
        }

        private void StopLoadingSquares()
        {
            FrameworkElement[] squares = { LoadingSquareA, LoadingSquareB, LoadingSquareC, LoadingSquareD };

            foreach (var square in squares)
            {
                if (square == null)
                    continue;

                square.BeginAnimation(UIElement.OpacityProperty, null);

                if (square.RenderTransform is ScaleTransform scale)
                {
                    scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                    scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                }
            }
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
                if (!group.IsFrozen && !existingScale.IsFrozen && !existingTranslate.IsFrozen)
                {
                    scaleTransform = existingScale;
                    translateTransform = existingTranslate;
                    return;
                }
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
                    Description = "Режим питания Windows и работа устройства от сети или батареи."
                },
                ["Cpu"] = new BoardNode
                {
                    Title = "CPU",
                    Description = "Частоты процессора, автоматическое ускорение и тепловой запас под нагрузкой."
                },
                ["Gpu"] = new BoardNode
                {
                    Title = "GPU",
                    Description = "Драйвер, графический профиль приложений и нагрев видеокарты."
                },
                ["Ram"] = new BoardNode
                {
                    Title = "Оперативная память",
                    Description = "Загрузка ОЗУ, каналы памяти и стабильность под тяжёлыми задачами."
                },
                ["Cooling"] = new BoardNode
                {
                    Title = "Охлаждение",
                    Description = "Вентиляторы, датчики температуры и запас охлаждения для производительных режимов."
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
        }

        private sealed class PerformanceSettingsCacheEntry
        {
            public PerformanceSettingsCacheEntry(IReadOnlyList<PerformanceTuningItem> items, DateTime createdAtUtc)
            {
                Items = items ?? Array.Empty<PerformanceTuningItem>();
                CreatedAtUtc = createdAtUtc;
            }

            public IReadOnlyList<PerformanceTuningItem> Items { get; }
            public DateTime CreatedAtUtc { get; }
        }

        private sealed class PerformanceSettingSectionViewModel
        {
            public string Title { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
            public IReadOnlyList<PerformanceTuningItem> Items { get; set; } = Array.Empty<PerformanceTuningItem>();
            public int FirstOrder { get; set; }
        }

        private sealed class PerformanceSearchSuggestion
        {
            public string SettingId { get; set; } = string.Empty;
            public string Title { get; set; } = string.Empty;
            public string SectionTitle { get; set; } = string.Empty;
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
