using CommunityToolkit.Mvvm.DependencyInjection;
using DawnCapture.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;

namespace DawnCapture.Views;

public sealed partial class AboutPage : Page
{
    public AboutPage()
    {
        InitializeComponent();
        DataContext = Ioc.Default.GetRequiredService<AboutViewModel>();
    }
}
