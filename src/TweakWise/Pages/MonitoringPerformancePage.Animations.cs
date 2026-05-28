using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using WindowsPoint = System.Windows.Point;

namespace TweakWise.Pages
{
    public partial class MonitoringPerformancePage
    {
        private const double NodeDetailsPanelTop = 78;
        private const double NodeDetailsOrbTargetXRatio = 0.52;
        private const double NodeDetailsOrbTargetCenterY = NodeDetailsPanelTop - 30;
        private const double NodeDetailsOrbSize = 44;

        private void PlayNodeDetailsOrbOpenAnimation(string sourceNodeKey)
        {
            var orbButton = NodeDetailsOrbButtonElement;
            var orbTranslate = NodeDetailsOrbTranslateElement;
            var orbScale = NodeDetailsOrbScaleElement;

            if (orbButton == null ||
                orbTranslate == null ||
                orbScale == null)
            {
                return;
            }

            StopNodeDetailsOrbBreathing();
            _nodeDetailsOrbSourceNodeKey = string.IsNullOrWhiteSpace(sourceNodeKey)
                ? _selectedNodeKey
                : sourceNodeKey;
            _nodeDetailsOrbStartPoint = GetNodeCenterInPage(_nodeDetailsOrbSourceNodeKey);
            _nodeDetailsOrbTargetPoint = GetNodeDetailsOrbTargetCenter();
            double startLeft = ToOrbLeft(_nodeDetailsOrbStartPoint);
            double startTop = ToOrbTop(_nodeDetailsOrbStartPoint);
            double targetLeft = ToOrbLeft(_nodeDetailsOrbTargetPoint);
            double targetTop = ToOrbTop(_nodeDetailsOrbTargetPoint);

            orbButton.Visibility = Visibility.Visible;
            ResetNodeDetailsOrbAnimation(orbButton, orbTranslate, orbScale);
            PositionNodeDetailsOrb(_nodeDetailsOrbStartPoint, 0.42, 0);

            orbButton.BeginAnimation(
                UIElement.OpacityProperty,
                new DoubleAnimation
                {
                    From = 0,
                    To = 1,
                    Duration = TimeSpan.FromMilliseconds(210),
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                });

            var flyEase = new CubicEase { EasingMode = EasingMode.EaseOut };
            orbTranslate.BeginAnimation(
                TranslateTransform.XProperty,
                new DoubleAnimation
                {
                    From = startLeft,
                    To = targetLeft,
                    Duration = TimeSpan.FromMilliseconds(520),
                    EasingFunction = flyEase
                });
            orbTranslate.BeginAnimation(
                TranslateTransform.YProperty,
                new DoubleAnimation
                {
                    From = startTop,
                    To = targetTop,
                    Duration = TimeSpan.FromMilliseconds(520),
                    EasingFunction = flyEase
                });

            var scale = new DoubleAnimation
            {
                From = 0.42,
                To = 1,
                Duration = TimeSpan.FromMilliseconds(520),
                EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.28 }
            };
            scale.Completed += (sender, args) =>
            {
                if (_isDetailsOpen)
                    StartNodeDetailsOrbBreathing();
            };

            orbScale.BeginAnimation(ScaleTransform.ScaleXProperty, scale);
            orbScale.BeginAnimation(ScaleTransform.ScaleYProperty, scale.Clone());
        }

        private void PlayNodeDetailsOrbCloseAnimation()
        {
            var orbButton = NodeDetailsOrbButtonElement;
            var orbTranslate = NodeDetailsOrbTranslateElement;
            var orbScale = NodeDetailsOrbScaleElement;

            if (orbButton == null ||
                orbTranslate == null ||
                orbScale == null)
            {
                if (!_isDetailsOpen && NodeDetailsLayer != null)
                    NodeDetailsLayer.Visibility = Visibility.Collapsed;

                return;
            }

            StopNodeDetailsOrbBreathing();
            var returnPoint = GetNodeCenterInPage(_nodeDetailsOrbSourceNodeKey);
            double currentLeft = orbTranslate.X;
            double currentTop = orbTranslate.Y;
            double returnLeft = ToOrbLeft(returnPoint);
            double returnTop = ToOrbTop(returnPoint);

            var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(360))
            {
                BeginTime = TimeSpan.FromMilliseconds(70),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };
            fade.Completed += (sender, args) =>
            {
                if (!_isDetailsOpen)
                {
                    orbButton.Visibility = Visibility.Collapsed;
                    NodeDetailsLayer.Visibility = Visibility.Collapsed;
                }
            };

