using System.Windows;
using TweakWise.Catalog;
using Border = System.Windows.Controls.Border;
using TextBlock = System.Windows.Controls.TextBlock;
using UserControl = System.Windows.Controls.UserControl;

namespace TweakWise.Controls
{
    public partial class CatalogBadgeControl : UserControl
    {
        public static readonly DependencyProperty TextProperty =
            DependencyProperty.Register(
                nameof(Text),
                typeof(string),
                typeof(CatalogBadgeControl),
                new PropertyMetadata(string.Empty));

        public static readonly DependencyProperty ToneProperty =
            DependencyProperty.Register(
                nameof(Tone),
                typeof(CatalogBadgeTone),
                typeof(CatalogBadgeControl),
                new PropertyMetadata(CatalogBadgeTone.Neutral, OnToneChanged));

        public CatalogBadgeControl()
        {
            InitializeComponent();
            ApplyTone();
        }

        public string Text
        {
            get => (string)GetValue(TextProperty);
            set => SetValue(TextProperty, value);
        }

        public CatalogBadgeTone Tone
        {
            get => (CatalogBadgeTone)GetValue(ToneProperty);
            set => SetValue(ToneProperty, value);
        }

        private static void OnToneChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is CatalogBadgeControl control)
                control.ApplyTone();
        }

        private void ApplyTone()
        {
            BadgeBorder.SetResourceReference(Border.BackgroundProperty, GetBackgroundKey(Tone));
            BadgeBorder.SetResourceReference(Border.BorderBrushProperty, GetBorderBrushKey(Tone));
            BadgeTextBlock.SetResourceReference(TextBlock.ForegroundProperty, GetForegroundKey(Tone));
        }

        private static string GetBackgroundKey(CatalogBadgeTone tone)
        {
            return tone switch
            {
                CatalogBadgeTone.Info => "ContentBackground",
                CatalogBadgeTone.Success => "ContentBackground",
                CatalogBadgeTone.Warning => "ContentBackground",
                CatalogBadgeTone.Danger => "ContentBackground",
                _ => "ContentBackground"
            };
        }

        private static string GetBorderBrushKey(CatalogBadgeTone tone)
        {
            return tone switch
            {
                CatalogBadgeTone.Info => "DialogInfoForeground",
                CatalogBadgeTone.Success => "DialogSuccessForeground",
                CatalogBadgeTone.Warning => "DialogWarningForeground",
                CatalogBadgeTone.Danger => "DialogErrorForeground",
                _ => "BorderBrush"
            };
        }

        private static string GetForegroundKey(CatalogBadgeTone tone)
        {
            return tone switch
            {
                CatalogBadgeTone.Info => "DialogInfoForeground",
                CatalogBadgeTone.Success => "DialogSuccessForeground",
                CatalogBadgeTone.Warning => "DialogWarningForeground",
                CatalogBadgeTone.Danger => "DialogErrorForeground",
                _ => "WindowForeground"
            };
        }
    }
}
