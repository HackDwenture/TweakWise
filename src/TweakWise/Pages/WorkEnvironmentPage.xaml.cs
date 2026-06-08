using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;
using TweakWise.Managers;
using TweakWise.Models;
using TweakWise.Services;
using Application = System.Windows.Application;
using Button = System.Windows.Controls.Button;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using WindowsPoint = System.Windows.Point;

namespace TweakWise.Pages
{
    public partial class WorkEnvironmentPage : Page
    {
        private const double DetailsPanelTop = 78;
        private const double DetailsOrbTargetXRatio = 0.52;
        private const double DetailsOrbTargetCenterY = DetailsPanelTop - 30;
        private const double DetailsOrbSize = 44;
        private const string BackupFileName = "work-environment-backups.json";
        private const int MaxBackupRecords = 120;

        private readonly IComputerHealthService _healthService;
        private readonly Dictionary<string, EnvironmentNodeInfo> _nodes;
        private readonly Dictionary<string, Border> _zones = new Dictionary<string, Border>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Border> _glows = new Dictionary<string, Border>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Line> _routes = new Dictionary<string, Line>(StringComparer.OrdinalIgnoreCase);
        private readonly List<EnvironmentFindingViewModel> _currentFindings = new List<EnvironmentFindingViewModel>();
        private readonly List<EnvironmentSection> _currentEnvironmentSettings = new List<EnvironmentSection>();

        private string _selectedNodeKey = "Display";
        private string _detailsOrbSourceNodeKey = "Display";
        private string _pendingEnvironmentSearchSettingId = string.Empty;
        private string _pendingEnvironmentSearchSection = string.Empty;
        private string _pendingExternalFindingId = string.Empty;
        private int _environmentNavigationVersion;
        private EnvironmentSignalFilter _environmentSignalFilter = EnvironmentSignalFilter.All;
        private bool _isPageActive;
        private bool _isDetailsOpen;

        public WorkEnvironmentPage()
        {
            InitializeComponent();

            _healthService = App.ComputerHealthService;
            _nodes = BuildNodes();
            InitializeMaps();
            PruneEnvironmentBackups();
            ApplyModuleStatus();
        }

        public WorkEnvironmentPage(string targetFindingId)
            : this()
        {
            _pendingExternalFindingId = targetFindingId ?? string.Empty;
        }

        private void InitializeMaps()
        {
            AddElement(_zones, "Display", DisplayZone);
            AddElement(_zones, "Explorer", ExplorerZone);
            AddElement(_zones, "Windows", WindowsZone);
            AddElement(_zones, "Start", StartZone);
            AddElement(_zones, "Taskbar", TaskbarZone);
            AddElement(_zones, "Search", SearchZone);
            AddElement(_zones, "Tray", TrayZone);

            AddElement(_glows, "Display", DisplayGlow);
            AddElement(_glows, "Explorer", ExplorerGlow);
            AddElement(_glows, "Windows", WindowsGlow);
            AddElement(_glows, "Start", StartGlow);
            AddElement(_glows, "Taskbar", TaskbarGlow);
            AddElement(_glows, "Search", SearchGlow);
            AddElement(_glows, "Tray", TrayGlow);

            AddElement(_routes, "Display", DisplayRouteLine);
            AddElement(_routes, "Explorer", ExplorerRouteLine);
            AddElement(_routes, "Windows", WindowsRouteLine);
            AddElement(_routes, "Start", StartRouteLine);
            AddElement(_routes, "Taskbar", TaskbarRouteLine);
            AddElement(_routes, "Search", SearchRouteLine);
            AddElement(_routes, "Tray", TrayRouteLine);
        }

        private static void AddElement<T>(Dictionary<string, T> map, string key, T element)
            where T : class
        {
            if (element != null)
                map[key] = element;
        }

        private async void Page_Loaded(object sender, RoutedEventArgs e)
        {
            _isPageActive = true;
            Focus();

            if (_healthService != null)
                _healthService.HealthStatusChanged += HealthService_HealthStatusChanged;

            ResetMapMotion();
            ApplyModuleStatus();
            AnimateOpacity(RootContent, 1, 220);

            await RefreshHealthStatusQuietlyAsync();
            TryOpenPendingExternalFindingTarget();
        }

        private void Page_Unloaded(object sender, RoutedEventArgs e)
        {
            _isPageActive = false;
            _environmentNavigationVersion++;

            if (_healthService != null)
                _healthService.HealthStatusChanged -= HealthService_HealthStatusChanged;

            StopEnvironmentLoadingSquares(GetEnvironmentBusySquares());
            StopDetailsOrbBreathing();
            ClearCalloutLayerSafely();
            ResetMapMotion();
        }

        private async Task RefreshHealthStatusQuietlyAsync()
        {
            try
            {
                if (_healthService != null)
                    await _healthService.RefreshStatusAsync();
            }
            catch
            {
            }
        }

        private void HealthService_HealthStatusChanged(object sender, EventArgs e)
        {
            if (!_isPageActive || Dispatcher == null || Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
                return;

            try
            {
                Dispatcher.BeginInvoke((Action)(() =>
                {
                    if (_isPageActive)
                        ApplyModuleStatus();
                }), DispatcherPriority.Background);
            }
            catch
            {
            }
        }

        private void Page_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Escape && e.Key != Key.Back)
                return;

            e.Handled = true;
            if (_isDetailsOpen)
                HideNodeDetails();
            else
                NavigateHome();
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            NavigateHome();
        }

        private void NodeDetailsOrbButton_Click(object sender, RoutedEventArgs e)
        {
            HideNodeDetails();
        }

