using System.Windows;
using RomManager.App.ViewModels;

namespace RomManager.App;

public partial class TitleCleanupWindow : Window
{
    public TitleCleanupWindow() => InitializeComponent();

    private TitleCleanupViewModel ViewModel => (TitleCleanupViewModel)DataContext;

    private async void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is TitleCleanupItem item) await ViewModel.ApplyAsync(item);
    }

    private void Skip_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is TitleCleanupItem item) ViewModel.Skip(item);
    }

    private async void ApplyAll_Click(object sender, RoutedEventArgs e) => await ViewModel.ApplyAllAsync();

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
