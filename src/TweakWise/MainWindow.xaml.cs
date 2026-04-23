using System;
using System.Collections.Specialized;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TweakWise.Managers;
using TweakWise.Search;
using Application = System.Windows.Application;
using Button = System.Windows.Controls.Button;

namespace TweakWise
{
    public partial class MainWindow : Window
    {
        private readonly SettingsManager _settingsManager;
        private readonly UpdateManager _updateManager;
        private readonly DialogManager _dialogManager;
        private readonly TrayTemperatureManager _trayTemperatureManager;
        private bool _settingsLoaded;
        private bool _allowClose;

        public MainWindow()
        {
            InitializeComponent();

            _settingsManager = App.SettingsManager;
            _updateManager = App.UpdateManager;
            _dialogManager = App.DialogManager;
            _trayTemperatureManager = new TrayTemperatureManager();

            NotificationsList.ItemsSource = App.NotificationManager.Notifications;
            App.NotificationManager.UnreadCountChanged += UpdateBadge;
            App.NotificationManager.Notifications.CollectionChanged += Notifications_CollectionChanged;

            UpdateBadge();
            UpdateNotificationsState();

            SettingsButton.Checked += (s, e) => SettingsPopup.IsOpen = true;
            SettingsButton.Unchecked += (s, e) => SettingsPopup.IsOpen = false;

            NotificationsButton.Checked += (s, e) => NotificationsPopup.IsOpen = true;
            NotificationsButton.Unchecked += (s, e) => NotificationsPopup.IsOpen = false;

            Loaded += MainWindow_Loaded;
            Closed += MainWindow_Closed;
            Closing += MainWindow_Closing;

            NavigateToTopLevelPage("Dashboard");

            LoadSavedTheme();
            LoadSavedSettings();
        }

        private void LoadSavedTheme()
        {
            string savedTheme = _settingsManager.CurrentSettings.Theme;
            if (savedTheme == "Light")
                ThemeLight.IsChecked = true;
            else if (savedTheme == "Dark")
                ThemeDark.IsChecked = true;
            else
                ThemeSystem.IsChecked = true;
        }

        private void LoadSavedSettings()
        {
            _settingsLoaded = false;
            RunOnStartupCheckBox.IsChecked = _settingsManager.CurrentSettings.RunOnStartup;
            AutoCheckUpdatesCheckBox.IsChecked = _settingsManager.CurrentSettings.AutoCheckUpdates;
            ShowNotificationsCheckBox.IsChecked = _settingsManager.CurrentSettings.ShowNotifications;
            ShowTrayTemperatureCheckBox.IsChecked = _settingsManager.CurrentSettings.ShowTrayTemperature;
            MinimizeToTrayOnCloseCheckBox.IsChecked = _settingsManager.CurrentSettings.MinimizeToTrayOnClose;
            StartMinimizedToTrayCheckBox.IsChecked = _settingsManager.CurrentSettings.StartMinimizedToTray;
            _settingsLoaded = true;
        }

        public void NavigateToPage(string pageName)
        {
            NavigateToTopLevelPage(pageName);
        }

        private void UpdateBadge()
        {
            int unreadCount = App.NotificationManager.UnreadCount;
            if (unreadCount > 0)
            {
                NotificationBadge.Visibility = Visibility.Visible;
                BadgeCount.Text = unreadCount.ToString();
            }
            else
            {
                NotificationBadge.Visibility = Visibility.Collapsed;
            }
        }

        private void UpdateNotificationsState()
        {
            bool hasNotifications = App.NotificationManager.Notifications.Count > 0;
            NotificationsScrollViewer.Visibility = hasNotifications ? Visibility.Visible : Visibility.Collapsed;
            NotificationsList.Visibility = hasNotifications ? Visibility.Visible : Visibility.Collapsed;
            EmptyNotificationsText.Visibility = hasNotifications ? Visibility.Collapsed : Visibility.Visible;
        }

        private void Notifications_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            UpdateNotificationsState();
        }

        private void ClearAllNotifications_Click(object sender, RoutedEventArgs e)
        {
            App.NotificationManager.ClearAll();
            UpdateNotificationsState();
        }

        private void NotificationsPopup_Opened(object sender, EventArgs e)
        {
            App.NotificationManager.MarkAllAsRead();
        }

