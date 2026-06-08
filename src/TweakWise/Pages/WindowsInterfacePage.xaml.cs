using System.Windows;
using System.Windows.Controls;
using Application = System.Windows.Application;
using TweakWise.Models;

namespace TweakWise.Pages
{
    public partial class WindowsInterfacePage : Page
    {
        public WindowsInterfacePage()
        {
            InitializeComponent();
            Loaded += WindowsInterfacePage_Loaded;
        }

        private void WindowsInterfacePage_Loaded(object sender, RoutedEventArgs e)
        {
            if (Application.Current.MainWindow is MainWindow mainWindow)
                mainWindow.OpenModuleWorkspace(CoreModuleId.WindowsSetup);
        }
    }
}
