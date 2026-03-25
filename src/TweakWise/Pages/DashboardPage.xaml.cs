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
            App.NotificationManager.AddNotification(
                "Оптимизация завершена",
                "Выбранные параметры успешно применены. Для вступления изменений может потребоваться перезагрузка.");

            App.DialogManager.Show(
                Application.Current.MainWindow,
                "Оптимизация",
                "Изменения применены",
                "Оптимизация выполнена в демонстрационном режиме.",
                AppDialogKind.Success);
        }
    }
}