        private void NotificationsPopup_Closed(object sender, EventArgs e)
        {
            if (NotificationsButton.IsChecked == true)
                NotificationsButton.IsChecked = false;
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
                DragMove();
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void MaximizeButton_Click(object sender, RoutedEventArgs e)
        {
            if (WindowState == WindowState.Normal)
            {
                WindowState = WindowState.Maximized;
                MaximizeButton.Content = "\uE73F";
                MaximizeButton.ToolTip = "Восстановить";
            }
            else
            {
                WindowState = WindowState.Normal;
                MaximizeButton.Content = "\uE739";
                MaximizeButton.ToolTip = "Развернуть";
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void SettingsPopup_Closed(object sender, EventArgs e)
        {
            if (SettingsButton.IsChecked == true)
                SettingsButton.IsChecked = false;
        }

        private void ThemeRadio_Checked(object sender, RoutedEventArgs e)
        {
            if (_settingsManager == null)
                return;

            if (ThemeSystem.IsChecked == true)
                _settingsManager.ChangeTheme("System");
            else if (ThemeLight.IsChecked == true)
                _settingsManager.ChangeTheme("Light");
            else if (ThemeDark.IsChecked == true)
                _settingsManager.ChangeTheme("Dark");
        }

        private void SettingsCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (!_settingsLoaded)
                return;

            _settingsManager.SetRunOnStartup(RunOnStartupCheckBox.IsChecked == true);
            _settingsManager.SetAutoCheckUpdates(AutoCheckUpdatesCheckBox.IsChecked == true);
            _settingsManager.SetShowNotifications(ShowNotificationsCheckBox.IsChecked == true);
            _settingsManager.SetShowTrayTemperature(ShowTrayTemperatureCheckBox.IsChecked == true);
            _settingsManager.SetMinimizeToTrayOnClose(MinimizeToTrayOnCloseCheckBox.IsChecked == true);
            _settingsManager.SetStartMinimizedToTray(StartMinimizedToTrayCheckBox.IsChecked == true);
            ApplyTrayPreferences();
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            ApplyTrayPreferences();

            if (ShouldStartHiddenInTray())
                HideToTray();

            if (_settingsManager.CurrentSettings.AutoCheckUpdates)
                await CheckForUpdatesAsync(false);
        }

        private void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_allowClose)
                return;

            if (ShouldHideToTrayOnClose())
            {
                e.Cancel = true;
                HideToTray();
            }
        }

        private void MainWindow_Closed(object sender, EventArgs e)
        {
            _trayTemperatureManager.Dispose();
        }

        private void ApplyTrayPreferences()
        {
            bool trayActive = IsTrayModeEnabled();
            _trayTemperatureManager.ApplyPreferences(trayActive, _settingsManager.CurrentSettings.ShowTrayTemperature);
        }

        private bool IsTrayModeEnabled()
        {
            return _settingsManager.CurrentSettings.ShowTrayTemperature ||
                   _settingsManager.CurrentSettings.MinimizeToTrayOnClose ||
                   _settingsManager.CurrentSettings.StartMinimizedToTray;
        }

        private bool ShouldHideToTrayOnClose()
        {
            return IsTrayModeEnabled() && _settingsManager.CurrentSettings.MinimizeToTrayOnClose;
        }

        private bool ShouldStartHiddenInTray()
        {
            string[] args = Environment.GetCommandLineArgs();
            bool launchedForTray = Array.Exists(args, arg => string.Equals(arg, "--tray-start", StringComparison.OrdinalIgnoreCase));
            return IsTrayModeEnabled() && (_settingsManager.CurrentSettings.StartMinimizedToTray || launchedForTray);
        }

        private void HideToTray()
        {
            ApplyTrayPreferences();
            ShowInTaskbar = false;
            WindowState = WindowState.Minimized;
            Hide();
        }

        public void AllowCloseAndShutdown()
        {
            _allowClose = true;
            Application.Current.Shutdown();
        }

        private async void CheckUpdatesButton_Click(object sender, RoutedEventArgs e)
        {
            await CheckForUpdatesAsync(true);
        }

        private async Task CheckForUpdatesAsync(bool userInitiated)
        {
            CheckUpdatesButton.IsEnabled = false;
            string originalButtonText = CheckUpdatesButton.Content?.ToString() ?? "Проверить наличие обновлений";
            CheckUpdatesButton.Content = "Проверка...";

            try
            {
                var result = await _updateManager.CheckForUpdatesAsync();

                switch (result.Status)
                {
                    case UpdateCheckStatus.UpdateAvailable:
                        HandleAvailableUpdate(result, userInitiated);
                        break;
                    case UpdateCheckStatus.UpToDate:
                        if (userInitiated)
                        {
                            _dialogManager.Show(
                                this,
                                "Обновления",
                                "Установлена актуальная версия",
                                $"У вас уже установлена последняя версия приложения: {AppInfo.DisplayVersion}.",
                                AppDialogKind.Info);
                        }
                        break;
                    case UpdateCheckStatus.Error:
                        if (userInitiated)
                        {
                            _dialogManager.Show(
                                this,
                                "Ошибка проверки",
                                "Не удалось проверить обновления",
                                result.ErrorMessage,
                                AppDialogKind.Error);
                        }
                        break;
                }
            }
            finally
            {
                CheckUpdatesButton.Content = originalButtonText;
                CheckUpdatesButton.IsEnabled = true;
            }
        }

