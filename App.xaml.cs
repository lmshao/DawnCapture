using System;
using System.Threading.Tasks;
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
        var settingsService = new SettingsService();
        LocalizationService.ApplyLanguage(settingsService.Current.Language);

        Log.Init();
        Log.Info("Application startup initialization started.");
        Log.Info($"Log file: {Log.FilePath}");

        InitializeComponent();

        UnhandledException += (_, e) =>
        {
            Log.Error("Application.UnhandledException", e.Exception);
        };

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            Log.Error("AppDomain.UnhandledException", e.ExceptionObject as Exception);
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Error("TaskScheduler.UnobservedTaskException", e.Exception);
            e.SetObserved();
        };

        ConfigureServices(settingsService);
        Log.Info("Service container initialization completed.");
    }

    public static Window? MainWindow { get; private set; }

    private static void ConfigureServices(ISettingsService settingsService)
    {
        var services = new ServiceCollection();

        services.AddSingleton<IRecordingCatalogService, RecordingCatalogService>();
        services.AddSingleton<IRecordingLibraryService, RecordingLibraryService>();
        services.AddSingleton<ISettingsService>(settingsService);
        services.AddSingleton<IRecordingService, RecordingService>();
        services.AddSingleton<IMonitorService, MonitorService>();

        services.AddSingleton<MainViewModel>();
        services.AddSingleton<CaptureViewModel>();
        services.AddSingleton<RecordingsViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<AboutViewModel>();

        services.AddSingleton<MainWindow>();

        Ioc.Default.ConfigureServices(services.BuildServiceProvider());
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            Log.Info("OnLaunched started creating the main window.");

            var settingsService = Ioc.Default.GetRequiredService<ISettingsService>();
            var catalogService = Ioc.Default.GetRequiredService<IRecordingCatalogService>();
            await catalogService.SyncLibraryAsync(settingsService.Current.OutputFolder);

            MainWindow = Ioc.Default.GetRequiredService<MainWindow>();
            MainWindow.Activate();
            Log.Info("Main window activated.");
        }
        catch (Exception ex)
        {
            Log.Error("OnLaunched failed", ex);
            throw;
        }
    }
}
