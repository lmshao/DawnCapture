// Copyright (c) 2026 SHAO Liming <lmshao@163.com>
// SPDX-License-Identifier: MIT

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
        Loaded += (_, _) =>
        {
            ViewModel.RefreshHotkeyRegistrationWarning();

            // Endpoints can be plugged in or removed while the app runs, so the lists are
            // rebuilt every time the page is shown.
            ViewModel.RefreshAudioDevices();
        };
    }

    /// <summary>
    /// Device lists are rebuilt whenever the page is shown, which also clears the selection in
    /// the combo box. The lists are therefore bound one way and the user's actual picks arrive
    /// here - a null selection is a rebuild and is ignored by the view model.
    /// </summary>
    private void MicrophoneDevices_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ViewModel.ApplyMicrophoneDevice((sender as ComboBox)?.SelectedItem as SettingsViewModel.AudioDeviceOption);
    }

    private void SystemAudioDevices_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ViewModel.ApplySystemAudioDevice((sender as ComboBox)?.SelectedItem as SettingsViewModel.AudioDeviceOption);
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
