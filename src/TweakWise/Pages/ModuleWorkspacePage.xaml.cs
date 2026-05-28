using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Shapes;
using TweakWise.Managers;
using TweakWise.Models;
using TweakWise.Services;
using Application = System.Windows.Application;

namespace TweakWise.Pages
{
    public partial class ModuleWorkspacePage : Page
    {
        private readonly IComputerHealthService _healthService;
        private readonly CoreModuleId _moduleId;

        public ModuleWorkspacePage(CoreModuleId moduleId)
        {
            InitializeComponent();

            _moduleId = moduleId;
            _healthService = App.ComputerHealthService;
            ApplyState();
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            if (_healthService != null)
                _healthService.HealthStatusChanged += HealthService_HealthStatusChanged;
        }

        private void Page_Unloaded(object sender, RoutedEventArgs e)
        {
            if (_healthService != null)
                _healthService.HealthStatusChanged -= HealthService_HealthStatusChanged;
        }

        private void HealthService_HealthStatusChanged(object sender, EventArgs e)
        {
            Dispatcher.Invoke(ApplyState);
        }

        private void ApplyState()
        {
            if (_healthService == null)
                return;

            var overall = _healthService.GetOverallStatus();
            var module = _healthService.GetModule(_moduleId);
            if (module == null)
                return;

            MiniCoreControl.Status = overall.OverallStatus;
            MiniCoreControl.ProblemCount = overall.ProblemCount;
            MiniCoreControl.RecommendationCount = overall.RecommendationCount;
            MiniCoreControl.CriticalCount = overall.CriticalCount;

            ModuleTitleTextBlock.Text = module.Title;
            ModuleDescriptionTextBlock.Text = module.Description;
            ModuleStatusTextBlock.Text = GetModuleStatusText(module.Status.Status);
            ModuleStatusIndicator.SetResourceReference(Shape.FillProperty, GetStatusBrushKey(module.Status.Status));

            var findings = module.Status.Findings
                .Select(finding => new FindingRow
                {
                    Id = finding.Id,
                    Title = finding.Title,
                    Description = finding.Description,
                    ActionText = finding.ActionText,
                    Level = finding.Level,
                    StatusText = GetFindingStatusText(finding.Level)
                })
                .ToList();

            FindingsPanel.Visibility = findings.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            FindingsItemsControl.ItemsSource = findings;

            SectionsItemsControl.ItemsSource = module.Sections
                .Select(section => new SectionRow
                {
                    Title = section,
                    StatusText = "Не проверено"
                })
                .ToList();
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            if (Application.Current.MainWindow is MainWindow mainWindow)
                mainWindow.NavigateToCoreHome();
        }

        private async void IgnoreFindingButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not System.Windows.Controls.Button button || button.Tag is not string id || string.IsNullOrWhiteSpace(id))
                return;

            var module = _healthService?.GetModule(_moduleId);
            var finding = module?.Status?.Findings.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase));
            if (finding == null)
                return;

            await HealthSignalActionHelper.PromptAndApplyAsync(
                Window.GetWindow(this),
                new[] { finding.Id },
                finding.Title,
                finding.Level == HealthLevel.Attention || finding.Level == HealthLevel.Warning || finding.Level == HealthLevel.Critical);
        }

        private static string GetModuleStatusText(HealthLevel status)
        {
            return status switch
            {
                HealthLevel.Good => "В норме",
                HealthLevel.Normal => "Есть рекомендации",
                HealthLevel.Attention => "Требуется внимание",
                HealthLevel.Warning => "Требуется внимание",
                HealthLevel.Critical => "Требуется внимание",
                HealthLevel.Checking => "Проверка",
                _ => "Не проверено"
            };
        }

        private static string GetFindingStatusText(HealthLevel status)
        {
            return status switch
            {
                HealthLevel.Critical => "Критично",
                HealthLevel.Warning => "Проблема",
                HealthLevel.Attention => "Внимание",
                HealthLevel.Normal => "Рекомендация",
                _ => "Проверка"
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

        private sealed class SectionRow
        {
            public string Title { get; set; } = string.Empty;
            public string StatusText { get; set; } = string.Empty;
        }

        private sealed class FindingRow
        {
            public string Id { get; set; } = string.Empty;
            public string Title { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
            public string ActionText { get; set; } = string.Empty;
            public HealthLevel Level { get; set; }
            public string StatusText { get; set; } = string.Empty;
        }
    }
}
