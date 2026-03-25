using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TweakWise.Managers;

namespace TweakWise
{
    public partial class MainWindow : Window
    {
        private SettingsManager _settingsManager;

        public MainWindow()
        {
            InitializeComponent();

            _settingsManager = App.SettingsManager;
            NotificationsList.ItemsSource = App.NotificationManager.Notifications;
            App.NotificationManager.UnreadCountChanged += UpdateBadge;
            UpdateBadge();

            SettingsButton.Checked += (s, e) => SettingsPopup.IsOpen = true;
            SettingsButton.Unchecked += (s, e) => SettingsPopup.IsOpen = false;

            NotificationsButton.Checked += (s, e) => NotificationsPopup.IsOpen = true;
            NotificationsButton.Unchecked += (s, e) => NotificationsPopup.IsOpen = false;

            MainFrame.Navigate(new Pages.DashboardPage());

            LoadSavedTheme();
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

        private void ClearAllNotifications_Click(object sender, RoutedEventArgs e)
        {
            App.NotificationManager.ClearAll();
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
                this.DragMove();
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }

        private void MaximizeButton_Click(object sender, RoutedEventArgs e)
        {
            if (this.WindowState == WindowState.Normal)
            {
                this.WindowState = WindowState.Maximized;
                MaximizeButton.Content = "\uE73F";
                MaximizeButton.ToolTip = "Восстановить";
            }
            else
            {
                this.WindowState = WindowState.Normal;
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
            if (_settingsManager == null) return;
            if (ThemeSystem.IsChecked == true)
                _settingsManager.ChangeTheme("System");
            else if (ThemeLight.IsChecked == true)
                _settingsManager.ChangeTheme("Light");
            else if (ThemeDark.IsChecked == true)
                _settingsManager.ChangeTheme("Dark");
        }

        private void ResetButton_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show("Сбросить все настройки и уведомления? Программа будет закрыта. При следующем запуске потребуется повторное принятие лицензионного соглашения.",
                "Подтверждение сброса", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    string settingsPath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        "TweakWise", "settings.json");

                    if (File.Exists(settingsPath))
                        File.Delete(settingsPath);
                }
                catch { }

                Application.Current.Shutdown();
            }
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