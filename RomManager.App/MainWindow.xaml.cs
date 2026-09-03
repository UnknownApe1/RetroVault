using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using RomManager.App.ViewModels;
using RomManager.Core.Models;

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
        foreach (var removed in e.RemovedItems) if (removed is GameSummary game) viewModel.SelectedGames.Remove(game);
        foreach (var added in e.AddedItems) if (added is GameSummary game) viewModel.SelectedGames.Add(game);
    }
}
