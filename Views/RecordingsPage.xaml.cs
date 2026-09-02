using CommunityToolkit.Mvvm.DependencyInjection;
using DawnCapture.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;

namespace DawnCapture.Views;

public sealed partial class RecordingsPage : Page
{
    public RecordingsViewModel ViewModel { get; }

    public RecordingsPage()
    {
        ViewModel = Ioc.Default.GetRequiredService<RecordingsViewModel>();
        InitializeComponent();
    }
}
