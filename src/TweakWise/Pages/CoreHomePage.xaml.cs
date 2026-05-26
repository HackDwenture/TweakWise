using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using System.Windows.Shapes;
using TweakWise.Controls;
using TweakWise.Models;
using TweakWise.Services;
using WindowsPoint = System.Windows.Point;

namespace TweakWise.Pages
{
    public partial class CoreHomePage : Page
    {
        private readonly IComputerHealthService _healthService = App.ComputerHealthService;
        private HardwareTemperatureService _temperatureService;
        private readonly DispatcherTimer _temperatureTimer;
        private readonly ObservableCollection<TemperatureReadingViewModel> _temperatureCards = new ObservableCollection<TemperatureReadingViewModel>();
        private readonly object _temperatureServiceSync = new object();
        private const double ConnectionLineDashOffset = 140;
        private bool _detailsOpened;
        private bool _temperatureOptionsLoaded;
        private bool _isPageActive;
        private bool _temperatureRefreshRunning;
        private int _temperatureRefreshVersion;

        public CoreHomePage()
        {
            InitializeComponent();

            TemperatureDetailsItemsControl.ItemsSource = _temperatureCards;

            Loaded += CoreHomePage_Loaded;
            Unloaded += CoreHomePage_Unloaded;

            _temperatureTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(8)
            };
            _temperatureTimer.Tick += async (sender, args) => await RefreshTemperaturesAsync();
        }

        private async void CoreHomePage_Loaded(object sender, RoutedEventArgs e)
        {
            _isPageActive = true;
            _healthService.HealthStatusChanged += HealthService_HealthStatusChanged;
            LoadTemperatureOptions();
            RenderHealthStatus();
            await RefreshTemperaturesAsync();
            if (_isPageActive)
                _temperatureTimer.Start();
        }

        private void CoreHomePage_Unloaded(object sender, RoutedEventArgs e)
        {
            _isPageActive = false;
            _temperatureRefreshVersion++;
            _healthService.HealthStatusChanged -= HealthService_HealthStatusChanged;
            _temperatureTimer.Stop();
            lock (_temperatureServiceSync)
            {
                _temperatureService?.Dispose();
                _temperatureService = null;
            }
        }

        private void HealthService_HealthStatusChanged(object sender, EventArgs e)
        {
            Dispatcher.Invoke(RenderHealthStatus);
        }

        private void RenderHealthStatus()
        {
            var overall = _healthService.GetOverallStatus();
            var modules = _healthService.GetModules();

            MainCore.Status = overall.OverallStatus;
            MainCore.ProblemCount = overall.ProblemCount;
            MainCore.RecommendationCount = overall.RecommendationCount;
            MainCore.CriticalCount = overall.CriticalCount;

            ExpandedCore.Status = overall.OverallStatus;
            ExpandedCore.ProblemCount = overall.ProblemCount;
            ExpandedCore.RecommendationCount = overall.RecommendationCount;
            ExpandedCore.CriticalCount = overall.CriticalCount;

            HeaderStatusTextBlock.Text = BuildHeaderStatus(overall);
            HeaderCountersTextBlock.Text = $"{overall.ProblemCount} проблем · {overall.RecommendationCount} рекомендаций";
            LastCheckTextBlock.Text = overall.LastCheckedAt.HasValue
                ? $"Последняя проверка: {overall.LastCheckedAt.Value:dd.MM.yyyy HH:mm}"
                : "Последняя проверка ещё не выполнялась";

            CoreSignalTextBlock.Text = BuildCoreSignalText(overall);
            CoreImpactTextBlock.Text = BuildCoreImpactText(overall);

            ApplyModuleToNode(WindowsSetupNode, modules, CoreModuleId.WindowsSetup);
            ApplyModuleToNode(SystemParametersNode, modules, CoreModuleId.SystemParameters);
            ApplyModuleToNode(ResourcesNode, modules, CoreModuleId.Resources);
            ApplyModuleToNode(StorageNode, modules, CoreModuleId.Maintenance);
            ApplyModuleToNode(DevicesNode, modules, CoreModuleId.Devices);
            ApplyModuleToNode(NetworkNode, modules, CoreModuleId.Network);

            ApplyModuleToConnectionLine(WindowsSetupActiveLine, modules, CoreModuleId.WindowsSetup);
            ApplyModuleToConnectionLine(SystemParametersActiveLine, modules, CoreModuleId.SystemParameters);
            ApplyModuleToConnectionLine(ResourcesActiveLine, modules, CoreModuleId.Resources);
            ApplyModuleToConnectionLine(StorageActiveLine, modules, CoreModuleId.Maintenance);
            ApplyModuleToConnectionLine(DevicesActiveLine, modules, CoreModuleId.Devices);
            ApplyModuleToConnectionLine(NetworkActiveLine, modules, CoreModuleId.Network);
        }

