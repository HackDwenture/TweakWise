using System;
using System.Windows;
using System.Windows.Media;
using TweakWise.Catalog;
using TweakWise.Pages;
using Page = System.Windows.Controls.Page;
using UserControl = System.Windows.Controls.UserControl;

namespace TweakWise.Controls
{
    public partial class LocalTemplateCardControl : UserControl
    {
        public static readonly DependencyProperty TemplateCardProperty =
            DependencyProperty.Register(
                nameof(TemplateCard),
                typeof(LocalTemplateCardViewModel),
                typeof(LocalTemplateCardControl),
                new PropertyMetadata(null));

        public LocalTemplateCardControl()
        {
            InitializeComponent();
        }

        public LocalTemplateCardViewModel TemplateCard
        {
            get => (LocalTemplateCardViewModel)GetValue(TemplateCardProperty);
            set => SetValue(TemplateCardProperty, value);
        }

        private void ApplyButton_Click(object sender, RoutedEventArgs e)
        {
            string targetPageKey = ResolveNavigationTarget();
            var currentPage = FindAncestorPage(this);

            if (Window.GetWindow(this) is MainWindow mainWindow &&
                !IsSameTopLevelPage(currentPage, targetPageKey))
            {
                mainWindow.NavigateToPage(targetPageKey);
                return;
            }

            BringIntoView();
        }

        private string ResolveNavigationTarget()
        {
            string templateId = TemplateCard?.Id ?? string.Empty;
            string scopeText = TemplateCard?.ScopeText ?? string.Empty;

            if (templateId.Contains("windows-interface", StringComparison.OrdinalIgnoreCase) ||
                scopeText.Contains("Интерфейс Windows", StringComparison.OrdinalIgnoreCase))
            {
                return "WindowsInterface";
            }

            if (templateId.Contains("system-", StringComparison.OrdinalIgnoreCase) ||
                scopeText.Contains("Система", StringComparison.OrdinalIgnoreCase))
            {
                return "System";
            }

            if (templateId.Contains("maintenance-", StringComparison.OrdinalIgnoreCase) ||
                templateId.Contains("care-", StringComparison.OrdinalIgnoreCase) ||
                scopeText.Contains("Обслуживание", StringComparison.OrdinalIgnoreCase))
            {
                return "Maintenance";
            }

            if (templateId.Contains("monitor-", StringComparison.OrdinalIgnoreCase) ||
                scopeText.Contains("Мониторинг", StringComparison.OrdinalIgnoreCase) ||
                scopeText.Contains("Производительность", StringComparison.OrdinalIgnoreCase))
            {
                return "MonitoringPerformance";
            }

            return "Dashboard";
        }

        private static Page FindAncestorPage(DependencyObject current)
        {
            var parent = current;

            while (parent != null)
            {
                if (parent is Page page)
                    return page;

                parent = VisualTreeHelper.GetParent(parent);
            }

            return null;
        }

        private static bool IsSameTopLevelPage(Page page, string targetPageKey)
        {
            return (page is DashboardPage && targetPageKey == "Dashboard") ||
                   ((page is WindowsInterfacePage || page is WorkEnvironmentPage) && targetPageKey == "WindowsInterface") ||
                   (page is SystemHubPage && targetPageKey == "System") ||
                   (page is MaintenancePage && targetPageKey == "Maintenance") ||
                   (page is MonitoringPerformancePage && targetPageKey == "MonitoringPerformance");
        }
    }
}
