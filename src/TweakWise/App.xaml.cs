using System;
using System.Windows;
using Microsoft.Win32;
using TweakWise.Managers;
using TweakWise.Providers;
using TweakWise.Search;
using Application = System.Windows.Application;

namespace TweakWise
{
    public partial class App : Application
    {
        public static SettingsManager SettingsManager { get; private set; }
        public static NotificationManager NotificationManager { get; private set; }
        public static UpdateManager UpdateManager { get; private set; }
        public static DialogManager DialogManager { get; private set; }
        public static ITweakCatalogProvider TweakCatalogProvider { get; private set; }
        public static GlobalSearchService GlobalSearchService { get; private set; }

        public App()
        {
            SettingsManager = new SettingsManager();
            NotificationManager = new NotificationManager(SettingsManager);
            UpdateManager = new UpdateManager();
            DialogManager = new DialogManager();
            TweakCatalogProvider = new MockTweakCatalogProvider();
            GlobalSearchService = new GlobalSearchService(TweakCatalogProvider);
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            SettingsManager.ApplySavedSystemSettings();
            ChangeTheme(SettingsManager.CurrentSettings.Theme);

            var mainWindow = new MainWindow();
            MainWindow = mainWindow;

            if (!SettingsManager.CurrentSettings.FirstRunCompleted)
            {
                var license = new LicenseWindow();
                license.ShowDialog();
                if (!license.Accepted)
                {
                    Shutdown();
                    return;
                }

                SettingsManager.SetFirstRunCompleted();
            }

            mainWindow.Show();
        }

        public void ChangeTheme(string themeName)
        {
            var merged = Resources.MergedDictionaries;
            ResourceDictionary themeDict = null;

            foreach (var dict in merged)
            {
                if (dict.Source != null &&
                    (dict.Source.OriginalString.Contains("Light.xaml") || dict.Source.OriginalString.Contains("Dark.xaml")))
                {
                    themeDict = dict;
                    break;
                }
            }

            if (themeDict != null)
                merged.Remove(themeDict);

            string uri = string.Empty;
            switch (themeName)
            {
                case "Light":
                    uri = "Themes/Light.xaml";
                    break;
                case "Dark":
                    uri = "Themes/Dark.xaml";
                    break;
                case "System":
                    bool isLight = true;
                    try
                    {
                        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                        if (key != null)
                        {
                            var value = key.GetValue("AppsUseLightTheme");
                            if (value is int intVal)
                                isLight = intVal == 1;
                        }
                    }
                    catch
                    {
                    }

                    uri = isLight ? "Themes/Light.xaml" : "Themes/Dark.xaml";
                    break;
            }

            if (!string.IsNullOrEmpty(uri))
            {
                var newDict = new ResourceDictionary { Source = new Uri(uri, UriKind.Relative) };
                merged.Add(newDict);
            }
        }
    }
}
