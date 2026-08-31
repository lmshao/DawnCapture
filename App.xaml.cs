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
        Log.Info("应用启动，开始初始化。");
        Log.Info($"日志文件：{Log.FilePath}");

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
        Log.Info("服务容器初始化完成。");
    }

    public static Window? MainWindow { get; private set; }

    private static void ConfigureServices(ISettingsService settingsService)
    {
        var services = new ServiceCollection();

        services.AddSingleton<ISettingsService>(settingsService);
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
        try
        {
            Log.Info("OnLaunched 开始创建主窗口。");
            MainWindow = Ioc.Default.GetRequiredService<MainWindow>();
            MainWindow.Activate();
            Log.Info("主窗口已激活。");
        }
        catch (Exception ex)
        {
            Log.Error("OnLaunched 失败", ex);
            throw;
        }
    }
}
