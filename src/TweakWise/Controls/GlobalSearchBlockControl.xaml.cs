using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TweakWise.Search;
using Application = System.Windows.Application;
using Button = System.Windows.Controls.Button;
using UserControl = System.Windows.Controls.UserControl;

namespace TweakWise.Controls
{
    public partial class GlobalSearchBlockControl : UserControl
    {
        public static readonly DependencyProperty IsCompactProperty =
            DependencyProperty.Register(
                nameof(IsCompact),
                typeof(bool),
                typeof(GlobalSearchBlockControl),
                new PropertyMetadata(false, OnIsCompactChanged));

        private bool _suppressRefresh;

        public GlobalSearchBlockControl()
        {
            InitializeComponent();
            ApplyCompactMode();
        }

        public bool IsCompact
        {
            get => (bool)GetValue(IsCompactProperty);
            set => SetValue(IsCompactProperty, value);
        }

        private void GlobalSearchTextBox_GotKeyboardFocus(object sender, RoutedEventArgs e)
        {
            RefreshGlobalSearchResults();
        }

        private void GlobalSearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_suppressRefresh)
                return;

            RefreshGlobalSearchResults();
        }

        private async void GlobalSearchTextBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Escape)
            {
                ClearGlobalSearchState();
                e.Handled = true;
                return;
            }

            if (e.Key != System.Windows.Input.Key.Enter || GlobalSearchResultsItemsControl.Items.Count == 0)
                return;

            if (GlobalSearchResultsItemsControl.Items[0] is not GlobalSearchResultViewModel result)
                return;

            e.Handled = true;
            await HandleGlobalSearchSelectionAsync(result);
        }

        private async void GlobalSearchResultButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not GlobalSearchResultViewModel result)
                return;

            await HandleGlobalSearchSelectionAsync(result);
        }

        private void GlobalSearchPopup_Closed(object sender, System.EventArgs e)
        {
            if (!GlobalSearchTextBox.IsKeyboardFocusWithin)
                GlobalSearchResultsItemsControl.ItemsSource = null;
        }

        private void RefreshGlobalSearchResults()
        {
            var searchService = App.GlobalSearchService;
            if (searchService == null)
                return;

            string query = GlobalSearchTextBox.Text?.Trim() ?? string.Empty;
            var results = searchService.Search(query, 9);

            GlobalSearchResultsItemsControl.ItemsSource = results;
            GlobalSearchPopupCaption.Text = string.IsNullOrWhiteSpace(query)
                ? "Быстрый переход и действия"
                : "Результаты поиска";

            bool hasResults = results.Count > 0;
            GlobalSearchScrollViewer.Visibility = hasResults ? Visibility.Visible : Visibility.Collapsed;
            GlobalSearchEmptyText.Visibility = !hasResults && !string.IsNullOrWhiteSpace(query)
                ? Visibility.Visible
                : Visibility.Collapsed;
            GlobalSearchPopup.IsOpen = hasResults || !string.IsNullOrWhiteSpace(query);
        }

        private async Task HandleGlobalSearchSelectionAsync(GlobalSearchResultViewModel result)
        {
            ClearGlobalSearchState();

            if (Application.Current.MainWindow is MainWindow mainWindow)
                await mainWindow.HandleGlobalSearchSelectionAsync(result);
        }

        private void ClearGlobalSearchState()
        {
            _suppressRefresh = true;
            GlobalSearchTextBox.Text = string.Empty;
            GlobalSearchResultsItemsControl.ItemsSource = null;
            GlobalSearchPopup.IsOpen = false;
            GlobalSearchEmptyText.Visibility = Visibility.Collapsed;
            GlobalSearchScrollViewer.Visibility = Visibility.Visible;
            _suppressRefresh = false;
        }

        private static void OnIsCompactChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is GlobalSearchBlockControl control)
                control.ApplyCompactMode();
        }

        private void ApplyCompactMode()
        {
            bool compact = IsCompact;

            HeaderTextBlock.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
            DescriptionTextBlock.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
            FooterTextBlock.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;

            if (compact)
            {
                ContainerBorder.Background = System.Windows.Media.Brushes.Transparent;
                ContainerBorder.BorderBrush = null;
                ContainerBorder.BorderThickness = new Thickness(0);
                ContainerBorder.Padding = new Thickness(0);
                SearchInputBorder.Margin = new Thickness(0);
                GlobalSearchTextBox.ToolTip = "Глобальный поиск по разделам, настройкам, шаблонам и действиям";
            }
            else
            {
                ContainerBorder.ClearValue(Border.BackgroundProperty);
                ContainerBorder.ClearValue(Border.BorderBrushProperty);
                ContainerBorder.ClearValue(Border.BorderThicknessProperty);
                ContainerBorder.ClearValue(Border.PaddingProperty);
                SearchInputBorder.Margin = new Thickness(0, 14, 0, 0);
                GlobalSearchTextBox.ToolTip = "Искать разделы, настройки, шаблоны и действия";
            }
        }
    }
}
