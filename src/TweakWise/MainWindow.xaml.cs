using System;
using System.Collections.Specialized;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TweakWise.Managers;

namespace TweakWise
{
    public partial class MainWindow : Window
    {
        private readonly SettingsManager _settingsManager;
        private readonly UpdateManager _updateManager;
        private readonly DialogManager _dialogManager;
        private bool _settingsLoaded;

        public MainWindow()
        {
            InitializeComponent();

            _settingsManager = App.SettingsManager;
            _updateManager = App.UpdateManager;
            _dialogManager = App.DialogManager;

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

            MainFrame.Navigate(new Pages.DashboardPage());

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
            _settingsLoaded = true;
        }

        public void NavigateToPage(string pageName)
        {
            switch (pageName)
            {
                case "Explorer":
                    MainFrame.Navigate(new Pages.ExplorerPage());
                    break;
            }
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
            Application.Current.Shutdown();
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
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (_settingsManager.CurrentSettings.AutoCheckUpdates)
                await CheckForUpdatesAsync(false);
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
                var result = await _updateManager.CheckForUpdatesAsync(_settingsManager.CurrentSettings.LastNotifiedReleaseCommit);

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
                                "Изменений нет",
                                "Новых коммитов в ветке release не найдено.",
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
                if (_settingsManager.CurrentSettings.LastNotifiedReleaseCommit == result.ReleaseCommitSha)
                    return;

                _settingsManager.CurrentSettings.LastNotifiedReleaseCommit = result.ReleaseCommitSha;
                _settingsManager.SaveSettings();

                if (_settingsManager.CurrentSettings.ShowNotifications)
                {
                    App.NotificationManager.AddNotification(
                        "Доступно обновление",
                        $"В ветке release появились изменения {result.LatestVersionText}. Нажмите, чтобы открыть changelog.",
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

        private void NavButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string pageName)
            {
                switch (pageName)
                {
                    case "Dashboard": MainFrame.Navigate(new Pages.DashboardPage()); break;
                    case "Explorer": MainFrame.Navigate(new Pages.ExplorerPage()); break;
                    case "StartMenu": MainFrame.Navigate(new Pages.StartMenuPage()); break;
                    case "Taskbar": MainFrame.Navigate(new Pages.TaskbarPage()); break;
                    case "Optimize": MainFrame.Navigate(new Pages.OptimizePage()); break;
                    case "System": MainFrame.Navigate(new Pages.SystemPage()); break;
                }
            }
        }
    }
}