        private static string BuildHeaderStatus(ComputerHealthStatus overall)
        {
            return overall.OverallStatus switch
            {
                HealthLevel.Good => "Компьютер: в норме",
                HealthLevel.Normal => "Компьютер: есть рекомендации",
                HealthLevel.Attention => "Компьютер: требуется внимание",
                HealthLevel.Warning => "Компьютер: есть проблемы",
                HealthLevel.Critical => "Компьютер: критическое состояние",
                HealthLevel.Checking => "Компьютер: выполняется проверка",
                _ => "Компьютер: нет данных"
            };
        }

        private static string BuildCoreSignalText(ComputerHealthStatus overall)
        {
            if (overall.OverallStatus == HealthLevel.Checking)
                return "Выполняется проверка узлов. После завершения обновятся статусы разделов и подсветка связей.";

            if (overall.CriticalCount > 0)
                return $"Обнаружены критические признаки: {overall.CriticalCount}. Сначала нужно открыть подсвеченные узлы и устранить причины.";

            if (overall.ProblemCount > 0)
                return $"Проблемы: {overall.ProblemCount}. Рекомендации: {overall.RecommendationCount}. Подсвеченные разделы требуют проверки.";

            if (overall.RecommendationCount > 0)
                return $"Критических проблем нет. Есть {overall.RecommendationCount} рекомендаций, которые можно применить осознанно.";

            return "Проверенные разделы не передают тревожных сигналов. Можно переходить к точечной настройке.";
        }

        private static string BuildCoreImpactText(ComputerHealthStatus overall)
        {
            if (overall.PendingRestart)
                return "Есть изменения, ожидающие полноценной перезагрузки. Выключение с быстрым запуском может не завершить их полностью.";

            if (overall.ProblemCount > 0)
                return "Влияние зависит от конкретного узла: лишний фон снижает отклик системы, проблемы драйверов — стабильность, накопителей — скорость доступа.";

            if (overall.RecommendationCount > 0)
                return "Рекомендации не являются ошибками. Их можно применить выборочно в соответствующих разделах.";

            return "Все доступные безопасные проверки сейчас не нашли факторов, которые заметно ухудшают работу компьютера.";
        }

        private static void ApplyModuleToNode(CoreModuleNodeControl node, IReadOnlyList<CoreModuleDefinition> modules, CoreModuleId moduleId)
        {
            var module = modules.FirstOrDefault(item => item.Id == moduleId);
            if (module == null)
                return;

            node.ModuleId = module.Id;
            node.Title = module.Title;
            node.Hint = module.ShortHint;
            node.Status = module.Status.Status;
            node.ProblemCount = module.Status.ProblemCount;
            node.RecommendationCount = module.Status.RecommendationCount;
            node.Tag = module.Id;
        }

