using System.Collections.Generic;
using System.Windows.Controls;

namespace TweakWise.Pages
{
    public partial class WindowsInterfacePage : Page
    {
        public WindowsInterfacePage()
        {
            InitializeComponent();

            SectionsItemsControl.ItemsSource = new List<PlaceholderSection>
            {
                new() { Title = "Проводник" },
                new() { Title = "Меню Пуск" },
                new() { Title = "Панель задач" },
                new() { Title = "Контекстное меню" },
                new() { Title = "Поиск" },
                new() { Title = "Рабочий стол" },
                new() { Title = "Уведомления" }
            };
        }

        private sealed class PlaceholderSection
        {
            public string Title { get; set; } = string.Empty;
        }
    }
}
