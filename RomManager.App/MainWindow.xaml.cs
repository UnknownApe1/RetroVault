using System.ComponentModel;
using System.Windows;
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
}
