using System;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using TweakWise.Managers;
using TweakWise.Search;
using TweakWise.Services;
using Application = System.Windows.Application;

namespace TweakWise
{
    public partial class App : Application
    {
        public static SettingsManager SettingsManager { get; private set; }
        public static NotificationManager NotificationManager { get; private set; }
        public static UpdateManager UpdateManager { get; private set; }
        public static DialogManager DialogManager { get; private set; }
        public static GlobalSearchService GlobalSearchService { get; private set; }
        public static IComputerHealthService ComputerHealthService { get; private set; }

        public App()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            SettingsManager = new SettingsManager();
            NotificationManager = new NotificationManager(SettingsManager);
            UpdateManager = new UpdateManager();
            DialogManager = new DialogManager();
            ComputerHealthService = new ComputerHealthService(SettingsManager);
            GlobalSearchService = new GlobalSearchService();
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            if (TemperatureProbeRunner.IsProbeRequest(e.Args))
            {
                Shutdown(TemperatureProbeRunner.RunProbeAndWriteResult());
                return;
            }

            DispatcherUnhandledException += App_DispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;

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

        private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            if (e.Exception is AccessViolationException)
            {
                e.Handled = true;
                ReportApplicationError(e.Exception, "Небезопасный системный вызов был остановлен. Приложение продолжит работу без аварийного закрытия.", showDialog: false);
                return;
            }

            if (IsRecoverableWpfAnimationException(e.Exception))
            {
                e.Handled = true;
                ReportApplicationError(e.Exception, "Анимация интерфейса была остановлена безопасно.", showDialog: false);
                return;
            }

            e.Handled = true;
            ReportApplicationError(e.Exception, "Ошибка обработана без закрытия приложения.", showDialog: true);
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is AccessViolationException accessViolation)
            {
                ReportApplicationError(accessViolation, "Зафиксирован сбой небезопасного системного вызова.", showDialog: false);
                return;
            }

            if (e.ExceptionObject is Exception exception)
                ReportApplicationError(exception, "Произошла критическая ошибка фонового потока.", showDialog: true);
        }

        private void TaskScheduler_UnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
        {
            e.SetObserved();
            ReportApplicationError(e.Exception, "Ошибка фоновой задачи обработана.", showDialog: true);
        }

        private int _isReportingApplicationError;

        private void ReportApplicationError(Exception exception, string context, bool showDialog)
        {
            if (exception == null || System.Threading.Interlocked.Exchange(ref _isReportingApplicationError, 1) == 1)
                return;

            try
            {
                string title = exception.GetType().Name;
                string message = BuildApplicationErrorMessage(exception, context);

                try
                {
                    NotificationManager?.AddNotification("Ошибка программы", $"{title}: {exception.Message}");
                }
                catch
                {
                }

                if (!showDialog)
                    return;

                void ShowError()
                {
                    try
                    {
                        DialogManager?.Show(
                            MainWindow,
                            "Ошибка программы",
                            "TweakWise перехватил исключение",
                            message,
                            AppDialogKind.Error,
                            AppDialogButtons.Ok);
                    }
                    catch
                    {
                    }
                }

                if (Dispatcher == null || Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
                    return;

                if (Dispatcher.CheckAccess())
                    ShowError();
                else
                    Dispatcher.BeginInvoke((Action)ShowError, DispatcherPriority.Background);
            }
            finally
            {
                System.Threading.Interlocked.Exchange(ref _isReportingApplicationError, 0);
            }
        }

        private static string BuildApplicationErrorMessage(Exception exception, string context)
        {
            string stack = exception.StackTrace ?? string.Empty;
            if (stack.Length > 1600)
                stack = stack.Substring(0, 1600) + "...";

            return $"{context}\n\n" +
                   $"Тип: {exception.GetType().FullName}\n" +
                   $"Сообщение: {exception.Message}\n\n" +
                   $"Стек:\n{stack}";
        }

        private static bool IsRecoverableWpfAnimationException(Exception exception)
        {
            string message = exception.Message ?? string.Empty;
            string stackTrace = exception.StackTrace ?? string.Empty;

            if (exception is NullReferenceException)
            {
                return stackTrace.IndexOf("System.Windows.Media.Animation.Clock", StringComparison.OrdinalIgnoreCase) >= 0 ||
                       stackTrace.IndexOf("System.Windows.Media.Animation.TimeManager", StringComparison.OrdinalIgnoreCase) >= 0;
            }

            if (exception is InvalidOperationException &&
                (message.IndexOf("только чтение", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 message.IndexOf("read-only", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                return stackTrace.IndexOf("System.Windows.Media.Animation", StringComparison.OrdinalIgnoreCase) >= 0 ||
                       stackTrace.IndexOf("System.Windows.Media.Freezable", StringComparison.OrdinalIgnoreCase) >= 0;
            }

            return exception is InvalidOperationException &&
                   (message.IndexOf("собственный журнал", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    message.IndexOf("own journal", StringComparison.OrdinalIgnoreCase) >= 0);
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
