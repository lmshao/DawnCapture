using CommunityToolkit.Mvvm.DependencyInjection;
using DawnCapture.Models;
using DawnCapture.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace DawnCapture.Views;

public sealed partial class RecordingsPage : Page
{
    public RecordingsViewModel ViewModel { get; }

    public RecordingsPage()
    {
        ViewModel = Ioc.Default.GetRequiredService<RecordingsViewModel>();
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        await ViewModel.RefreshAsync();
    }

    private void OnPlayMenuItemClick(object sender, RoutedEventArgs e)
    {
        if ((sender as MenuFlyoutItem)?.Tag is RecordingListItem item)
        {
            ViewModel.PlayRecordingCommand.Execute(item);
        }
    }

    private async void OnRenameMenuItemClick(object sender, RoutedEventArgs e)
    {
        if ((sender as MenuFlyoutItem)?.Tag is RecordingListItem item)
        {
            await ViewModel.RenameRecordingAsync(item);
        }
    }

    private async void OnDeleteMenuItemClick(object sender, RoutedEventArgs e)
    {
        if ((sender as MenuFlyoutItem)?.Tag is RecordingListItem item)
        {
            await ViewModel.DeleteRecordingAsync(item);
        }
    }

    private void OnRevealMenuItemClick(object sender, RoutedEventArgs e)
    {
        if ((sender as MenuFlyoutItem)?.Tag is RecordingListItem item)
        {
            ViewModel.RevealInFolder(item);
        }
    }
}