        private static void ApplyModuleToConnectionLine(Line line, IReadOnlyList<CoreModuleDefinition> modules, CoreModuleId moduleId)
        {
            var module = modules.FirstOrDefault(item => item.Id == moduleId);
            if (module == null)
                return;

            line.SetResourceReference(Shape.StrokeProperty, GetStatusBrushKey(module.Status.Status));
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

        private void LoadTemperatureOptions()
        {
            _temperatureOptionsLoaded = false;
            var settings = App.SettingsManager.CurrentSettings;
            ShowCpuTemperatureCheckBox.IsChecked = settings.ShowCoreCpuTemperature;
            ShowGpuTemperatureCheckBox.IsChecked = settings.ShowCoreGpuTemperature;
            ShowStorageTemperatureCheckBox.IsChecked = settings.ShowCoreStorageTemperature;
            ShowMotherboardTemperatureCheckBox.IsChecked = settings.ShowCoreMotherboardTemperature;
            ShowOtherTemperatureCheckBox.IsChecked = settings.ShowCoreOtherTemperature;
            _temperatureOptionsLoaded = true;
        }

        private async Task RefreshTemperaturesAsync()
        {
            var visibleGroups = GetVisibleTemperatureGroups();
            int refreshVersion = ++_temperatureRefreshVersion;

            if (visibleGroups.Count == 0)
            {
                lock (_temperatureServiceSync)
                {
                    _temperatureService?.Dispose();
                    _temperatureService = null;
                }

                _temperatureCards.Clear();
                TemperatureDockTextBlock.Text = "Температуры: отключены";
                TemperatureDetailsTextBlock.Text = "Включите нужные группы датчиков, чтобы TweakWise начал их читать.";
                return;
            }

            if (_temperatureRefreshRunning)
                return;

            var temperatureService = EnsureTemperatureService();
            _temperatureRefreshRunning = true;

            IReadOnlyList<TemperatureSensorReading> readings;
            try
            {
                readings = await Task.Run(() =>
                {
                    lock (_temperatureServiceSync)
                    {
                        return temperatureService?.GetTemperatures() ?? Array.Empty<TemperatureSensorReading>();
                    }
                });
            }
            finally
            {
                _temperatureRefreshRunning = false;
            }

            if (!_isPageActive || refreshVersion != _temperatureRefreshVersion)
                return;

            var visibleReadings = readings
                .Where(item => visibleGroups.Contains(item.Group))
                .GroupBy(item => item.Group)
                .Select(group => group.OrderByDescending(item => item.ValueCelsius).First())
                .ToList();

            _temperatureCards.Clear();
            foreach (var reading in visibleReadings)
            {
                _temperatureCards.Add(new TemperatureReadingViewModel
                {
                    Title = GetGroupTitle(reading.Group),
                    DisplayValue = HardwareTemperatureService.FormatTemperature(reading.ValueCelsius),
                    SensorName = reading.Title
                });
            }

            if (visibleReadings.Count == 0)
            {
                TemperatureDockTextBlock.Text = "Температуры: датчики не обнаружены или не поддерживаются";
                TemperatureDetailsTextBlock.Text = "Доступные температурные датчики пока не найдены. На некоторых устройствах они появляются только после запуска с правами администратора или при поддержке контроллера.";
                return;
            }

            TemperatureDockTextBlock.Text = "Температуры: " + string.Join(" · ", visibleReadings.Select(item => $"{GetGroupTitle(item.Group)} {HardwareTemperatureService.FormatTemperature(item.ValueCelsius)}"));
            TemperatureDetailsTextBlock.Text = "Показываются самые горячие доступные датчики по выбранным группам. Состав можно менять тумблерами ниже.";
        }

        private HardwareTemperatureService EnsureTemperatureService()
        {
            lock (_temperatureServiceSync)
            {
                if (_temperatureService != null)
                    return _temperatureService;

                try
                {
                    _temperatureService = new HardwareTemperatureService();
                }
                catch
                {
                    _temperatureService = null;
                }

                return _temperatureService;
            }
        }

        private HashSet<string> GetVisibleTemperatureGroups()
        {
            var settings = App.SettingsManager.CurrentSettings;
            var groups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (settings.ShowCoreCpuTemperature)
                groups.Add("Cpu");
            if (settings.ShowCoreGpuTemperature)
                groups.Add("Gpu");
            if (settings.ShowCoreStorageTemperature)
                groups.Add("Storage");
            if (settings.ShowCoreMotherboardTemperature)
                groups.Add("Motherboard");
            if (settings.ShowCoreOtherTemperature)
                groups.Add("Other");

            return groups;
        }

        private static string GetGroupTitle(string group)
        {
            return group switch
            {
                "Cpu" => "CPU",
                "Gpu" => "GPU",
                "Storage" => "Диски",
                "Motherboard" => "Плата",
                _ => "Прочее"
            };
        }

        private void MainCore_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            ShowCoreDetails();
        }