        private void HandleAvailableUpdate(UpdateCheckResult result, bool userInitiated)
        {
            if (!userInitiated)
            {
                if (_settingsManager.CurrentSettings.LastNotifiedUpdateVersion == result.LatestVersionId)
                    return;

                _settingsManager.CurrentSettings.LastNotifiedUpdateVersion = result.LatestVersionId;
                _settingsManager.SaveSettings();

                if (_settingsManager.CurrentSettings.ShowNotifications)
                {
                    App.NotificationManager.AddNotification(
                        "Доступно обновление",
                        $"Доступна новая версия {result.LatestVersionText}. Нажмите, чтобы открыть changelog.",
                        () => ShowUpdateWindow(result));
                }
                return;
            }

            ShowUpdateWindow(result);
        }

        private void ShowUpdateWindow(UpdateCheckResult result)
        {
            var updateWindow = new UpdateWindow(_updateManager, result)
            {
                Owner = this
            };

            updateWindow.ShowDialog();
        }

        private void ResetButton_Click(object sender, RoutedEventArgs e)
        {
            var dialogResult = _dialogManager.Show(
                this,
                "Подтверждение сброса",
                "Сбросить все настройки?",
                "Будут удалены настройки и уведомления. Программа закроется, а при следующем запуске потребуется заново принять лицензионное соглашение.",
                AppDialogKind.Warning,
                AppDialogButtons.YesNo);

            if (dialogResult != AppDialogResult.Primary)
                return;

            try
            {
                string settingsPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "TweakWise",
                    "settings.json");

                if (File.Exists(settingsPath))
                    File.Delete(settingsPath);
            }
            catch
            {
            }

            Application.Current.Shutdown();
        }

        public async Task HandleGlobalSearchSelectionAsync(GlobalSearchResultViewModel result)
        {
            if (result.NavigationTarget.ResultKind == GlobalSearchResultKind.Action)
            {
                await ExecuteGlobalSearchActionAsync(result.NavigationTarget.ActionKey);
                return;
            }

            GlobalSearchNavigationStore.Clear();

            if (ShouldStorePendingNavigation(result.NavigationTarget))
                GlobalSearchNavigationStore.SetPending(result.NavigationTarget);

            NavigateToTopLevelPage(result.NavigationTarget.PageKey);
        }

        private async Task ExecuteGlobalSearchActionAsync(string actionKey)
        {
            switch (actionKey)
            {
                case "OpenSettings":
                    SettingsButton.IsChecked = true;
                    break;
                case "OpenNotifications":
                    NotificationsButton.IsChecked = true;
                    break;
                case "CheckUpdates":
                    await CheckForUpdatesAsync(true);
                    break;
            }
        }

        private static bool ShouldStorePendingNavigation(GlobalSearchNavigationTarget target)
        {
            return target.PageKey == "WindowsInterface" &&
                   (target.ResultKind == GlobalSearchResultKind.Subsection ||
                    target.ResultKind == GlobalSearchResultKind.Setting ||
                    target.ResultKind == GlobalSearchResultKind.Template);
        }

        private void NavigateToTopLevelPage(string pageName)
        {
            switch (pageName)
            {
                case "Explorer":
                case "WindowsInterface":
                case "StartMenu":
                case "Taskbar":
                    MainFrame.Navigate(new Pages.WindowsInterfacePage());
                    break;
                case "System":
                    MainFrame.Navigate(new Pages.SystemHubPage());
                    break;
                case "Maintenance":
                    MainFrame.Navigate(new Pages.MaintenancePage());
                    break;
                case "MonitoringPerformance":
                    MainFrame.Navigate(new Pages.MonitoringPerformancePage());
                    break;
                default:
                    MainFrame.Navigate(new Pages.DashboardPage());
                    break;
            }
        }

        private void NavButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string pageName)
            {
                GlobalSearchNavigationStore.Clear();
                NavigateToTopLevelPage(pageName);
            }
        }
    }
}
