using System.Windows;
using TweakWise.Catalog;
using TweakWise.Execution;
using UserControl = System.Windows.Controls.UserControl;

namespace TweakWise.Controls
{
    public partial class SettingCardControl : UserControl
    {
        public static readonly DependencyProperty SettingProperty =
            DependencyProperty.Register(
                nameof(Setting),
                typeof(SettingCardViewModel),
                typeof(SettingCardControl),
                new PropertyMetadata(null, OnSettingChanged));

        public static readonly DependencyProperty IsCompactProperty =
            DependencyProperty.Register(
                nameof(IsCompact),
                typeof(bool),
                typeof(SettingCardControl),
                new PropertyMetadata(false));

        public SettingCardControl()
        {
            InitializeComponent();
        }

        public SettingCardViewModel Setting
        {
            get => (SettingCardViewModel)GetValue(SettingProperty);
            set => SetValue(SettingProperty, value);
        }

        public bool IsCompact
        {
            get => (bool)GetValue(IsCompactProperty);
            set => SetValue(IsCompactProperty, value);
        }

        private static void OnSettingChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
        {
            if (dependencyObject is SettingCardControl control)
                control.RefreshExecutionState();
        }

        private void RefreshExecutionState()
        {
            if (Setting == null)
                return;

            var executionService = App.TweakExecutionService;
            if (executionService == null || !executionService.IsSupported(Setting.Id))
            {
                Setting.CanApply = false;
                Setting.ApplyButtonText = "Недоступно в этой версии";
                Setting.RollbackAvailable = false;
                return;
            }

            Setting.CanApply = true;
            Setting.ApplyButtonText = "Применить";
            Setting.RollbackAvailable = executionService.CanRollback(Setting.Id);

            var state = executionService.ReadState(Setting.Id);
            if (state.Success)
                Setting.CurrentState = state.CurrentState;
        }

        private void ApplyButton_Click(object sender, RoutedEventArgs e)
        {
            if (Setting == null || App.TweakExecutionService == null)
                return;

            try
            {
                var result = App.TweakExecutionService.Apply(Setting.Id, new TweakExecutionOptions());
                ApplyExecutionResult(result);
            }
            catch
            {
                Setting.ExecutionStatusMessage = "Не удалось применить настройку. Приложение продолжает работу.";
            }
        }

        private void RollbackButton_Click(object sender, RoutedEventArgs e)
        {
            if (Setting == null || App.TweakExecutionService == null)
                return;

            try
            {
                var result = App.TweakExecutionService.Rollback(Setting.Id);
                ApplyExecutionResult(result);
            }
            catch
            {
                Setting.ExecutionStatusMessage = "Не удалось выполнить откат. Приложение продолжает работу.";
            }
        }

        private void ApplyExecutionResult(TweakExecutionResult result)
        {
            if (Setting == null || result == null)
                return;

            if (!string.IsNullOrWhiteSpace(result.NewValue))
                Setting.CurrentState = result.NewValue;

            Setting.RollbackAvailable = result.RollbackAvailable;
            Setting.ExecutionStatusMessage = BuildUserMessage(result);
        }

        private static string BuildUserMessage(TweakExecutionResult result)
        {
            if (!result.Success)
                return string.IsNullOrWhiteSpace(result.Message)
                    ? "Не удалось выполнить действие."
                    : result.Message;

            if (result.RequiresRestart && !result.Message.Contains("перезапуск"))
                return $"{result.Message} Для полного эффекта может потребоваться перезапуск.";

            return result.Message;
        }
    }
}
