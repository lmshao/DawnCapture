using CommunityToolkit.Mvvm.DependencyInjection;
using DawnCapture.ViewModels;
using Microsoft.Extensions.DependencyInjection;
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
}
