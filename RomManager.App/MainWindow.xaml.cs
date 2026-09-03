using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using RomManager.App.ViewModels;

namespace RomManager.App;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    protected override void OnClosing(CancelEventArgs e)
    {
        if (DataContext is MainViewModel { IsScanning: true } viewModel)
        {
            var answer = MessageBox.Show("A library scan is still running. Exit and resume it automatically next time?", "Scan in progress", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes) { e.Cancel = true; return; }
            viewModel.CancelActiveScan();
        }
        base.OnClosing(e);
    }

    private void GamesListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel) return;
        foreach (var removed in e.RemovedItems) if (removed is GameListItem game) viewModel.SelectedGames.Remove(game);
        foreach (var added in e.AddedItems) if (added is GameListItem game) viewModel.SelectedGames.Add(game);
    }

    // Uses Loaded rather than DataContextChanged: inside a GridViewColumn.CellTemplate, DataContextChanged
    // never fires on the cell's root element (a WPF quirk with GridView's cell content hosting), while
    // Loaded fires reliably both for freshly-realized rows and for recycled containers bound to new rows.
    private void ThumbnailCell_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: GameListItem item }) item.EnsureThumbnailLoadedAsync();
    }
}