        private void DetailsScrim_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            HideNodeDetails();
        }

        private void Component_MouseEnter(object sender, MouseEventArgs e)
        {
            if (sender is Border border && border.Tag is string key)
                SetNodeVisualState(key, active: true);
        }

        private void Component_MouseLeave(object sender, MouseEventArgs e)
        {
            if (sender is Border border && border.Tag is string key)
                SetNodeVisualState(key, active: false);
        }

        private void Component_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border border && border.Tag is string key)
            {
                e.Handled = true;
                SelectNode(key, openDetails: true);
            }
        }

        private async void IgnoreSelectedFinding_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not string id || string.IsNullOrWhiteSpace(id))
                return;

            var finding = _currentFindings.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase));
            if (finding == null)
                return;

            bool applied = await HealthSignalActionHelper.PromptAndApplyAsync(
                Window.GetWindow(this),
                new[] { id },
                finding.Title,
                IsProblemLevel(finding.Level));

            if (applied)
                ApplyModuleStatus();
        }

        private async void IgnoreEnvironmentSettingSignal_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not string id || string.IsNullOrWhiteSpace(id))
                return;

            var finding = _currentFindings.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase));
            if (finding == null)
                return;

            bool applied = await HealthSignalActionHelper.PromptAndApplyAsync(
                Window.GetWindow(this),
                new[] { id },
                finding.Title,
                IsProblemLevel(finding.Level));

            if (applied)
                ApplyModuleStatus();
        }

        private void SelectedEnvironmentFindingCard_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is not Border border || border.DataContext is not EnvironmentFindingViewModel finding)
                return;

            string brushKey = GetStatusBrushKey(finding.Level);
            border.SetResourceReference(Border.BorderBrushProperty, brushKey);

            if (border.Child is StackPanel panel && panel.Children.Count > 0 && panel.Children[0] is TextBlock kindText)
                kindText.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
        }

        private void SelectedEnvironmentFindingCard_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is DependencyObject original &&
                FindVisualParent<Button>(original) != null)
            {
                return;
            }

            if (sender is not FrameworkElement element || element.DataContext is not EnvironmentFindingViewModel finding)
                return;

            NavigateToEnvironmentFindingTarget(finding, openNode: false);
            e.Handled = true;
        }

        private void NavigateToEnvironmentFindingTarget(EnvironmentFindingViewModel finding, bool openNode)
        {
            if (finding == null)
                return;

            if (openNode &&
                !string.IsNullOrWhiteSpace(finding.NodeKey) &&
                (!string.Equals(_selectedNodeKey, finding.NodeKey, StringComparison.OrdinalIgnoreCase) || !_isDetailsOpen))
            {
                SelectNode(finding.NodeKey, openDetails: true);
            }

            FocusEnvironmentFinding(finding, showBusy: true);
        }

        private void TryOpenPendingExternalFindingTarget()
        {
            if (string.IsNullOrWhiteSpace(_pendingExternalFindingId))
                return;

            string targetId = _pendingExternalFindingId;
            _pendingExternalFindingId = string.Empty;

            var finding = _currentFindings.FirstOrDefault(item =>
                string.Equals(item.Id, targetId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(NormalizeEnvironmentFindingSettingId(item.Id), targetId, StringComparison.OrdinalIgnoreCase));

            if (finding != null)
            {
                NavigateToEnvironmentFindingTarget(finding, openNode: true);
                return;
            }

            string normalizedTargetId = NormalizeEnvironmentFindingSettingId(targetId);
            var setting = GetEnvironmentSettingDefinitions().FirstOrDefault(item =>
                string.Equals(item.Id, normalizedTargetId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(item.Id, targetId, StringComparison.OrdinalIgnoreCase));

            if (setting == null)
                return;

            SelectNode(setting.NodeKey, openDetails: true);
            _pendingEnvironmentSearchSettingId = setting.Id;
            _pendingEnvironmentSearchSection = string.Empty;

            if (EnvironmentSearchTextBox != null)
            {
                EnvironmentSearchTextBox.Text = setting.Title;
                EnvironmentSearchTextBox.CaretIndex = 0;
            }

            QueueEnvironmentTargetNavigation(showBusy: true);
        }

        private void FocusEnvironmentFinding(EnvironmentFindingViewModel finding, bool showBusy)
        {
            var target = ResolveEnvironmentSettingForFinding(finding);
            if (target == null)
                return;

            _pendingEnvironmentSearchSettingId = target.Id;
            _pendingEnvironmentSearchSection = string.Empty;

            if (EnvironmentSearchTextBox != null)
            {
                EnvironmentSearchTextBox.Text = target.Title;
                EnvironmentSearchTextBox.CaretIndex = 0;
            }

            if (EnvironmentSearchSuggestionsItemsControl != null)
                EnvironmentSearchSuggestionsItemsControl.Visibility = Visibility.Collapsed;

            QueueEnvironmentTargetNavigation(showBusy);
        }

        private EnvironmentSection ResolveEnvironmentSettingForFinding(EnvironmentFindingViewModel finding)
        {
            if (finding == null)
                return null;

            var settings = _currentEnvironmentSettings.Count > 0
                ? _currentEnvironmentSettings
                : BuildNodeSettings(finding.NodeKey).ToList();

            string normalizedId = NormalizeEnvironmentFindingSettingId(finding.Id);
            var exact = settings.FirstOrDefault(setting =>
                string.Equals(setting.Id, normalizedId, StringComparison.OrdinalIgnoreCase));
            if (exact != null)
                return exact;

            string text = $"{finding.Id} {finding.Title} {finding.Description}";
            return settings.FirstOrDefault(setting => MatchesEnvironmentText(text, setting.Title)) ??
                   settings.FirstOrDefault(setting => MatchesEnvironmentText(text, setting.Scope)) ??
                   settings.FirstOrDefault();
        }

        private static string NormalizeEnvironmentFindingSettingId(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return string.Empty;

            return id switch
            {
                "workenv.search.web-suggestions" => "workenv.search.web-policy",
                _ => id
            };
        }

        private void EnvironmentSettingCard_Loaded(object sender, RoutedEventArgs e)
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

            if (element is Border border && element.DataContext is EnvironmentSection statusItem)
            {
                var level = statusItem.SignalLevel;
                if (level == HealthLevel.Normal || level == HealthLevel.Attention || level == HealthLevel.Warning || level == HealthLevel.Critical)
                    border.SetResourceReference(Border.BorderBrushProperty, GetStatusBrushKey(level));
            }

            if (element.DataContext is not EnvironmentSection section)
                return;

            bool isTargetSetting = !string.IsNullOrWhiteSpace(_pendingEnvironmentSearchSettingId) &&
                                   string.Equals(section.Id, _pendingEnvironmentSearchSettingId, StringComparison.OrdinalIgnoreCase);
            bool isTargetSection = string.IsNullOrWhiteSpace(_pendingEnvironmentSearchSettingId) &&
                                   !string.IsNullOrWhiteSpace(_pendingEnvironmentSearchSection) &&
                                   string.Equals(section.Scope, _pendingEnvironmentSearchSection, StringComparison.CurrentCultureIgnoreCase);

            if (!isTargetSetting && !isTargetSection)
                return;

            _pendingEnvironmentSearchSettingId = string.Empty;
            _pendingEnvironmentSearchSection = string.Empty;

            Dispatcher.BeginInvoke(new Action(() =>
            {
                element.BringIntoView();
                PlaySearchResultHighlight(element);
            }), DispatcherPriority.Background);
        }

        private void EnvironmentSearchTextBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            QueueEnvironmentSearchCaretAtStart();
        }

        private void EnvironmentSearchTextBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (EnvironmentSearchTextBox == null ||
                !string.IsNullOrEmpty(EnvironmentSearchTextBox.Text))
            {
                return;
            }

            e.Handled = true;
            if (!EnvironmentSearchTextBox.IsKeyboardFocusWithin)
                EnvironmentSearchTextBox.Focus();

            QueueEnvironmentSearchCaretAtStart();
        }

        private void EnvironmentSearchTextBox_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (EnvironmentSearchTextBox == null ||
                !string.IsNullOrEmpty(EnvironmentSearchTextBox.Text))
            {
                return;
            }

            e.Handled = true;
            QueueEnvironmentSearchCaretAtStart();
        }

        private void EnvironmentSearchTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter ||
                EnvironmentSearchSuggestionsItemsControl?.ItemsSource is not IEnumerable<EnvironmentSearchSuggestion> suggestions)
            {
                return;
            }

            var suggestion = suggestions.FirstOrDefault();
            if (suggestion == null)
                return;

            ApplyEnvironmentSearchSuggestion(suggestion);
            e.Handled = true;
        }

        private void EnvironmentSearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyEnvironmentSearchFilter();
        }

        private void EnvironmentSearchClearButton_Click(object sender, RoutedEventArgs e)
        {
            ResetEnvironmentSearch(clearText: true);
            EnvironmentSearchTextBox?.Focus();
        }

        private void EnvironmentSearchSuggestion_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement element ||
                element.DataContext is not EnvironmentSearchSuggestion suggestion)
            {
                return;
            }

            ApplyEnvironmentSearchSuggestion(suggestion);
        }

        private void ApplyEnvironmentSearchSuggestion(EnvironmentSearchSuggestion suggestion)
        {
            if (suggestion == null)
                return;

            _pendingEnvironmentSearchSettingId = suggestion.IsSection ? string.Empty : suggestion.SettingId;
            _pendingEnvironmentSearchSection = suggestion.IsSection ? suggestion.SectionTitle : string.Empty;

            if (EnvironmentSearchSuggestionsItemsControl != null)
                EnvironmentSearchSuggestionsItemsControl.Visibility = Visibility.Collapsed;

            if (EnvironmentSearchTextBox != null)
            {
                EnvironmentSearchTextBox.Text = suggestion.Title;
                EnvironmentSearchTextBox.CaretIndex = 0;
            }

            if (EnvironmentSearchSuggestionsItemsControl != null)
                EnvironmentSearchSuggestionsItemsControl.Visibility = Visibility.Collapsed;

            QueueEnvironmentTargetNavigation(showBusy: true);
        }

        private async void ApplyEnvironmentSetting_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is EnvironmentSection section)
            {
                var option = section.GetSelectedOption();
                if (option != null)
                    await ApplyEnvironmentSettingAsync(section.Id, option.Value, option.Label);
                return;
            }

            if (sender is Button button && button.Tag is string id)
                await ApplyEnvironmentSettingAsync(id, useRecommendedValue: true);
        }

        private async void ResetEnvironmentSetting_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string id)
                await ApplyEnvironmentSettingAsync(id, useRecommendedValue: false);
        }

        private async void ApplyEnvironmentOption_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is EnvironmentSettingOptionViewModel option)
                await ApplyEnvironmentSettingAsync(option.SettingId, option.Value, option.Label);
        }

        private async void RollbackEnvironmentSetting_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is EnvironmentSection section)
            {
                await RollbackEnvironmentSettingAsync(section.Id);
                return;
            }

            if (sender is Button button && button.Tag is string id)
                await RollbackEnvironmentSettingAsync(id);
        }

        private Task ApplyEnvironmentSettingAsync(string id, bool useRecommendedValue)
        {
            var setting = GetEnvironmentSettingDefinitions()
                .FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase));
            if (setting == null)
                return Task.CompletedTask;

            object targetValue = useRecommendedValue ? setting.RecommendedValue : setting.DefaultValue;
            string targetLabel = useRecommendedValue ? setting.RecommendedLabel : setting.DefaultLabel;
            return ApplyEnvironmentSettingAsync(id, targetValue, targetLabel);
        }

        private async Task ApplyEnvironmentSettingAsync(string id, object targetValue, string targetLabel)
        {
            var setting = GetEnvironmentSettingDefinitions()
                .FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase));
            if (setting == null)
                return;

            try
            {
                SetEnvironmentNavigationBusy(true);
                string result = await Task.Run(() => WriteEnvironmentRegistryValue(setting, targetValue, targetLabel));
                await RefreshHealthStatusQuietlyAsync();

                if (_nodes.TryGetValue(_selectedNodeKey, out var node))
                    UpdateDetailsContent(node);

                if (!string.IsNullOrWhiteSpace(result))
                {
                    App.DialogManager?.Show(
                        Window.GetWindow(this),
                        "Параметр применён",
                        setting.Title,
                        result,
                        AppDialogKind.Success);
                }
            }
            catch (Exception ex)
            {
                App.DialogManager?.Show(
                    Window.GetWindow(this),
                    "Ошибка применения",
                    "Не удалось применить параметр",
                    $"Параметр «{setting.Title}» не был изменён.\n\n{ex.Message}",
                    AppDialogKind.Warning);
            }
            finally
            {
                SetEnvironmentNavigationBusy(false);
            }
        }

        private async Task RollbackEnvironmentSettingAsync(string id)
        {
            var setting = GetEnvironmentSettingDefinitions()
                .FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase));
            if (setting == null)
                return;

            try
            {
                SetEnvironmentNavigationBusy(true);
                string result = await Task.Run(() => RollbackEnvironmentRegistryValue(setting));
                await RefreshHealthStatusQuietlyAsync();

                if (_nodes.TryGetValue(_selectedNodeKey, out var node))
                    UpdateDetailsContent(node);

                App.DialogManager?.Show(
                    Window.GetWindow(this),
                    "Откат выполнен",
                    setting.Title,
                    result,
                    AppDialogKind.Success);
            }
            catch (Exception ex)
            {
                App.DialogManager?.Show(
                    Window.GetWindow(this),
                    "Ошибка отката",
                    "Не удалось восстановить резервное значение",
                    $"Параметр «{setting.Title}» не был восстановлен.\n\n{ex.Message}",
                    AppDialogKind.Warning);
            }
            finally
            {
                SetEnvironmentNavigationBusy(false);
            }
        }

        private void SelectNode(string key, bool openDetails)
        {
            if (!_nodes.TryGetValue(key ?? string.Empty, out var node))
                return;

            string previousKey = _selectedNodeKey;
            _selectedNodeKey = node.Key;
            _detailsOrbSourceNodeKey = node.Key;
            bool nodeChanged = !string.Equals(previousKey, node.Key, StringComparison.OrdinalIgnoreCase);

            if (!string.IsNullOrWhiteSpace(previousKey) && nodeChanged)
            {
                SetNodeVisualState(previousKey, active: false, force: true);
                ResetEnvironmentSearch(clearText: true);
                _environmentSignalFilter = EnvironmentSignalFilter.All;
                UpdateEnvironmentSignalFilterButtons();
            }

            SetNodeVisualState(node.Key, active: true, force: true);
            UpdateDetailsContent(node);

            if (openDetails)
                ShowNodeDetails();
        }

        private void UpdateDetailsContent(EnvironmentNodeInfo node)
        {
            var nodeFindings = _currentFindings
                .Where(finding => string.Equals(finding.NodeKey, node.Key, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(finding => GetSeverity(finding.Level))
                .ThenBy(finding => finding.Title)
                .ToList();

            SelectedTitleTextBlock.Text = node.Title;
            SelectedDescriptionTextBlock.Text = node.Description;
            _currentEnvironmentSettings.Clear();
            _currentEnvironmentSettings.AddRange(BuildNodeSettings(node.Key).Select(setting => AttachEnvironmentSignal(setting, nodeFindings)));
            ApplyEnvironmentSearchFilter();
            UpdateEnvironmentSignalFilterButtons();
            SelectedFindingsItemsControl.ItemsSource = nodeFindings;
            SelectedFindingsEmptyText.Visibility = nodeFindings.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            SelectedFindingSummaryTextBlock.Text = nodeFindings.Count == 0
                ? "Активных сигналов нет. Узел можно использовать как справочник по связанным параметрам."
                : FormatFindingSummary(nodeFindings);
            SelectedFindingSummaryTextBlock.SetResourceReference(
                TextBlock.ForegroundProperty,
                nodeFindings.Count == 0 ? "CoreGoodBrush" : GetStatusBrushKey(nodeFindings[0].Level));
        }

        private static EnvironmentSection AttachEnvironmentSignal(EnvironmentSection setting, IReadOnlyList<EnvironmentFindingViewModel> findings)
        {
            if (setting == null)
                return setting;

            var finding = findings?
                .Where(item => item != null)
                .Where(item => string.Equals(item.Id, setting.Id, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(item => GetSeverity(item.Level))
                .FirstOrDefault();

            if (finding == null)
            {
                setting.SignalLevel = HealthLevel.Good;
                setting.SignalId = string.Empty;
                return setting;
            }

            setting.SignalLevel = finding.Level;
            setting.SignalId = finding.Id ?? string.Empty;
            return setting;
        }

        private void SetNodeVisualState(string key, bool active, bool force = false)
        {
            bool selected = _isDetailsOpen && string.Equals(key, _selectedNodeKey, StringComparison.OrdinalIgnoreCase);
            bool highlighted = active || selected;

            if (_zones.TryGetValue(key, out var zone))
            {
                var transforms = EnsureTransforms(zone);
                AnimateScale(transforms.Scale, highlighted ? selected ? 1.018 : 1.026 : 1, force ? 130 : 160);
                AnimateTranslateY(transforms.Translate, highlighted ? -6 : 0, force ? 130 : 160);
            }

            if (_glows.TryGetValue(key, out var glow))
                AnimateOpacity(glow, selected ? 0.16 : active ? 0.22 : 0, force ? 130 : 160);

            if (_routes.TryGetValue(key, out var route))
            {
                AnimateOpacity(route, highlighted ? 0.88 : 0, force ? 130 : 160);
                if (highlighted)
                    StartRoutePulse(route);
                else
                    StopRoutePulse(route);
            }

            if (highlighted)
                StartNodeMicroAnimation(key);
            else
                StopNodeMicroAnimation(key);
        }

        private void ApplyModuleStatus()
        {
            var module = _healthService?.GetModule(CoreModuleId.WindowsSetup);
            var status = module?.Status?.Status ?? HealthLevel.Good;
            int problems = module?.Status?.ProblemCount ?? 0;
            int recommendations = module?.Status?.RecommendationCount ?? 0;

            ModuleStatusTextBlock.Text = GetModuleStatusText(status, problems, recommendations);
            ModuleStatusIndicator.SetResourceReference(Shape.FillProperty, GetStatusBrushKey(status));

            _currentFindings.Clear();
            _currentFindings.AddRange(BuildEnvironmentFindings(module?.Status));
            UpdateNodeSignalVisuals();
            RenderCallouts();

            if (_nodes.TryGetValue(_selectedNodeKey, out var node))
                UpdateDetailsContent(node);
        }

        private void UpdateNodeSignalVisuals()
        {
            foreach (var key in _nodes.Keys)
            {
                var strongest = _currentFindings
                    .Where(finding => string.Equals(finding.NodeKey, key, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(finding => GetSeverity(finding.Level))
                    .FirstOrDefault();

                string brushKey = strongest == null ? "CoreLineActiveBrush" : GetStatusBrushKey(strongest.Level);

                if (_glows.TryGetValue(key, out var glow))
                    glow.SetResourceReference(Border.BackgroundProperty, brushKey);

                if (_routes.TryGetValue(key, out var route))
                    route.SetResourceReference(Shape.StrokeProperty, brushKey);
            }
        }

        private void RenderCallouts()
        {
            ClearCalloutLayerSafely();

            var groups = _currentFindings
                .GroupBy(finding => finding.NodeKey, StringComparer.OrdinalIgnoreCase)
                .Select(group =>
                {
                    var ordered = group
                        .OrderByDescending(finding => GetSeverity(finding.Level))
                        .ThenBy(finding => finding.Title)
                        .ToList();

                    return new EnvironmentCalloutSeed(
                        group.Key,
                        ordered[0],
                        Math.Max(0, ordered.Count - 1),
                        GetPreferredCalloutSide(group.Key),
                        GetPreferredCalloutY(group.Key),
                        EstimateCalloutHeight(ordered[0], Math.Max(0, ordered.Count - 1)));
                })
                .OrderByDescending(group => GetSeverity(group.Finding.Level))
                .ThenBy(group => GetNodeDisplayOrder(group.NodeKey))
                .Take(5)
                .ToList();

            var layouts = BuildCalloutLayouts(groups);
            for (int index = 0; index < layouts.Count; index++)
                AddCallout(layouts[index], index);
        }

        private void AddCallout(EnvironmentCalloutLayout layout, int index)
        {
            var target = layout.Source;
            var lineEnd = GetCalloutLineEnd(layout);
            string brushKey = GetStatusBrushKey(layout.Finding.Level);

            var line = new Line
            {
                X1 = lineEnd.X,
                Y1 = lineEnd.Y,
                X2 = target.X,
                Y2 = target.Y,
                StrokeThickness = 1.35,
                Opacity = 0.88,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                IsHitTestVisible = false
            };
            line.SetResourceReference(Shape.StrokeProperty, brushKey);
            CalloutLayer.Children.Add(line);

            var anchor = new Ellipse
            {
                Width = 10,
                Height = 10,
                Opacity = 1,
                IsHitTestVisible = false
            };
            anchor.SetResourceReference(Shape.FillProperty, brushKey);
            Canvas.SetLeft(anchor, target.X - 5);
            Canvas.SetTop(anchor, target.Y - 5);
            CalloutLayer.Children.Add(anchor);

            var card = new Border
            {
                Width = layout.CardWidth,
                MinHeight = 92,
                Style = FindResource("DiagnosticCardStyle") as Style,
                Opacity = 1,
                Cursor = System.Windows.Input.Cursors.Hand,
                Tag = layout.Finding
            };
            card.SetResourceReference(Border.BorderBrushProperty, brushKey);
            card.MouseLeftButtonUp += (sender, args) =>
            {
                args.Handled = true;
                NavigateToEnvironmentFindingTarget(layout.Finding, openNode: true);
            };

            var panel = new StackPanel();
            var kind = new TextBlock
            {
                Text = layout.Finding.KindText,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Opacity = 0.76
            };
            kind.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
            panel.Children.Add(kind);
            panel.Children.Add(new TextBlock
            {
                Text = layout.Finding.Title,
                Margin = new Thickness(0, 7, 0, 0),
                FontSize = 12.5,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap
            });
            panel.Children.Add(new TextBlock
            {
                Text = TrimForCallout(layout.Finding.Description, 92),
                Margin = new Thickness(0, 7, 0, 0),
                FontSize = 11.3,
                LineHeight = 16,
                MaxHeight = 64,
                Opacity = 0.76,
                TextWrapping = TextWrapping.Wrap
            });

            if (layout.ExtraCount > 0)
            {
                var extra = new TextBlock
                {
                    Text = $"+ ещё {FormatCount(layout.ExtraCount, "сигнал", "сигнала", "сигналов")} внутри узла",
                    Margin = new Thickness(0, 8, 0, 0),
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold,
                    LineHeight = 15,
                    MaxHeight = 36,
                    TextWrapping = TextWrapping.Wrap
                };
                extra.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
                panel.Children.Add(extra);
            }

            card.Child = panel;
            Canvas.SetLeft(card, layout.Card.X);
            Canvas.SetTop(card, layout.Card.Y);
            CalloutLayer.Children.Add(card);

        }

        private void ClearCalloutLayerSafely()
        {
            if (CalloutLayer == null)
                return;

            CalloutLayer.Children.Clear();
        }

        private void ShowNodeDetails()
        {
            if (!_nodes.ContainsKey(_selectedNodeKey))
                return;

            ResetNodeDetailsPanelAnimations();
            _isDetailsOpen = true;

            if (NodeDetailsLayer.Visibility != Visibility.Visible)
                NodeDetailsLayer.Visibility = Visibility.Visible;

            NodeDetailsLayer.Opacity = 1;
            DetailsScrim.Opacity = 0;
            NodeDetailsPanel.Opacity = 0;
            NodeDetailsTranslate.X = 0;
            NodeDetailsTranslate.Y = 12;
            NodeDetailsScale.ScaleX = 0.985;
            NodeDetailsScale.ScaleY = 0.985;

            AnimateOpacity(DetailsScrim, 0.80, 220);
            NodeDetailsTranslate.BeginAnimation(
                TranslateTransform.YProperty,
                new DoubleAnimation(0, TimeSpan.FromMilliseconds(240))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                });
            NodeDetailsScale.BeginAnimation(
                ScaleTransform.ScaleXProperty,
                new DoubleAnimation(1, TimeSpan.FromMilliseconds(240))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                });
            NodeDetailsScale.BeginAnimation(
                ScaleTransform.ScaleYProperty,
                new DoubleAnimation(1, TimeSpan.FromMilliseconds(240))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                });
            AnimateOpacity(NodeDetailsPanel, 1, 220);
            PlayDetailsOrbOpenAnimation(_selectedNodeKey);
        }

        private void HideNodeDetails()
        {
            if (!_isDetailsOpen && NodeDetailsLayer.Visibility != Visibility.Visible)
                return;

            _isDetailsOpen = false;
            StopDetailsOrbBreathing();

            var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(180))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };
            DetailsScrim.BeginAnimation(UIElement.OpacityProperty, fade);
            NodeDetailsPanel.BeginAnimation(UIElement.OpacityProperty, fade.Clone());
            NodeDetailsTranslate.BeginAnimation(
                TranslateTransform.YProperty,
                new DoubleAnimation(18, TimeSpan.FromMilliseconds(220))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
                });
            NodeDetailsScale.BeginAnimation(
                ScaleTransform.ScaleXProperty,
                new DoubleAnimation(0.985, TimeSpan.FromMilliseconds(220))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
                });
            NodeDetailsScale.BeginAnimation(
                ScaleTransform.ScaleYProperty,
                new DoubleAnimation(0.985, TimeSpan.FromMilliseconds(220))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
                });

            PlayDetailsOrbCloseAnimation();
        }

        private void ResetNodeDetailsPanelAnimations()
        {
            DetailsScrim?.BeginAnimation(UIElement.OpacityProperty, null);
            NodeDetailsPanel?.BeginAnimation(UIElement.OpacityProperty, null);
            NodeDetailsTranslate?.BeginAnimation(TranslateTransform.XProperty, null);
            NodeDetailsTranslate?.BeginAnimation(TranslateTransform.YProperty, null);
            NodeDetailsScale?.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            NodeDetailsScale?.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        }

        private void PlayDetailsOrbOpenAnimation(string sourceNodeKey)
        {
            StopDetailsOrbBreathing();
            _detailsOrbSourceNodeKey = string.IsNullOrWhiteSpace(sourceNodeKey) ? _selectedNodeKey : sourceNodeKey;

            var startPoint = GetNodeCenterInPage(_detailsOrbSourceNodeKey);
            var targetPoint = GetDetailsOrbTargetCenter();
            double startLeft = ToOrbLeft(startPoint);
            double startTop = ToOrbTop(startPoint);
            double targetLeft = ToOrbLeft(targetPoint);
            double targetTop = ToOrbTop(targetPoint);

            NodeDetailsOrbButton.Visibility = Visibility.Visible;
            ResetDetailsOrbAnimation();
            PositionDetailsOrb(startPoint, 0.42, 0);

            NodeDetailsOrbButton.BeginAnimation(
                UIElement.OpacityProperty,
                new DoubleAnimation
                {
                    From = 0,
                    To = 1,
                    Duration = TimeSpan.FromMilliseconds(210),
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                });

            var flyEase = new CubicEase { EasingMode = EasingMode.EaseOut };
            NodeDetailsOrbTranslate.BeginAnimation(
                TranslateTransform.XProperty,
                new DoubleAnimation
                {
                    From = startLeft,
                    To = targetLeft,
                    Duration = TimeSpan.FromMilliseconds(520),
                    EasingFunction = flyEase
                });
            NodeDetailsOrbTranslate.BeginAnimation(
                TranslateTransform.YProperty,
                new DoubleAnimation
                {
                    From = startTop,
                    To = targetTop,
                    Duration = TimeSpan.FromMilliseconds(520),
                    EasingFunction = flyEase
                });

            var scale = new DoubleAnimation
            {
                From = 0.42,
                To = 1,
                Duration = TimeSpan.FromMilliseconds(520),
                EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.28 }
            };
            scale.Completed += (sender, args) =>
            {
                if (_isDetailsOpen)
                    StartDetailsOrbBreathing();
            };

            NodeDetailsOrbScale.BeginAnimation(ScaleTransform.ScaleXProperty, scale);
            NodeDetailsOrbScale.BeginAnimation(ScaleTransform.ScaleYProperty, scale.Clone());
        }

        private void PlayDetailsOrbCloseAnimation()
        {
            StopDetailsOrbBreathing();
            var returnPoint = GetNodeCenterInPage(_detailsOrbSourceNodeKey);
            double currentLeft = NodeDetailsOrbTranslate.X;
            double currentTop = NodeDetailsOrbTranslate.Y;
            double returnLeft = ToOrbLeft(returnPoint);
            double returnTop = ToOrbTop(returnPoint);

            var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(360))
            {
                BeginTime = TimeSpan.FromMilliseconds(70),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };
            fade.Completed += (sender, args) =>
            {
                if (!_isDetailsOpen)
                {
                    NodeDetailsOrbButton.Visibility = Visibility.Collapsed;
                    NodeDetailsLayer.Visibility = Visibility.Collapsed;
                    ResetMapMotion();
                }
            };

            NodeDetailsOrbButton.BeginAnimation(UIElement.OpacityProperty, fade);
            NodeDetailsOrbTranslate.BeginAnimation(
                TranslateTransform.XProperty,
                new DoubleAnimation
                {
                    From = currentLeft,
                    To = returnLeft,
                    Duration = TimeSpan.FromMilliseconds(420),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
                });
            NodeDetailsOrbTranslate.BeginAnimation(
                TranslateTransform.YProperty,
                new DoubleAnimation
                {
                    From = currentTop,
                    To = returnTop,
                    Duration = TimeSpan.FromMilliseconds(420),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
                });
            NodeDetailsOrbScale.BeginAnimation(
                ScaleTransform.ScaleXProperty,
                new DoubleAnimation(0.42, TimeSpan.FromMilliseconds(420)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } });
            NodeDetailsOrbScale.BeginAnimation(
                ScaleTransform.ScaleYProperty,
                new DoubleAnimation(0.42, TimeSpan.FromMilliseconds(420)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } });
        }

        private WindowsPoint GetNodeCenterInPage(string nodeKey)
        {
            try
            {
                if (_zones.TryGetValue(nodeKey, out var zone) &&
                    zone.ActualWidth > 0 &&
                    zone.ActualHeight > 0)
                {
                    return zone.TranslatePoint(
                        new WindowsPoint(zone.ActualWidth / 2, zone.ActualHeight / 2),
                        this);
                }
            }
            catch
            {
            }

            double width = NodeDetailsLayer?.ActualWidth > 0 ? NodeDetailsLayer.ActualWidth : ActualWidth;
            double height = NodeDetailsLayer?.ActualHeight > 0 ? NodeDetailsLayer.ActualHeight : ActualHeight;
            return new WindowsPoint(width / 2, height / 2);
        }

        private WindowsPoint GetDetailsOrbTargetCenter()
        {
            double width = NodeDetailsLayer?.ActualWidth > 0 ? NodeDetailsLayer.ActualWidth : ActualWidth;
            return new WindowsPoint(width * DetailsOrbTargetXRatio, DetailsOrbTargetCenterY);
        }

        private void PositionDetailsOrb(WindowsPoint center, double scale, double opacity)
        {
            NodeDetailsOrbTranslate.X = ToOrbLeft(center);
            NodeDetailsOrbTranslate.Y = ToOrbTop(center);
            NodeDetailsOrbScale.ScaleX = scale;
            NodeDetailsOrbScale.ScaleY = scale;
            NodeDetailsOrbButton.Opacity = opacity;
        }

        private void ResetDetailsOrbAnimation()
        {
            NodeDetailsOrbButton?.BeginAnimation(UIElement.OpacityProperty, null);
            NodeDetailsOrbTranslate?.BeginAnimation(TranslateTransform.XProperty, null);
            NodeDetailsOrbTranslate?.BeginAnimation(TranslateTransform.YProperty, null);
            NodeDetailsOrbScale?.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            NodeDetailsOrbScale?.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        }

        private void StartDetailsOrbBreathing()
        {
            var scale = new DoubleAnimation(0.96, 1.055, TimeSpan.FromMilliseconds(1650))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            };

            var opacity = new DoubleAnimation(0.86, 1, TimeSpan.FromMilliseconds(1650))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            };

            NodeDetailsOrbScale.BeginAnimation(ScaleTransform.ScaleXProperty, scale);
            NodeDetailsOrbScale.BeginAnimation(ScaleTransform.ScaleYProperty, scale.Clone());
            NodeDetailsOrbButton.BeginAnimation(UIElement.OpacityProperty, opacity);
        }

        private void StopDetailsOrbBreathing()
        {
            NodeDetailsOrbButton?.BeginAnimation(UIElement.OpacityProperty, null);
            NodeDetailsOrbScale?.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            NodeDetailsOrbScale?.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        }

        private double ToOrbLeft(WindowsPoint center)
        {
            double width = NodeDetailsOrbButton?.ActualWidth > 0 ? NodeDetailsOrbButton.ActualWidth : DetailsOrbSize;
            return center.X - width / 2;
        }

        private double ToOrbTop(WindowsPoint center)
        {
            double height = NodeDetailsOrbButton?.ActualHeight > 0 ? NodeDetailsOrbButton.ActualHeight : DetailsOrbSize;
            return center.Y - height / 2;
        }

        private void NavigateHome()
        {
            if (Application.Current.MainWindow is MainWindow mainWindow)
                mainWindow.NavigateToCoreHome();
        }

        private void ResetMapMotion()
        {
            foreach (var zone in _zones.Values)
            {
                var transforms = EnsureTransforms(zone);
                transforms.Scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                transforms.Scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                transforms.Translate.BeginAnimation(TranslateTransform.YProperty, null);
                transforms.Scale.ScaleX = 1;
                transforms.Scale.ScaleY = 1;
                transforms.Translate.Y = 0;
            }

            foreach (var glow in _glows.Values)
            {
                glow.BeginAnimation(UIElement.OpacityProperty, null);
                glow.Opacity = 0;
            }

            foreach (var route in _routes.Values)
            {
                route.BeginAnimation(UIElement.OpacityProperty, null);
                route.Opacity = 0;
                StopRoutePulse(route);
            }

            foreach (var key in _nodes.Keys)
                StopNodeMicroAnimation(key);
        }

        private static void StartRoutePulse(Shape route)
        {
            route.BeginAnimation(
                Shape.StrokeDashOffsetProperty,
                new DoubleAnimation
                {
                    From = 0,
                    To = -18,
                    Duration = TimeSpan.FromMilliseconds(720),
                    RepeatBehavior = RepeatBehavior.Forever
                });
        }

        private static void StopRoutePulse(Shape route)
        {
            route.BeginAnimation(Shape.StrokeDashOffsetProperty, null);
            route.StrokeDashOffset = 0;
        }

        private void StartNodeMicroAnimation(string key)
        {
            if (string.Equals(key, "Explorer", StringComparison.OrdinalIgnoreCase))
            {
                ExplorerScanPulse.BeginAnimation(
                    FrameworkElement.WidthProperty,
                    new DoubleAnimation(58, 156, TimeSpan.FromMilliseconds(880))
                    {
                        AutoReverse = true,
                        RepeatBehavior = RepeatBehavior.Forever,
                        EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
                    });
                return;
            }

            if (string.Equals(key, "Start", StringComparison.OrdinalIgnoreCase))
            {
                StartTilePulse.BeginAnimation(
                    Canvas.LeftProperty,
                    new DoubleAnimation(18, 120, TimeSpan.FromMilliseconds(820))
                    {
                        AutoReverse = true,
                        RepeatBehavior = RepeatBehavior.Forever,
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
                    });
                return;
            }

            if (string.Equals(key, "Taskbar", StringComparison.OrdinalIgnoreCase))
            {
                var transforms = EnsureTransforms(TaskbarActivePulse);
                AnimateScale(transforms.Scale, 1.18, 240);
                TaskbarActivePulse.BeginAnimation(
                    UIElement.OpacityProperty,
                    new DoubleAnimation(0.58, 1, TimeSpan.FromMilliseconds(760))
                    {
                        AutoReverse = true,
                        RepeatBehavior = RepeatBehavior.Forever,
                        EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
                    });
                return;
            }

            var element = GetNodeMicroElement(key);
            if (element == null)
                return;

            element.BeginAnimation(
                UIElement.OpacityProperty,
                new DoubleAnimation(GetDefaultMicroOpacity(key) * 0.58, 1, TimeSpan.FromMilliseconds(980))
                {
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
                });
        }

        private void StopNodeMicroAnimation(string key)
        {
            if (string.Equals(key, "Explorer", StringComparison.OrdinalIgnoreCase))
            {
                ExplorerScanPulse.BeginAnimation(FrameworkElement.WidthProperty, null);
                ExplorerScanPulse.Width = 64;
                ExplorerScanPulse.Opacity = 0.86;
                return;
            }

            if (string.Equals(key, "Start", StringComparison.OrdinalIgnoreCase))
            {
                StartTilePulse.BeginAnimation(Canvas.LeftProperty, null);
                Canvas.SetLeft(StartTilePulse, 18);
                StartTilePulse.Opacity = 0.86;
                return;
            }

            if (string.Equals(key, "Taskbar", StringComparison.OrdinalIgnoreCase))
            {
                var transforms = EnsureTransforms(TaskbarActivePulse);
                transforms.Scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                transforms.Scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                transforms.Scale.ScaleX = 1;
                transforms.Scale.ScaleY = 1;
                TaskbarActivePulse.BeginAnimation(UIElement.OpacityProperty, null);
                TaskbarActivePulse.Opacity = 0.86;
                return;
            }

            var element = GetNodeMicroElement(key);
            if (element == null)
                return;

            element.BeginAnimation(UIElement.OpacityProperty, null);
            element.Opacity = GetDefaultMicroOpacity(key);
        }

        private UIElement GetNodeMicroElement(string key)
        {
            return key switch
            {
                "Display" => DisplayPulseFrame,
                "Explorer" => ExplorerScanPulse,
                "Windows" => WindowsActivityPulse,
                "Start" => StartTilePulse,
                "Taskbar" => TaskbarActivePulse,
                "Search" => SearchPulseFrame,
                "Tray" => TrayPulseDot,
                _ => null
            };
        }

        private static double GetDefaultMicroOpacity(string key)
        {
            return key switch
            {
                "Display" => 0.97,
                "Search" => 1,
                _ => 0.86
            };
        }

        private static (ScaleTransform Scale, TranslateTransform Translate) EnsureTransforms(FrameworkElement element)
        {
            if (element.RenderTransform is not TransformGroup group)
            {
                group = new TransformGroup();
                group.Children.Add(new ScaleTransform(1, 1));
                group.Children.Add(new TranslateTransform());
                element.RenderTransform = group;
            }

            var scale = group.Children.OfType<ScaleTransform>().FirstOrDefault();
            if (scale == null)
            {
                scale = new ScaleTransform(1, 1);
                group.Children.Insert(0, scale);
            }

            var translate = group.Children.OfType<TranslateTransform>().FirstOrDefault();
            if (translate == null)
            {
                translate = new TranslateTransform();
                group.Children.Add(translate);
            }

            return (scale, translate);
        }

        private static void AnimateScale(ScaleTransform transform, double value, int milliseconds)
        {
            transform.BeginAnimation(ScaleTransform.ScaleXProperty, CreateAnimation(value, milliseconds));
            transform.BeginAnimation(ScaleTransform.ScaleYProperty, CreateAnimation(value, milliseconds));
        }

        private static void AnimateTranslateY(TranslateTransform transform, double value, int milliseconds)
        {
            transform.BeginAnimation(TranslateTransform.YProperty, CreateAnimation(value, milliseconds));
        }

        private static void AnimateOpacity(UIElement element, double value, int milliseconds)
        {
            element.BeginAnimation(UIElement.OpacityProperty, CreateAnimation(value, milliseconds));
        }

        private static DoubleAnimation CreateAnimation(double to, int milliseconds)
        {
            return new DoubleAnimation
            {
                To = to,
                Duration = TimeSpan.FromMilliseconds(milliseconds),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
        }

        private void ResetEnvironmentSearch(bool clearText)
        {
            _pendingEnvironmentSearchSettingId = string.Empty;
            _pendingEnvironmentSearchSection = string.Empty;
            _environmentNavigationVersion++;
            SetEnvironmentNavigationBusy(false);

            if (EnvironmentSearchSuggestionsItemsControl != null)
            {
                EnvironmentSearchSuggestionsItemsControl.ItemsSource = null;
                EnvironmentSearchSuggestionsItemsControl.Visibility = Visibility.Collapsed;
            }

            if (EnvironmentSearchEmptyText != null)
                EnvironmentSearchEmptyText.Visibility = Visibility.Collapsed;

            if (EnvironmentSearchClearButton != null)
                EnvironmentSearchClearButton.Visibility = Visibility.Collapsed;

            if (EnvironmentSearchPlaceholderTextBlock != null)
                EnvironmentSearchPlaceholderTextBlock.Visibility = Visibility.Visible;

            if (clearText && EnvironmentSearchTextBox != null)
            {
                EnvironmentSearchTextBox.Text = string.Empty;
                EnvironmentSearchTextBox.CaretIndex = 0;
            }
        }

        private void ApplyEnvironmentSearchFilter()
        {
            if (SelectedSectionsItemsControl == null)
                return;

            string query = EnvironmentSearchTextBox?.Text?.Trim() ?? string.Empty;
            bool hasQuery = !string.IsNullOrWhiteSpace(query);

            if (EnvironmentSearchPlaceholderTextBlock != null)
                EnvironmentSearchPlaceholderTextBlock.Visibility = hasQuery ? Visibility.Collapsed : Visibility.Visible;

            if (EnvironmentSearchClearButton != null)
                EnvironmentSearchClearButton.Visibility = hasQuery ? Visibility.Visible : Visibility.Collapsed;

            IEnumerable<EnvironmentSection> filtered = _currentEnvironmentSettings;

            if (hasQuery)
                filtered = filtered.Where(setting => MatchesEnvironmentSearch(setting, query));

            filtered = ApplyEnvironmentSignalFilter(filtered, _environmentSignalFilter);

            var result = filtered.ToList();
            SelectedSectionsItemsControl.ItemsSource = result;

            bool noItems = _currentEnvironmentSettings.Count == 0;
            if (EnvironmentSearchEmptyText != null)
                EnvironmentSearchEmptyText.Visibility = !noItems && result.Count == 0 && (hasQuery || _environmentSignalFilter != EnvironmentSignalFilter.All) ? Visibility.Visible : Visibility.Collapsed;

            if (EnvironmentSettingsEmptyText != null)
                EnvironmentSettingsEmptyText.Visibility = noItems ? Visibility.Visible : Visibility.Collapsed;

            UpdateEnvironmentSearchSuggestions(_currentEnvironmentSettings, query);
        }

        private static IEnumerable<EnvironmentSection> ApplyEnvironmentSignalFilter(IEnumerable<EnvironmentSection> items, EnvironmentSignalFilter filter)
        {
            return filter switch
            {
                EnvironmentSignalFilter.Recommendations => items.Where(item => item.SignalLevel == HealthLevel.Normal),
                EnvironmentSignalFilter.Problems => items.Where(item => item.SignalLevel == HealthLevel.Warning || item.SignalLevel == HealthLevel.Attention),
                EnvironmentSignalFilter.Critical => items.Where(item => item.SignalLevel == HealthLevel.Critical),
                _ => items
            };
        }

        private void EnvironmentSignalFilter_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not ToggleButton button)
                return;

            _environmentSignalFilter = ParseEnvironmentSignalFilter(button.Tag?.ToString());
            UpdateEnvironmentSignalFilterButtons();
            ApplyEnvironmentSearchFilter();
        }

        private static EnvironmentSignalFilter ParseEnvironmentSignalFilter(string value)
        {
            return Enum.TryParse(value, ignoreCase: true, out EnvironmentSignalFilter filter)
                ? filter
                : EnvironmentSignalFilter.All;
        }

        private void UpdateEnvironmentSignalFilterButtons()
        {
            SetEnvironmentFilterButtonState(EnvironmentFilterAllButton, EnvironmentSignalFilter.All);
            SetEnvironmentFilterButtonState(EnvironmentFilterRecommendationsButton, EnvironmentSignalFilter.Recommendations);
            SetEnvironmentFilterButtonState(EnvironmentFilterProblemsButton, EnvironmentSignalFilter.Problems);
            SetEnvironmentFilterButtonState(EnvironmentFilterCriticalButton, EnvironmentSignalFilter.Critical);
        }

        private void SetEnvironmentFilterButtonState(ToggleButton button, EnvironmentSignalFilter filter)
        {
            if (button == null)
                return;

            button.IsChecked = _environmentSignalFilter == filter;
        }

        private void UpdateEnvironmentSearchSuggestions(IReadOnlyList<EnvironmentSection> settings, string query)
        {
            if (EnvironmentSearchSuggestionsItemsControl == null)
                return;

            if (settings == null || string.IsNullOrWhiteSpace(query))
            {
                EnvironmentSearchSuggestionsItemsControl.ItemsSource = null;
                EnvironmentSearchSuggestionsItemsControl.Visibility = Visibility.Collapsed;
                return;
            }

            var sectionSuggestions = settings
                .Where(setting => MatchesEnvironmentText(setting.Scope, query))
                .GroupBy(setting => setting.Scope, StringComparer.CurrentCultureIgnoreCase)
                .Select(group => new EnvironmentSearchSuggestion
                {
                    IsSection = true,
                    Title = group.Key,
                    SectionTitle = group.Key,
                    Caption = "Раздел узла"
                })
                .Take(3);

            var settingSuggestions = settings
                .Where(setting => MatchesEnvironmentSearch(setting, query))
                .Select(setting => new EnvironmentSearchSuggestion
                {
                    SettingId = setting.Id,
                    Title = setting.Title,
                    SectionTitle = setting.Scope,
                    Caption = $"Параметр: {setting.Scope}"
                })
                .Take(5);

            var suggestions = sectionSuggestions
                .Concat(settingSuggestions)
                .GroupBy(suggestion => $"{suggestion.IsSection}|{suggestion.Title}|{suggestion.SectionTitle}", StringComparer.CurrentCultureIgnoreCase)
                .Select(group => group.First())
                .Take(6)
                .ToList();

            EnvironmentSearchSuggestionsItemsControl.ItemsSource = suggestions;
            EnvironmentSearchSuggestionsItemsControl.Visibility = suggestions.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private static bool MatchesEnvironmentSearch(EnvironmentSection setting, string query)
        {
            if (setting == null || string.IsNullOrWhiteSpace(query))
                return false;

            return MatchesEnvironmentText(setting.Title, query) ||
                   MatchesEnvironmentText(setting.Description, query) ||
                   MatchesEnvironmentText(setting.Scope, query) ||
                   MatchesEnvironmentText(setting.Source, query) ||
                   MatchesEnvironmentText(setting.CurrentState, query) ||
                   MatchesEnvironmentText(setting.RecommendedState, query) ||
                   MatchesEnvironmentText(setting.RiskText, query) ||
                   setting.Options.Any(option => MatchesEnvironmentText(option.Label, query) || MatchesEnvironmentText(option.ButtonText, query));
        }

        private static bool MatchesEnvironmentText(string source, string query)
        {
            return !string.IsNullOrWhiteSpace(source) &&
                   !string.IsNullOrWhiteSpace(query) &&
                   source.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0;
        }

        private async void QueueEnvironmentTargetNavigation(bool showBusy)
        {
            int version = ++_environmentNavigationVersion;

            if (showBusy)
                SetEnvironmentNavigationBusy(true);

            try
            {
                await Dispatcher.Yield(DispatcherPriority.Background);

                if (version != _environmentNavigationVersion || !_isPageActive)
                    return;

                ApplyEnvironmentSearchFilter();
                await Dispatcher.Yield(DispatcherPriority.ContextIdle);

                if (showBusy)
                    await Task.Delay(140);
            }
            finally
            {
                if (showBusy && version == _environmentNavigationVersion)
                    SetEnvironmentNavigationBusy(false);
            }
        }

        private void SetEnvironmentNavigationBusy(bool isBusy)
        {
            if (EnvironmentNavigationBusyOverlay == null)
                return;

            if (isBusy && !_isPageActive)
                return;

            EnvironmentNavigationBusyOverlay.Visibility = isBusy ? Visibility.Visible : Visibility.Collapsed;
            EnvironmentNavigationBusyOverlay.Opacity = isBusy ? 1 : 0;

            if (isBusy)
                StartEnvironmentLoadingSquares(GetEnvironmentBusySquares());
            else
                StopEnvironmentLoadingSquares(GetEnvironmentBusySquares());
        }

        private IEnumerable<Border> GetEnvironmentBusySquares()
        {
            if (EnvironmentBusySquareA != null)
                yield return EnvironmentBusySquareA;
            if (EnvironmentBusySquareB != null)
                yield return EnvironmentBusySquareB;
            if (EnvironmentBusySquareC != null)
                yield return EnvironmentBusySquareC;
            if (EnvironmentBusySquareD != null)
                yield return EnvironmentBusySquareD;
        }

        private static void StartEnvironmentLoadingSquares(IEnumerable<Border> squares)
        {
            int index = 0;
            foreach (var square in squares)
            {
                double delay = index * 130;
                square.BeginAnimation(
                    UIElement.OpacityProperty,
                    new DoubleAnimation(0.32, 1, TimeSpan.FromMilliseconds(360))
                    {
                        BeginTime = TimeSpan.FromMilliseconds(delay),
                        AutoReverse = true,
                        RepeatBehavior = RepeatBehavior.Forever,
                        EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
                    });

                var transforms = EnsureTransforms(square);
                transforms.Scale.BeginAnimation(
                    ScaleTransform.ScaleXProperty,
                    new DoubleAnimation(0.86, 1.08, TimeSpan.FromMilliseconds(360))
                    {
                        BeginTime = TimeSpan.FromMilliseconds(delay),
                        AutoReverse = true,
                        RepeatBehavior = RepeatBehavior.Forever,
                        EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
                    });
                transforms.Scale.BeginAnimation(
                    ScaleTransform.ScaleYProperty,
                    new DoubleAnimation(0.86, 1.08, TimeSpan.FromMilliseconds(360))
                    {
                        BeginTime = TimeSpan.FromMilliseconds(delay),
                        AutoReverse = true,
                        RepeatBehavior = RepeatBehavior.Forever,
                        EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
                    });
                index++;
            }
        }

        private static void StopEnvironmentLoadingSquares(IEnumerable<Border> squares)
        {
            foreach (var square in squares)
            {
                square.BeginAnimation(UIElement.OpacityProperty, null);
                square.Opacity = 1;
                var transforms = EnsureTransforms(square);
                transforms.Scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                transforms.Scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                transforms.Scale.ScaleX = 1;
                transforms.Scale.ScaleY = 1;
            }
        }

        private void QueueEnvironmentSearchCaretAtStart()
        {
            Dispatcher.BeginInvoke(new Action(PlaceEnvironmentSearchCaretAtStartWhenEmpty), DispatcherPriority.Input);
        }

        private void PlaceEnvironmentSearchCaretAtStartWhenEmpty()
        {
            if (EnvironmentSearchTextBox == null ||
                !string.IsNullOrEmpty(EnvironmentSearchTextBox.Text))
            {
                return;
            }

            EnvironmentSearchTextBox.CaretIndex = 0;
            EnvironmentSearchTextBox.Select(0, 0);
            EnvironmentSearchTextBox.ScrollToHorizontalOffset(0);
        }

        private static void PlaySearchResultHighlight(FrameworkElement element)
        {
            if (element == null)
                return;

            var overlay = FindTaggedChild<Border>(element, "SearchHighlightOverlay");
            var target = overlay ?? element;

            target.Opacity = 0;
            target.BeginAnimation(
                UIElement.OpacityProperty,
                new DoubleAnimationUsingKeyFrames
                {
                    KeyFrames =
                    {
                        new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)),
                        new EasingDoubleKeyFrame(0.74, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(180)))
                        {
                            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                        },
                        new EasingDoubleKeyFrame(0.18, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(760)))
                        {
                            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
                        },
                        new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1250)))
                        {
                            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
                        }
                    }
                });
        }

        private static T FindTaggedChild<T>(DependencyObject root, object tag)
            where T : FrameworkElement
        {
            if (root == null)
                return null;

            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int index = 0; index < count; index++)
            {
                var child = VisualTreeHelper.GetChild(root, index);
                if (child is T typed && Equals(typed.Tag, tag))
                    return typed;

                var nested = FindTaggedChild<T>(child, tag);
                if (nested != null)
                    return nested;
            }

            return null;
        }

        private static T FindVisualParent<T>(DependencyObject source)
            where T : DependencyObject
        {
            DependencyObject current = source;
            while (current != null)
            {
                if (current is T typed)
                    return typed;

                current = VisualTreeHelper.GetParent(current);
            }

            return null;
        }

        private static IReadOnlyList<EnvironmentSettingOptionViewModel> BuildEnvironmentSettingOptions(
            EnvironmentRegistrySettingDefinition setting,
            object effectiveValue)
        {
            var options = setting.Options.Count > 0
                ? setting.Options
                : new List<EnvironmentRegistrySettingOption> { new EnvironmentRegistrySettingOption(setting.RecommendedLabel, setting.RecommendedValue, isRecommended: true) };

            return options
                .Select(option =>
                {
                    bool isCurrent = ValuesEqual(effectiveValue, option.Value, setting.ValueKind);
                    string label = string.IsNullOrWhiteSpace(option.Label)
                        ? FormatRegistryValue(option.Value)
                        : option.Label;

                    return new EnvironmentSettingOptionViewModel(
                        setting.Id,
                        label,
                        option.Value,
                        option.IsRecommended,
                        isCurrent,
                        isCurrent ? $"Сейчас: {label}" : $"Выбрать: {label}",
                        !isCurrent);
                })
                .ToList();
        }

        private static IReadOnlyList<EnvironmentSection> BuildNodeSettings(string nodeKey)
        {
            return GetEnvironmentSettingDefinitions()
                .Where(setting => string.Equals(setting.NodeKey, nodeKey, StringComparison.OrdinalIgnoreCase))
                .Select(ReadEnvironmentSetting)
                .ToList();
        }

        private static EnvironmentSection ReadEnvironmentSetting(EnvironmentRegistrySettingDefinition setting)
        {
            bool exists = TryReadEnvironmentRegistryValue(setting, out object rawValue);
            object effectiveValue = exists ? rawValue : setting.DefaultValue;
            bool isRecommended = ValuesEqual(effectiveValue, setting.RecommendedValue, setting.ValueKind);
            bool isDefault = ValuesEqual(effectiveValue, setting.DefaultValue, setting.ValueKind);
            var options = BuildEnvironmentSettingOptions(setting, effectiveValue);
            var currentOption = options.FirstOrDefault(option => option.IsCurrent);

            string currentState = currentOption != null
                ? $"Сейчас: {currentOption.Label}"
                : isRecommended
                    ? $"Сейчас: {setting.RecommendedLabel}"
                    : isDefault
                        ? $"Сейчас: {setting.DefaultLabel}"
                        : $"Сейчас: своё значение ({FormatRegistryValue(effectiveValue)})";
            bool hasBackup = HasEnvironmentBackup(setting.Id);

            return new EnvironmentSection(
                setting.Id,
                setting.Title,
                setting.Description,
                setting.Scope,
                setting.Source,
                currentState,
                $"Рекомендация: {setting.RecommendedLabel}",
                setting.RiskText,
                backupState: hasBackup ? "Точка отката: сохранена" : "Точка отката появится после изменения",
                canRollback: hasBackup,
                options: options);
        }

        private static IReadOnlyList<EnvironmentRegistrySettingDefinition> GetEnvironmentSettingDefinitions()
        {
            const string explorerAdvanced = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
            const string personalize = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
            const string search = @"Software\Microsoft\Windows\CurrentVersion\Search";
            const string desktop = @"Control Panel\Desktop";
            const string windowMetrics = @"Control Panel\Desktop\WindowMetrics";
            const string dwm = @"Software\Microsoft\Windows\DWM";
            const string policiesExplorer = @"Software\Policies\Microsoft\Windows\Explorer";
            const string pushNotifications = @"Software\Microsoft\Windows\CurrentVersion\PushNotifications";
            const string notificationSettings = @"Software\Microsoft\Windows\CurrentVersion\Notifications\Settings";
            const string peopleBand = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced\People";

            return new[]
            {
                Setting("workenv.display.transparency", "Display", "Прозрачность интерфейса", "Отключает прозрачные поверхности Windows. Это снижает визуальный шум и немного уменьшает нагрузку на оболочку.", "интерфейс", "реестр HKCU", RegistryHive.CurrentUser, personalize, "EnableTransparency", RegistryValueKind.DWord, 0, 1, "прозрачность отключена", "прозрачность включена"),
                Setting("workenv.display.window-animation", "Display", "Анимация окон", "Управляет анимацией сворачивания и разворачивания окон через параметр WindowMetrics.", "окна", "реестр HKCU", RegistryHive.CurrentUser, windowMetrics, "MinAnimate", RegistryValueKind.String, "0", "1", "анимация окон отключена", "анимация окон включена"),
                WithOptions(Setting("workenv.display.title-accent", "Display", "Акцент на заголовках окон", "Определяет, будет ли Windows окрашивать заголовки окон акцентным цветом.", "интерфейс", "реестр HKCU", RegistryHive.CurrentUser, dwm, "ColorPrevalence", RegistryValueKind.DWord, 0, 0, "акцент на заголовках выключен", "нейтральные заголовки"),
                    Option("акцент на заголовках выключен", 0, isRecommended: true),
                    Option("акцент на заголовках включён", 1, isRecommended: false)
                ),
                Setting("workenv.display.apps-dark-mode", "Display", "Тёмная тема приложений", "Переключает приложения Windows в тёмный режим. Это снижает яркость рабочей среды без внешних настроек.", "тема", "реестр HKCU", RegistryHive.CurrentUser, personalize, "AppsUseLightTheme", RegistryValueKind.DWord, 0, 1, "приложения в тёмном режиме", "приложения в светлом режиме"),
                Setting("workenv.display.system-dark-mode", "Display", "Тёмный режим системы", "Переключает системную оболочку Windows в тёмный режим для единой цветовой схемы интерфейса.", "тема", "реестр HKCU", RegistryHive.CurrentUser, personalize, "SystemUsesLightTheme", RegistryValueKind.DWord, 0, 1, "система в тёмном режиме", "система в светлом режиме"),
                WithOptions(Setting("workenv.display.menu-delay", "Display", "Задержка открытия меню", "Уменьшает задержку появления меню в оболочке Windows, чтобы контекстные меню ощущались быстрее.", "отклик", "реестр HKCU", RegistryHive.CurrentUser, desktop, "MenuShowDelay", RegistryValueKind.String, "120", "400", "меню открываются быстрее", "задержка меню 400 мс"),
                    Option("быстро: 120 мс", "120", isRecommended: true),
                    Option("стандарт: 400 мс", "400", isRecommended: false),
                    Option("без задержки: 0 мс", "0", isRecommended: false)
                ),
                WithOptions(Setting("workenv.display.desktop-icons", "Display", "Значки рабочего стола", "Проверяет, не скрыты ли значки рабочего стола системным параметром Explorer.", "рабочий стол", "реестр HKCU", RegistryHive.CurrentUser, explorerAdvanced, "HideIcons", RegistryValueKind.DWord, 0, 0, "значки рабочего стола видны", "значки рабочего стола видны"),
                    Option("значки рабочего стола видны", 0, isRecommended: true),
                    Option("значки рабочего стола скрыты", 1, isRecommended: false)
                ),
                WithOptions(Setting("workenv.display.font-smoothing", "Display", "Сглаживание шрифтов", "Включает системное сглаживание шрифтов рабочего стола. Это влияет на читаемость текста в классических окнах и оболочке.", "экран", "реестр HKCU", RegistryHive.CurrentUser, desktop, "FontSmoothing", RegistryValueKind.String, "2", "2", "сглаживание включено", "сглаживание включено"),
                    Option("сглаживание включено", "2", isRecommended: true),
                    Option("сглаживание отключено", "0", isRecommended: false)
                ),
                WithOptions(Setting("workenv.display.drag-full-windows", "Display", "Показывать содержимое при перетаскивании", "Оставляет видимым содержимое окна во время перетаскивания, чтобы точнее раскладывать рабочие окна.", "окна", "реестр HKCU", RegistryHive.CurrentUser, desktop, "DragFullWindows", RegistryValueKind.String, "1", "1", "содержимое показывается", "содержимое показывается"),
                    Option("содержимое показывается", "1", isRecommended: true),
                    Option("только контур окна", "0", isRecommended: false)
                ),
                Setting("workenv.display.low-level-hooks-timeout", "Display", "Таймаут системных хуков", "Снижает риск подвисания ввода из-за долгих обработчиков хуков клавиатуры и мыши в пользовательской оболочке.", "отклик", "реестр HKCU", RegistryHive.CurrentUser, desktop, "LowLevelHooksTimeout", RegistryValueKind.String, "1000", "5000", "таймаут ограничен", "стандартный таймаут"),

                Setting("workenv.explorer.hide-file-extensions", "Explorer", "Расширения файлов", "Показывает расширения известных типов файлов в Проводнике. Это помогает отличать документы от исполняемых файлов.", "файлы", "реестр HKCU", RegistryHive.CurrentUser, explorerAdvanced, "HideFileExt", RegistryValueKind.DWord, 0, 1, "расширения отображаются", "расширения скрыты"),
                Setting("workenv.explorer.sync-provider-notifications", "Explorer", "Предложения Проводника", "Отключает рекламные и информационные предложения Microsoft внутри Проводника.", "проводник", "реестр HKCU", RegistryHive.CurrentUser, explorerAdvanced, "ShowSyncProviderNotifications", RegistryValueKind.DWord, 0, 1, "предложения отключены", "предложения включены"),
                Setting("workenv.explorer.separate-process", "Explorer", "Отдельный процесс Проводника", "Запускает окна Проводника в отдельном процессе. При сбое одного окна оболочка обычно восстанавливается спокойнее.", "стабильность", "реестр HKCU", RegistryHive.CurrentUser, explorerAdvanced, "SeparateProcess", RegistryValueKind.DWord, 1, 0, "отдельный процесс включён", "общий процесс"),
                WithOptions(Setting("workenv.explorer.hidden-files", "Explorer", "Скрытые файлы", "Оставляет скрытые файлы скрытыми по умолчанию. Это безопасный стандарт для обычной работы.", "файлы", "реестр HKCU", RegistryHive.CurrentUser, explorerAdvanced, "Hidden", RegistryValueKind.DWord, 2, 2, "скрытые файлы не показываются", "скрытые файлы не показываются"),
                    Option("скрытые файлы не показываются", 2, isRecommended: true),
                    Option("скрытые файлы показываются", 1, isRecommended: false)
                ),
                WithOptions(Setting("workenv.explorer.show-status-bar", "Explorer", "Строка состояния", "Включает строку состояния Проводника, чтобы быстро видеть количество элементов и сведения о выделении.", "проводник", "реестр HKCU", RegistryHive.CurrentUser, explorerAdvanced, "ShowStatusBar", RegistryValueKind.DWord, 1, 1, "строка состояния включена", "строка состояния включена"),
                    Option("строка состояния включена", 1, isRecommended: true),
                    Option("строка состояния скрыта", 0, isRecommended: false)
                ),
                WithOptions(Setting("workenv.explorer.info-tips", "Explorer", "Подсказки файлов", "Оставляет информационные подсказки файлов и папок в Проводнике для быстрых сведений без открытия свойств.", "проводник", "реестр HKCU", RegistryHive.CurrentUser, explorerAdvanced, "ShowInfoTip", RegistryValueKind.DWord, 1, 1, "подсказки включены", "подсказки включены"),
                    Option("подсказки включены", 1, isRecommended: true),
                    Option("подсказки отключены", 0, isRecommended: false)
                ),
                WithOptions(Setting("workenv.explorer.checkboxes", "Explorer", "Флажки выбора элементов", "Отключает постоянные флажки выбора элементов, если они мешают плотной работе с файлами мышью и клавиатурой.", "файлы", "реестр HKCU", RegistryHive.CurrentUser, explorerAdvanced, "AutoCheckSelect", RegistryValueKind.DWord, 0, 0, "флажки скрыты", "флажки скрыты"),
                    Option("флажки скрыты", 0, isRecommended: true),
                    Option("флажки выбора включены", 1, isRecommended: false)
                ),

                WithOptions(Setting("workenv.explorer.show-super-hidden", "Explorer", "Системные защищённые файлы", "Скрывает защищённые системные файлы, чтобы случайно не повредить рабочую среду Windows.", "безопасность", "реестр HKCU", RegistryHive.CurrentUser, explorerAdvanced, "ShowSuperHidden", RegistryValueKind.DWord, 0, 0, "защищённые файлы скрыты", "защищённые файлы скрыты"),
                    Option("защищённые файлы скрыты", 0, isRecommended: true),
                    Option("защищённые файлы показываются", 1, isRecommended: false)
                ),
                WithOptions(Setting("workenv.explorer.launch-to-this-pc", "Explorer", "Проводник открывает Этот компьютер", "Открывает Проводник сразу в разделе Этот компьютер вместо Быстрого доступа.", "навигация", "реестр HKCU", RegistryHive.CurrentUser, explorerAdvanced, "LaunchTo", RegistryValueKind.DWord, 1, 2, "открывать Этот компьютер", "открывать Быстрый доступ"),
                    Option("открывать Этот компьютер", 1, isRecommended: true),
                    Option("открывать Быстрый доступ", 2, isRecommended: false),
                    Option("открывать Загрузки", 3, isRecommended: false)
                ),
                Setting("workenv.explorer.expand-current-folder", "Explorer", "Раскрывать текущую папку", "Автоматически раскрывает текущий путь в области навигации Проводника.", "навигация", "реестр HKCU", RegistryHive.CurrentUser, explorerAdvanced, "NavPaneExpandToCurrentFolder", RegistryValueKind.DWord, 1, 0, "текущая папка раскрывается", "авто-раскрытие выключено"),
                WithOptions(Setting("workenv.explorer.checkboxes", "Explorer", "Флажки выбора элементов", "Отключает лишние флажки выбора в списках Проводника, если они мешают обычной работе мышью.", "проводник", "реестр HKCU", RegistryHive.CurrentUser, explorerAdvanced, "AutoCheckSelect", RegistryValueKind.DWord, 0, 0, "флажки выбора отключены", "флажки выбора отключены"),
                    Option("флажки скрыты", 0, isRecommended: true),
                    Option("флажки выбора включены", 1, isRecommended: false)
                ),
                Setting("workenv.explorer.compact-mode", "Explorer", "Компактный режим Проводника", "Включает компактные отступы Проводника для более плотного отображения файлов.", "проводник", "реестр HKCU", RegistryHive.CurrentUser, explorerAdvanced, "UseCompactMode", RegistryValueKind.DWord, 1, 0, "компактный режим включён", "обычные отступы"),

                Setting("workenv.start.recommendations", "Start", "Рекомендации меню Пуск", "Отключает блок рекомендаций и рекламных подсказок в меню Пуск.", "меню Пуск", "реестр HKCU", RegistryHive.CurrentUser, explorerAdvanced, "Start_IrisRecommendations", RegistryValueKind.DWord, 0, 1, "рекомендации Пуска отключены", "рекомендации Пуска включены"),
                Setting("workenv.start.track-docs-enabled", "Start", "Недавние элементы", "Отключает показ недавно открытых файлов в Пуске, списках переходов и Проводнике.", "приватность", "реестр HKCU", RegistryHive.CurrentUser, explorerAdvanced, "Start_TrackDocs", RegistryValueKind.DWord, 0, 1, "недавние элементы скрыты", "недавние элементы включены"),
                WithOptions(Setting("workenv.start.more-pins", "Start", "Компоновка Пуска", "Использует компактный вариант меню Пуск без расширенного блока рекомендаций.", "меню Пуск", "реестр HKCU", RegistryHive.CurrentUser, explorerAdvanced, "Start_Layout", RegistryValueKind.DWord, 1, 0, "больше закреплений", "обычная компоновка"),
                    Option("больше закреплений", 1, isRecommended: true),
                    Option("стандартная компоновка", 0, isRecommended: false),
                    Option("больше рекомендаций", 2, isRecommended: false)
                ),
                Setting("workenv.start.track-programs", "Start", "История запуска программ", "Отключает учёт часто используемых программ для персонализации меню Пуск.", "приватность", "реестр HKCU", RegistryHive.CurrentUser, explorerAdvanced, "Start_TrackProgs", RegistryValueKind.DWord, 0, 1, "история программ отключена", "история программ включена"),
                Setting("workenv.start.recent-apps", "Start", "Недавно добавленные приложения", "Скрывает блок недавно добавленных приложений в меню Пуск.", "меню Пуск", "реестр HKCU", RegistryHive.CurrentUser, explorerAdvanced, "HideRecentlyAddedApps", RegistryValueKind.DWord, 1, 0, "новые приложения скрыты", "новые приложения показываются"),
                Setting("workenv.start.app-suggestions", "Start", "Предложения приложений", "Отключает предложения приложений и рекламные подсказки в меню Пуск через пользовательскую политику Explorer.", "пуск", "политика HKCU", RegistryHive.CurrentUser, policiesExplorer, "NoNewAppAlert", RegistryValueKind.DWord, 1, 0, "предложения отключены", "предложения разрешены"),
                Setting("workenv.start.disable-spotlight", "Start", "Потребительские подсказки Windows", "Отключает часть потребительских подсказок и рекламных предложений Windows для более чистого рабочего сценария.", "пуск", "политика HKCU", RegistryHive.CurrentUser, @"Software\Policies\Microsoft\Windows\CloudContent", "DisableWindowsConsumerFeatures", RegistryValueKind.DWord, 1, 0, "подсказки отключены", "подсказки разрешены"),

                Setting("workenv.start.account-notifications", "Start", "Уведомления аккаунта в Пуске", "Отключает подсказки и уведомления аккаунта Microsoft в меню Пуск.", "меню Пуск", "реестр HKCU", RegistryHive.CurrentUser, explorerAdvanced, "Start_AccountNotifications", RegistryValueKind.DWord, 0, 1, "уведомления аккаунта отключены", "уведомления аккаунта включены"),

                Setting("workenv.taskbar.widgets-enabled", "Taskbar", "Виджеты на панели задач", "Скрывает системную кнопку виджетов на панели задач.", "панель задач", "реестр HKCU", RegistryHive.CurrentUser, explorerAdvanced, "TaskbarDa", RegistryValueKind.DWord, 0, 1, "виджеты скрыты", "виджеты включены"),
                Setting("workenv.taskbar.chat-enabled", "Taskbar", "Кнопка чата", "Скрывает кнопку чата или Teams на панели задач, если она не используется.", "панель задач", "реестр HKCU", RegistryHive.CurrentUser, explorerAdvanced, "TaskbarMn", RegistryValueKind.DWord, 0, 1, "кнопка чата скрыта", "кнопка чата включена"),
                WithOptions(Setting("workenv.taskbar.search-mode", "Taskbar", "Вид поиска на панели задач", "Оставляет компактную кнопку поиска вместо широкой строки на панели задач.", "панель задач", "реестр HKCU", RegistryHive.CurrentUser, search, "SearchboxTaskbarMode", RegistryValueKind.DWord, 1, 1, "компактный поиск", "компактный поиск"),
                    Option("поиск скрыт", 0, isRecommended: false),
                    Option("кнопка поиска", 1, isRecommended: true),
                    Option("строка поиска", 2, isRecommended: false)
                ),
                Setting("workenv.taskbar.left-align", "Taskbar", "Выравнивание панели задач", "Использует левое выравнивание значков панели задач, чтобы элементы были ближе к классическому расположению Windows.", "панель задач", "реестр HKCU", RegistryHive.CurrentUser, explorerAdvanced, "TaskbarAl", RegistryValueKind.DWord, 0, 1, "значки слева", "значки по центру"),
                Setting("workenv.taskbar.badges", "Taskbar", "Значки уведомлений приложений", "Отключает бейджи приложений на панели задач, если они отвлекают от работы.", "панель задач", "реестр HKCU", RegistryHive.CurrentUser, explorerAdvanced, "TaskbarBadges", RegistryValueKind.DWord, 0, 1, "бейджи скрыты", "бейджи показываются"),
                Setting("workenv.taskbar.task-view", "Taskbar", "Кнопка представления задач", "Скрывает отдельную кнопку представления задач на панели, оставляя доступ через Win+Tab.", "панель задач", "реестр HKCU", RegistryHive.CurrentUser, explorerAdvanced, "ShowTaskViewButton", RegistryValueKind.DWord, 0, 1, "кнопка скрыта", "кнопка включена"),
                WithOptions(Setting("workenv.taskbar.small-icons", "Taskbar", "Маленькие значки панели", "Включает компактный размер значков панели задач там, где параметр поддерживается системой.", "панель задач", "реестр HKCU", RegistryHive.CurrentUser, explorerAdvanced, "TaskbarSmallIcons", RegistryValueKind.DWord, 1, 0, "маленькие значки включены", "обычный размер"),
                    Option("маленькие значки", 1, isRecommended: true),
                    Option("обычные значки", 0, isRecommended: false)
                ),
                WithOptions(Setting("workenv.taskbar.combine-buttons", "Taskbar", "Группировка кнопок", "Оставляет группировку кнопок панели задач включённой, чтобы панель не переполнялась при большом числе окон.", "панель задач", "реестр HKCU", RegistryHive.CurrentUser, explorerAdvanced, "TaskbarGlomLevel", RegistryValueKind.DWord, 0, 0, "кнопки группируются", "кнопки группируются"),
                    Option("всегда группировать", 0, isRecommended: true),
                    Option("группировать при заполнении", 1, isRecommended: false),
                    Option("никогда не группировать", 2, isRecommended: false)
                ),

                Setting("workenv.taskbar.people", "Taskbar", "Люди на панели задач", "Отключает устаревший блок Люди на панели задач, если параметр присутствует в системе.", "панель задач", "реестр HKCU", RegistryHive.CurrentUser, peopleBand, "PeopleBand", RegistryValueKind.DWord, 0, 1, "блок Люди скрыт", "блок Люди включён"),

                Setting("workenv.search.web-policy", "Search", "Веб-подсказки поиска", "Отключает веб-подсказки в поиске Windows через пользовательскую политику Explorer.", "поиск", "политика HKCU", RegistryHive.CurrentUser, policiesExplorer, "DisableSearchBoxSuggestions", RegistryValueKind.DWord, 1, 0, "веб-подсказки отключены", "веб-подсказки разрешены"),
                Setting("workenv.search.bing-search", "Search", "Bing в поиске Windows", "Отключает смешивание локального поиска с Bing-результатами.", "поиск", "реестр HKCU", RegistryHive.CurrentUser, search, "BingSearchEnabled", RegistryValueKind.DWord, 0, 1, "Bing отключён", "Bing разрешён"),
                Setting("workenv.search.cortana-consent", "Search", "Согласие Cortana/Search", "Сбрасывает пользовательское согласие на онлайн-компоненты поиска.", "поиск", "реестр HKCU", RegistryHive.CurrentUser, search, "CortanaConsent", RegistryValueKind.DWord, 0, 1, "онлайн-компонент отключён", "онлайн-компонент разрешён"),
                Setting("workenv.search.location", "Search", "Геопозиция для поиска", "Запрещает системному поиску использовать геопозицию для подсказок.", "поиск", "реестр HKCU", RegistryHive.CurrentUser, search, "AllowSearchToUseLocation", RegistryValueKind.DWord, 0, 1, "геопозиция не используется", "геопозиция разрешена"),
                Setting("workenv.search.connected-web", "Search", "Подключённый веб-поиск", "Отключает дополнительный веб-поиск Windows Search для локальных запросов.", "поиск", "реестр HKCU", RegistryHive.CurrentUser, search, "ConnectedSearchUseWeb", RegistryValueKind.DWord, 0, 1, "веб-поиск отключён", "веб-поиск включён"),
                Setting("workenv.search.web-metered", "Search", "Веб-поиск через лимитное подключение", "Запрещает Windows Search выполнять веб-запросы через лимитные подключения.", "поиск", "реестр HKCU", RegistryHive.CurrentUser, search, "ConnectedSearchUseWebOverMeteredConnections", RegistryValueKind.DWord, 0, 1, "веб через лимит отключён", "веб через лимит включён"),
                Setting("workenv.search.device-history", "Search", "История поиска на устройстве", "Отключает локальную историю поиска на устройстве, если пользователь хочет более приватную рабочую среду.", "приватность", "реестр HKCU", RegistryHive.CurrentUser, search, "IsDeviceSearchHistoryEnabled", RegistryValueKind.DWord, 0, 1, "история поиска отключена", "история поиска включена"),
                Setting("workenv.search.msa-cloud", "Search", "Облачный поиск Microsoft", "Отключает поиск по личному Microsoft-аккаунту из строки поиска Windows.", "приватность", "реестр HKCU", RegistryHive.CurrentUser, search, "IsMSACloudSearchEnabled", RegistryValueKind.DWord, 0, 1, "MSA-облако отключено", "MSA-облако включено"),
                Setting("workenv.search.aad-cloud", "Search", "Облачный поиск организации", "Отключает поиск по рабочей или учебной учётной записи из строки поиска Windows.", "приватность", "реестр HKCU", RegistryHive.CurrentUser, search, "IsAADCloudSearchEnabled", RegistryValueKind.DWord, 0, 1, "AAD-облако отключено", "AAD-облако включено"),

                WithOptions(Setting("workenv.notifications.toast-disabled", "Tray", "Системные уведомления", "Включает toast-уведомления Windows, чтобы важные события не терялись.", "уведомления", "реестр HKCU", RegistryHive.CurrentUser, pushNotifications, "ToastEnabled", RegistryValueKind.DWord, 1, 1, "уведомления включены", "уведомления включены"),
                    Option("уведомления включены", 1, isRecommended: true),
                    Option("уведомления отключены", 0, isRecommended: false)
                ),
                WithOptions(Setting("workenv.notifications.global-toasts", "Tray", "Глобальные уведомления", "Проверяет общий переключатель уведомлений в центре уведомлений Windows.", "уведомления", "реестр HKCU", RegistryHive.CurrentUser, notificationSettings, "NOC_GLOBAL_SETTING_TOASTS_ENABLED", RegistryValueKind.DWord, 1, 1, "глобальные уведомления включены", "глобальные уведомления включены"),
                    Option("глобальные уведомления включены", 1, isRecommended: true),
                    Option("глобальные уведомления отключены", 0, isRecommended: false)
                ),
                WithOptions(Setting("workenv.notifications.lock-screen", "Tray", "Уведомления на экране блокировки", "Отключает обычные уведомления поверх экрана блокировки, чтобы не показывать лишние данные до входа в систему.", "уведомления", "реестр HKCU", RegistryHive.CurrentUser, notificationSettings, "NOC_GLOBAL_SETTING_ALLOW_TOASTS_ABOVE_LOCK", RegistryValueKind.DWord, 0, 1, "на экране блокировки скрыты", "на экране блокировки разрешены"),
                    Option("на блокировке скрыты", 0, isRecommended: true),
                    Option("на блокировке разрешены", 1, isRecommended: false)
                ),
                WithOptions(Setting("workenv.notifications.critical-lock-screen", "Tray", "Критические уведомления на блокировке", "Оставляет критические уведомления доступными на экране блокировки.", "уведомления", "реестр HKCU", RegistryHive.CurrentUser, notificationSettings, "NOC_GLOBAL_SETTING_ALLOW_CRITICAL_TOASTS_ABOVE_LOCK", RegistryValueKind.DWord, 1, 1, "критические уведомления включены", "критические уведомления включены"),
                    Option("критические уведомления включены", 1, isRecommended: true),
                    Option("критические уведомления скрыты", 0, isRecommended: false)
                ),
                WithOptions(Setting("workenv.notifications.suggested-content", "Tray", "Предлагаемое содержимое уведомлений", "Отключает дополнительные советы и предлагаемое содержимое в уведомлениях Windows, если параметр поддерживается системой.", "уведомления", "реестр HKCU", RegistryHive.CurrentUser, notificationSettings, "NOC_GLOBAL_SETTING_ALLOW_NOTIFICATION_SOUND", RegistryValueKind.DWord, 0, 1, "звуки уведомлений выключены", "звуки уведомлений включены"),
                    Option("звуки уведомлений выключены", 0, isRecommended: true),
                    Option("звуки уведомлений включены", 1, isRecommended: false)
                ),

                WithOptions(Setting("workenv.notifications.center-policy", "Tray", "Центр уведомлений", "Проверяет пользовательскую политику центра уведомлений Windows.", "политика", "политика HKCU", RegistryHive.CurrentUser, policiesExplorer, "DisableNotificationCenter", RegistryValueKind.DWord, 0, 0, "центр уведомлений доступен", "центр уведомлений доступен"),
                    Option("центр уведомлений доступен", 0, isRecommended: true),
                    Option("центр уведомлений отключён политикой", 1, isRecommended: false)
                ),

                WithOptions(Setting("workenv.windows.snap", "Windows", "Привязка окон", "Включает системную привязку окон при перетаскивании к краю экрана.", "окна", "реестр HKCU", RegistryHive.CurrentUser, desktop, "WindowArrangementActive", RegistryValueKind.String, "1", "1", "привязка окон включена", "привязка окон включена"),
                    Option("привязка окон включена", "1", isRecommended: true),
                    Option("привязка окон отключена", "0", isRecommended: false)
                ),
                WithOptions(Setting("workenv.windows.snap-flyout", "Windows", "Подсказки привязки окон", "Включает панель Snap Layouts при работе с окнами.", "окна", "реестр HKCU", RegistryHive.CurrentUser, explorerAdvanced, "EnableSnapAssistFlyout", RegistryValueKind.DWord, 1, 1, "подсказки привязки включены", "подсказки привязки включены"),
                    Option("подсказки привязки включены", 1, isRecommended: true),
                    Option("подсказки привязки отключены", 0, isRecommended: false)
                ),
                WithOptions(Setting("workenv.windows.alt-tab-edge", "Windows", "Вкладки Edge в Alt+Tab", "Оставляет Alt+Tab в режиме окон, без добавления большого количества вкладок браузера.", "задачи", "реестр HKCU", RegistryHive.CurrentUser, explorerAdvanced, "MultiTaskingAltTabFilter", RegistryValueKind.DWord, 3, 0, "Alt+Tab показывает только окна", "Alt+Tab с окнами и вкладками"),
                    Option("только окна", 3, isRecommended: true),
                    Option("окна и 3 последние вкладки", 2, isRecommended: false),
                    Option("окна и 5 последних вкладок", 1, isRecommended: false),
                    Option("окна и все вкладки", 0, isRecommended: false)
                ),
                WithOptions(Setting("workenv.windows.snap-assist", "Windows", "Предложения Snap Assist", "Включает подсказки выбора соседнего окна после привязки текущего окна.", "окна", "реестр HKCU", RegistryHive.CurrentUser, explorerAdvanced, "SnapAssist", RegistryValueKind.DWord, 1, 1, "Snap Assist включён", "Snap Assist включён"),
                    Option("Snap Assist включён", 1, isRecommended: true),
                    Option("Snap Assist отключён", 0, isRecommended: false)
                ),
                WithOptions(Setting("workenv.windows.snap-fill", "Windows", "Автозаполнение привязки", "Оставляет автоматическое заполнение свободной области при изменении размера привязанных окон.", "окна", "реестр HKCU", RegistryHive.CurrentUser, explorerAdvanced, "SnapFill", RegistryValueKind.DWord, 1, 1, "автозаполнение включено", "автозаполнение включено"),
                    Option("автозаполнение включено", 1, isRecommended: true),
                    Option("автозаполнение отключено", 0, isRecommended: false)
                ),
                WithOptions(Setting("workenv.windows.joint-resize", "Windows", "Совместное изменение размера", "Включает одновременное изменение размеров соседних привязанных окон.", "окна", "реестр HKCU", RegistryHive.CurrentUser, explorerAdvanced, "JointResize", RegistryValueKind.DWord, 1, 1, "совместное изменение включено", "совместное изменение включено"),
                    Option("совместное изменение включено", 1, isRecommended: true),
                    Option("совместное изменение отключено", 0, isRecommended: false)
                ),
                WithOptions(Setting("workenv.windows.snap-bar", "Windows", "Панель привязки сверху", "Включает верхнюю панель Snap Bar, если она поддерживается установленной версией Windows.", "окна", "реестр HKCU", RegistryHive.CurrentUser, explorerAdvanced, "EnableSnapBar", RegistryValueKind.DWord, 1, 1, "Snap Bar включён", "Snap Bar включён"),
                    Option("Snap Bar включён", 1, isRecommended: true),
                    Option("Snap Bar отключён", 0, isRecommended: false)
                ),
                WithOptions(Setting("workenv.windows.aero-peek", "Windows", "Aero Peek", "Включает предварительный просмотр рабочего стола и окон, если механизм поддерживается текущей оболочкой Windows.", "окна", "реестр HKCU", RegistryHive.CurrentUser, explorerAdvanced, "DisablePreviewDesktop", RegistryValueKind.DWord, 0, 0, "Aero Peek доступен", "Aero Peek доступен"),
                    Option("Aero Peek доступен", 0, isRecommended: true),
                    Option("Aero Peek отключён", 1, isRecommended: false)
                ),
                WithOptions(Setting("workenv.windows.aero-shake", "Windows", "Встряхивание окна", "Оставляет доступным Aero Shake для быстрого сворачивания остальных окон движением активного окна.", "окна", "политика HKCU", RegistryHive.CurrentUser, policiesExplorer, "NoWindowMinimizingShortcuts", RegistryValueKind.DWord, 0, 0, "встряхивание доступно", "встряхивание доступно"),
                    Option("встряхивание доступно", 0, isRecommended: true),
                    Option("встряхивание отключено", 1, isRecommended: false)
                ),

                WithOptions(Setting("workenv.windows.virtual-desktop-alt-tab", "Windows", "Alt+Tab текущего рабочего стола", "Ограничивает Alt+Tab окнами текущего виртуального рабочего стола.", "задачи", "реестр HKCU", RegistryHive.CurrentUser, explorerAdvanced, "VirtualDesktopAltTabFilter", RegistryValueKind.DWord, 0, 0, "только текущий рабочий стол", "только текущий рабочий стол"),
                    Option("только текущий рабочий стол", 0, isRecommended: true),
                    Option("все рабочие столы", 1, isRecommended: false)
                )
            };
        }

        private static EnvironmentRegistrySettingDefinition Setting(
            string id,
            string nodeKey,
            string title,
            string description,
            string scope,
            string source,
            RegistryHive hive,
            string path,
            string valueName,
            RegistryValueKind valueKind,
            object recommendedValue,
            object defaultValue,
            string recommendedLabel,
            string defaultLabel,
            string riskText = "низкий риск")
        {
            var definition = new EnvironmentRegistrySettingDefinition
            {
                Id = id,
                NodeKey = nodeKey,
                Title = title,
                Description = description,
                Scope = scope,
                Source = source,
                Hive = hive,
                SubKeyPath = path,
                ValueName = valueName,
                ValueKind = valueKind,
                RecommendedValue = recommendedValue,
                DefaultValue = defaultValue,
                RecommendedLabel = recommendedLabel,
                DefaultLabel = defaultLabel,
                RiskText = riskText
            };

            definition.Options.Add(new EnvironmentRegistrySettingOption(recommendedLabel, recommendedValue, isRecommended: true));
            if (!ValuesEqual(recommendedValue, defaultValue, valueKind) ||
                !string.Equals(recommendedLabel, defaultLabel, StringComparison.OrdinalIgnoreCase))
            {
                definition.Options.Add(new EnvironmentRegistrySettingOption(defaultLabel, defaultValue, isRecommended: false));
            }
            else
            {
                AddBinaryAlternativeOption(definition);
            }

            return definition;
        }

        private static EnvironmentRegistrySettingOption Option(string label, object value, bool isRecommended = false)
        {
            return new EnvironmentRegistrySettingOption(label, value, isRecommended);
        }

        private static EnvironmentRegistrySettingDefinition WithOptions(
            EnvironmentRegistrySettingDefinition definition,
            params EnvironmentRegistrySettingOption[] options)
        {
            if (definition == null)
                return null;

            definition.Options.Clear();
            foreach (var option in options ?? Array.Empty<EnvironmentRegistrySettingOption>())
            {
                if (option != null)
                    AddUniqueOption(definition, option);
            }

            if (definition.Options.Count == 0)
                definition.Options.Add(new EnvironmentRegistrySettingOption(definition.RecommendedLabel, definition.RecommendedValue, isRecommended: true));

            return definition;
        }

        private static void AddBinaryAlternativeOption(EnvironmentRegistrySettingDefinition definition)
        {
            if (definition == null || !TryGetBinaryAlternativeValue(definition.RecommendedValue, definition.ValueKind, out object alternativeValue))
                return;

            string label = BuildAlternativeOptionLabel(definition, alternativeValue);
            AddUniqueOption(definition, new EnvironmentRegistrySettingOption(label, alternativeValue, isRecommended: false));
        }

        private static bool TryGetBinaryAlternativeValue(object value, RegistryValueKind valueKind, out object alternativeValue)
        {
            alternativeValue = null;
            try
            {
                if (valueKind == RegistryValueKind.String || valueKind == RegistryValueKind.ExpandString)
                {
                    string text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
                    if (string.Equals(text, "0", StringComparison.OrdinalIgnoreCase))
                    {
                        alternativeValue = "1";
                        return true;
                    }

                    if (string.Equals(text, "1", StringComparison.OrdinalIgnoreCase))
                    {
                        alternativeValue = "0";
                        return true;
                    }

                    return false;
                }

                int numeric = Convert.ToInt32(value, CultureInfo.InvariantCulture);
                if (numeric == 0 || numeric == 1)
                {
                    alternativeValue = numeric == 0 ? 1 : 0;
                    return true;
                }
            }
            catch
            {
            }

            return false;
        }

        private static string BuildAlternativeOptionLabel(EnvironmentRegistrySettingDefinition definition, object alternativeValue)
        {
            string valueText = FormatRegistryValue(alternativeValue);
            return $"Альтернатива: значение {valueText}";
        }

        private static void AddUniqueOption(EnvironmentRegistrySettingDefinition definition, EnvironmentRegistrySettingOption option)
        {
            if (definition.Options.Any(existing => ValuesEqual(existing.Value, option.Value, definition.ValueKind)))
                return;

            definition.Options.Add(option);
        }

        private static bool TryReadEnvironmentRegistryValue(EnvironmentRegistrySettingDefinition setting, out object value)
        {
            value = null;

            try
            {
                using var key = OpenRegistryHive(setting.Hive).OpenSubKey(setting.SubKeyPath, writable: false);
                if (key == null || !RegistryValueExists(key, setting.ValueName))
                    return false;

                value = key.GetValue(setting.ValueName);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string WriteEnvironmentRegistryValue(EnvironmentRegistrySettingDefinition setting, object targetValue, string targetLabel)
        {
            object normalizedTarget = NormalizeRegistryValue(targetValue, setting.ValueKind);
            bool currentExists = TryReadEnvironmentRegistryValue(setting, out object currentValue);
            object effectiveCurrent = currentExists ? currentValue : setting.DefaultValue;
            string label = string.IsNullOrWhiteSpace(targetLabel) ? FormatRegistryValue(normalizedTarget) : targetLabel;

            if (currentExists && ValuesEqual(currentValue, normalizedTarget, setting.ValueKind))
                return $"Значение уже установлено: {label}.";

            bool backupSaved = SaveEnvironmentBackup(setting, currentExists, effectiveCurrent);

            using var key = OpenRegistryHive(setting.Hive).CreateSubKey(setting.SubKeyPath, writable: true)
                ?? throw new InvalidOperationException("Не удалось открыть раздел реестра для записи.");

            key.SetValue(setting.ValueName, normalizedTarget, setting.ValueKind);

            string backupText = backupSaved
                ? " Точка отката сохранена."
                : HasEnvironmentBackup(setting.Id)
                    ? " Ранее созданная точка отката сохранена без перезаписи."
                    : " Внимание: точку отката сохранить не удалось.";
            return $"Установлено: {label}.{backupText}";
        }

        private static string RollbackEnvironmentRegistryValue(EnvironmentRegistrySettingDefinition setting)
        {
            var backup = GetEnvironmentBackup(setting.Id);
            if (backup == null)
                return "Для этого параметра нет сохранённой точки отката.";

            using var key = OpenRegistryHive(setting.Hive).CreateSubKey(setting.SubKeyPath, writable: true)
                ?? throw new InvalidOperationException("Не удалось открыть раздел реестра для восстановления.");

            if (backup.ValueExisted)
            {
                object value = DeserializeBackupValue(backup.SerializedValue, setting.ValueKind);
                key.SetValue(setting.ValueName, NormalizeRegistryValue(value, setting.ValueKind), setting.ValueKind);
            }
            else
            {
                key.DeleteValue(setting.ValueName, throwOnMissingValue: false);
            }

            RemoveEnvironmentBackup(setting.Id);
            return backup.ValueExisted
                ? $"Восстановлено прежнее значение: {FormatRegistryValue(DeserializeBackupValue(backup.SerializedValue, setting.ValueKind))}."
                : "Параметр был удалён, потому что до применения его не было в реестре.";
        }

        private static RegistryKey OpenRegistryHive(RegistryHive hive)
        {
            return hive switch
            {
                RegistryHive.ClassesRoot => Registry.ClassesRoot,
                RegistryHive.CurrentConfig => Registry.CurrentConfig,
                RegistryHive.CurrentUser => Registry.CurrentUser,
                RegistryHive.LocalMachine => Registry.LocalMachine,
                RegistryHive.Users => Registry.Users,
                _ => Registry.CurrentUser
            };
        }

        private static bool RegistryValueExists(RegistryKey key, string valueName)
        {
            if (key == null)
                return false;

            return key
                .GetValueNames()
                .Any(name => string.Equals(name, valueName ?? string.Empty, StringComparison.OrdinalIgnoreCase));
        }

        private static object NormalizeRegistryValue(object value, RegistryValueKind kind)
        {
            return kind switch
            {
                RegistryValueKind.DWord => Convert.ToInt32(value),
                RegistryValueKind.QWord => Convert.ToInt64(value),
                RegistryValueKind.String or RegistryValueKind.ExpandString => Convert.ToString(value) ?? string.Empty,
                _ => value
            };
        }

        private static bool ValuesEqual(object left, object right, RegistryValueKind kind)
        {
            try
            {
                object normalizedLeft = NormalizeRegistryValue(left, kind);
                object normalizedRight = NormalizeRegistryValue(right, kind);

                return kind switch
                {
                    RegistryValueKind.String or RegistryValueKind.ExpandString =>
                        string.Equals(Convert.ToString(normalizedLeft), Convert.ToString(normalizedRight), StringComparison.OrdinalIgnoreCase),
                    _ => Equals(normalizedLeft, normalizedRight)
                };
            }
            catch
            {
                return false;
            }
        }

        private static string FormatRegistryValue(object value)
        {
            return value switch
            {
                null => "не задано",
                string text => text,
                _ => Convert.ToString(value) ?? "не задано"
            };
        }

        private static bool HasEnvironmentBackup(string settingId)
        {
            return GetEnvironmentBackup(settingId) != null;
        }

        private static EnvironmentSettingBackupRecord GetEnvironmentBackup(string settingId)
        {
            if (string.IsNullOrWhiteSpace(settingId))
                return null;

            return LoadEnvironmentBackups()
                .FirstOrDefault(item => string.Equals(item.SettingId, settingId, StringComparison.OrdinalIgnoreCase));
        }

        private static bool SaveEnvironmentBackup(EnvironmentRegistrySettingDefinition setting, bool valueExisted, object oldValue)
        {
            var backups = LoadEnvironmentBackups();
            if (backups.Any(item => string.Equals(item.SettingId, setting.Id, StringComparison.OrdinalIgnoreCase)))
            {
                SaveEnvironmentBackups(backups);
                return false;
            }

            backups.Insert(0, new EnvironmentSettingBackupRecord
            {
                SettingId = setting.Id,
                Title = setting.Title,
                Hive = setting.Hive.ToString(),
                SubKeyPath = setting.SubKeyPath,
                ValueName = setting.ValueName,
                ValueKind = setting.ValueKind.ToString(),
                ValueExisted = valueExisted,
                SerializedValue = valueExisted ? SerializeBackupValue(oldValue, setting.ValueKind) : string.Empty,
                CreatedAtUtc = DateTime.UtcNow
            });

            SaveEnvironmentBackups(backups);
            return GetEnvironmentBackup(setting.Id) != null;
        }

        private static void RemoveEnvironmentBackup(string settingId)
        {
            var backups = LoadEnvironmentBackups()
                .Where(item => !string.Equals(item.SettingId, settingId, StringComparison.OrdinalIgnoreCase))
                .ToList();

            SaveEnvironmentBackups(backups);
        }

        private static void PruneEnvironmentBackups()
        {
            var backups = LoadEnvironmentBackups();
            if (backups.Count > 0)
                SaveEnvironmentBackups(backups);
        }

        private static List<EnvironmentSettingBackupRecord> LoadEnvironmentBackups()
        {
            try
            {
                string path = GetEnvironmentBackupPath();
                if (!File.Exists(path))
                    return new List<EnvironmentSettingBackupRecord>();

                string json = File.ReadAllText(path);
                var backups = JsonSerializer.Deserialize<List<EnvironmentSettingBackupRecord>>(json);
                return CompactEnvironmentBackups(backups).ToList();
            }
            catch
            {
                return new List<EnvironmentSettingBackupRecord>();
            }
        }

        private static void SaveEnvironmentBackups(IReadOnlyList<EnvironmentSettingBackupRecord> backups)
        {
            try
            {
                string path = GetEnvironmentBackupPath();
                string dir = System.IO.Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(dir))
                    Directory.CreateDirectory(dir);

                var compact = CompactEnvironmentBackups(backups).ToList();
                string json = JsonSerializer.Serialize(compact, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(path, json);
            }
            catch
            {
            }
        }

        private static IReadOnlyList<EnvironmentSettingBackupRecord> CompactEnvironmentBackups(IReadOnlyList<EnvironmentSettingBackupRecord> backups)
        {
            DateTime cutoffUtc = DateTime.UtcNow - TimeSpan.FromDays(30);
            return (backups ?? Array.Empty<EnvironmentSettingBackupRecord>())
                .Where(item => item != null)
                .Where(item => !string.IsNullOrWhiteSpace(item.SettingId))
                .Where(item => item.CreatedAtUtc == default || item.CreatedAtUtc >= cutoffUtc)
                .GroupBy(item => item.SettingId, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.OrderByDescending(item => item.CreatedAtUtc).First())
                .OrderByDescending(item => item.CreatedAtUtc)
                .Take(MaxBackupRecords)
                .ToList();
        }

        private static string GetEnvironmentBackupPath()
        {
            return System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TweakWise",
                BackupFileName);
        }

        private static string SerializeBackupValue(object value, RegistryValueKind kind)
        {
            if (value == null)
                return string.Empty;

            return kind switch
            {
                RegistryValueKind.DWord => Convert.ToInt32(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
                RegistryValueKind.QWord => Convert.ToInt64(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
                RegistryValueKind.String or RegistryValueKind.ExpandString => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
                _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty
            };
        }

        private static object DeserializeBackupValue(string serializedValue, RegistryValueKind kind)
        {
            return kind switch
            {
                RegistryValueKind.DWord => int.TryParse(serializedValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out int dwordValue) ? dwordValue : 0,
                RegistryValueKind.QWord => long.TryParse(serializedValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out long qwordValue) ? qwordValue : 0L,
                RegistryValueKind.String or RegistryValueKind.ExpandString => serializedValue ?? string.Empty,
                _ => serializedValue
            };
        }

        private static List<EnvironmentFindingViewModel> BuildEnvironmentFindings(ModuleHealthStatus status)
        {
            var findings = status?.Findings ?? new List<ModuleHealthFinding>();

            return findings
                .Where(finding => finding != null)
                .Select(finding =>
                {
                    string description = string.IsNullOrWhiteSpace(finding.ActionText)
                        ? finding.Description
                        : string.IsNullOrWhiteSpace(finding.Description)
                            ? finding.ActionText
                            : $"{finding.Description} {finding.ActionText}";

                    return new EnvironmentFindingViewModel
                    {
                        Id = string.IsNullOrWhiteSpace(finding.Id) ? $"{finding.ModuleId}.{finding.Title}" : finding.Id,
                        NodeKey = ResolveFindingNodeKey(finding),
                        Level = NormalizeFindingLevel(finding.Level),
                        KindText = GetFindingKindText(finding.Level),
                        Title = finding.Title,
                        Description = description
                    };
                })
                .GroupBy(finding => $"{finding.NodeKey}|{NormalizeIdPart(finding.Id)}", StringComparer.OrdinalIgnoreCase)
                .Select(group => group
                    .OrderByDescending(item => GetSeverity(item.Level))
                    .ThenByDescending(item => item.Description?.Length ?? 0)
                    .First())
                .OrderByDescending(finding => GetSeverity(finding.Level))
                .ThenBy(finding => GetNodeDisplayOrder(finding.NodeKey))
                .ThenBy(finding => finding.Title)
                .ToList();
        }

        private static string ResolveFindingNodeKey(ModuleHealthFinding finding)
        {
            string text = $"{finding?.Id} {finding?.Title} {finding?.Description} {finding?.ActionText}".ToLowerInvariant();

            if (text.Contains("explorer") || text.Contains("проводник") || text.Contains("расширени") || text.Contains("файл"))
                return "Explorer";

            if (text.Contains("start") || text.Contains("пуск"))
                return "Start";

            if (text.Contains("taskbar") || text.Contains("панел") || text.Contains("виджет") || text.Contains("чат"))
                return "Taskbar";

            if (text.Contains("search") || text.Contains("поиск") || text.Contains("веб-подсказ") || text.Contains("bing"))
                return "Search";

            if (text.Contains("notification") || text.Contains("toast") || text.Contains("уведом") || text.Contains("трей"))
                return "Tray";

            if (text.Contains("window") || text.Contains("диспетчер") || text.Contains("окн") || text.Contains("процесс"))
                return "Windows";

            return "Display";
        }

        private static HealthLevel NormalizeFindingLevel(HealthLevel level)
        {
            return level == HealthLevel.Checking || level == HealthLevel.Unknown
                ? HealthLevel.Normal
                : level;
        }

        private static bool IsProblemLevel(HealthLevel level)
        {
            return level == HealthLevel.Attention || level == HealthLevel.Warning || level == HealthLevel.Critical;
        }

        private static int GetSeverity(HealthLevel level)
        {
            return level switch
            {
                HealthLevel.Critical => 6,
                HealthLevel.Warning => 5,
                HealthLevel.Attention => 4,
                HealthLevel.Normal => 3,
                HealthLevel.Good => 2,
                HealthLevel.Checking => 1,
                _ => 0
            };
        }

        private List<EnvironmentCalloutLayout> BuildCalloutLayouts(IReadOnlyList<EnvironmentCalloutSeed> groups)
        {
            const double leftX = 6;
            const double centerX = 507;
            const double rightX = 1038;
            const double top = 82;
            const double bottom = 644;
            const double gap = 18;
            const double width = 224;

            var layouts = new List<EnvironmentCalloutLayout>();
            var left = groups.Where(item => item.Side == EnvironmentCalloutSide.Left).ToList();
            var right = groups.Where(item => item.Side == EnvironmentCalloutSide.Right).ToList();
            var center = groups.Where(item => item.Side == EnvironmentCalloutSide.Center).ToList();

            layouts.AddRange(LayoutCalloutColumn(left, leftX, top, bottom, width, gap, new Vector(-18, 0)));
            layouts.AddRange(LayoutCalloutColumn(right, rightX, top, bottom, width, gap, new Vector(18, 0)));

            foreach (var item in center.OrderBy(entry => entry.PreferredY))
            {
                double y = Math.Max(top - 18, Math.Min(item.PreferredY, 138));
                layouts.Add(new EnvironmentCalloutLayout(
                    item.NodeKey,
                    item.Finding,
                    item.ExtraCount,
                    new WindowsPoint(centerX, y),
                    width,
                    item.EstimatedHeight,
                    GetNodeCenterOnBoard(item.NodeKey),
                    new Vector(0, -16),
                    item.Side));
            }

            return layouts
                .OrderBy(layout => GetNodeDisplayOrder(layout.NodeKey))
                .ThenBy(layout => layout.Card.Y)
                .ToList();
        }

        private List<EnvironmentCalloutLayout> LayoutCalloutColumn(
            IReadOnlyList<EnvironmentCalloutSeed> items,
            double x,
            double top,
            double bottom,
            double width,
            double gap,
            Vector entranceOffset)
        {
            var ordered = items
                .OrderBy(item => item.PreferredY)
                .ToList();

            var placements = new List<(EnvironmentCalloutSeed Seed, double Y)>();
            double cursor = top;

            foreach (var item in ordered)
            {
                double y = Math.Max(item.PreferredY, cursor);
                placements.Add((item, y));
                cursor = y + item.EstimatedHeight + gap;
            }

            double overflow = placements.Count == 0 ? 0 : placements[^1].Y + placements[^1].Seed.EstimatedHeight - bottom;
            if (overflow > 0)
            {
                for (int index = 0; index < placements.Count; index++)
                    placements[index] = (placements[index].Seed, placements[index].Y - overflow);
            }

            cursor = top;
            for (int index = 0; index < placements.Count; index++)
            {
                double adjustedY = Math.Max(placements[index].Y, cursor);
                placements[index] = (placements[index].Seed, adjustedY);
                cursor = adjustedY + placements[index].Seed.EstimatedHeight + gap;
            }

            var result = new List<EnvironmentCalloutLayout>();
            foreach (var placement in placements)
            {
                result.Add(new EnvironmentCalloutLayout(
                    placement.Seed.NodeKey,
                    placement.Seed.Finding,
                    placement.Seed.ExtraCount,
                    new WindowsPoint(x, placement.Y),
                    width,
                    placement.Seed.EstimatedHeight,
                    GetNodeCenterOnBoard(placement.Seed.NodeKey),
                    entranceOffset,
                    placement.Seed.Side));
            }

            return result;
        }

        private static WindowsPoint GetCalloutLineEnd(EnvironmentCalloutLayout layout)
        {
            return layout.Side switch
            {
                EnvironmentCalloutSide.Left => new WindowsPoint(layout.Card.X + layout.CardWidth, layout.Card.Y + Math.Min(76, layout.EstimatedHeight * 0.5)),
                EnvironmentCalloutSide.Right => new WindowsPoint(layout.Card.X, layout.Card.Y + Math.Min(76, layout.EstimatedHeight * 0.5)),
                _ => new WindowsPoint(layout.Card.X + layout.CardWidth / 2, layout.Card.Y + Math.Min(layout.EstimatedHeight, 96))
            };
        }

        private static EnvironmentCalloutSide GetPreferredCalloutSide(string nodeKey)
        {
            return nodeKey switch
            {
                "Explorer" or "Start" or "Taskbar" => EnvironmentCalloutSide.Left,
                _ => EnvironmentCalloutSide.Right
            };
        }

        private static double GetPreferredCalloutY(string nodeKey)
        {
            return nodeKey switch
            {
                "Display" => 96,
                "Explorer" => 132,
                "Windows" => 238,
                "Start" => 342,
                "Tray" => 442,
                "Taskbar" => 506,
                "Search" => 516,
                _ => 180
            };
        }

        private static double EstimateCalloutHeight(EnvironmentFindingViewModel finding, int extraCount)
        {
            string title = finding?.Title ?? string.Empty;
            string description = TrimForCallout(finding?.Description, 92);

            int titleLines = Math.Max(1, (int)Math.Ceiling(title.Length / 24d));
            int descriptionLines = Math.Max(2, (int)Math.Ceiling(description.Length / 34d));
            double height = 88 + Math.Max(0, titleLines - 1) * 16 + Math.Max(0, descriptionLines - 2) * 14;

            if (extraCount > 0)
                height += 28;

            return Math.Clamp(height, 92, 136);
        }

        private sealed class EnvironmentCalloutSeed
        {
            public EnvironmentCalloutSeed(string nodeKey, EnvironmentFindingViewModel finding, int extraCount, EnvironmentCalloutSide side, double preferredY, double estimatedHeight)
            {
                NodeKey = nodeKey;
                Finding = finding;
                ExtraCount = extraCount;
                Side = side;
                PreferredY = preferredY;
                EstimatedHeight = estimatedHeight;
            }

            public string NodeKey { get; }
            public EnvironmentFindingViewModel Finding { get; }
            public int ExtraCount { get; }
            public EnvironmentCalloutSide Side { get; }
            public double PreferredY { get; }
            public double EstimatedHeight { get; }
        }

        private sealed class EnvironmentCalloutLayout
        {
            public EnvironmentCalloutLayout(string nodeKey, EnvironmentFindingViewModel finding, int extraCount, WindowsPoint card, double cardWidth, double estimatedHeight, WindowsPoint source, Vector entranceOffset, EnvironmentCalloutSide side)
            {
                NodeKey = nodeKey;
                Finding = finding;
                ExtraCount = extraCount;
                Card = card;
                CardWidth = cardWidth;
                EstimatedHeight = estimatedHeight;
                Source = source;
                EntranceOffset = entranceOffset;
                Side = side;
            }

            public string NodeKey { get; }
            public EnvironmentFindingViewModel Finding { get; }
            public int ExtraCount { get; }
            public WindowsPoint Card { get; }
            public double CardWidth { get; }
            public double EstimatedHeight { get; }
            public WindowsPoint Source { get; }
            public Vector EntranceOffset { get; }
            public EnvironmentCalloutSide Side { get; }
        }

        private enum EnvironmentCalloutSide
        {
            Left,
            Center,
            Right
        }

        private static int GetNodeDisplayOrder(string nodeKey)
        {
            return nodeKey switch
            {
                "Display" => 0,
                "Explorer" => 1,
                "Windows" => 2,
                "Start" => 3,
                "Taskbar" => 4,
                "Search" => 5,
                "Tray" => 6,
                _ => 9
            };
        }

        private static WindowsPoint GetNodeCenterOnBoard(string nodeKey)
        {
            return nodeKey switch
            {
                "Display" => new WindowsPoint(610, 146),
                "Explorer" => new WindowsPoint(361, 254),
                "Windows" => new WindowsPoint(825, 300),
                "Start" => new WindowsPoint(395, 442),
                "Taskbar" => new WindowsPoint(388, 547),
                "Search" => new WindowsPoint(638, 547),
                "Tray" => new WindowsPoint(887, 547),
                _ => new WindowsPoint(610, 340)
            };
        }

        private static WindowsPoint GetCalloutPosition(string nodeKey)
        {
            return nodeKey switch
            {
                "Display" => new WindowsPoint(948, 132),
                "Explorer" => new WindowsPoint(36, 160),
                "Windows" => new WindowsPoint(976, 244),
                "Start" => new WindowsPoint(36, 338),
                "Taskbar" => new WindowsPoint(36, 504),
                "Search" => new WindowsPoint(976, 504),
                "Tray" => new WindowsPoint(976, 382),
                _ => new WindowsPoint(900, 132)
            };
        }

        private static string GetModuleStatusText(HealthLevel status, int problems, int recommendations)
        {
            return status switch
            {
                HealthLevel.Critical => problems > 0 ? FormatCount(problems, "критическая проблема", "критические проблемы", "критических проблем") : "Критическое состояние",
                HealthLevel.Warning or HealthLevel.Attention => problems > 0 ? FormatCount(problems, "проблема", "проблемы", "проблем") : "Требуется внимание",
                HealthLevel.Normal => recommendations > 0 ? FormatCount(recommendations, "рекомендация", "рекомендации", "рекомендаций") : "Есть рекомендации",
                HealthLevel.Checking => "Проверка",
                HealthLevel.Good => "В норме",
                _ => "Не проверено"
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

        private static string GetFindingKindText(HealthLevel level)
        {
            return level switch
            {
                HealthLevel.Critical => "Критическая проблема",
                HealthLevel.Warning or HealthLevel.Attention => "Проблема",
                HealthLevel.Normal => "Рекомендация",
                HealthLevel.Good => "В норме",
                _ => "Сигнал"
            };
        }

        private static string FormatFindingSummary(IReadOnlyList<EnvironmentFindingViewModel> findings)
        {
            int critical = findings.Count(finding => finding.Level == HealthLevel.Critical);
            int problems = findings.Count(finding => IsProblemLevel(finding.Level));
            int recommendations = findings.Count - problems;
            var parts = new List<string>();

            if (critical > 0)
                parts.Add(FormatCount(critical, "критическая проблема", "критические проблемы", "критических проблем"));

            int regularProblems = problems - critical;
            if (regularProblems > 0)
                parts.Add(FormatCount(regularProblems, "проблема", "проблемы", "проблем"));

            if (recommendations > 0)
                parts.Add(FormatCount(recommendations, "рекомендация", "рекомендации", "рекомендаций"));

            return parts.Count == 0
                ? "Активных сигналов нет."
                : string.Join(" · ", parts);
        }

        private static string FormatCount(int count, string one, string few, string many)
        {
            int value = Math.Abs(count) % 100;
            int digit = value % 10;
            string word = value > 10 && value < 20
                ? many
                : digit == 1
                    ? one
                    : digit >= 2 && digit <= 4
                        ? few
                        : many;

            return $"{count} {word}";
        }

        private static string TrimForCallout(string value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            string compact = string.Join(" ", value.Split(new[] { '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries)).Trim();
            if (compact.Length <= maxLength)
                return compact;

            return compact.Substring(0, Math.Max(0, maxLength - 1)).TrimEnd() + "…";
        }

        private static string NormalizeIdPart(string value)
        {
            string normalized = new string((value ?? string.Empty)
                .ToLowerInvariant()
                .Select(ch => char.IsLetterOrDigit(ch) ? ch : '-')
                .ToArray())
                .Trim('-');

            while (normalized.Contains("--", StringComparison.Ordinal))
                normalized = normalized.Replace("--", "-", StringComparison.Ordinal);

            return string.IsNullOrWhiteSpace(normalized) ? "signal" : normalized;
        }

        private static Dictionary<string, EnvironmentNodeInfo> BuildNodes()
        {
            var nodes = new[]
            {
                new EnvironmentNodeInfo
                {
                    Key = "Display",
                    Title = "Экран",
                    Description = "Тема, масштаб, визуальные эффекты и читаемость интерфейса Windows."
                },
                new EnvironmentNodeInfo
                {
                    Key = "Explorer",
                    Title = "Проводник",
                    Description = "Отображение файлов, расширений, быстрый доступ и поведение контекстного меню."
                },
                new EnvironmentNodeInfo
                {
                    Key = "Windows",
                    Title = "Окна и задачи",
                    Description = "Активные окна, диспетчер задач, процессы и поведение многозадачности."
                },
                new EnvironmentNodeInfo
                {
                    Key = "Start",
                    Title = "Пуск",
                    Description = "Закрепленные приложения, рекомендации и недавние элементы меню Пуск."
                },
                new EnvironmentNodeInfo
                {
                    Key = "Taskbar",
                    Title = "Панель задач",
                    Description = "Закрепления, системные кнопки, группировка окон и индикаторы."
                },
                new EnvironmentNodeInfo
                {
                    Key = "Search",
                    Title = "Поиск",
                    Description = "Индексирование, локальные результаты, ввод и веб-подсказки Windows."
                },
                new EnvironmentNodeInfo
                {
                    Key = "Tray",
                    Title = "Уведомления",
                    Description = "Системные уведомления, быстрые действия и значки трея."
                }
            };

            return nodes.ToDictionary(node => node.Key, StringComparer.OrdinalIgnoreCase);
        }

        private sealed class EnvironmentNodeInfo
        {
            public string Key { get; set; } = string.Empty;
            public string Title { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
        }

        private sealed class EnvironmentSection
        {
            public EnvironmentSection(
                string id,
                string title,
                string description,
                string scope,
                string source,
                string currentState,
                string recommendedState,
                string riskText,
                string backupState,
                bool canRollback,
                IReadOnlyList<EnvironmentSettingOptionViewModel> options)
            {
                Id = id;
                Title = title;
                Description = description;
                Scope = scope;
                Source = source;
                CurrentState = currentState;
                RecommendedState = recommendedState;
                CurrentValue = currentState;
                Recommendation = recommendedState;
                RiskText = riskText;
                BackupState = backupState;
                CanRollback = canRollback;
                Options = options ?? Array.Empty<EnvironmentSettingOptionViewModel>();
                SelectedOption = Options.FirstOrDefault(option => option.IsCurrent) ?? Options.FirstOrDefault(option => option.IsRecommended) ?? Options.FirstOrDefault();
                ToggleValue = SelectedOption == null || Options.Count <= 1 || Equals(SelectedOption, Options[0]);
            }

            public string Id { get; }
            public string Title { get; }
            public string Description { get; }
            public string Scope { get; }
            public string Source { get; }
            public string CurrentState { get; }
            public string RecommendedState { get; }
            public string CurrentValue { get; }
            public string Recommendation { get; }
            public string RiskText { get; }
            public string BackupState { get; }
            public bool CanRollback { get; }
            public IReadOnlyList<EnvironmentSettingOptionViewModel> Options { get; }
            public EnvironmentSettingOptionViewModel SelectedOption { get; set; }
            public bool ToggleValue { get; set; }
            public HealthLevel SignalLevel { get; set; } = HealthLevel.Good;
            public string SignalId { get; set; } = string.Empty;
            public bool IsToggle => Options.Count <= 2;
            public bool IsCombo => Options.Count > 2;
            public bool CanApply => Options.Count > 0;
            public bool ShowApplyAction => CanApply;
            public string ApplyButtonText => "Применить";
            public bool HasCurrentValue => !string.IsNullOrWhiteSpace(CurrentValue);
            public bool HasRecommendation => !string.IsNullOrWhiteSpace(Recommendation);
            public bool HasStatusMessage => false;
            public string StatusMessage => string.Empty;
            public bool HasActiveSignal => SignalLevel == HealthLevel.Normal || SignalLevel == HealthLevel.Attention || SignalLevel == HealthLevel.Warning || SignalLevel == HealthLevel.Critical;
            public string SignalActionText => "Игнорировать";
            public string SignalKindText
            {
                get
                {
                    return SignalLevel switch
                    {
                        HealthLevel.Critical => "критично",
                        HealthLevel.Warning => "проблема",
                        HealthLevel.Attention => "внимание",
                        HealthLevel.Normal => "рекомендация",
                        _ => string.Empty
                    };
                }
            }

            public EnvironmentSettingOptionViewModel GetSelectedOption()
            {
                if (Options.Count == 0)
                    return null;

                if (IsToggle)
                    return ToggleValue ? Options[0] : Options[Math.Min(1, Options.Count - 1)];

                return SelectedOption ?? Options.FirstOrDefault(option => option.IsCurrent) ?? Options.FirstOrDefault(option => option.IsRecommended) ?? Options[0];
            }
        }

        private enum EnvironmentSignalFilter
        {
            All,
            Recommendations,
            Problems,
            Critical
        }

        private sealed class EnvironmentSettingOptionViewModel
        {
            public EnvironmentSettingOptionViewModel(string settingId, string label, object value, bool isRecommended, bool isCurrent, string buttonText, bool canApply)
            {
                SettingId = settingId;
                Label = label;
                Value = value;
                IsRecommended = isRecommended;
                IsCurrent = isCurrent;
                ButtonText = buttonText;
                CanApply = canApply;
            }

            public string SettingId { get; }
            public string Label { get; }
            public object Value { get; }
            public bool IsRecommended { get; }
            public bool IsCurrent { get; }
            public string ButtonText { get; }
            public bool CanApply { get; }

            public override string ToString()
            {
                return Label;
            }
        }

        private sealed class EnvironmentRegistrySettingDefinition
        {
            public string Id { get; set; } = string.Empty;
            public string NodeKey { get; set; } = string.Empty;
            public string Title { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
            public string Scope { get; set; } = string.Empty;
            public string Source { get; set; } = string.Empty;
            public RegistryHive Hive { get; set; } = RegistryHive.CurrentUser;
            public string SubKeyPath { get; set; } = string.Empty;
            public string ValueName { get; set; } = string.Empty;
            public RegistryValueKind ValueKind { get; set; } = RegistryValueKind.DWord;
            public object RecommendedValue { get; set; } = 0;
            public object DefaultValue { get; set; } = 0;
            public string RecommendedLabel { get; set; } = string.Empty;
            public string DefaultLabel { get; set; } = string.Empty;
            public string RiskText { get; set; } = "низкий риск";
            public List<EnvironmentRegistrySettingOption> Options { get; } = new List<EnvironmentRegistrySettingOption>();
        }

        private sealed class EnvironmentRegistrySettingOption
        {
            public EnvironmentRegistrySettingOption(string label, object value, bool isRecommended = false)
            {
                Label = label;
                Value = value;
                IsRecommended = isRecommended;
            }

            public string Label { get; }
            public object Value { get; }
            public bool IsRecommended { get; }
        }

        private sealed class EnvironmentSettingBackupRecord
        {
            public string SettingId { get; set; } = string.Empty;
            public string Title { get; set; } = string.Empty;
            public string Hive { get; set; } = string.Empty;
            public string SubKeyPath { get; set; } = string.Empty;
            public string ValueName { get; set; } = string.Empty;
            public string ValueKind { get; set; } = string.Empty;
            public bool ValueExisted { get; set; }
            public string SerializedValue { get; set; } = string.Empty;
            public DateTime CreatedAtUtc { get; set; }
        }

        private sealed class EnvironmentSearchSuggestion
        {
            public bool IsSection { get; set; }
            public string SettingId { get; set; } = string.Empty;
            public string Title { get; set; } = string.Empty;
            public string SectionTitle { get; set; } = string.Empty;
            public string Caption { get; set; } = string.Empty;
        }

        private sealed class EnvironmentFindingViewModel
        {
            public string Id { get; set; } = string.Empty;
            public string NodeKey { get; set; } = string.Empty;
            public HealthLevel Level { get; set; } = HealthLevel.Normal;
            public string KindText { get; set; } = string.Empty;
            public string Title { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
        }
    }
}
