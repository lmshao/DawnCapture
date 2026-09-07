using CommunityToolkit.Mvvm.DependencyInjection;
using DawnCapture.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;

namespace DawnCapture.Views;

public sealed partial class SettingsPage : Page
{
    public SettingsViewModel ViewModel { get; }

    public SettingsPage()
    {
        ViewModel = Ioc.Default.GetRequiredService<SettingsViewModel>();
        InitializeComponent();
    }
}
