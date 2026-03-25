using System.Windows;
using System.Windows.Controls;

namespace TweakWise.Pages
{
    public partial class DashboardPage : Page
    {
        public DashboardPage()
        {
            InitializeComponent();
        }

        private void ApplyOptimization_Click(object sender, RoutedEventArgs e)
        {
            App.NotificationManager.AddNotification("Оптимизация завершена",
                "Выбранные параметры успешно применены. Для вступления изменений может потребоваться перезагрузка.",
                null);
            MessageBox.Show("Оптимизация выполнена (демо).", "TweakWise", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}