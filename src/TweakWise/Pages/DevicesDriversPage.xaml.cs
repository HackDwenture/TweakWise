using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using TweakWise.Managers;
using TweakWise.Models;
using TweakWise.Services;
using Application = System.Windows.Application;
using Button = System.Windows.Controls.Button;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using TextBox = System.Windows.Controls.TextBox;
using WindowsPoint = System.Windows.Point;

namespace TweakWise.Pages
{
    public partial class DevicesDriversPage : Page
    {
        private readonly DeviceDriverDiagnosticsService _diagnosticsService = new DeviceDriverDiagnosticsService();
        private readonly IComputerHealthService _healthService = App.ComputerHealthService;
        private readonly List<DeviceDriverGroupViewModel> _workspaceGroups = new List<DeviceDriverGroupViewModel>();
        private readonly HashSet<string> _locallyIgnoredSignalIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private DeviceDriverDiagnosticsSnapshot _snapshot = new DeviceDriverDiagnosticsSnapshot();
        private DeviceDriverDashboardMode _activeMode = DeviceDriverDashboardMode.Drivers;
        private DeviceDriverDashboardMode _workspaceMode = DeviceDriverDashboardMode.Drivers;
        private DeviceWorkspaceFilter _workspaceFilter = DeviceWorkspaceFilter.All;
        private CancellationTokenSource _scanCts;
        private bool _isPageActive;
        private string _pendingWorkspaceTargetGroupId = string.Empty;
        private string _pendingWorkspaceTargetItemId = string.Empty;
        private int _workspaceNavigationVersion;
        private const double GhostDockOffset = 390;
        private const double IncomingGhostScale = 1.72;
        private const double OutgoingCenterScale = 0.82;
        private const double WorkspacePanelTop = 78;
        private const double WorkspaceReturnOrbTargetXRatio = 0.52;
        private const double WorkspaceReturnOrbTargetCenterY = WorkspacePanelTop - 30;
        private const double WorkspaceReturnOrbSize = 44;
        private const int WorkspaceItemRenderLimit = 5;
        private const int WorkspaceSearchItemRenderLimit = 12;
        private const int WorkspaceInitialGroupBatchSize = 2;

        private bool _isSwitchingMode;
        private bool _isWorkspaceOpen;
        private bool _isPointerDown;
        private bool _isSwipeGesture;
        private bool _pointerStartedOnOrb;
        private WindowsPoint _pointerStart;
        private WindowsPoint _workspaceReturnOrbStartPoint;
        private WindowsPoint _workspaceReturnOrbTargetPoint;

        public DevicesDriversPage()
        {
            InitializeComponent();
        }

        private async void Page_Loaded(object sender, RoutedEventArgs e)
        {
            _isPageActive = true;
            Focus();
            SetActiveMode(DeviceDriverDashboardMode.Drivers, animate: false, direction: 1);
            ApplyModuleStatus();
            AnimateOpacity(RootContent, 1, 220);
            await RefreshDiagnosticsAsync();
        }

        private void Page_Unloaded(object sender, RoutedEventArgs e)
        {
            _isPageActive = false;
            var scanCts = _scanCts;
            _scanCts = null;
            scanCts?.Cancel();
            scanCts?.Dispose();
            _workspaceNavigationVersion++;
            _pendingWorkspaceTargetGroupId = string.Empty;
            _pendingWorkspaceTargetItemId = string.Empty;
            StopLoadingSquares();
            StopOrbitAnimations();
            StopCenterOrbHoverAnimation();
            StopWorkspaceReturnOrbBreathing();
        }

        private void Page_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (_isWorkspaceOpen && (e.Key == Key.Escape || e.Key == Key.Back))
            {
                e.Handled = true;
                CloseWorkspace();
                return;
            }

            if (!_isWorkspaceOpen && (e.Key == Key.Enter || e.Key == Key.Space) && Keyboard.FocusedElement is not TextBox)
            {
                e.Handled = true;
                OpenActiveWorkspace();
                return;
            }

            if (e.Key == Key.Escape || e.Key == Key.Back)
            {
                e.Handled = true;
                NavigateHome();
                return;
            }

            if (e.Key == Key.Left)
            {
                e.Handled = true;
                SwitchToPreviousMode();
                return;
            }

            if (e.Key == Key.Right)
            {
                e.Handled = true;
                SwitchToNextMode();
            }
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isWorkspaceOpen)
            {
                CloseWorkspace();
                return;
            }

