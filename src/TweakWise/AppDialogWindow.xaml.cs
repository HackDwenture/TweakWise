using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;

namespace TweakWise
{
    public partial class AppDialogWindow : Window
    {
        public AppDialogResult Result { get; private set; } = AppDialogResult.None;

        public AppDialogWindow(string title, string header, string message, AppDialogKind kind, AppDialogButtons buttons)
        {
            InitializeComponent();

            DialogTitleTextBlock.Text = title;
            DialogHeaderTextBlock.Text = header;
            DialogMessageTextBlock.Text = message;

            ConfigureKind(kind);
            ConfigureButtons(buttons);
        }

        private void ConfigureKind(AppDialogKind kind)
        {
            string backgroundKey;
            string foregroundKey;
            string glyph;

            switch (kind)
            {
                case AppDialogKind.Success:
                    backgroundKey = "DialogSuccessBackground";
                    foregroundKey = "DialogSuccessForeground";
                    glyph = "\uE73E";
                    break;
                case AppDialogKind.Warning:
                    backgroundKey = "DialogWarningBackground";
                    foregroundKey = "DialogWarningForeground";
                    glyph = "\uE7BA";
                    break;
                case AppDialogKind.Error:
                    backgroundKey = "DialogErrorBackground";
                    foregroundKey = "DialogErrorForeground";
                    glyph = "\uEA39";
                    break;
                default:
                    backgroundKey = "DialogInfoBackground";
                    foregroundKey = "DialogInfoForeground";
                    glyph = "\uE946";
                    break;
            }

            if (TryFindResource(backgroundKey) is Brush background)
                IconBadge.Background = background;

            if (TryFindResource(foregroundKey) is Brush foreground)
                DialogIconTextBlock.Foreground = foreground;

            DialogIconTextBlock.Text = glyph;
        }

        private void ConfigureButtons(AppDialogButtons buttons)
        {
            switch (buttons)
            {
                case AppDialogButtons.YesNo:
                    SecondaryButton.Visibility = Visibility.Visible;
                    SecondaryButton.Content = "Нет";
                    PrimaryButton.Content = "Да";
                    break;
                default:
                    SecondaryButton.Visibility = Visibility.Collapsed;
                    PrimaryButton.Content = "OK";
                    break;
            }
        }

        private void PrimaryButton_Click(object sender, RoutedEventArgs e)
        {
            Result = AppDialogResult.Primary;
            DialogResult = true;
            Close();
        }

        private void SecondaryButton_Click(object sender, RoutedEventArgs e)
        {
            Result = AppDialogResult.Secondary;
            DialogResult = false;
            Close();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Result = AppDialogResult.None;
            Close();
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
                DragMove();
        }
    }

    public enum AppDialogKind
    {
        Info,
        Success,
        Warning,
        Error
    }

    public enum AppDialogButtons
    {
        Ok,
        YesNo
    }

    public enum AppDialogResult
    {
        None,
        Primary,
        Secondary
    }
}
