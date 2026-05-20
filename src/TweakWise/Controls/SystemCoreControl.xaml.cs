using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using TweakWise.Models;
using UserControl = System.Windows.Controls.UserControl;

namespace TweakWise.Controls
{
    public partial class SystemCoreControl : UserControl
    {
        public static readonly DependencyProperty StatusProperty =
            DependencyProperty.Register(
                nameof(Status),
                typeof(HealthLevel),
                typeof(SystemCoreControl),
                new PropertyMetadata(HealthLevel.Unknown, OnVisualStateChanged));

        public static readonly DependencyProperty ProblemCountProperty =
            DependencyProperty.Register(
                nameof(ProblemCount),
                typeof(int),
                typeof(SystemCoreControl),
                new PropertyMetadata(0, OnVisualStateChanged));

        public static readonly DependencyProperty RecommendationCountProperty =
            DependencyProperty.Register(
                nameof(RecommendationCount),
                typeof(int),
                typeof(SystemCoreControl),
                new PropertyMetadata(0, OnVisualStateChanged));

        public static readonly DependencyProperty CriticalCountProperty =
            DependencyProperty.Register(
                nameof(CriticalCount),
                typeof(int),
                typeof(SystemCoreControl),
                new PropertyMetadata(0, OnVisualStateChanged));

        private Storyboard _pulseStoryboard;
        private readonly ScaleTransform _hoverScale;

        public SystemCoreControl()
        {
            InitializeComponent();
            _hoverScale = FindName("HoverScale") as ScaleTransform;
            UpdateVisualState();
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

        public int CriticalCount
        {
            get => (int)GetValue(CriticalCountProperty);
            set => SetValue(CriticalCountProperty, value);
        }

        private static void OnVisualStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is SystemCoreControl control)
                control.UpdateVisualState();
        }

        private void Root_Loaded(object sender, RoutedEventArgs e)
        {
            if (!SystemParameters.ClientAreaAnimation)
                return;

            _pulseStoryboard = (Storyboard)FindResource("CorePulseStoryboard");
            _pulseStoryboard.Begin(this, true);
        }

        private void Root_Unloaded(object sender, RoutedEventArgs e)
        {
            _pulseStoryboard?.Stop(this);
        }

        private void Root_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
        {
            AnimateHoverScale(1.055, 170);
        }

        private void Root_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
        {
            AnimateHoverScale(1, 180);
        }

        private void AnimateHoverScale(double targetScale, int milliseconds)
        {
            if (_hoverScale == null)
                return;

            var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };
            _hoverScale.BeginAnimation(ScaleTransform.ScaleXProperty,
                new DoubleAnimation(targetScale, TimeSpan.FromMilliseconds(milliseconds)) { EasingFunction = ease });
            _hoverScale.BeginAnimation(ScaleTransform.ScaleYProperty,
                new DoubleAnimation(targetScale, TimeSpan.FromMilliseconds(milliseconds)) { EasingFunction = ease });
        }

        private void UpdateVisualState()
        {
            if (StatusTextBlock == null)
                return;

            StatusTextBlock.Text = GetStatusText(Status);
            CountTextBlock.Text = GetCountText();

            string brushKey = GetStatusBrushKey(Status);
            SetShapeResource(BreathHalo, Shape.StrokeProperty, brushKey);
            SetShapeResource(AuraWaveA, Shape.StrokeProperty, brushKey);
            SetShapeResource(AuraWaveB, Shape.StrokeProperty, brushKey);
            SetShapeResource(AuraWaveC, Shape.StrokeProperty, brushKey);
            SetShapeResource(CoreShell, Shape.FillProperty, brushKey);
            SetShapeResource(CoreShell, Shape.StrokeProperty, brushKey);
            SetShapeResource(OuterGlow, Shape.StrokeProperty, brushKey);
            SetShapeResource(SoftRing, Shape.StrokeProperty, brushKey);
            SetShapeResource(EnergyMassA, Shape.FillProperty, brushKey);
            SetShapeResource(EnergyMassB, Shape.FillProperty, brushKey);
            SetShapeResource(EnergyMassC, Shape.FillProperty, brushKey);
            SetShapeResource(PlasmaBoltA, Shape.StrokeProperty, brushKey);
            SetShapeResource(PlasmaBoltB, Shape.StrokeProperty, brushKey);
            SetShapeResource(PlasmaBoltC, Shape.StrokeProperty, brushKey);
        }

        private string GetCountText()
        {
            if (Status == HealthLevel.Unknown)
                return "Нет данных";

            if (Status == HealthLevel.Checking)
                return "Проверка";

            if (CriticalCount > 0)
                return FormatCount(CriticalCount, "критическая проблема", "критические проблемы", "критических проблем");

            if (ProblemCount > 0)
                return FormatCount(ProblemCount, "проблема", "проблемы", "проблем");

            if (RecommendationCount > 0)
                return FormatCount(RecommendationCount, "рекомендация", "рекомендации", "рекомендаций");

            return "0 проблем";
        }

        private static string GetStatusText(HealthLevel status)
        {
            return status switch
            {
                HealthLevel.Good => "В норме",
                HealthLevel.Normal => "Есть рекомендации",
                HealthLevel.Attention => "Требуется внимание",
                HealthLevel.Warning => "Есть проблемы",
                HealthLevel.Critical => "Есть проблемы",
                HealthLevel.Checking => "Идёт проверка",
                _ => "Нет данных"
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

        private static void SetShapeResource(Shape shape, DependencyProperty property, string resourceKey)
        {
            shape.SetResourceReference(property, resourceKey);
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
