using System.Collections.Generic;
using System.Windows.Controls;

namespace TweakWise.Pages
{
    public partial class MaintenancePage : Page
    {
        public MaintenancePage()
        {
            InitializeComponent();

            SectionsItemsControl.ItemsSource = new List<PlaceholderSection>
            {
                new() { Title = "Очистка файлов" },
                new() { Title = "Системные остатки" },
                new() { Title = "Удаление программ" },
                new() { Title = "Удаление встроенных приложений" },
                new() { Title = "Быстрые исправления" },
                new() { Title = "Обслуживание по расписанию" }
            };
        }

        private sealed class PlaceholderSection
        {
            public string Title { get; set; } = string.Empty;
        }
    }
}
