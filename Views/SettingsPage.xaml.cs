using CommunityToolkit.Mvvm.DependencyInjection;
using DawnCapture.Helpers;
using DawnCapture.Models;
using DawnCapture.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using FocusState = Microsoft.UI.Xaml.FocusState;
using Windows.System;

namespace DawnCapture.Views;

public sealed partial class SettingsPage : Page
{
    public SettingsViewModel ViewModel { get; }

    public SettingsPage()
    {
        ViewModel = Ioc.Default.GetRequiredService<SettingsViewModel>();
        InitializeComponent();
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SettingsViewModel.HotkeyCaptureTarget) &&
                ViewModel.HotkeyCaptureTarget != HotkeyCaptureTarget.None)
            {
                this.Focus(FocusState.Programmatic);
            }
        };
        Loaded += (_, _) => ViewModel.RefreshHotkeyRegistrationWarning();
    }

    private void PageRoot_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (ViewModel.HotkeyCaptureTarget == HotkeyCaptureTarget.None)
        {
            return;
        }

        if (e.Key == VirtualKey.Escape)
        {
            ViewModel.CancelHotkeyCapture();
            e.Handled = true;
            return;
        }

        if (HotkeyHelper.IsModifierVirtualKey(e.Key))
        {
            return;
        }

        var binding = HotkeyHelper.FromKeyEvent(e.Key, HotkeyHelper.GetCurrentModifiers());
        if (ViewModel.TryApplyCapturedHotkey(binding))
        {
            e.Handled = true;
        }
    }
}
