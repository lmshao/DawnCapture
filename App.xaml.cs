using CommunityToolkit.Mvvm.DependencyInjection;
using DawnCapture.Services;
using DawnCapture.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;

namespace DawnCapture;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();
        ConfigureServices();
    }

    public static Window? MainWindow { get; private set; }

    private static void ConfigureServices()
    {
        var services = new ServiceCollection();

        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IRecordingService, RecordingService>();

        services.AddSingleton<MainViewModel>();
        services.AddSingleton<HomeViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<AboutViewModel>();

        services.AddSingleton<MainWindow>();

        Ioc.Default.ConfigureServices(services.BuildServiceProvider());
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        MainWindow = Ioc.Default.GetRequiredService<MainWindow>();
        MainWindow.Activate();
    }
}
