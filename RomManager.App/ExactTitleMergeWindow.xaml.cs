using System.Windows;
using RomManager.App.ViewModels;

namespace RomManager.App;

public partial class ExactTitleMergeWindow : Window
{
    public ExactTitleMergeWindow() => InitializeComponent();

    private ExactTitleMergeViewModel ViewModel => (ExactTitleMergeViewModel)DataContext;

    private async void Merge_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is ExactTitleMergeItem item) await ViewModel.MergeAsync(item);
    }

    private void Skip_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is ExactTitleMergeItem item) ViewModel.Skip(item);
    }

    private async void MergeAll_Click(object sender, RoutedEventArgs e) => await ViewModel.MergeAllAsync();

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
