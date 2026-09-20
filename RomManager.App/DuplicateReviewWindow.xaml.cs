using System.Windows;
using RomManager.App.ViewModels;

namespace RomManager.App;

public partial class DuplicateReviewWindow : Window
{
    public DuplicateReviewWindow() => InitializeComponent();

    private DuplicateReviewViewModel ViewModel => (DuplicateReviewViewModel)DataContext;

    private async void KeepA_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is DuplicateReviewItem item) await ViewModel.KeepAsync(item, keepA: true);
    }

    private async void KeepB_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is DuplicateReviewItem item) await ViewModel.KeepAsync(item, keepA: false);
    }

    private void Dismiss_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is DuplicateReviewItem item) ViewModel.Dismiss(item);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