            NavigateHome();
        }

        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            await RefreshDiagnosticsAsync();
        }

        private void PreviousModeButton_Click(object sender, RoutedEventArgs e)
        {
            SwitchToPreviousMode();
        }

        private void NextModeButton_Click(object sender, RoutedEventArgs e)
        {
            SwitchToNextMode();
        }

        private void CenterOrbButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isSwipeGesture)
                OpenActiveWorkspace();
        }

        private void CenterOrbButton_MouseEnter(object sender, MouseEventArgs e)
        {
            if (_isSwitchingMode || _isWorkspaceOpen)
                return;

            CenterOrbButtonScale.BeginAnimation(ScaleTransform.ScaleXProperty, CreateAnimation(1.035, 180));
            CenterOrbButtonScale.BeginAnimation(ScaleTransform.ScaleYProperty, CreateAnimation(1.035, 180));
            OrbGlow.BeginAnimation(UIElement.OpacityProperty, CreateAnimation(0.24, 180));
        }

        private void CenterOrbButton_MouseLeave(object sender, MouseEventArgs e)
        {
            if (_isWorkspaceOpen)
                return;

            StopCenterOrbHoverAnimation();
        }

        private void StopCenterOrbHoverAnimation()
        {
            CenterOrbButtonScale?.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            CenterOrbButtonScale?.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            OrbGlow?.BeginAnimation(UIElement.OpacityProperty, null);

            if (CenterOrbButtonScale != null)
            {
                CenterOrbButtonScale.ScaleX = 1;
                CenterOrbButtonScale.ScaleY = 1;
            }

            if (OrbGlow != null)
                OrbGlow.Opacity = 0.16;
        }

        private void OrbitStage_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (IsInsideElement(e.OriginalSource as DependencyObject, PreviousModeButton) ||
                IsInsideElement(e.OriginalSource as DependencyObject, NextModeButton))
            {
                _isPointerDown = false;
                _isSwipeGesture = false;
                _pointerStartedOnOrb = false;
                return;
            }

            _isPointerDown = true;
            _isSwipeGesture = false;
            _pointerStartedOnOrb = IsInsideElement(e.OriginalSource as DependencyObject, CenterOrbButton);
            _pointerStart = e.GetPosition(this);
            OrbitStage.CaptureMouse();
        }

        private void OrbitStage_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isPointerDown || _isSwitchingMode)
                return;

            var current = e.GetPosition(this);
            double delta = current.X - _pointerStart.X;
            if (Math.Abs(delta) < 84)
                return;

            _isSwipeGesture = true;
            _pointerStartedOnOrb = false;
            _isPointerDown = false;
            OrbitStage.ReleaseMouseCapture();

            if (delta < 0)
                SwitchToNextMode();
            else
                SwitchToPreviousMode();
        }

        private void OrbitStage_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isPointerDown && _pointerStartedOnOrb && !_isSwipeGesture && !_isSwitchingMode)
            {
                var current = e.GetPosition(this);
                double delta = Math.Abs(current.X - _pointerStart.X) + Math.Abs(current.Y - _pointerStart.Y);
                if (delta < 18)
                    OpenActiveWorkspace();
            }

            _isPointerDown = false;
            _pointerStartedOnOrb = false;
            OrbitStage.ReleaseMouseCapture();
        }

        private async Task RefreshDiagnosticsAsync()
        {
            var previousScanCts = _scanCts;
            var currentScanCts = new CancellationTokenSource();
            _scanCts = currentScanCts;
            previousScanCts?.Cancel();
            var token = currentScanCts.Token;

            SetLoading(true);
            if (_isWorkspaceOpen)
                SetWorkspaceContentLoading(true);

            try
            {
                var result = await _diagnosticsService.ScanAsync(token);
                if (!_isPageActive || token.IsCancellationRequested)
                    return;

                _snapshot = result ?? new DeviceDriverDiagnosticsSnapshot();
                UpdateActiveModeContent();
                ApplyModuleStatus();
                RefreshWorkspaceIfOpen();
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                if (!_isPageActive)
                    return;

                _snapshot = BuildErrorSnapshot(ex);
                UpdateActiveModeContent();
                ApplyModuleStatus();
                RefreshWorkspaceIfOpen();
            }
            finally
            {
                if (ReferenceEquals(_scanCts, currentScanCts))
                {
                    _scanCts = null;
                    currentScanCts.Dispose();

                    if (_isPageActive)
                    {
                        SetLoading(false);
                        SetWorkspaceContentLoading(false);
                    }
                }
                else
                {
                    currentScanCts.Dispose();
                }
            }
        }

        private void SetActiveMode(DeviceDriverDashboardMode mode, bool animate, int direction)
        {
            if (_isSwitchingMode && animate)
                return;

            if (_activeMode == mode && animate)
                return;

            if (!animate)
            {
                _activeMode = mode;
                UpdateActiveModeContent();
                ResetOrbitTransform();
                return;
            }

            PlayOrbitTransition(mode, direction);
        }

        private void SwitchToNextMode()
        {
            var next = _activeMode == DeviceDriverDashboardMode.Drivers
                ? DeviceDriverDashboardMode.Devices
                : DeviceDriverDashboardMode.Drivers;
            SetActiveMode(next, animate: true, direction: 1);
        }

        private void SwitchToPreviousMode()
        {
            var next = _activeMode == DeviceDriverDashboardMode.Drivers
                ? DeviceDriverDashboardMode.Devices
                : DeviceDriverDashboardMode.Drivers;
            SetActiveMode(next, animate: true, direction: -1);
        }

        private void PlayOrbitTransition(DeviceDriverDashboardMode targetMode, int direction)
        {
            if (_activeMode == targetMode)
                return;

            _isSwitchingMode = true;
            StopOrbitAnimations();
            StopCenterOrbHoverAnimation();

            bool moveLeft = direction > 0;
            double sign = moveLeft ? -1 : 1;
            IEasingFunction carouselEase = null;
            var fadeEase = new QuadraticEase { EasingMode = EasingMode.EaseOut };

            CenterOrbButton.Opacity = 1;
            CenterOrbButtonTranslate.X = 0;
            CenterOrbButtonScale.ScaleX = 1;
            CenterOrbButtonScale.ScaleY = 1;
            OrbGlow.Opacity = 0.16;

            LeftGhost.Opacity = 0.25;
            RightGhost.Opacity = 0.25;
            LeftGhostTranslate.X = 0;
            RightGhostTranslate.X = 0;
            LeftGhostScale.ScaleX = 1;
            LeftGhostScale.ScaleY = 1;
            RightGhostScale.ScaleX = 1;
            RightGhostScale.ScaleY = 1;

            var centerOut = CreateAnimation(sign * 120, 180);
            centerOut.EasingFunction = carouselEase;
            var centerOutScale = CreateAnimation(0.94, 180);
            centerOutScale.EasingFunction = carouselEase;
            var centerOutFade = CreateAnimation(0, 165);
            centerOutFade.EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn };
            centerOutFade.Completed += (_, _) =>
            {
                _activeMode = targetMode;
                UpdateActiveModeContent();

                CenterOrbButton.BeginAnimation(UIElement.OpacityProperty, null);
                CenterOrbButtonTranslate.BeginAnimation(TranslateTransform.XProperty, null);
                CenterOrbButtonScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                CenterOrbButtonScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                OrbGlow.BeginAnimation(UIElement.OpacityProperty, null);

                CenterOrbButton.Opacity = 0;
                CenterOrbButtonTranslate.X = -sign * 120;
                CenterOrbButtonScale.ScaleX = 0.94;
                CenterOrbButtonScale.ScaleY = 0.94;
                OrbGlow.Opacity = 0;

                LeftGhost.BeginAnimation(UIElement.OpacityProperty, null);
                RightGhost.BeginAnimation(UIElement.OpacityProperty, null);
                LeftGhostTranslate.BeginAnimation(TranslateTransform.XProperty, null);
                RightGhostTranslate.BeginAnimation(TranslateTransform.XProperty, null);
                LeftGhostScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                LeftGhostScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                RightGhostScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                RightGhostScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);

                LeftGhostTranslate.X = 0;
                RightGhostTranslate.X = 0;
                LeftGhostScale.ScaleX = 1;
                LeftGhostScale.ScaleY = 1;
                RightGhostScale.ScaleX = 1;
                RightGhostScale.ScaleY = 1;
                LeftGhost.Opacity = 0.10;
                RightGhost.Opacity = 0.10;

                var centerIn = CreateAnimation(0, 205);
                centerIn.EasingFunction = carouselEase;
                var centerInScale = CreateAnimation(1, 205);
                centerInScale.EasingFunction = carouselEase;
                var centerInFade = CreateAnimation(1, 185);
                centerInFade.EasingFunction = fadeEase;
                centerInFade.Completed += (_, _) =>
                {
                    LeftGhost.BeginAnimation(UIElement.OpacityProperty, CreateAnimation(0.25, 160));
                    RightGhost.BeginAnimation(UIElement.OpacityProperty, CreateAnimation(0.25, 160));
                    ResetOrbitTransform();
                    _isSwitchingMode = false;
                };

                var glowIn = CreateAnimation(0.16, 210);
                glowIn.EasingFunction = fadeEase;

                CenterOrbButtonTranslate.BeginAnimation(TranslateTransform.XProperty, centerIn);
                CenterOrbButtonScale.BeginAnimation(ScaleTransform.ScaleXProperty, centerInScale);
                CenterOrbButtonScale.BeginAnimation(ScaleTransform.ScaleYProperty, centerInScale.Clone());
                CenterOrbButton.BeginAnimation(UIElement.OpacityProperty, centerInFade);
                OrbGlow.BeginAnimation(UIElement.OpacityProperty, glowIn);
            };

            var glowOut = CreateAnimation(0, 150);
            glowOut.EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn };

            var incomingGhost = moveLeft ? RightGhost : LeftGhost;
            var outgoingGhost = moveLeft ? LeftGhost : RightGhost;
            var incomingTranslate = moveLeft ? RightGhostTranslate : LeftGhostTranslate;
            var outgoingTranslate = moveLeft ? LeftGhostTranslate : RightGhostTranslate;

            var incomingPreview = CreateAnimation(sign * 90, 180);
            incomingPreview.EasingFunction = carouselEase;
            var outgoingPreview = CreateAnimation(sign * -90, 180);
            outgoingPreview.EasingFunction = carouselEase;

            incomingTranslate.BeginAnimation(TranslateTransform.XProperty, incomingPreview);
            outgoingTranslate.BeginAnimation(TranslateTransform.XProperty, outgoingPreview);
            incomingGhost.BeginAnimation(UIElement.OpacityProperty, CreateAnimation(0.32, 130));
            outgoingGhost.BeginAnimation(UIElement.OpacityProperty, CreateAnimation(0.08, 150));

            CenterOrbButtonTranslate.BeginAnimation(TranslateTransform.XProperty, centerOut);
            CenterOrbButtonScale.BeginAnimation(ScaleTransform.ScaleXProperty, centerOutScale);
            CenterOrbButtonScale.BeginAnimation(ScaleTransform.ScaleYProperty, centerOutScale.Clone());
            CenterOrbButton.BeginAnimation(UIElement.OpacityProperty, centerOutFade);
            OrbGlow.BeginAnimation(UIElement.OpacityProperty, glowOut);
        }

        private void ResetOrbitTransform()
        {
            StopOrbitAnimations();
            OrbitTextureLayer.Opacity = 1;
            OrbitSceneTranslate.X = 0;
            OrbitSceneScale.ScaleX = 1;
            OrbitSceneScale.ScaleY = 1;

            if (CenterOrbButton != null)
            {
                CenterOrbButton.Opacity = 1;
                CenterOrbButtonTranslate.X = 0;
            }

            if (CenterOrbButtonScale != null)
            {
                CenterOrbButtonScale.ScaleX = 1;
                CenterOrbButtonScale.ScaleY = 1;
            }

            if (LeftGhost != null)
                LeftGhost.Opacity = 0.25;
            if (RightGhost != null)
                RightGhost.Opacity = 0.25;
            if (LeftGhostTranslate != null)
                LeftGhostTranslate.X = 0;
            if (RightGhostTranslate != null)
                RightGhostTranslate.X = 0;
            if (LeftGhostScale != null)
            {
                LeftGhostScale.ScaleX = 1;
                LeftGhostScale.ScaleY = 1;
            }
            if (RightGhostScale != null)
            {
                RightGhostScale.ScaleX = 1;
                RightGhostScale.ScaleY = 1;
            }
        }

        private void StopOrbitAnimations()
        {
            OrbitTextureLayer?.BeginAnimation(UIElement.OpacityProperty, null);
            OrbitSceneTranslate?.BeginAnimation(TranslateTransform.XProperty, null);
            OrbitSceneScale?.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            OrbitSceneScale?.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            CenterOrbButton?.BeginAnimation(UIElement.OpacityProperty, null);
            CenterOrbButtonTranslate?.BeginAnimation(TranslateTransform.XProperty, null);
            CenterOrbButtonScale?.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            CenterOrbButtonScale?.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            LeftGhost?.BeginAnimation(UIElement.OpacityProperty, null);
            RightGhost?.BeginAnimation(UIElement.OpacityProperty, null);
            LeftGhostTranslate?.BeginAnimation(TranslateTransform.XProperty, null);
            RightGhostTranslate?.BeginAnimation(TranslateTransform.XProperty, null);
            LeftGhostScale?.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            LeftGhostScale?.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            RightGhostScale?.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            RightGhostScale?.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        }

        private void UpdateActiveModeContent()
        {
            var snapshot = _snapshot ?? new DeviceDriverDiagnosticsSnapshot();
            var findings = GetVisibleFindings(_activeMode)
                .Select(DeviceReasonViewModel.FromFinding)
                .ToList();

            var level = findings.Select(item => item.Level)
                .DefaultIfEmpty(HealthLevel.Good)
                .OrderByDescending(GetSeverity)
                .First();

            string brushKey = GetStatusBrushKey(level);
            bool drivers = _activeMode == DeviceDriverDashboardMode.Drivers;

            DriverGlyph.Visibility = drivers ? Visibility.Visible : Visibility.Collapsed;
            DeviceGlyph.Visibility = drivers ? Visibility.Collapsed : Visibility.Visible;
            OrbTitleTextBlock.Text = drivers ? "Драйверы" : "Устройства";
            OrbSummaryTextBlock.Text = drivers
                ? GetSummaryText(snapshot.DriverSummary, "Данные о драйверах ещё не получены")
                : GetSummaryText(snapshot.DeviceSummary, "Данные об устройствах ещё не получены");
            RailTitleTextBlock.Text = drivers ? "Проблемы и рекомендации по драйверам" : "Проблемы и рекомендации по устройствам";

            LeftGhostTitle.Text = drivers ? "Устройства" : "Драйверы";
            RightGhostTitle.Text = LeftGhostTitle.Text;
            LeftGhostDriverGlyph.Visibility = drivers ? Visibility.Collapsed : Visibility.Visible;
            RightGhostDriverGlyph.Visibility = LeftGhostDriverGlyph.Visibility;
            LeftGhostDeviceGlyph.Visibility = drivers ? Visibility.Visible : Visibility.Collapsed;
            RightGhostDeviceGlyph.Visibility = LeftGhostDeviceGlyph.Visibility;

            OrbGlow.SetResourceReference(Shape.FillProperty, brushKey);
            ModuleStatusIndicator.SetResourceReference(Shape.FillProperty, brushKey);
            SignalRailItemsControl.ItemsSource = findings;

            if (findings.Count == 0)
                SignalRailItemsControl.ItemsSource = new[] { DeviceReasonViewModel.CreateGood(_activeMode) };
        }

        private void ApplyModuleStatus()
        {
            var module = _healthService?.GetModule(CoreModuleId.Devices);
            var status = module?.Status?.Status ?? HealthLevel.Good;
            int problems = module?.Status?.ProblemCount ?? 0;
            int recommendations = module?.Status?.RecommendationCount ?? 0;
            var findings = (_snapshot?.Findings ?? new List<DeviceDriverFinding>())
                .Where(item => item != null && !IsSignalHidden(item.Id))
                .ToList();

            if (findings.Count > 0)
            {
                problems = findings.Count(item => IsProblemLevel(item.Level));
                recommendations = findings.Count(item => item.Level == HealthLevel.Normal);
                status = findings.Select(item => item.Level).DefaultIfEmpty(HealthLevel.Good).OrderByDescending(GetSeverity).First();
            }

            ModuleStatusTextBlock.Text = GetModuleStatusText(status, problems, recommendations);
            ModuleStatusIndicator.SetResourceReference(Shape.FillProperty, GetStatusBrushKey(status));
        }

        private IReadOnlyList<DeviceDriverFinding> GetVisibleFindings(DeviceDriverDashboardMode mode)
        {
            return (_snapshot?.GetFindings(mode) ?? new List<DeviceDriverFinding>())
                .Where(item => item != null && !IsSignalHidden(item.Id))
                .ToList();
        }

        private bool IsSignalHidden(string signalId)
        {
            if (string.IsNullOrWhiteSpace(signalId))
                return false;

            return _locallyIgnoredSignalIds.Contains(signalId) ||
                   App.SettingsManager?.IsHealthSignalSuppressed(signalId) == true;
        }

        private void OpenActiveWorkspace()
        {
            OpenWorkspace(_activeMode);
        }

        private void OpenWorkspace(
            DeviceDriverDashboardMode mode,
            string targetGroupId = "",
            string targetItemId = "",
            string searchQuery = "")
        {
            if (_isSwitchingMode)
                return;

            bool hasTarget = !string.IsNullOrWhiteSpace(targetGroupId) || !string.IsNullOrWhiteSpace(targetItemId);
            _workspaceNavigationVersion++;
            int navigationVersion = _workspaceNavigationVersion;

            if (hasTarget)
            {
                _pendingWorkspaceTargetGroupId = targetGroupId ?? string.Empty;
                _pendingWorkspaceTargetItemId = targetItemId ?? string.Empty;
                _workspaceFilter = DeviceWorkspaceFilter.All;
                searchQuery = string.Empty;
                SetWorkspaceNavigationBusy(true);
            }
            else
            {
                _pendingWorkspaceTargetGroupId = string.Empty;
                _pendingWorkspaceTargetItemId = string.Empty;
                SetWorkspaceNavigationBusy(false);
            }

            _workspaceMode = mode;
            ConfigureWorkspaceHeader();
            UpdateWorkspaceFilterButtons();

            if (WorkspaceSearchTextBox != null)
                WorkspaceSearchTextBox.Text = searchQuery ?? string.Empty;

            ClearWorkspaceContentBeforeLoad();
            SetWorkspaceContentLoading(true);

            if (_isWorkspaceOpen)
            {
                ScheduleWorkspaceContentBuild(navigationVersion, hasTarget, searchQuery, focusSearch: !hasTarget);
                return;
            }

            _isWorkspaceOpen = true;
            WorkspaceLayer.Visibility = Visibility.Visible;
            WorkspaceLayer.Opacity = 0;
            WorkspacePanel.Opacity = 0;
            WorkspacePanelScale.ScaleX = 0.93;
            WorkspacePanelScale.ScaleY = 0.93;
            WorkspacePanelTranslate.Y = 34;
            PrepareWorkspaceReturnOrb();

            AnimateOpacity(WorkspaceLayer, 1, 220);
            AnimateOpacity(WorkspacePanel, 1, 220);
            AnimateWorkspaceReturnOrb(show: true);
            WorkspacePanelScale.BeginAnimation(ScaleTransform.ScaleXProperty, CreateAnimation(1, 300));
            WorkspacePanelScale.BeginAnimation(ScaleTransform.ScaleYProperty, CreateAnimation(1, 300));
            WorkspacePanelTranslate.BeginAnimation(TranslateTransform.YProperty, CreateAnimation(0, 300));
            ScheduleWorkspaceContentBuild(navigationVersion, hasTarget, searchQuery, focusSearch: !hasTarget);
        }

        private void ClearWorkspaceContentBeforeLoad()
        {
            if (WorkspaceFindingsItemsControl != null)
                WorkspaceFindingsItemsControl.ItemsSource = null;
            if (WorkspaceGroupsItemsControl != null)
                WorkspaceGroupsItemsControl.ItemsSource = null;
            if (WorkspaceFindingsEmptyText != null)
                WorkspaceFindingsEmptyText.Visibility = Visibility.Collapsed;
            if (WorkspaceEmptyTextBlock != null)
                WorkspaceEmptyTextBlock.Visibility = Visibility.Collapsed;
            if (WorkspaceSearchSuggestionsItemsControl != null)
                WorkspaceSearchSuggestionsItemsControl.Visibility = Visibility.Collapsed;
        }

        private async void ScheduleWorkspaceContentBuild(int navigationVersion, bool hasTarget, string searchQuery, bool focusSearch)
        {
            await Dispatcher.Yield(DispatcherPriority.Render);
            await Task.Delay(180);

            if (!_isPageActive || !_isWorkspaceOpen || navigationVersion != _workspaceNavigationVersion)
                return;

            var snapshot = _snapshot ?? new DeviceDriverDiagnosticsSnapshot();
            var mode = _workspaceMode;
            var filter = _workspaceFilter;
            string query = WorkspaceSearchTextBox?.Text?.Trim() ?? searchQuery ?? string.Empty;
            var groupsSource = (snapshot.GetGroups(mode) ?? new List<DeviceDriverGroupSnapshot>()).ToList();
            var findingsSource = GetVisibleFindings(mode).ToList();

            WorkspaceContentBuildResult result;
            try
            {
                result = await Task.Run(() => BuildWorkspaceContent(groupsSource, findingsSource, query, filter));
            }
            catch
            {
                result = new WorkspaceContentBuildResult();
            }

            if (!_isPageActive || !_isWorkspaceOpen || navigationVersion != _workspaceNavigationVersion)
                return;

            ConfigureWorkspaceHeader();
            UpdateWorkspaceFilterButtons();
            await ApplyWorkspaceContentProgressivelyAsync(result, query, navigationVersion);
            ScheduleWorkspaceSearchFocus(searchQuery, focusSearch: focusSearch);

            if (hasTarget)
                ScheduleWorkspaceTargetFallback();
        }

        private static WorkspaceContentBuildResult BuildWorkspaceContent(
            IReadOnlyList<DeviceDriverGroupSnapshot> groupsSource,
            IReadOnlyList<DeviceDriverFinding> findingsSource,
            string query,
            DeviceWorkspaceFilter filter)
        {
            var groups = (groupsSource ?? Array.Empty<DeviceDriverGroupSnapshot>())
                .Select(DeviceDriverGroupViewModel.FromGroup)
                .ToList();

            var visibleFindings = (findingsSource ?? Array.Empty<DeviceDriverFinding>())
                .Where(finding => finding != null)
                .ToList();

            var workspaceFindings = visibleFindings
                .Where(finding => MatchesWorkspaceFilter(finding.Level, filter))
                .Select(DeviceReasonViewModel.FromFinding)
                .ToList();

            var filteredGroups = groups
                .Where(group => MatchesWorkspaceFilter(group.Level, filter))
                .Where(group => MatchesSearch(group.SearchText, query))
                .Select(group => group.WithFilteredItems(query, filter))
                .Where(group => group.Items.Count > 0 || MatchesSearch(group.SearchText, query))
                .ToList();

            return new WorkspaceContentBuildResult
            {
                Groups = groups,
                FilteredGroups = filteredGroups,
                VisibleFindings = visibleFindings,
                FilteredFindings = workspaceFindings
            };
        }

        private async Task ApplyWorkspaceContentProgressivelyAsync(
            WorkspaceContentBuildResult result,
            string query,
            int navigationVersion)
        {
            result ??= new WorkspaceContentBuildResult();

            _workspaceGroups.Clear();
            _workspaceGroups.AddRange(result.Groups ?? new List<DeviceDriverGroupViewModel>());

            UpdateWorkspaceFindingSummary(result.VisibleFindings ?? new List<DeviceDriverFinding>());

            WorkspaceSearchPlaceholderTextBlock.Visibility = string.IsNullOrWhiteSpace(query) ? Visibility.Visible : Visibility.Collapsed;
            WorkspaceSearchClearButton.Visibility = string.IsNullOrWhiteSpace(query) ? Visibility.Collapsed : Visibility.Visible;
            WorkspaceSearchSuggestionsItemsControl.Visibility = Visibility.Collapsed;

            var findings = result.FilteredFindings ?? new List<DeviceReasonViewModel>();
            WorkspaceFindingsItemsControl.ItemsSource = findings;
            WorkspaceFindingsEmptyText.Visibility = findings.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            var groups = result.FilteredGroups ?? new List<DeviceDriverGroupViewModel>();
            var renderedGroups = new ObservableCollection<DeviceDriverGroupViewModel>();
            WorkspaceGroupsItemsControl.ItemsSource = renderedGroups;
            WorkspaceEmptyTextBlock.Visibility = groups.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            await Dispatcher.Yield(DispatcherPriority.Render);

            int firstBatch = Math.Min(WorkspaceInitialGroupBatchSize, groups.Count);
            for (int index = 0; index < firstBatch; index++)
                renderedGroups.Add(groups[index]);

            await Dispatcher.Yield(DispatcherPriority.Render);
            SetWorkspaceContentLoading(false);

            for (int index = firstBatch; index < groups.Count; index++)
            {
                if (!_isPageActive || !_isWorkspaceOpen || navigationVersion != _workspaceNavigationVersion)
                    return;

                renderedGroups.Add(groups[index]);

                if (index % 1 == 0)
                    await Dispatcher.Yield(DispatcherPriority.Background);
            }

            if (!_isPageActive || !_isWorkspaceOpen || navigationVersion != _workspaceNavigationVersion)
                return;

            UpdateWorkspaceSearchSuggestions(query);
        }

        private void ScheduleWorkspaceSearchFocus(string searchQuery, bool focusSearch = true)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (focusSearch)
                    WorkspaceSearchTextBox?.Focus();
                if (!string.IsNullOrWhiteSpace(searchQuery))
                    WorkspaceScrollViewer?.ScrollToVerticalOffset(240);
            }), DispatcherPriority.Input);
        }

        private void CloseWorkspace()
        {
            if (!_isWorkspaceOpen)
                return;

            _isWorkspaceOpen = false;
            _pendingWorkspaceTargetGroupId = string.Empty;
            _pendingWorkspaceTargetItemId = string.Empty;
            _workspaceNavigationVersion++;
            SetWorkspaceNavigationBusy(false);
            SetWorkspaceContentLoading(false);
            WorkspacePanelScale.BeginAnimation(ScaleTransform.ScaleXProperty, CreateAnimation(0.94, 180));
            WorkspacePanelScale.BeginAnimation(ScaleTransform.ScaleYProperty, CreateAnimation(0.94, 180));
            WorkspacePanelTranslate.BeginAnimation(TranslateTransform.YProperty, CreateAnimation(24, 180));
            AnimateWorkspaceReturnOrb(show: false);
            AnimateOpacity(WorkspacePanel, 0, 150);
            AnimateOpacity(WorkspaceLayer, 0, 180, () =>
            {
                if (!_isWorkspaceOpen)
                    WorkspaceLayer.Visibility = Visibility.Collapsed;
            });
        }

        private void PrepareWorkspaceReturnOrb()
        {
            if (WorkspaceReturnOrbButton == null)
                return;

            StopWorkspaceReturnOrbBreathing();
            _workspaceReturnOrbStartPoint = GetCenterOrbCenterInPage();
            _workspaceReturnOrbTargetPoint = GetWorkspaceReturnOrbTargetCenter();
            WorkspaceReturnOrbButton.Visibility = Visibility.Visible;
            ResetWorkspaceReturnOrbAnimation();
            PositionWorkspaceReturnOrb(_workspaceReturnOrbStartPoint, 0.42, 0);
        }

        private void AnimateWorkspaceReturnOrb(bool show)
        {
            if (WorkspaceReturnOrbButton == null ||
                WorkspaceReturnOrbTranslate == null ||
                WorkspaceReturnOrbScale == null)
                return;

            if (show)
            {
                WorkspaceReturnOrbButton.Visibility = Visibility.Visible;
                StopWorkspaceReturnOrbBreathing();

                double startLeft = ToWorkspaceOrbLeft(_workspaceReturnOrbStartPoint);
                double startTop = ToWorkspaceOrbTop(_workspaceReturnOrbStartPoint);
                double targetLeft = ToWorkspaceOrbLeft(_workspaceReturnOrbTargetPoint);
                double targetTop = ToWorkspaceOrbTop(_workspaceReturnOrbTargetPoint);

                WorkspaceReturnOrbButton.BeginAnimation(
                    UIElement.OpacityProperty,
                    new DoubleAnimation
                    {
                        From = 0,
                        To = 1,
                        Duration = TimeSpan.FromMilliseconds(210),
                        EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                    });

                var flyEase = new CubicEase { EasingMode = EasingMode.EaseOut };
                WorkspaceReturnOrbTranslate.BeginAnimation(
                    TranslateTransform.XProperty,
                    new DoubleAnimation
                    {
                        From = startLeft,
                        To = targetLeft,
                        Duration = TimeSpan.FromMilliseconds(520),
                        EasingFunction = flyEase
                    });
                WorkspaceReturnOrbTranslate.BeginAnimation(
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
                    if (_isWorkspaceOpen)
                        StartWorkspaceReturnOrbBreathing();
                };

                WorkspaceReturnOrbScale.BeginAnimation(ScaleTransform.ScaleXProperty, scale);
                WorkspaceReturnOrbScale.BeginAnimation(ScaleTransform.ScaleYProperty, scale.Clone());
                return;
            }

            StopWorkspaceReturnOrbBreathing();
            var returnPoint = GetCenterOrbCenterInPage();
            double currentLeft = WorkspaceReturnOrbTranslate.X;
            double currentTop = WorkspaceReturnOrbTranslate.Y;
            double returnLeft = ToWorkspaceOrbLeft(returnPoint);
            double returnTop = ToWorkspaceOrbTop(returnPoint);

            var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(360))
            {
                BeginTime = TimeSpan.FromMilliseconds(70),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };
            fade.Completed += (sender, args) =>
            {
                if (!_isWorkspaceOpen && WorkspaceReturnOrbButton != null)
                    WorkspaceReturnOrbButton.Visibility = Visibility.Collapsed;
            };

            WorkspaceReturnOrbButton.BeginAnimation(UIElement.OpacityProperty, fade);
            WorkspaceReturnOrbTranslate.BeginAnimation(
                TranslateTransform.XProperty,
                new DoubleAnimation
                {
                    From = currentLeft,
                    To = returnLeft,
                    Duration = TimeSpan.FromMilliseconds(420),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
                });
            WorkspaceReturnOrbTranslate.BeginAnimation(
                TranslateTransform.YProperty,
                new DoubleAnimation
                {
                    From = currentTop,
                    To = returnTop,
                    Duration = TimeSpan.FromMilliseconds(420),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
                });
            WorkspaceReturnOrbScale.BeginAnimation(
                ScaleTransform.ScaleXProperty,
                new DoubleAnimation(0.42, TimeSpan.FromMilliseconds(420)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } });
            WorkspaceReturnOrbScale.BeginAnimation(
                ScaleTransform.ScaleYProperty,
                new DoubleAnimation(0.42, TimeSpan.FromMilliseconds(420)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } });
        }

        private WindowsPoint GetCenterOrbCenterInPage()
        {
            try
            {
                if (CenterOrbButton != null &&
                    CenterOrbButton.ActualWidth > 0 &&
                    CenterOrbButton.ActualHeight > 0)
                {
                    return CenterOrbButton.TranslatePoint(
                        new WindowsPoint(CenterOrbButton.ActualWidth / 2, CenterOrbButton.ActualHeight / 2),
                        this);
                }
            }
            catch
            {
            }

            double width = WorkspaceLayer?.ActualWidth > 0 ? WorkspaceLayer.ActualWidth : ActualWidth;
            double height = WorkspaceLayer?.ActualHeight > 0 ? WorkspaceLayer.ActualHeight : ActualHeight;
            return new WindowsPoint(width / 2, height / 2);
        }

        private WindowsPoint GetWorkspaceReturnOrbTargetCenter()
        {
            double width = WorkspaceLayer?.ActualWidth > 0 ? WorkspaceLayer.ActualWidth : ActualWidth;
            if (width <= 0)
                width = 1220;

            return new WindowsPoint(width * WorkspaceReturnOrbTargetXRatio, WorkspaceReturnOrbTargetCenterY);
        }

        private void PositionWorkspaceReturnOrb(WindowsPoint center, double scale, double opacity)
        {
            if (WorkspaceReturnOrbButton == null || WorkspaceReturnOrbTranslate == null || WorkspaceReturnOrbScale == null)
                return;

            WorkspaceReturnOrbTranslate.X = ToWorkspaceOrbLeft(center);
            WorkspaceReturnOrbTranslate.Y = ToWorkspaceOrbTop(center);
            WorkspaceReturnOrbScale.ScaleX = scale;
            WorkspaceReturnOrbScale.ScaleY = scale;
            WorkspaceReturnOrbButton.Opacity = opacity;
        }

        private void ResetWorkspaceReturnOrbAnimation()
        {
            WorkspaceReturnOrbButton?.BeginAnimation(UIElement.OpacityProperty, null);
            WorkspaceReturnOrbTranslate?.BeginAnimation(TranslateTransform.XProperty, null);
            WorkspaceReturnOrbTranslate?.BeginAnimation(TranslateTransform.YProperty, null);
            WorkspaceReturnOrbScale?.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            WorkspaceReturnOrbScale?.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        }

        private void StartWorkspaceReturnOrbBreathing()
        {
            if (WorkspaceReturnOrbButton == null || WorkspaceReturnOrbScale == null)
                return;

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

            WorkspaceReturnOrbScale.BeginAnimation(ScaleTransform.ScaleXProperty, scale);
            WorkspaceReturnOrbScale.BeginAnimation(ScaleTransform.ScaleYProperty, scale.Clone());
            WorkspaceReturnOrbButton.BeginAnimation(UIElement.OpacityProperty, opacity);
        }

        private void StopWorkspaceReturnOrbBreathing()
        {
            WorkspaceReturnOrbButton?.BeginAnimation(UIElement.OpacityProperty, null);
            if (WorkspaceReturnOrbScale == null)
                return;

            WorkspaceReturnOrbScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            WorkspaceReturnOrbScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        }

        private double ToWorkspaceOrbLeft(WindowsPoint center)
        {
            double width = WorkspaceReturnOrbButton?.ActualWidth > 0 ? WorkspaceReturnOrbButton.ActualWidth : WorkspaceReturnOrbSize;
            return center.X - width / 2;
        }

        private double ToWorkspaceOrbTop(WindowsPoint center)
        {
            double height = WorkspaceReturnOrbButton?.ActualHeight > 0 ? WorkspaceReturnOrbButton.ActualHeight : WorkspaceReturnOrbSize;
            return center.Y - height / 2;
        }

        private void RefreshWorkspaceIfOpen()
        {
            if (!_isWorkspaceOpen)
                return;

            _workspaceNavigationVersion++;
            int navigationVersion = _workspaceNavigationVersion;
            ClearWorkspaceContentBeforeLoad();
            SetWorkspaceContentLoading(true);
            ScheduleWorkspaceContentBuild(navigationVersion, hasTarget: false, searchQuery: WorkspaceSearchTextBox?.Text ?? string.Empty, focusSearch: false);
        }

        private void ConfigureWorkspaceHeader()
        {
            var snapshot = _snapshot ?? new DeviceDriverDiagnosticsSnapshot();
            bool drivers = _workspaceMode == DeviceDriverDashboardMode.Drivers;

            WorkspaceEyebrowTextBlock.Text = "Выбранный узел";
            WorkspaceTitleTextBlock.Text = drivers ? "Драйверы" : "Устройства";
            WorkspaceSummaryTextBlock.Text = drivers
                ? GetSummaryText(snapshot.DriverSummary, "Данные о драйверах ещё не получены")
                : GetSummaryText(snapshot.DeviceSummary, "Данные об устройствах ещё не получены");
            WorkspaceSearchPlaceholderTextBlock.Text = drivers
                ? "Поиск по звуку, графике, INF, версии, производителю"
                : "Поиск по принтерам, USB, PnP, Hardware ID или устройствам";
        }

        private void RebuildWorkspaceGroups()
        {
            var snapshot = _snapshot ?? new DeviceDriverDiagnosticsSnapshot();
            _workspaceGroups.Clear();
            _workspaceGroups.AddRange(snapshot.GetGroups(_workspaceMode).Select(DeviceDriverGroupViewModel.FromGroup));

            var visibleFindings = GetVisibleFindings(_workspaceMode).ToList();
            UpdateWorkspaceFindingSummary(visibleFindings);

            var workspaceFindings = visibleFindings
                .Where(finding => MatchesWorkspaceFilter(finding.Level))
                .Select(DeviceReasonViewModel.FromFinding)
                .ToList();

            WorkspaceFindingsItemsControl.ItemsSource = workspaceFindings;
            WorkspaceFindingsEmptyText.Visibility = workspaceFindings.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void UpdateWorkspaceFindingSummary(IReadOnlyList<DeviceDriverFinding> findings)
        {
            if (WorkspaceFindingSummaryTextBlock == null)
                return;

            var selected = findings ?? Array.Empty<DeviceDriverFinding>();
            if (selected.Count == 0)
            {
                WorkspaceFindingSummaryTextBlock.Text = "Активных сигналов нет";
                WorkspaceFindingSummaryTextBlock.SetResourceReference(TextBlock.ForegroundProperty, "CoreGoodBrush");
                return;
            }

            int problemCount = selected.Count(item => IsProblemLevel(item.Level));
            int recommendationCount = selected.Count - problemCount;
            var highestLevel = selected.OrderByDescending(item => GetSeverity(item.Level)).First().Level;

            WorkspaceFindingSummaryTextBlock.Text = problemCount > 0
                ? $"{problemCount} проблем · {recommendationCount} рекомендаций"
                : $"{recommendationCount} рекомендаций";
            WorkspaceFindingSummaryTextBlock.SetResourceReference(TextBlock.ForegroundProperty, GetStatusBrushKey(highestLevel));
        }

        private void ApplyWorkspaceSearch()
        {
            string query = WorkspaceSearchTextBox?.Text?.Trim() ?? string.Empty;
            bool hasQuery = query.Length > 0;

            WorkspaceSearchPlaceholderTextBlock.Visibility = hasQuery ? Visibility.Collapsed : Visibility.Visible;
            WorkspaceSearchClearButton.Visibility = hasQuery ? Visibility.Visible : Visibility.Collapsed;

            var filtered = _workspaceGroups
                .Where(group => MatchesWorkspaceFilter(group.Level))
                .Where(group => MatchesSearch(group.SearchText, query))
                .Select(group => group.WithFilteredItems(query, _workspaceFilter))
                .Where(group => group.Items.Count > 0 || MatchesSearch(group.SearchText, query))
                .ToList();

            bool contentLoading = WorkspaceContentLoadingPanel?.Visibility == Visibility.Visible;
            WorkspaceGroupsItemsControl.ItemsSource = filtered;
            WorkspaceEmptyTextBlock.Visibility = !contentLoading && filtered.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            if (contentLoading)
                WorkspaceSearchSuggestionsItemsControl.Visibility = Visibility.Collapsed;
            else
                UpdateWorkspaceSearchSuggestions(query);
        }

        private static bool MatchesSearch(string searchText, string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return true;

            string source = searchText ?? string.Empty;
            string[] terms = query.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            return terms.All(term => source.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private void UpdateWorkspaceSearchSuggestions(string query)
        {
            if (WorkspaceSearchSuggestionsItemsControl == null)
                return;

            if (string.IsNullOrWhiteSpace(query) || _workspaceGroups.Count == 0)
            {
                WorkspaceSearchSuggestionsItemsControl.ItemsSource = null;
                WorkspaceSearchSuggestionsItemsControl.Visibility = Visibility.Collapsed;
                return;
            }

            var groupSuggestions = _workspaceGroups
                .Where(group => group != null && MatchesSearch(group.SearchText, query))
                .Take(3)
                .Select(group => new WorkspaceSearchSuggestionViewModel
                {
                    Title = group.Title,
                    Caption = "Раздел диагностики",
                    GroupId = group.Id
                });

            var itemSuggestions = _workspaceGroups
                .SelectMany(group => (group.AllItems ?? new List<DeviceDriverItemViewModel>())
                    .Where(item => item != null && MatchesSearch(item.SearchText, query))
                    .Take(4)
                    .Select(item => new WorkspaceSearchSuggestionViewModel
                    {
                        Title = item.Title,
                        Caption = string.IsNullOrWhiteSpace(group.Title) ? "Элемент узла" : $"Элемент: {group.Title}",
                        GroupId = group.Id,
                        ItemId = item.Id
                    }))
                .Take(6);

            var suggestions = groupSuggestions
                .Concat(itemSuggestions)
                .GroupBy(item => $"{item.GroupId}|{item.ItemId}|{item.Title}", StringComparer.CurrentCultureIgnoreCase)
                .Select(group => group.First())
                .Take(6)
                .ToList();

            WorkspaceSearchSuggestionsItemsControl.ItemsSource = suggestions;
            WorkspaceSearchSuggestionsItemsControl.Visibility = suggestions.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private static string FirstNotEmpty(params string[] values)
        {
            if (values == null)
                return string.Empty;

            return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
        }

        private void WorkspaceScrim_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            CloseWorkspace();
        }

        private void WorkspaceReturnOrbButton_Click(object sender, RoutedEventArgs e)
        {
            CloseWorkspace();
        }

        private void WorkspaceSearchTextBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            QueueWorkspaceSearchCaretAtStart();
        }

        private void WorkspaceSearchTextBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (WorkspaceSearchTextBox == null ||
                !string.IsNullOrEmpty(WorkspaceSearchTextBox.Text))
            {
                return;
            }

            e.Handled = true;
            if (!WorkspaceSearchTextBox.IsKeyboardFocusWithin)
                WorkspaceSearchTextBox.Focus();

            QueueWorkspaceSearchCaretAtStart();
        }

        private void WorkspaceSearchTextBox_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (WorkspaceSearchTextBox == null ||
                !string.IsNullOrEmpty(WorkspaceSearchTextBox.Text))
            {
                return;
            }

            e.Handled = true;
            QueueWorkspaceSearchCaretAtStart();
        }

        private void WorkspaceSearchTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter ||
                WorkspaceSearchSuggestionsItemsControl?.ItemsSource is not IEnumerable<WorkspaceSearchSuggestionViewModel> suggestions)
            {
                return;
            }

            var suggestion = suggestions.FirstOrDefault();
            if (suggestion == null)
                return;

            ApplyWorkspaceSearchSuggestion(suggestion);
            e.Handled = true;
        }

        private void WorkspaceSearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyWorkspaceSearch();
        }

        private void WorkspaceSearchClearButton_Click(object sender, RoutedEventArgs e)
        {
            _pendingWorkspaceTargetGroupId = string.Empty;
            _pendingWorkspaceTargetItemId = string.Empty;
            _workspaceNavigationVersion++;
            SetWorkspaceNavigationBusy(false);
            WorkspaceSearchTextBox.Text = string.Empty;
            WorkspaceSearchTextBox.Focus();
            WorkspaceSearchTextBox.CaretIndex = 0;
            WorkspaceSearchSuggestionsItemsControl.Visibility = Visibility.Collapsed;
        }

        private void WorkspaceSearchSuggestion_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement element ||
                element.DataContext is not WorkspaceSearchSuggestionViewModel suggestion)
            {
                return;
            }

            ApplyWorkspaceSearchSuggestion(suggestion);
        }

        private void ApplyWorkspaceSearchSuggestion(WorkspaceSearchSuggestionViewModel suggestion)
        {
            if (suggestion == null)
                return;

            _workspaceNavigationVersion++;
            _pendingWorkspaceTargetGroupId = suggestion.GroupId ?? string.Empty;
            _pendingWorkspaceTargetItemId = suggestion.ItemId ?? string.Empty;
            _workspaceFilter = DeviceWorkspaceFilter.All;
            UpdateWorkspaceFilterButtons();
            SetWorkspaceNavigationBusy(true);

            if (WorkspaceSearchSuggestionsItemsControl != null)
                WorkspaceSearchSuggestionsItemsControl.Visibility = Visibility.Collapsed;

            if (WorkspaceSearchTextBox != null)
            {
                WorkspaceSearchTextBox.Text = suggestion.Title ?? string.Empty;
                WorkspaceSearchTextBox.CaretIndex = 0;
            }

            ApplyWorkspaceSearch();
            if (WorkspaceSearchSuggestionsItemsControl != null)
                WorkspaceSearchSuggestionsItemsControl.Visibility = Visibility.Collapsed;
            ScheduleWorkspaceTargetFallback();
        }

        private void PlaceWorkspaceSearchCaretAtStartWhenEmpty()
        {
            if (WorkspaceSearchTextBox == null ||
                !string.IsNullOrEmpty(WorkspaceSearchTextBox.Text))
            {
                return;
            }

            WorkspaceSearchTextBox.CaretIndex = 0;
            WorkspaceSearchTextBox.Select(0, 0);
            WorkspaceSearchTextBox.ScrollToHorizontalOffset(0);
        }

        private void QueueWorkspaceSearchCaretAtStart()
        {
            Dispatcher.BeginInvoke(new Action(PlaceWorkspaceSearchCaretAtStartWhenEmpty), DispatcherPriority.Input);
        }

        private void WorkspaceFilterButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not ToggleButton button)
                return;

            string tag = button.Tag as string ?? string.Empty;
            _workspaceFilter = tag switch
            {
                "Problems" => DeviceWorkspaceFilter.Problems,
                "Recommendations" => DeviceWorkspaceFilter.Recommendations,
                "Critical" => DeviceWorkspaceFilter.Critical,
                _ => DeviceWorkspaceFilter.All
            };

            UpdateWorkspaceFilterButtons();
            RebuildWorkspaceGroups();
            ApplyWorkspaceSearch();
        }

        private void UpdateWorkspaceFilterButtons()
        {
            UpdateWorkspaceFilterButton(WorkspaceFilterAllButton, _workspaceFilter == DeviceWorkspaceFilter.All);
            UpdateWorkspaceFilterButton(WorkspaceFilterProblemsButton, _workspaceFilter == DeviceWorkspaceFilter.Problems);
            UpdateWorkspaceFilterButton(WorkspaceFilterRecommendationsButton, _workspaceFilter == DeviceWorkspaceFilter.Recommendations);
            UpdateWorkspaceFilterButton(WorkspaceFilterCriticalButton, _workspaceFilter == DeviceWorkspaceFilter.Critical);
        }

        private static void UpdateWorkspaceFilterButton(ToggleButton button, bool active)
        {
            if (button == null)
                return;

            button.IsChecked = active;
            button.Opacity = active ? 1 : 0.82;
        }

        private bool MatchesWorkspaceFilter(HealthLevel level)
        {
            return MatchesWorkspaceFilter(level, _workspaceFilter);
        }

        private static bool MatchesWorkspaceFilter(HealthLevel level, DeviceWorkspaceFilter filter)
        {
            return filter switch
            {
                DeviceWorkspaceFilter.Problems => IsProblemLevel(level),
                DeviceWorkspaceFilter.Recommendations => level == HealthLevel.Normal,
                DeviceWorkspaceFilter.Critical => level == HealthLevel.Critical,
                _ => true
            };
        }

        private void SignalRailItem_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is DependencyObject original && IsInsideElement<Button>(original))
                return;

            if (sender is not FrameworkElement element || element.DataContext is not DeviceReasonViewModel reason)
                return;

            OpenWorkspaceForSignal(reason);
            e.Handled = true;
        }

        private void OpenWorkspaceForSignal(DeviceReasonViewModel reason)
        {
            if (reason == null)
                return;

            if (_activeMode != reason.Mode && !_isWorkspaceOpen)
            {
                _activeMode = reason.Mode;
                UpdateActiveModeContent();
                ResetOrbitTransform();
            }

            OpenWorkspace(reason.Mode, reason.TargetGroupId, reason.TargetItemId);
        }

        private async void IgnoreSignal_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;

            if (sender is not Button button || button.Tag is not DeviceReasonViewModel reason || string.IsNullOrWhiteSpace(reason.Id))
                return;

            bool applied = await HealthSignalActionHelper.PromptAndApplyAsync(
                Window.GetWindow(this),
                new[] { reason.Id },
                reason.Title,
                IsProblemLevel(reason.Level));

            if (!applied)
                return;

            _locallyIgnoredSignalIds.Add(reason.Id);
            UpdateActiveModeContent();
            RefreshWorkspaceIfOpen();
            ApplyModuleStatus();
        }

        private async void InventoryActionButton_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            if (sender is not Button button || button.Tag is not DeviceDriverActionViewModel action)
                return;

            await ExecuteInventoryActionAsync(action);
        }

        private async void InstallDriverInfButton_Click(object sender, RoutedEventArgs e)
        {
            await ExecuteInventoryActionAsync(new DeviceDriverActionViewModel
            {
                Kind = DeviceDriverInventoryActionKind.InstallInf,
                Label = "Установить INF",
                Title = "Установить драйвер из INF",
                RiskText = "Риск: средний — устанавливайте только INF от производителя устройства или из доверенного бэкапа.",
                RequiresAdmin = true
            });
        }

        private async void RollbackDriverFromBackupButton_Click(object sender, RoutedEventArgs e)
        {
            await ExecuteInventoryActionAsync(new DeviceDriverActionViewModel
            {
                Kind = DeviceDriverInventoryActionKind.RollbackFromBackup,
                Label = "Откат из бэкапа",
                Title = "Откат драйвера из резервной копии",
                RiskText = "Риск: средний — проверьте устройство, производителя и дату INF перед откатом.",
                RequiresAdmin = true,
                IsDestructive = true
            });
        }

        private async void BackupDriversButton_Click(object sender, RoutedEventArgs e)
        {
            await ExecuteInventoryActionAsync(new DeviceDriverActionViewModel
            {
                Kind = DeviceDriverInventoryActionKind.BackupDrivers,
                Label = "Бэкап драйверов",
                Title = "Создать резервную копию драйверов",
                RiskText = "Риск: низкий — операция только экспортирует установленные пакеты драйверов.",
                RequiresAdmin = true
            });
        }

        private async void ScanDevicesButton_Click(object sender, RoutedEventArgs e)
        {
            await ExecuteInventoryActionAsync(new DeviceDriverActionViewModel
            {
                Kind = DeviceDriverInventoryActionKind.ScanDevices,
                Label = "Сканировать устройства",
                Title = "Повторно просканировать PnP-устройства",
                RiskText = "Риск: низкий — Windows повторно проверит подключённые устройства.",
                RequiresAdmin = true
            });
        }

        private async void OpenDeviceManagerButton_Click(object sender, RoutedEventArgs e)
        {
            await ExecuteInventoryActionAsync(new DeviceDriverActionViewModel
            {
                Kind = DeviceDriverInventoryActionKind.OpenDeviceManager,
                Label = "Диспетчер устройств",
                Title = "Открыть диспетчер устройств"
            });
        }

        private async void EnableSafeModeButton_Click(object sender, RoutedEventArgs e)
        {
            await ExecuteInventoryActionAsync(new DeviceDriverActionViewModel
            {
                Kind = DeviceDriverInventoryActionKind.EnableSafeMode,
                Label = "Safe Mode",
                Title = "Включить загрузку Safe Mode",
                RiskText = "Риск: высокий — следующая загрузка Windows пойдёт в безопасный режим. После диагностики отключите Safe Mode.",
                RequiresAdmin = true,
                IsDestructive = true
            });
        }

        private async void DisableSafeModeButton_Click(object sender, RoutedEventArgs e)
        {
            await ExecuteInventoryActionAsync(new DeviceDriverActionViewModel
            {
                Kind = DeviceDriverInventoryActionKind.DisableSafeMode,
                Label = "Отключить Safe Mode",
                Title = "Отключить загрузку Safe Mode",
                RiskText = "Риск: средний — команда меняет текущую запись загрузчика Windows.",
                RequiresAdmin = true
            });
        }

        private async Task ExecuteInventoryActionAsync(DeviceDriverActionViewModel action)
        {
            if (action == null)
                return;

            if (!ConfirmInventoryAction(action))
                return;

            DeviceDriverOperationResult result;
            try
            {
                result = await Task.Run(() => ExecuteInventoryAction(action));
            }
            catch (Exception ex)
            {
                result = DeviceDriverOperationResult.Fail(ex.Message);
            }

            if (!IsSilentAction(action.Kind) || !result.Success)
                ShowOperationResult(action, result);

            if (result.Success && ShouldRefreshAfterAction(action.Kind))
                await RefreshDiagnosticsAsync();
        }

        private DeviceDriverOperationResult ExecuteInventoryAction(DeviceDriverActionViewModel action)
        {
            return action.Kind switch
            {
                DeviceDriverInventoryActionKind.SearchOnline => DeviceDriverDiagnosticsService.SearchOnline(action.SearchQuery),
                DeviceDriverInventoryActionKind.InstallInf => ExecuteInstallInfAction(action, rollback: false),
                DeviceDriverInventoryActionKind.RollbackFromBackup => ExecuteInstallInfAction(action, rollback: true),
                DeviceDriverInventoryActionKind.BackupDrivers => DeviceDriverDiagnosticsService.BackupDrivers(DeviceDriverDiagnosticsService.CreateDriverBackupFolder()),
                DeviceDriverInventoryActionKind.EnableDevice => DeviceDriverDiagnosticsService.EnableDevice(action.InstanceId),
                DeviceDriverInventoryActionKind.DisableDevice => DeviceDriverDiagnosticsService.DisableDevice(action.InstanceId),
                DeviceDriverInventoryActionKind.RestartDevice => DeviceDriverDiagnosticsService.RestartDevice(action.InstanceId),
                DeviceDriverInventoryActionKind.ScanDevices => DeviceDriverDiagnosticsService.ScanDevices(),
                DeviceDriverInventoryActionKind.OpenDeviceManager => DeviceDriverDiagnosticsService.OpenDeviceManager(),
                DeviceDriverInventoryActionKind.OpenPrinterQueue => DeviceDriverDiagnosticsService.OpenPrinterQueue(action.PrinterName),
                DeviceDriverInventoryActionKind.EnableSafeMode => DeviceDriverDiagnosticsService.EnableSafeMode(),
                DeviceDriverInventoryActionKind.DisableSafeMode => DeviceDriverDiagnosticsService.DisableSafeMode(),
                _ => DeviceDriverOperationResult.Fail("Действие пока не поддерживается.")
            };
        }

        private DeviceDriverOperationResult ExecuteInstallInfAction(DeviceDriverActionViewModel action, bool rollback)
        {
            string initialDirectory = rollback ? DeviceDriverDiagnosticsService.GetDriverBackupRoot() : string.Empty;
            string infPath = PickInfFile(initialDirectory);
            if (string.IsNullOrWhiteSpace(infPath))
                return DeviceDriverOperationResult.Fail("INF-файл не выбран.");

            return DeviceDriverDiagnosticsService.InstallInf(infPath);
        }

        private string PickInfFile(string initialDirectory)
        {
            string selected = string.Empty;
            Dispatcher.Invoke(() =>
            {
                var dialog = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "Выберите INF-файл драйвера",
                    Filter = "INF-файлы драйверов (*.inf)|*.inf|Все файлы (*.*)|*.*",
                    CheckFileExists = true,
                    Multiselect = false
                };

                if (!string.IsNullOrWhiteSpace(initialDirectory) && System.IO.Directory.Exists(initialDirectory))
                    dialog.InitialDirectory = initialDirectory;

                if (dialog.ShowDialog(Window.GetWindow(this)) == true)
                    selected = dialog.FileName;
            });

            return selected;
        }

        private bool ConfirmInventoryAction(DeviceDriverActionViewModel action)
        {
            if (action.Kind == DeviceDriverInventoryActionKind.SearchOnline ||
                action.Kind == DeviceDriverInventoryActionKind.OpenDeviceManager ||
                action.Kind == DeviceDriverInventoryActionKind.OpenPrinterQueue)
                return true;

            string header = action.IsDestructive ? "Подтвердите рискованное действие" : "Подтвердите действие";
            string message = $"{action.Title}\n\n{FirstNotEmpty(action.Description, action.Label)}";

            if (!string.IsNullOrWhiteSpace(action.RiskText))
                message += $"\n\n{action.RiskText}";

            if (!string.IsNullOrWhiteSpace(action.InstanceId))
                message += $"\n\nInstance ID: {action.InstanceId}";

            if (action.RequiresAdmin)
                message += "\n\nWindows может запросить права администратора через UAC.";

            var result = App.DialogManager.Show(
                Window.GetWindow(this),
                "Драйверы и устройства",
                header,
                message,
                action.IsDestructive ? AppDialogKind.Warning : AppDialogKind.Info,
                AppDialogButtons.YesNo);

            return result == AppDialogResult.Primary;
        }

        private void ShowOperationResult(DeviceDriverActionViewModel action, DeviceDriverOperationResult result)
        {
            App.DialogManager.Show(
                Window.GetWindow(this),
                "Драйверы и устройства",
                result.Success ? "Команда запущена" : "Команда не выполнена",
                result.Message,
                result.Success ? AppDialogKind.Success : AppDialogKind.Warning,
                AppDialogButtons.Ok);
        }

        private static bool ShouldRefreshAfterAction(DeviceDriverInventoryActionKind kind)
        {
            return kind == DeviceDriverInventoryActionKind.InstallInf ||
                   kind == DeviceDriverInventoryActionKind.RollbackFromBackup ||
                   kind == DeviceDriverInventoryActionKind.BackupDrivers ||
                   kind == DeviceDriverInventoryActionKind.EnableDevice ||
                   kind == DeviceDriverInventoryActionKind.DisableDevice ||
                   kind == DeviceDriverInventoryActionKind.RestartDevice ||
                   kind == DeviceDriverInventoryActionKind.ScanDevices;
        }

        private static bool IsSilentAction(DeviceDriverInventoryActionKind kind)
        {
            return kind == DeviceDriverInventoryActionKind.SearchOnline ||
                   kind == DeviceDriverInventoryActionKind.OpenDeviceManager ||
                   kind == DeviceDriverInventoryActionKind.OpenPrinterQueue;
        }

        private void WorkspaceCard_MouseEnter(object sender, MouseEventArgs e)
        {
            if (sender is not FrameworkElement element)
                return;

            var transform = EnsureCardTransform(element);
            transform.BeginAnimation(TranslateTransform.YProperty, CreateAnimation(-3, 140));
            AnimateOpacity(element, 0.96, 140);
        }

        private void WorkspaceCard_MouseLeave(object sender, MouseEventArgs e)
        {
            if (sender is not FrameworkElement element)
                return;

            var transform = EnsureCardTransform(element);
            transform.BeginAnimation(TranslateTransform.YProperty, CreateAnimation(0, 140));
            AnimateOpacity(element, 1, 140);
        }

        private static TranslateTransform EnsureCardTransform(FrameworkElement element)
        {
            if (element.RenderTransform is TranslateTransform translate && !translate.IsFrozen)
                return translate;

            translate = new TranslateTransform();
            element.RenderTransform = translate;
            return translate;
        }

        private void SignalRailItem_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is not Border border)
                return;

            HealthLevel level = border.DataContext switch
            {
                DeviceReasonViewModel signalReason => signalReason.Level,
                _ => HealthLevel.Good
            };

            string brushKey = GetStatusBrushKey(level);
            border.SetResourceReference(Border.BorderBrushProperty, brushKey);

            var accent = FindVisualChild<Border>(border, "RailAccent");
            accent?.SetResourceReference(Border.BackgroundProperty, brushKey);

            SetBadgeBrush(border, "SignalStatusBadge", "SignalStatusLabel", level);
            if (border.DataContext is DeviceReasonViewModel reason)
                SetBadgeBrush(border, "SignalRiskBadge", "SignalRiskLabel", reason.RiskLevel);
        }

        private void WorkspaceGroup_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is not Border border || border.DataContext is not DeviceDriverGroupViewModel group)
                return;

            border.SetResourceReference(Border.BorderBrushProperty, GetStatusBrushKey(group.Level));

            if (!string.IsNullOrWhiteSpace(_pendingWorkspaceTargetGroupId) &&
                string.Equals(group.Id, _pendingWorkspaceTargetGroupId, StringComparison.OrdinalIgnoreCase) &&
                string.IsNullOrWhiteSpace(_pendingWorkspaceTargetItemId))
            {
                _pendingWorkspaceTargetGroupId = string.Empty;
                CompleteWorkspaceTargetNavigation(border);
            }
        }

        private void WorkspaceInventoryCard_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is not Border border || border.DataContext is not DeviceDriverItemViewModel item)
                return;

            border.SetResourceReference(Border.BorderBrushProperty, GetStatusBrushKey(item.Level));
            SetBadgeBrush(border, "ItemStatusBadge", "ItemStatusLabel", item.Level);
            SetBadgeBrush(border, "ItemRiskBadge", "ItemRiskLabel", item.RiskLevel);

            if (!string.IsNullOrWhiteSpace(_pendingWorkspaceTargetItemId) &&
                string.Equals(item.Id, _pendingWorkspaceTargetItemId, StringComparison.OrdinalIgnoreCase))
            {
                _pendingWorkspaceTargetItemId = string.Empty;
                _pendingWorkspaceTargetGroupId = string.Empty;
                CompleteWorkspaceTargetNavigation(border);
            }
        }

        private void SetBadgeBrush(DependencyObject root, string badgeName, string labelName, HealthLevel level)
        {
            string brushKey = GetStatusBrushKey(level);
            var badge = FindVisualChild<Border>(root, badgeName);
            var label = FindVisualChild<TextBlock>(root, labelName);

            badge?.SetResourceReference(Border.BorderBrushProperty, brushKey);
            label?.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
        }

        private void CompleteWorkspaceTargetNavigation(FrameworkElement element)
        {
            if (element == null)
                return;

            _workspaceNavigationVersion++;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                element.BringIntoView();
                PlaySearchResultHighlight(element);
                Dispatcher.BeginInvoke(new Action(() => SetWorkspaceNavigationBusy(false)), DispatcherPriority.Background);
            }), DispatcherPriority.Background);
        }

        private async void ScheduleWorkspaceTargetFallback()
        {
            int version = _workspaceNavigationVersion;
            await Task.Delay(900);

            if (!_isPageActive || version != _workspaceNavigationVersion)
                return;

            if (string.IsNullOrWhiteSpace(_pendingWorkspaceTargetGroupId) &&
                string.IsNullOrWhiteSpace(_pendingWorkspaceTargetItemId))
            {
                return;
            }

            _pendingWorkspaceTargetGroupId = string.Empty;
            _pendingWorkspaceTargetItemId = string.Empty;
            SetWorkspaceNavigationBusy(false);
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

        private void SetLoading(bool isLoading)
        {
            if (LoadingOverlay == null)
                return;

            LoadingOverlay.Visibility = isLoading ? Visibility.Visible : Visibility.Collapsed;
            LoadingOverlay.BeginAnimation(UIElement.OpacityProperty, CreateAnimation(isLoading ? 1 : 0, 160));

            if (isLoading)
                StartLoadingSquares(GetPrimaryLoadingSquares());
            else
                StopLoadingSquares(GetPrimaryLoadingSquares());
        }

        private void SetWorkspaceContentLoading(bool isLoading)
        {
            if (WorkspaceContentLoadingPanel == null)
                return;

            WorkspaceContentLoadingPanel.Visibility = isLoading ? Visibility.Visible : Visibility.Collapsed;
            if (WorkspaceNodeLoadingOverlay != null)
            {
                WorkspaceNodeLoadingOverlay.Visibility = isLoading ? Visibility.Visible : Visibility.Collapsed;
                WorkspaceNodeLoadingOverlay.BeginAnimation(UIElement.OpacityProperty, CreateAnimation(isLoading ? 1 : 0, 130));
            }

            if (isLoading)
            {
                WorkspaceEmptyTextBlock.Visibility = Visibility.Collapsed;
                WorkspaceSearchSuggestionsItemsControl.Visibility = Visibility.Collapsed;
                StartLoadingSquares(GetWorkspaceContentLoadingSquares());
                StartLoadingSquares(GetWorkspaceNodeLoadingSquares());
            }
            else
            {
                StopLoadingSquares(GetWorkspaceContentLoadingSquares());
                StopLoadingSquares(GetWorkspaceNodeLoadingSquares());
            }
        }

        private void SetWorkspaceNavigationBusy(bool isBusy)
        {
            if (WorkspaceNavigationBusyOverlay == null)
                return;

            WorkspaceNavigationBusyOverlay.Visibility = isBusy ? Visibility.Visible : Visibility.Collapsed;
            WorkspaceNavigationBusyOverlay.Opacity = isBusy ? 1 : 0;

            if (isBusy)
            {
                WorkspaceEmptyTextBlock.Visibility = Visibility.Collapsed;
                WorkspaceSearchSuggestionsItemsControl.Visibility = Visibility.Collapsed;
                StartLoadingSquares(GetWorkspaceNavigationLoadingSquares());
            }
            else
            {
                StopLoadingSquares(GetWorkspaceNavigationLoadingSquares());
            }
        }

        private void StartLoadingSquares(params FrameworkElement[] targets)
        {
            FrameworkElement[] squares = targets == null || targets.Length == 0
                ? GetAllLoadingSquares()
                : targets;

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

        private void StopLoadingSquares(params FrameworkElement[] targets)
        {
            FrameworkElement[] squares = targets == null || targets.Length == 0
                ? GetAllLoadingSquares()
                : targets;

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

        private FrameworkElement[] GetPrimaryLoadingSquares()
        {
            return new FrameworkElement[] { LoadingSquareA, LoadingSquareB, LoadingSquareC, LoadingSquareD };
        }

        private FrameworkElement[] GetWorkspaceContentLoadingSquares()
        {
            return new FrameworkElement[]
            {
                WorkspaceContentLoadingSquareA,
                WorkspaceContentLoadingSquareB,
                WorkspaceContentLoadingSquareC,
                WorkspaceContentLoadingSquareD
            };
        }

        private FrameworkElement[] GetWorkspaceNodeLoadingSquares()
        {
            return new FrameworkElement[]
            {
                WorkspaceNodeLoadingSquareA,
                WorkspaceNodeLoadingSquareB,
                WorkspaceNodeLoadingSquareC,
                WorkspaceNodeLoadingSquareD
            };
        }

        private FrameworkElement[] GetWorkspaceNavigationLoadingSquares()
        {
            return new FrameworkElement[] { WorkspaceBusySquareA, WorkspaceBusySquareB, WorkspaceBusySquareC, WorkspaceBusySquareD };
        }

        private FrameworkElement[] GetAllLoadingSquares()
        {
            return GetPrimaryLoadingSquares()
                .Concat(GetWorkspaceContentLoadingSquares())
                .Concat(GetWorkspaceNodeLoadingSquares())
                .Concat(GetWorkspaceNavigationLoadingSquares())
                .Where(square => square != null)
                .Distinct()
                .ToArray();
        }

        private void NavigateHome()
        {
            if (Application.Current.MainWindow is MainWindow mainWindow)
                mainWindow.NavigateToCoreHome();
        }

        private static DeviceDriverDiagnosticsSnapshot BuildErrorSnapshot(Exception ex)
        {
            return new DeviceDriverDiagnosticsSnapshot
            {
                DriverSummary = "Диагностика прервана",
                DeviceSummary = "Диагностика прервана",
                Findings = new List<DeviceDriverFinding>
                {
                    new DeviceDriverFinding
                    {
                        Id = "devices.page.scan-error",
                        Mode = DeviceDriverDashboardMode.Drivers,
                        Level = HealthLevel.Warning,
                        Title = "Диагностика не завершилась",
                        Description = "Раздел не смог получить сведения без открытия стандартных окон Windows.",
                        ActionText = ex.Message
                    },
                    new DeviceDriverFinding
                    {
                        Id = "devices.page.scan-error.devices",
                        Mode = DeviceDriverDashboardMode.Devices,
                        Level = HealthLevel.Warning,
                        Title = "Диагностика устройств не завершилась",
                        Description = "Раздел не смог получить сведения о PnP-устройствах.",
                        ActionText = ex.Message
                    }
                }
            };
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

        private static void AnimateOpacity(UIElement element, double value, int milliseconds, Action completed = null)
        {
            if (element == null)
                return;

            var animation = CreateAnimation(value, milliseconds);
            if (completed != null)
                animation.Completed += (_, _) => completed();

            element.BeginAnimation(UIElement.OpacityProperty, animation);
        }

        private static string GetSummaryText(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value;
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

        private static string FormatCount(int count, string one, string few, string many)
        {
            int mod100 = Math.Abs(count) % 100;
            int mod10 = Math.Abs(count) % 10;
            string word = mod100 is >= 11 and <= 14
                ? many
                : mod10 == 1
                    ? one
                    : mod10 is >= 2 and <= 4
                        ? few
                        : many;

            return $"{count} {word}";
        }

        private static T FindVisualChild<T>(DependencyObject root, string name) where T : FrameworkElement
        {
            if (root == null)
                return null;

            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int index = 0; index < count; index++)
            {
                var child = VisualTreeHelper.GetChild(root, index);
                if (child is T element && string.Equals(element.Name, name, StringComparison.Ordinal))
                    return element;

                var nested = FindVisualChild<T>(child, name);
                if (nested != null)
                    return nested;
            }

            return null;
        }

        private static bool IsInsideElement<T>(DependencyObject source)
            where T : DependencyObject
        {
            var current = source;
            while (current != null)
            {
                if (current is T)
                    return true;

                current = VisualTreeHelper.GetParent(current);
            }

            return false;
        }

        private static bool IsInsideElement(DependencyObject source, DependencyObject target)
        {
            if (source == null || target == null)
                return false;

            var current = source;
            while (current != null)
            {
                if (ReferenceEquals(current, target))
                    return true;

                current = VisualTreeHelper.GetParent(current);
            }

            return false;
        }

        private sealed class WorkspaceContentBuildResult
        {
            public List<DeviceDriverGroupViewModel> Groups { get; set; } = new List<DeviceDriverGroupViewModel>();
            public List<DeviceDriverGroupViewModel> FilteredGroups { get; set; } = new List<DeviceDriverGroupViewModel>();
            public List<DeviceDriverFinding> VisibleFindings { get; set; } = new List<DeviceDriverFinding>();
            public List<DeviceReasonViewModel> FilteredFindings { get; set; } = new List<DeviceReasonViewModel>();
        }

        private enum DeviceWorkspaceFilter
        {
            All,
            Problems,
            Recommendations,
            Critical
        }

        private sealed class DeviceReasonViewModel
        {
            public string Id { get; set; } = string.Empty;
            public DeviceDriverDashboardMode Mode { get; set; }
            public HealthLevel Level { get; set; }
            public string LevelText { get; set; } = string.Empty;
            public string KindText => string.IsNullOrWhiteSpace(LevelText)
                ? string.Empty
                : LevelText.ToUpperInvariant();
            public string Title { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
            public string ActionText { get; set; } = string.Empty;

            public static DeviceReasonViewModel FromFinding(DeviceDriverFinding finding)
            {
                if (finding == null)
                    return new DeviceReasonViewModel();

                return new DeviceReasonViewModel
                {
                    Id = finding.Id ?? string.Empty,
                    Mode = finding.Mode,
                    Level = finding.Level,
                    LevelText = GetLevelText(finding.Level),
                    Title = finding.Title ?? string.Empty,
                    Description = finding.Description ?? string.Empty,
                    ActionText = finding.ActionText ?? string.Empty,
                    TargetGroupId = finding.TargetGroupId ?? string.Empty,
                    TargetItemId = finding.TargetItemId ?? string.Empty,
                    RiskLabel = GetRiskLabel(finding.Level),
                    RiskLevel = GetRiskLevel(finding.Level)
                };
            }

            public static DeviceReasonViewModel CreateGood(DeviceDriverDashboardMode mode)
            {
                return new DeviceReasonViewModel
                {
                    Mode = mode,
                    Level = HealthLevel.Good,
                    LevelText = "В норме",
                    Title = mode == DeviceDriverDashboardMode.Drivers ? "Драйверы без явных проблем" : "Устройства без явных проблем",
                    Description = "Критичных сигналов не найдено.",
                    ActionText = "Откройте раздел, чтобы посмотреть реальные группы устройств и драйверов."
                };
            }

            public string TargetGroupId { get; set; } = string.Empty;
            public string TargetItemId { get; set; } = string.Empty;
            public string RiskLabel { get; set; } = string.Empty;
            public HealthLevel RiskLevel { get; set; } = HealthLevel.Good;
            public Visibility RiskVisibility => string.IsNullOrWhiteSpace(RiskLabel) ? Visibility.Collapsed : Visibility.Visible;
            public bool CanIgnore => !string.IsNullOrWhiteSpace(Id) && Level != HealthLevel.Good;
            public Visibility IgnoreVisibility => CanIgnore ? Visibility.Visible : Visibility.Collapsed;

            private static string ResolveTargetSearchText(DeviceDriverFinding finding)
            {
                string id = finding?.Id ?? string.Empty;
                return id switch
                {
                    "drivers.unsigned" => "без подписи",
                    "drivers.old" => "Проверить обновление",
                    "drivers.microsoft-fallback" => "Microsoft",
                    "drivers.backup-missing" => "Бэкап драйверов",
                    "devices.problem" => "Код PnP",
                    "devices.unknown" => "Hardware ID",
                    "devices.printer-offline" => "Очередь печати",
                    "devices.printer-generic-driver" => "Принтер",
                    _ => string.Empty
                };
            }

            private static string GetLevelText(HealthLevel level)
            {
                return level switch
                {
                    HealthLevel.Critical => "Критично",
                    HealthLevel.Warning or HealthLevel.Attention => "Проблема",
                    HealthLevel.Normal => "Рекомендация",
                    HealthLevel.Good => "В норме",
                    _ => "Сигнал"
                };
            }

            private static string GetRiskLabel(HealthLevel level)
            {
                return level switch
                {
                    HealthLevel.Critical => "Риск: высокий",
                    HealthLevel.Warning or HealthLevel.Attention => "Риск: средний",
                    HealthLevel.Normal => "Риск: низкий",
                    _ => string.Empty
                };
            }

            private static HealthLevel GetRiskLevel(HealthLevel level)
            {
                return level switch
                {
                    HealthLevel.Critical => HealthLevel.Critical,
                    HealthLevel.Warning or HealthLevel.Attention => HealthLevel.Warning,
                    HealthLevel.Normal => HealthLevel.Normal,
                    _ => HealthLevel.Good
                };
            }

            private static string GetKindLabel(DeviceDriverInventoryItemKind kind)
            {
                return kind switch
                {
                    DeviceDriverInventoryItemKind.Driver => "Драйвер",
                    DeviceDriverInventoryItemKind.Device => "Устройство",
                    DeviceDriverInventoryItemKind.Printer => "Принтер",
                    DeviceDriverInventoryItemKind.Tool => "Инструмент",
                    _ => "Элемент"
                };
            }

            private static string GetStatusLabel(HealthLevel level)
            {
                return level switch
                {
                    HealthLevel.Critical => "Критично",
                    HealthLevel.Warning or HealthLevel.Attention => "Проблема",
                    HealthLevel.Normal => "Рекомендация",
                    HealthLevel.Good => "В норме",
                    _ => "Статус"
                };
            }

            private static string GetRiskLabel(string riskText, HealthLevel level)
            {
                string normalized = riskText ?? string.Empty;
                if (normalized.IndexOf("высок", StringComparison.CurrentCultureIgnoreCase) >= 0 ||
                    normalized.IndexOf("high", StringComparison.OrdinalIgnoreCase) >= 0)
                    return "Риск: высокий";

                if (normalized.IndexOf("сред", StringComparison.CurrentCultureIgnoreCase) >= 0 ||
                    normalized.IndexOf("medium", StringComparison.OrdinalIgnoreCase) >= 0)
                    return "Риск: средний";

                if (normalized.IndexOf("низ", StringComparison.CurrentCultureIgnoreCase) >= 0 ||
                    normalized.IndexOf("low", StringComparison.OrdinalIgnoreCase) >= 0)
                    return "Риск: низкий";

                return level switch
                {
                    HealthLevel.Critical => "Риск: высокий",
                    HealthLevel.Warning or HealthLevel.Attention => "Риск: средний",
                    HealthLevel.Normal => "Риск: низкий",
                    _ => string.Empty
                };
            }

            private static HealthLevel GetRiskLevel(string riskText, HealthLevel level)
            {
                string normalized = riskText ?? string.Empty;
                if (normalized.IndexOf("высок", StringComparison.CurrentCultureIgnoreCase) >= 0 ||
                    normalized.IndexOf("high", StringComparison.OrdinalIgnoreCase) >= 0)
                    return HealthLevel.Critical;

                if (normalized.IndexOf("сред", StringComparison.CurrentCultureIgnoreCase) >= 0 ||
                    normalized.IndexOf("medium", StringComparison.OrdinalIgnoreCase) >= 0)
                    return HealthLevel.Warning;

                if (normalized.IndexOf("низ", StringComparison.CurrentCultureIgnoreCase) >= 0 ||
                    normalized.IndexOf("low", StringComparison.OrdinalIgnoreCase) >= 0)
                    return HealthLevel.Normal;

                return level switch
                {
                    HealthLevel.Critical => HealthLevel.Critical,
                    HealthLevel.Warning or HealthLevel.Attention => HealthLevel.Warning,
                    HealthLevel.Normal => HealthLevel.Normal,
                    _ => HealthLevel.Good
                };
            }
        }

        private sealed class DeviceDriverGroupViewModel
        {
            public string Id { get; set; } = string.Empty;
            public HealthLevel Level { get; set; }
            public string Icon { get; set; } = string.Empty;
            public string Title { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
            public string Summary { get; set; } = string.Empty;
            public List<DeviceDriverItemViewModel> AllItems { get; set; } = new List<DeviceDriverItemViewModel>();
            public List<DeviceDriverItemViewModel> Items { get; set; } = new List<DeviceDriverItemViewModel>();
            public string HiddenItemsText { get; set; } = string.Empty;
            public Visibility HiddenItemsVisibility => string.IsNullOrWhiteSpace(HiddenItemsText) ? Visibility.Collapsed : Visibility.Visible;
            public string SearchText { get; set; } = string.Empty;

            public static DeviceDriverGroupViewModel FromGroup(DeviceDriverGroupSnapshot group)
            {
                var items = (group.Items ?? new List<DeviceDriverInventoryItem>())
                    .Select(DeviceDriverItemViewModel.FromItem)
                    .ToList();
                var shownItems = LimitRenderedItems(items, WorkspaceItemRenderLimit);

                return new DeviceDriverGroupViewModel
                {
                    Id = group.Id ?? string.Empty,
                    Level = group.Level,
                    Icon = group.Icon ?? string.Empty,
                    Title = group.Title ?? string.Empty,
                    Description = group.Description ?? string.Empty,
                    Summary = group.Summary ?? string.Empty,
                    AllItems = items,
                    Items = shownItems,
                    HiddenItemsText = GetHiddenItemsText(items.Count, shownItems.Count),
                    SearchText = string.Join(" ", new[]
                    {
                        group.Title,
                        group.Description,
                        group.Summary,
                        string.Join(" ", items.Select(item => item.SearchText))
                    }.Where(value => !string.IsNullOrWhiteSpace(value)))
                };
            }

            public DeviceDriverGroupViewModel WithFilteredItems(string query, DeviceWorkspaceFilter filter)
            {
                bool hasQuery = !string.IsNullOrWhiteSpace(query);
                bool FilterItem(DeviceDriverItemViewModel item)
                {
                    if (item == null)
                        return false;

                    bool levelMatches = filter switch
                    {
                        DeviceWorkspaceFilter.Problems => IsProblemLevel(item.Level),
                        DeviceWorkspaceFilter.Recommendations => item.Level == HealthLevel.Normal,
                        DeviceWorkspaceFilter.Critical => item.Level == HealthLevel.Critical,
                        _ => true
                    };

                    return levelMatches && (!hasQuery || MatchesSearch(item.SearchText, query));
                }

                var sourceItems = AllItems ?? new List<DeviceDriverItemViewModel>();
                var matchedItems = sourceItems.Where(FilterItem).ToList();
                int limit = hasQuery ? WorkspaceSearchItemRenderLimit : WorkspaceItemRenderLimit;
                var shownItems = LimitRenderedItems(matchedItems, limit);

                return new DeviceDriverGroupViewModel
                {
                    Id = Id,
                    Level = Level,
                    Icon = Icon,
                    Title = Title,
                    Description = Description,
                    Summary = Summary,
                    SearchText = SearchText,
                    AllItems = sourceItems,
                    Items = shownItems,
                    HiddenItemsText = GetHiddenItemsText(matchedItems.Count, shownItems.Count)
                };
            }

            private static List<DeviceDriverItemViewModel> LimitRenderedItems(IReadOnlyList<DeviceDriverItemViewModel> items, int limit)
            {
                if (items == null || items.Count == 0)
                    return new List<DeviceDriverItemViewModel>();

                return items.Take(Math.Max(1, limit)).ToList();
            }

            private static string GetHiddenItemsText(int total, int shown)
            {
                int hidden = Math.Max(0, total - shown);
                return hidden > 0
                    ? $"+ ещё {hidden} элементов доступны через поиск"
                    : string.Empty;
            }
        }

        private sealed class DeviceDriverItemViewModel
        {
            public string Id { get; set; } = string.Empty;
            public DeviceDriverInventoryItemKind Kind { get; set; }
            public HealthLevel Level { get; set; }
            public string KindLabel { get; set; } = string.Empty;
            public string StatusLabel { get; set; } = string.Empty;
            public string RiskLabel { get; set; } = string.Empty;
            public HealthLevel RiskLevel { get; set; } = HealthLevel.Good;
            public string Title { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
            public string MetaText { get; set; } = string.Empty;
            public string RiskText { get; set; } = string.Empty;
            public string ActionText { get; set; } = string.Empty;
            public string InfLabel { get; set; } = string.Empty;
            public string InstanceLabel { get; set; } = string.Empty;
            public bool RequiresAdmin { get; set; }
            public bool HasDestructiveAction { get; set; }
            public List<DeviceDriverActionViewModel> Actions { get; set; } = new List<DeviceDriverActionViewModel>();
            public string SearchText { get; set; } = string.Empty;
            public Visibility RiskVisibility => string.IsNullOrWhiteSpace(RiskLabel) ? Visibility.Collapsed : Visibility.Visible;
            public Visibility AdminVisibility => RequiresAdmin ? Visibility.Visible : Visibility.Collapsed;
            public Visibility DestructiveVisibility => HasDestructiveAction ? Visibility.Visible : Visibility.Collapsed;
            public Visibility InfVisibility => string.IsNullOrWhiteSpace(InfLabel) ? Visibility.Collapsed : Visibility.Visible;
            public Visibility InstanceVisibility => string.IsNullOrWhiteSpace(InstanceLabel) ? Visibility.Collapsed : Visibility.Visible;

            public static DeviceDriverItemViewModel FromItem(DeviceDriverInventoryItem item)
            {
                if (item == null)
                    return new DeviceDriverItemViewModel();

                var actions = (item.Actions ?? new List<DeviceDriverInventoryAction>())
                    .Select(DeviceDriverActionViewModel.FromAction)
                    .ToList();

                return new DeviceDriverItemViewModel
                {
                    Id = item.Id ?? string.Empty,
                    Kind = item.Kind,
                    Level = item.Level,
                    KindLabel = GetKindLabel(item.Kind),
                    StatusLabel = GetStatusLabel(item.Level),
                    RiskLabel = GetRiskLabel(item.RiskText, item.Level),
                    RiskLevel = GetRiskLevel(item.RiskText, item.Level),
                    Title = item.Title ?? string.Empty,
                    Description = item.Description ?? string.Empty,
                    MetaText = item.MetaText ?? string.Empty,
                    RiskText = item.RiskText ?? string.Empty,
                    ActionText = item.ActionText ?? string.Empty,
                    InfLabel = string.IsNullOrWhiteSpace(item.InfName) ? string.Empty : $"INF: {item.InfName}",
                    InstanceLabel = string.IsNullOrWhiteSpace(item.InstanceId) ? string.Empty : "Instance ID",
                    RequiresAdmin = actions.Any(action => action.RequiresAdmin),
                    HasDestructiveAction = actions.Any(action => action.IsDestructive),
                    Actions = actions,
                    SearchText = string.Join(" ", new[]
                    {
                        item.Title,
                        item.Description,
                        item.MetaText,
                        item.RiskText,
                        item.ActionText,
                        GetKindLabel(item.Kind),
                        GetStatusLabel(item.Level),
                        item.InstanceId,
                        item.InfName,
                        item.HardwareId,
                        item.SearchQuery,
                        string.Join(" ", actions.Select(action => action.SearchText))
                    }.Where(value => !string.IsNullOrWhiteSpace(value)))
                };
            }

            private static string GetKindLabel(DeviceDriverInventoryItemKind kind)
            {
                return kind switch
                {
                    DeviceDriverInventoryItemKind.Driver => "Драйвер",
                    DeviceDriverInventoryItemKind.Device => "Устройство",
                    DeviceDriverInventoryItemKind.Printer => "Принтер",
                    DeviceDriverInventoryItemKind.Tool => "Инструмент",
                    _ => "Элемент"
                };
            }

            private static string GetStatusLabel(HealthLevel level)
            {
                return level switch
                {
                    HealthLevel.Critical => "Критично",
                    HealthLevel.Warning or HealthLevel.Attention => "Проблема",
                    HealthLevel.Normal => "Рекомендация",
                    HealthLevel.Good => "В норме",
                    _ => "Статус"
                };
            }

            private static string GetRiskLabel(string riskText, HealthLevel level)
            {
                string normalized = riskText ?? string.Empty;
                if (normalized.IndexOf("высок", StringComparison.CurrentCultureIgnoreCase) >= 0 ||
                    normalized.IndexOf("high", StringComparison.OrdinalIgnoreCase) >= 0)
                    return "Риск: высокий";

                if (normalized.IndexOf("сред", StringComparison.CurrentCultureIgnoreCase) >= 0 ||
                    normalized.IndexOf("medium", StringComparison.OrdinalIgnoreCase) >= 0)
                    return "Риск: средний";

                if (normalized.IndexOf("низ", StringComparison.CurrentCultureIgnoreCase) >= 0 ||
                    normalized.IndexOf("low", StringComparison.OrdinalIgnoreCase) >= 0)
                    return "Риск: низкий";

                return level switch
                {
                    HealthLevel.Critical => "Риск: высокий",
                    HealthLevel.Warning or HealthLevel.Attention => "Риск: средний",
                    HealthLevel.Normal => "Риск: низкий",
                    _ => string.Empty
                };
            }

            private static HealthLevel GetRiskLevel(string riskText, HealthLevel level)
            {
                string normalized = riskText ?? string.Empty;
                if (normalized.IndexOf("высок", StringComparison.CurrentCultureIgnoreCase) >= 0 ||
                    normalized.IndexOf("high", StringComparison.OrdinalIgnoreCase) >= 0)
                    return HealthLevel.Critical;

                if (normalized.IndexOf("сред", StringComparison.CurrentCultureIgnoreCase) >= 0 ||
                    normalized.IndexOf("medium", StringComparison.OrdinalIgnoreCase) >= 0)
                    return HealthLevel.Warning;

                if (normalized.IndexOf("низ", StringComparison.CurrentCultureIgnoreCase) >= 0 ||
                    normalized.IndexOf("low", StringComparison.OrdinalIgnoreCase) >= 0)
                    return HealthLevel.Normal;

                return level switch
                {
                    HealthLevel.Critical => HealthLevel.Critical,
                    HealthLevel.Warning or HealthLevel.Attention => HealthLevel.Warning,
                    HealthLevel.Normal => HealthLevel.Normal,
                    _ => HealthLevel.Good
                };
            }
        }

        private sealed class DeviceDriverActionViewModel
        {
            public DeviceDriverInventoryActionKind Kind { get; set; }
            public string Label { get; set; } = string.Empty;
            public string Title { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
            public string InstanceId { get; set; } = string.Empty;
            public string InfName { get; set; } = string.Empty;
            public string SearchQuery { get; set; } = string.Empty;
            public string PrinterName { get; set; } = string.Empty;
            public string RiskText { get; set; } = string.Empty;
            public bool RequiresAdmin { get; set; }
            public bool IsDestructive { get; set; }
            public string SearchText { get; set; } = string.Empty;

            public static DeviceDriverActionViewModel FromAction(DeviceDriverInventoryAction action)
            {
                if (action == null)
                    return new DeviceDriverActionViewModel();

                return new DeviceDriverActionViewModel
                {
                    Kind = action.Kind,
                    Label = action.Label ?? string.Empty,
                    Title = action.Title ?? string.Empty,
                    Description = action.Description ?? string.Empty,
                    InstanceId = action.InstanceId ?? string.Empty,
                    InfName = action.InfName ?? string.Empty,
                    SearchQuery = action.SearchQuery ?? string.Empty,
                    PrinterName = action.PrinterName ?? string.Empty,
                    RiskText = action.RiskText ?? string.Empty,
                    RequiresAdmin = action.RequiresAdmin,
                    IsDestructive = action.IsDestructive,
                    SearchText = $"{action.Label} {action.Title} {action.Description} {action.InstanceId} {action.InfName} {action.SearchQuery} {action.PrinterName} {action.RiskText}"
                };
            }
        }

        private sealed class WorkspaceSearchSuggestionViewModel
        {
            public string Title { get; set; } = string.Empty;
            public string Caption { get; set; } = string.Empty;
            public string GroupId { get; set; } = string.Empty;
            public string ItemId { get; set; } = string.Empty;
        }
    }
}
