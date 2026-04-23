using System.Windows;
using TweakWise.Catalog;
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
    }
}
