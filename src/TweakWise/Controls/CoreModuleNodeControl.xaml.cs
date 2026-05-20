using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using TweakWise.Models;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using UserControl = System.Windows.Controls.UserControl;

namespace TweakWise.Controls
{
    public partial class CoreModuleNodeControl : UserControl
    {
        public static readonly DependencyProperty ModuleIdProperty =
            DependencyProperty.Register(
                nameof(ModuleId),
                typeof(CoreModuleId),
                typeof(CoreModuleNodeControl),
                new PropertyMetadata(CoreModuleId.WindowsSetup));

        public static readonly DependencyProperty TitleProperty =
            DependencyProperty.Register(
                nameof(Title),
                typeof(string),
                typeof(CoreModuleNodeControl),
                new PropertyMetadata(string.Empty, OnDisplayChanged));

        public static readonly DependencyProperty HintProperty =
            DependencyProperty.Register(
                nameof(Hint),
                typeof(string),
                typeof(CoreModuleNodeControl),
                new PropertyMetadata(string.Empty, OnDisplayChanged));

        public static readonly DependencyProperty StatusProperty =
            DependencyProperty.Register(
                nameof(Status),
                typeof(HealthLevel),
                typeof(CoreModuleNodeControl),
                new PropertyMetadata(HealthLevel.Unknown, OnDisplayChanged));

        public static readonly DependencyProperty ProblemCountProperty =
            DependencyProperty.Register(
                nameof(ProblemCount),
                typeof(int),
                typeof(CoreModuleNodeControl),
                new PropertyMetadata(0, OnDisplayChanged));

        public static readonly DependencyProperty RecommendationCountProperty =
            DependencyProperty.Register(
                nameof(RecommendationCount),
                typeof(int),
                typeof(CoreModuleNodeControl),
                new PropertyMetadata(0, OnDisplayChanged));

        public event EventHandler ModuleClick;

        public CoreModuleNodeControl()
        {
            InitializeComponent();
            UpdateDisplay();
        }

        public CoreModuleId ModuleId
        {
            get => (CoreModuleId)GetValue(ModuleIdProperty);
            set => SetValue(ModuleIdProperty, value);
        }

        public string Title
        {
            get => (string)GetValue(TitleProperty);
            set => SetValue(TitleProperty, value);
        }

        public string Hint
        {
            get => (string)GetValue(HintProperty);
            set => SetValue(HintProperty, value);
        }

        public HealthLevel Status
        {
            get => (HealthLevel)GetValue(StatusProperty);
            set => SetValue(StatusProperty, value);
        }

        public int ProblemCount
        {
            get => (int)GetValue(ProblemCountProperty);
            set => SetValue(ProblemCountProperty, value);
        }

        public int RecommendationCount
        {
            get => (int)GetValue(RecommendationCountProperty);
            set => SetValue(RecommendationCountProperty, value);
        }

        private static void OnDisplayChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is CoreModuleNodeControl control)
                control.UpdateDisplay();
        }

        private void UpdateDisplay()
        {
            if (TitleTextBlock == null)
                return;

            TitleTextBlock.Text = Title;
            HintTextBlock.Text = Hint;
            string statusLine = BuildStatusLine();
            StatusTextBlock.Text = statusLine;
            ToolTip = string.IsNullOrWhiteSpace(Hint)
                ? $"{Title}\n{statusLine}"
                : $"{Title}\n{Hint}\n{statusLine}";
            StatusIndicator.SetResourceReference(Shape.FillProperty, GetStatusBrushKey(Status));
            NodeRoot.SetResourceReference(Border.BorderBrushProperty, GetStatusBrushKey(Status));
        }

        private string BuildStatusLine()
        {
            if (Status == HealthLevel.Checking)
                return "Проверка состояния";

            if (Status == HealthLevel.Unknown)
                return "Нет данных";

            if (ProblemCount > 0)
                return FormatCount(ProblemCount, "проблема", "проблемы", "проблем");

            if (RecommendationCount > 0)
                return FormatCount(RecommendationCount, "рекомендация", "рекомендации", "рекомендаций");

            return GetModuleStatusText(Status);
        }

        private void Root_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
        {
            AnimateScale(1.035);
            NodeRoot.Opacity = 1;
        }

        private void Root_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
        {
            AnimateScale(1.0);
            NodeRoot.Opacity = 1;
        }

        private void AnimateScale(double value)
        {
            var duration = TimeSpan.FromMilliseconds(170);
            NodeScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(value, duration) { EasingFunction = new QuadraticEase() });
            NodeScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(value, duration) { EasingFunction = new QuadraticEase() });
        }

        private void Root_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            RaiseModuleClick();
        }

        private void Root_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter && e.Key != Key.Space)
                return;

            e.Handled = true;
            RaiseModuleClick();
        }

        private void RaiseModuleClick()
        {
            ModuleClick?.Invoke(this, EventArgs.Empty);
        }

        private static string GetModuleStatusText(HealthLevel status)
        {
            return status switch
            {
                HealthLevel.Good => "В норме",
                HealthLevel.Normal => "Есть рекомендации",
                HealthLevel.Attention => "Требуется внимание",
                HealthLevel.Warning => "Есть проблемы",
                HealthLevel.Critical => "Критично",
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

        private static string FormatCount(int count, string one, string few, string many)
        {
            int normalized = Math.Abs(count) % 100;
            int lastDigit = normalized % 10;

            string word = normalized is >= 11 and <= 14
                ? many
                : lastDigit switch
                {
                    1 => one,
                    >= 2 and <= 4 => few,
                    _ => many
                };

            return $"{count} {word}";
        }
    }
}
