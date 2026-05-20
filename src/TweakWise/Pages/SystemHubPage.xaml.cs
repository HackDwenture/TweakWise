using System.Collections.Generic;
using System.Windows.Controls;

namespace TweakWise.Pages
{
    public partial class SystemHubPage : Page
    {
        public SystemHubPage()
        {
            InitializeComponent();

            SectionsItemsControl.ItemsSource = new List<PlaceholderSection>
            {
                new() { Title = "Приватность и телеметрия" },
                new() { Title = "Обновления" },
                new() { Title = "Автозагрузка" },
                new() { Title = "Службы" },
                new() { Title = "Питание" },
                new() { Title = "Встроенные приложения" },
                new() { Title = "Драйверы и устройства" },
                new() { Title = "Сеть" },
                new() { Title = "Поведение системы" }
            };
        }

        private sealed class PlaceholderSection
        {
            public string Title { get; set; } = string.Empty;
        }
    }
}
