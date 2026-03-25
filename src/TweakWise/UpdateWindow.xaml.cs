using System.Windows;
using System.Windows.Input;
using TweakWise.Managers;

namespace TweakWise
{
    public partial class UpdateWindow : Window
    {
        private readonly UpdateManager _updateManager;
        private readonly UpdateCheckResult _result;

        public UpdateWindow(UpdateManager updateManager, UpdateCheckResult result)
        {
            InitializeComponent();

            _updateManager = updateManager;
            _result = result;

            VersionTextBlock.Text = $"Обнаружены изменения: {_result.LatestVersionText}";
            CurrentVersionTextBlock.Text = _result.CurrentVersionText;
            ReleaseNotesTextBlock.Text = _result.ReleaseNotes;
        }

        private void DownloadButton_Click(object sender, RoutedEventArgs e)
        {
            _updateManager.OpenDownload(_result.DownloadUrl);
            DialogResult = true;
            Close();
        }

        private void LaterButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
                DragMove();
        }
    }
}
