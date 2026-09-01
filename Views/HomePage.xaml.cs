using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.DependencyInjection;
using DawnCapture.Models;
using DawnCapture.Services;
using DawnCapture.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DawnCapture.Views;

public sealed partial class HomePage : Page
{
    private HomeViewModel _viewModel = null!;

    public HomePage()
    {
        InitializeComponent();

        _viewModel = Ioc.Default.GetRequiredService<HomeViewModel>();
        DataContext = _viewModel;

        _viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(HomeViewModel.IsRecording))
            {
                UpdateStartButtons();
            }
        };
    }

    private void UpdateStartButtons()
    {
        bool idle = !_viewModel.IsRecording;
        FullScreenButton.IsEnabled = idle;
        WindowButton.IsEnabled = idle;
        RegionButton.IsEnabled = idle;
    }

    private async void FullScreenButton_Click(object sender, RoutedEventArgs e)
    {
        await StartAsync(RecordingMode.FullScreen);
    }

    private async void WindowButton_Click(object sender, RoutedEventArgs e)
    {
        await StartAsync(RecordingMode.Window);
    }

    private async void RegionButton_Click(object sender, RoutedEventArgs e)
    {
        await StartAsync(RecordingMode.Region);
    }

    private async Task StartAsync(RecordingMode mode)
    {
        UpdateStartButtons();
        try
        {
            await _viewModel.StartRecordingAsync(mode);
        }
        catch (Exception ex)
        {
            Log.Error("Failed to start recording", ex);
            _viewModel.StatusText = string.Format(
                LocalizationService.GetString("Error_StartRecording"),
                ex.Message);
        }
        finally
        {
            UpdateStartButtons();
        }
    }
}
