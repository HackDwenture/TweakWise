using System.Collections.Generic;
using System.Windows.Controls;

namespace TweakWise.Pages
{
    public partial class DashboardPage : Page
    {
        public DashboardPage()
        {
            InitializeComponent();

            DashboardBlocksItemsControl.ItemsSource = new List<PlaceholderBlock>
            {
                new() { Title = "Быстрый старт" },
                new() { Title = "Состояние системы" },
                new() { Title = "Последние изменения" }
            };
        }

        private sealed class PlaceholderBlock
        {
            public string Title { get; set; } = string.Empty;
        }
    }
}