            orbButton.BeginAnimation(UIElement.OpacityProperty, fade);
            orbTranslate.BeginAnimation(
                TranslateTransform.XProperty,
                new DoubleAnimation
                {
                    From = currentLeft,
                    To = returnLeft,
                    Duration = TimeSpan.FromMilliseconds(420),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
                });
            orbTranslate.BeginAnimation(
                TranslateTransform.YProperty,
                new DoubleAnimation
                {
                    From = currentTop,
                    To = returnTop,
                    Duration = TimeSpan.FromMilliseconds(420),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
                });
            orbScale.BeginAnimation(
                ScaleTransform.ScaleXProperty,
                new DoubleAnimation(0.42, TimeSpan.FromMilliseconds(420)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } });
            orbScale.BeginAnimation(
                ScaleTransform.ScaleYProperty,
                new DoubleAnimation(0.42, TimeSpan.FromMilliseconds(420)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } });
        }

        private WindowsPoint GetNodeCenterInPage(string nodeKey)
        {
            try
            {
                if (_zones.TryGetValue(nodeKey, out var zone) &&
                    zone.ActualWidth > 0 &&
                    zone.ActualHeight > 0)
                {
                    return zone.TranslatePoint(
                        new WindowsPoint(zone.ActualWidth / 2, zone.ActualHeight / 2),
                        this);
                }
            }
            catch
            {
            }

            double width = NodeDetailsLayer?.ActualWidth > 0 ? NodeDetailsLayer.ActualWidth : ActualWidth;
            double height = NodeDetailsLayer?.ActualHeight > 0 ? NodeDetailsLayer.ActualHeight : ActualHeight;
            return new WindowsPoint(width / 2, height / 2);
        }

        private WindowsPoint GetNodeDetailsOrbTargetCenter()
        {
            double width = NodeDetailsLayer?.ActualWidth > 0 ? NodeDetailsLayer.ActualWidth : ActualWidth;
            return new WindowsPoint(width * NodeDetailsOrbTargetXRatio, NodeDetailsOrbTargetCenterY);
        }

        private void PositionNodeDetailsOrb(WindowsPoint center, double scale, double opacity)
        {
            var orbButton = NodeDetailsOrbButtonElement;
            var orbTranslate = NodeDetailsOrbTranslateElement;
            var orbScale = NodeDetailsOrbScaleElement;

            if (orbButton == null || orbTranslate == null || orbScale == null)
                return;

            orbTranslate.X = ToOrbLeft(center);
            orbTranslate.Y = ToOrbTop(center);
            orbScale.ScaleX = scale;
            orbScale.ScaleY = scale;
            orbButton.Opacity = opacity;
        }

        private static void ResetNodeDetailsOrbAnimation(
            UIElement orbButton,
            TranslateTransform orbTranslate,
            ScaleTransform orbScale)
        {
            orbButton?.BeginAnimation(UIElement.OpacityProperty, null);
            orbTranslate?.BeginAnimation(TranslateTransform.XProperty, null);
            orbTranslate?.BeginAnimation(TranslateTransform.YProperty, null);
            orbScale?.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            orbScale?.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        }

        private void StartNodeDetailsOrbBreathing()
        {
            var orbButton = NodeDetailsOrbButtonElement;
            var orbScale = NodeDetailsOrbScaleElement;
            if (orbButton == null || orbScale == null)
                return;

            var scale = new DoubleAnimation(0.96, 1.055, TimeSpan.FromMilliseconds(1650))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            };

            var opacity = new DoubleAnimation(0.86, 1, TimeSpan.FromMilliseconds(1650))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            };

            orbScale.BeginAnimation(ScaleTransform.ScaleXProperty, scale);
            orbScale.BeginAnimation(ScaleTransform.ScaleYProperty, scale.Clone());
            orbButton.BeginAnimation(UIElement.OpacityProperty, opacity);
        }

        private void StopNodeDetailsOrbBreathing()
        {
            var orbButton = NodeDetailsOrbButtonElement;
            var orbScale = NodeDetailsOrbScaleElement;

            orbButton?.BeginAnimation(UIElement.OpacityProperty, null);
            if (orbScale == null)
                return;

            orbScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            orbScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        }

        private double ToOrbLeft(WindowsPoint center)
        {
            var orbButton = NodeDetailsOrbButtonElement;
            double width = orbButton?.ActualWidth > 0 ? orbButton.ActualWidth : NodeDetailsOrbSize;
            return center.X - width / 2;
        }

        private double ToOrbTop(WindowsPoint center)
        {
            var orbButton = NodeDetailsOrbButtonElement;
            double height = orbButton?.ActualHeight > 0 ? orbButton.ActualHeight : NodeDetailsOrbSize;
            return center.Y - height / 2;
        }
    }
}
