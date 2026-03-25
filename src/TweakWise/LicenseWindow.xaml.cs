using System.Windows;
using System.Windows.Input;

namespace TweakWise
{
    public partial class LicenseWindow : Window
    {
        public bool Accepted { get; private set; } = false;

        public LicenseWindow()
        {
            InitializeComponent();
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
                this.DragMove();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Accepted = false;
            Close();
        }

        private void AcceptButton_Click(object sender, RoutedEventArgs e)
        {
            Accepted = true;
            Close();
        }

        private void DeclineButton_Click(object sender, RoutedEventArgs e)
        {
            Accepted = false;
            Close();
        }
    }
}