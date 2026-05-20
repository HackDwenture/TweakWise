using System.Collections.Generic;
using System.Windows.Controls;

namespace TweakWise.Pages
{
    public partial class MonitoringPerformancePage : Page
    {
        public MonitoringPerformancePage()
        {
            InitializeComponent();

            SectionsItemsControl.ItemsSource = new List<PlaceholderSection>
            {
                new() { Title = "Состояние системы" },
                new() { Title = "Температуры и датчики" },
                new() { Title = "CPU / GPU / RAM / диск" },
                new() { Title = "Батарея и SSD" },
                new() { Title = "Профили производительности" },
                new() { Title = "Вентиляторы и охлаждение" },
                new() { Title = "Безопасный тюнинг" },
                new() { Title = "Расширенные параметры" }
            };
        }

        private sealed class PlaceholderSection
        {
            public string Title { get; set; } = string.Empty;
        }
    }
}
