using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.DependencyInjection;
using DawnCapture.Helpers;
using DawnCapture.Services;
using DawnCapture.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using Microsoft.Windows.AppNotifications;

namespace DawnCapture;

public partial class App : Application
{
    public App()
    {
        var settingsService = new SettingsService();

        Log.Init();
        Log.Info("Application startup initialization started.");
        Log.Info($"Log file: {Log.FilePath}");

        InitializeComponent();
        LocalizationService.ApplyLanguage(settingsService.Current.Language);

        UnhandledException += (_, e) =>
        {
            Log.Error("Application.UnhandledException", e.Exception);
            CrashMarkerHelper.Write("Application.UnhandledException");
        };

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            Log.Error("AppDomain.UnhandledException", e.ExceptionObject as Exception);
            CrashMarkerHelper.Write("AppDomain.UnhandledException");
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
        services.AddSingleton<IGlobalHotkeyService, GlobalHotkeyService>();

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
        var instance = AppInstance.FindOrRegisterForKey("DawnCapture.SingleInstance");
        if (!instance.IsCurrent)
        {
            Log.Info("Another instance is already running; this instance exits.");
            return;
        }

        try
        {
            Log.Info("OnLaunched started creating the main window.");

            var settingsService = Ioc.Default.GetRequiredService<ISettingsService>();
            var catalogService = Ioc.Default.GetRequiredService<IRecordingCatalogService>();
            await catalogService.SyncLibraryAsync(OutputFolderHelper.Resolve(settingsService));

            MainWindow = Ioc.Default.GetRequiredService<MainWindow>();
            MainWindow.Activate();
            TryRegisterNotifications();
            NotifyLastCrashIfAny();
            Log.Info("Main window activated.");
        }
        catch (Exception ex)
        {
            Log.Error("OnLaunched failed", ex);
            throw;
        }
    }

    private static void NotifyLastCrashIfAny()
    {
        string? crashedAt = CrashMarkerHelper.TryConsume();
        if (crashedAt is null)
        {
            return;
        }

        Log.Info($"Previous session crashed at {crashedAt}; notifying user.");

        // Wait for the window to finish loading before showing the dialog,
        // so the XamlRoot is ready.
        MainWindow?.DispatcherQueue.TryEnqueue(async () =>
        {
            string message = string.Format(
                LocalizationService.GetString("App_LastSessionCrashed"),
                Log.FilePath);
            await DialogHelper.ShowErrorAsync(
                message,
                LocalizationService.GetString("App_LastSessionCrashedTitle"));
        });
    }

    private static void TryRegisterNotifications()
    {
        try
        {
            AppNotificationManager.Default.NotificationInvoked += OnNotificationInvoked;
            AppNotificationManager.Default.Register();
        }
        catch (Exception ex)
        {
            Log.Info($"App notification registration skipped: {ex.Message}");
        }
    }

    private static void OnNotificationInvoked(
        AppNotificationManager sender,
        AppNotificationActivatedEventArgs args)
    {
        try
        {
            if (!args.Arguments.TryGetValue("action", out string? action) ||
                !string.Equals(action, RecordingNotificationHelper.OpenFolderAction, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (!args.Arguments.TryGetValue("path", out string? folder))
            {
                return;
            }
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            {
                return;
            }

            Process.Start(new ProcessStartInfo("explorer.exe", folder)
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Log.Info($"Notification action failed: {ex.Message}");
        }
    }
}
