using System.Windows;
using TweakWise.Catalog;
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
                new PropertyMetadata(null));

        public SettingCardControl()
        {
            InitializeComponent();
        }

        public SettingCardViewModel Setting
        {
            get => (SettingCardViewModel)GetValue(SettingProperty);
            set => SetValue(SettingProperty, value);
        }
    }
}
