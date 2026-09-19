// Copyright (c) 2026 SHAO Liming <lmshao@163.com>
// SPDX-License-Identifier: MIT

using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
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
    private const string SingleInstanceKey = "DawnCapture.SingleInstance";

    // Stable notification identity for unpackaged builds. Packaged (MSIX)
    // builds ignore this and use their package identity instead.
    private const string AppUserModelId = "DawnCapture.App";

    // The notification registration below is removed by the uninstaller, not from here:
    // AppNotificationManager.Unregister reports success and leaves every key behind (measured).

    private AppInstance? _singleInstance;
    private readonly SettingsService _settingsService;

    public App()
    {
        var settingsService = new SettingsService();
        _settingsService = settingsService;

        Log.Init();
        Log.Info("Application startup initialization started.");
        Log.Info($"Log file: {Log.FilePath}");

        TrySetAppUserModelId();

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
        services.AddSingleton<ITrayIconService, TrayIconService>();
        services.AddSingleton<SessionStateService>();

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
        // Register before the single-instance check so notification
        // activations can be resolved in this process as well.
        TryRegisterNotifications();

        _singleInstance = AppInstance.FindOrRegisterForKey(SingleInstanceKey);
        if (!_singleInstance.IsCurrent)
        {
            Log.Info("Another instance is already running; handling activation.");
            AppActivationArguments activationArgs = AppInstance.GetCurrent().GetActivatedEventArgs();
            if (activationArgs.Kind == ExtendedActivationKind.AppNotification)
            {
                // Launched by a notification action while the main instance
                // is running: perform the action here and exit without
                // opening a second window.
                TryHandleNotificationActivation(activationArgs);
            }
            else
            {
                await _singleInstance.RedirectActivationToAsync(activationArgs);
            }
            Exit();
            return;
        }

        _singleInstance.Activated += OnAppInstanceActivated;

        try
        {
            Log.Info("OnLaunched started creating the main window.");

            var settingsService = Ioc.Default.GetRequiredService<ISettingsService>();
            var catalogService = Ioc.Default.GetRequiredService<IRecordingCatalogService>();

            var mainWindow = Ioc.Default.GetRequiredService<MainWindow>();
            MainWindow = mainWindow;
            Ioc.Default.GetRequiredService<ITrayIconService>().Attach(mainWindow);

            // Must be attached on this thread: the messages it relies on are dispatched by
            // the UI thread's message loop.
            Ioc.Default.GetRequiredService<SessionStateService>().Attach(
                WinRT.Interop.WindowNative.GetWindowHandle(mainWindow));

            mainWindow.Activate();
            _ = SyncLibraryInBackgroundAsync(catalogService, settingsService);
            NotifyPreviousSessionIssueIfAny();
            NotifySettingsResetIfAny();
            Log.Info("Main window activated.");
        }
        catch (Exception ex)
        {
            Log.Error("OnLaunched failed", ex);
            throw;
        }
    }

    /// <summary>
    /// Warms the recordings catalog after the main window is visible so the
    /// first-run hashing pass never delays startup.
    /// </summary>
    private static async Task SyncLibraryInBackgroundAsync(
        IRecordingCatalogService catalogService,
        ISettingsService settingsService)
    {
        try
        {
            await catalogService.SyncLibraryAsync(OutputFolderHelper.Resolve(settingsService));
        }
        catch (Exception ex)
        {
            Log.Error("Background library sync failed", ex);
        }
    }

    private void OnAppInstanceActivated(object? sender, AppActivationArguments args)
    {
        if (args.Kind == ExtendedActivationKind.AppNotification)
        {
            Log.Info("Notification activation redirected from a second instance.");
            TryHandleNotificationActivation(args);
            return;
        }

        Log.Info("Activation received from a second instance.");
        ActivateMainWindowFromRedirect();
    }

    private static void ActivateMainWindowFromRedirect()
    {
        if (MainWindow is not MainWindow mainWindow)
        {
            Log.Info("Redirected activation ignored because the main window is not ready.");
            return;
        }

        mainWindow.DispatcherQueue.TryEnqueue(() =>
        {
            mainWindow.RestoreFromTray();
            Log.Info("Main window restored after redirected activation.");
        });
    }

    /// <summary>
    /// Reports what the previous session left behind. A recording that never finished is the
    /// more actionable of the two: unlike a crash, its file is still on disk and may simply
    /// refuse to open.
    /// </summary>
    private static void NotifyPreviousSessionIssueIfAny()
    {
        SessionMarker? marker = CrashMarkerHelper.TryConsume();
        if (marker is null)
        {
            return;
        }

        if (string.Equals(marker.Kind, CrashMarkerHelper.RecordingKind, StringComparison.Ordinal))
        {
            Log.Info($"Previous recording did not finish ({marker.Detail}).");
            MainWindow?.DispatcherQueue.TryEnqueue(async () =>
            {
                string message = string.Format(
                    LocalizationService.GetString("App_LastRecordingUnfinished"),
                    marker.OutputPath ?? string.Empty);
                bool reveal = await DialogHelper.ShowConfirmAsync(
                    message,
                    LocalizationService.GetString("App_LastRecordingUnfinishedTitle"),
                    LocalizationService.GetString("Recordings_OpenFolder"));
                if (reveal)
                {
                    RevealInExplorer(marker.OutputPath);
                }
            });
            return;
        }

        Log.Info($"Previous session crashed at {marker.Detail}.");

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

    private void NotifySettingsResetIfAny()
    {
        if (!_settingsService.SettingsWereReset)
        {
            return;
        }

        Log.Info("The settings file could not be read and was replaced by defaults.");

        MainWindow?.DispatcherQueue.TryEnqueue(async () =>
        {
            string message = string.Format(
                LocalizationService.GetString("App_SettingsReset"),
                _settingsService.PreservedSettingsPath ?? _settingsService.Current.OutputFolder);
            await DialogHelper.ShowErrorAsync(
                message,
                LocalizationService.GetString("App_SettingsResetTitle"));
        });
    }

    private static void RevealInExplorer(string? path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            string? folder = Path.GetDirectoryName(path);
            string target = File.Exists(path)
                ? $"/select,\"{path}\""
                : Directory.Exists(folder) ? $"\"{folder}\"" : string.Empty;
            if (target.Length == 0)
            {
                return;
            }

            Process.Start(new ProcessStartInfo("explorer.exe", target)
            {
                UseShellExecute = true
            });
            Log.Info($"Revealed the unfinished recording: {path}");
        }
        catch (Exception ex)
        {
            Log.Info($"Could not reveal the unfinished recording: {ex.Message}");
        }
    }

    private static void TrySetAppUserModelId()
    {
        if (IsPackagedProcess())
        {
            return;
        }

        try
        {
            SetCurrentProcessExplicitAppUserModelID(AppUserModelId);
        }
        catch (Exception ex)
        {
            Log.Info($"Failed to set AppUserModelId: {ex.Message}");
        }
    }

    private static bool IsPackagedProcess()
    {
        try
        {
            _ = Windows.ApplicationModel.Package.Current;
            return true;
        }
        catch
        {
            return false;
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);

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
        HandleNotificationAction(args);
    }

    private static void TryHandleNotificationActivation(AppActivationArguments activationArgs)
    {
        if (activationArgs.Kind != ExtendedActivationKind.AppNotification)
        {
            return;
        }

        try
        {
            if (activationArgs.Data is AppNotificationActivatedEventArgs notificationArgs)
            {
                HandleNotificationAction(notificationArgs);
            }
        }
        catch (Exception ex)
        {
            Log.Info($"Notification activation handling failed: {ex.Message}");
        }
    }

    private static void HandleNotificationAction(AppNotificationActivatedEventArgs args)
    {
        try
        {
            if (!args.Arguments.TryGetValue("action", out string? action) ||
                !string.Equals(action, RecordingNotificationHelper.OpenFolderAction, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (!args.Arguments.TryGetValue("path", out string? folder) ||
                string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            {
                return;
            }

            Process.Start(new ProcessStartInfo("explorer.exe", folder)
            {
                UseShellExecute = true
            });
            Log.Info($"Opened recording folder from notification: {folder}");
        }
        catch (Exception ex)
        {
            Log.Info($"Notification action failed: {ex.Message}");
        }
    }
}