        private void ExpandedCore_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            HideCoreDetails();
        }

        private void ModuleNode_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is CoreModuleNodeControl node)
                OpenModule(node.ModuleId);
        }

        private void ModuleNode_MouseEnter(object sender, MouseEventArgs e)
        {
            if (sender is CoreModuleNodeControl node)
                AnimateConnectionLine(node.ModuleId, true, LineFlowDirection.CoreToCard);
        }

        private void ModuleNode_MouseLeave(object sender, MouseEventArgs e)
        {
            if (sender is CoreModuleNodeControl node)
                AnimateConnectionLine(node.ModuleId, false, LineFlowDirection.CoreToCard);
        }

        private void MainCore_MouseEnter(object sender, MouseEventArgs e)
        {
            AnimateAllConnectionLines(true, LineFlowDirection.CardToCore);
        }

        private void MainCore_MouseLeave(object sender, MouseEventArgs e)
        {
            AnimateAllConnectionLines(false, LineFlowDirection.CardToCore);
        }

        private void OpenModule(CoreModuleId moduleId)
        {
            if (Window.GetWindow(this) is MainWindow mainWindow)
                mainWindow.OpenModuleWorkspace(moduleId);
        }

        private async void CheckSystemButton_Click(object sender, RoutedEventArgs e)
        {
            await _healthService.RefreshStatusAsync();
            await RefreshTemperaturesAsync();
        }

        private void CloseCoreDetailsButton_Click(object sender, RoutedEventArgs e)
        {
            HideCoreDetails();
        }

        private void ShowCoreDetails()
        {
            if (_detailsOpened)
                return;

            _detailsOpened = true;
            CoreDetailsLayer.Visibility = Visibility.Visible;
            TemperatureOptionsPanel.Visibility = Visibility.Collapsed;

            AnimateOpacity(MapLayer, 0.08, 260);
            AnimateScale(MapScale, 0.94, 340, new CubicEase { EasingMode = EasingMode.EaseInOut });

            CoreDetailsLayer.Opacity = 0;
            CoreDetailsScale.ScaleX = 0.84;
            CoreDetailsScale.ScaleY = 0.84;
            AnimateOpacity(CoreDetailsLayer, 1, 420);
            AnimateScale(CoreDetailsScale, 1, 560, new BackEase { Amplitude = 0.28, EasingMode = EasingMode.EaseOut });
        }

        private void HideCoreDetails()
        {
            if (!_detailsOpened)
                return;

            _detailsOpened = false;
            AnimateOpacity(MapLayer, 1, 320);
            AnimateScale(MapScale, 1, 320, new CubicEase { EasingMode = EasingMode.EaseInOut });
            AnimateOpacity(CoreDetailsLayer, 0, 260, () => CoreDetailsLayer.Visibility = Visibility.Collapsed);
            AnimateScale(CoreDetailsScale, 0.84, 260, new CubicEase { EasingMode = EasingMode.EaseIn });
        }

        private void AnimateAllConnectionLines(bool show, LineFlowDirection direction)
        {
            AnimateConnectionLine(CoreModuleId.WindowsSetup, show, direction);
            AnimateConnectionLine(CoreModuleId.SystemParameters, show, direction);
            AnimateConnectionLine(CoreModuleId.Resources, show, direction);
            AnimateConnectionLine(CoreModuleId.Maintenance, show, direction);
            AnimateConnectionLine(CoreModuleId.Devices, show, direction);
            AnimateConnectionLine(CoreModuleId.Network, show, direction);
        }

        private void AnimateConnectionLine(CoreModuleId moduleId, bool show, LineFlowDirection direction)
        {
            var line = GetConnectionLine(moduleId);
            if (line == null)
                return;

            ApplyConnectionLineDirection(line, moduleId, direction);

            var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };
            var opacity = new DoubleAnimation(show ? 1 : 0, TimeSpan.FromMilliseconds(show ? 190 : 160))
            {
                EasingFunction = ease
            };
            line.BeginAnimation(OpacityProperty, opacity);

            if (!show)
                return;

            line.BeginAnimation(Shape.StrokeDashOffsetProperty, null);
            line.StrokeDashOffset = ConnectionLineDashOffset;

            var flow = new DoubleAnimation(ConnectionLineDashOffset, 0, TimeSpan.FromMilliseconds(520))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            line.BeginAnimation(Shape.StrokeDashOffsetProperty, flow);
        }

        private static void ApplyConnectionLineDirection(Line line, CoreModuleId moduleId, LineFlowDirection direction)
        {
            var points = GetConnectionLinePoints(moduleId);
            WindowsPoint start = direction == LineFlowDirection.CardToCore ? points.CardPoint : points.CorePoint;
            WindowsPoint end = direction == LineFlowDirection.CardToCore ? points.CorePoint : points.CardPoint;

            line.X1 = start.X;
            line.Y1 = start.Y;
            line.X2 = end.X;
            line.Y2 = end.Y;
        }

        private static ConnectionLinePoints GetConnectionLinePoints(CoreModuleId moduleId)
        {
            return moduleId switch
            {
                CoreModuleId.WindowsSetup => new ConnectionLinePoints(new WindowsPoint(550, 116), new WindowsPoint(550, 284)),
                CoreModuleId.SystemParameters => new ConnectionLinePoints(new WindowsPoint(340, 207), new WindowsPoint(482, 317)),
                CoreModuleId.Maintenance => new ConnectionLinePoints(new WindowsPoint(340, 427), new WindowsPoint(467, 392)),
                CoreModuleId.Network => new ConnectionLinePoints(new WindowsPoint(550, 512), new WindowsPoint(550, 456)),
                CoreModuleId.Resources => new ConnectionLinePoints(new WindowsPoint(760, 207), new WindowsPoint(618, 317)),
                CoreModuleId.Devices => new ConnectionLinePoints(new WindowsPoint(760, 427), new WindowsPoint(633, 392)),
                _ => new ConnectionLinePoints(new WindowsPoint(), new WindowsPoint())
            };
        }

        private Line GetConnectionLine(CoreModuleId moduleId)
        {
            return moduleId switch
            {
                CoreModuleId.WindowsSetup => WindowsSetupActiveLine,
                CoreModuleId.SystemParameters => SystemParametersActiveLine,
                CoreModuleId.Resources => ResourcesActiveLine,
                CoreModuleId.Maintenance => StorageActiveLine,
                CoreModuleId.Devices => DevicesActiveLine,
                CoreModuleId.Network => NetworkActiveLine,
                _ => null
            };
        }

        private static void AnimateOpacity(UIElement element, double target, int milliseconds, Action completed = null)
        {
            var animation = new DoubleAnimation(target, TimeSpan.FromMilliseconds(milliseconds))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };

            if (completed != null)
                animation.Completed += (sender, args) => completed();

            element.BeginAnimation(OpacityProperty, animation);
        }

        private static void AnimateScale(ScaleTransform scale, double target, int milliseconds, IEasingFunction easing = null)
        {
            easing ??= new QuadraticEase { EasingMode = EasingMode.EaseOut };
            var animationX = new DoubleAnimation(target, TimeSpan.FromMilliseconds(milliseconds)) { EasingFunction = easing };
            var animationY = new DoubleAnimation(target, TimeSpan.FromMilliseconds(milliseconds)) { EasingFunction = easing };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, animationX);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, animationY);
        }

        private void TemperatureDetailsCard_MouseEnter(object sender, MouseEventArgs e)
        {
            AnimateOpacity(TemperatureEditButton, 1, 140);
        }

        private void TemperatureDetailsCard_MouseLeave(object sender, MouseEventArgs e)
        {
            if (TemperatureOptionsPanel.Visibility != Visibility.Visible)
                AnimateOpacity(TemperatureEditButton, 0, 160);
        }

        private void TemperatureEditButton_Click(object sender, RoutedEventArgs e)
        {
            bool show = TemperatureOptionsPanel.Visibility != Visibility.Visible;
            if (show)
            {
                TemperatureOptionsPanel.Visibility = Visibility.Visible;
                TemperatureOptionsPanel.Opacity = 0;
                AnimateOpacity(TemperatureOptionsPanel, 1, 160);
            }
            else
            {
                AnimateOpacity(TemperatureOptionsPanel, 0, 140, () => TemperatureOptionsPanel.Visibility = Visibility.Collapsed);
            }
        }

        private void TemperatureToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (!_temperatureOptionsLoaded)
                return;

            var settings = App.SettingsManager.CurrentSettings;
            settings.ShowCoreCpuTemperature = ShowCpuTemperatureCheckBox.IsChecked == true;
            settings.ShowCoreGpuTemperature = ShowGpuTemperatureCheckBox.IsChecked == true;
            settings.ShowCoreStorageTemperature = ShowStorageTemperatureCheckBox.IsChecked == true;
            settings.ShowCoreMotherboardTemperature = ShowMotherboardTemperatureCheckBox.IsChecked == true;
            settings.ShowCoreOtherTemperature = ShowOtherTemperatureCheckBox.IsChecked == true;
            App.SettingsManager.SaveSettings();
            _ = RefreshTemperaturesAsync();
        }

        private sealed class TemperatureReadingViewModel
        {
            public string Title { get; set; } = string.Empty;
            public string DisplayValue { get; set; } = string.Empty;
            public string SensorName { get; set; } = string.Empty;
        }

        private enum LineFlowDirection
        {
            CardToCore,
            CoreToCard
        }

        private readonly struct ConnectionLinePoints
        {
            public ConnectionLinePoints(WindowsPoint cardPoint, WindowsPoint corePoint)
            {
                CardPoint = cardPoint;
                CorePoint = corePoint;
            }

            public WindowsPoint CardPoint { get; }
            public WindowsPoint CorePoint { get; }
        }
    }
}
