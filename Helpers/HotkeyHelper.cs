using System;
using System.Collections.Generic;
using DawnCapture.Models;
using DawnCapture.Services;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace DawnCapture.Helpers;

public static class HotkeyHelper
{
    public const uint ModAlt = 0x0001;
    public const uint ModControl = 0x0002;
    public const uint ModShift = 0x0004;
    public const uint ModWin = 0x0008;
    public const uint ModNorepeat = 0x4000;

    private const uint FunctionKeyMin = 0x70;
    private const uint FunctionKeyMax = 0x87;

    public static HotkeyBinding FromKeyEvent(VirtualKey key, uint modifiers)
    {
        return new HotkeyBinding
        {
            Modifiers = NormalizeModifiers(modifiers),
            VirtualKey = (uint)key
        };
    }

    public static uint GetCurrentModifiers()
    {
        uint modifiers = 0;

        if (IsKeyDown(VirtualKey.Control) || IsKeyDown(VirtualKey.LeftControl) || IsKeyDown(VirtualKey.RightControl))
        {
            modifiers |= ModControl;
        }

        if (IsKeyDown(VirtualKey.Menu) || IsKeyDown(VirtualKey.LeftMenu) || IsKeyDown(VirtualKey.RightMenu))
        {
            modifiers |= ModAlt;
        }

        if (IsKeyDown(VirtualKey.Shift) || IsKeyDown(VirtualKey.LeftShift) || IsKeyDown(VirtualKey.RightShift))
        {
            modifiers |= ModShift;
        }

        if (IsKeyDown(VirtualKey.LeftWindows) || IsKeyDown(VirtualKey.RightWindows))
        {
            modifiers |= ModWin;
        }

        return modifiers;
    }

    public static bool IsModifierVirtualKey(VirtualKey key)
    {
        return key is VirtualKey.Control or VirtualKey.LeftControl or VirtualKey.RightControl
            or VirtualKey.Menu or VirtualKey.LeftMenu or VirtualKey.RightMenu
            or VirtualKey.Shift or VirtualKey.LeftShift or VirtualKey.RightShift
            or VirtualKey.LeftWindows or VirtualKey.RightWindows;
    }

    public static string FormatDisplay(HotkeyBinding binding)
    {
        if (binding.IsEmpty)
        {
            return LocalizationService.GetString("Settings_Hotkey_None");
        }

        var parts = new List<string>();
        if ((binding.Modifiers & ModControl) != 0)
        {
            parts.Add("Ctrl");
        }

        if ((binding.Modifiers & ModAlt) != 0)
        {
            parts.Add("Alt");
        }

        if ((binding.Modifiers & ModShift) != 0)
        {
            parts.Add("Shift");
        }

        if ((binding.Modifiers & ModWin) != 0)
        {
            parts.Add("Win");
        }

        parts.Add(FormatVirtualKey(binding.VirtualKey));
        return string.Join("+", parts);
    }

    public static string? ValidateForSettings(
        HotkeyBinding candidate,
        HotkeyBinding otherBinding,
        Func<HotkeyBinding, bool> probeAvailability)
    {
        if (candidate.IsEmpty)
        {
            return LocalizationService.GetString("Settings_Hotkey_Error_Invalid");
        }

        if (!IsAllowedGlobalBinding(candidate))
        {
            return LocalizationService.GetString("Settings_Hotkey_Error_NeedModifier");
        }

        if (candidate.Equals(otherBinding))
        {
            return LocalizationService.GetString("Settings_Hotkey_Error_Duplicate");
        }

        if (!probeAvailability(candidate))
        {
            return LocalizationService.GetString("Settings_Hotkey_Error_External");
        }

        return null;
    }

    public static uint ToRegisterHotKeyModifiers(uint modifiers) =>
        NormalizeModifiers(modifiers) | ModNorepeat;

    private static bool IsAllowedGlobalBinding(HotkeyBinding binding)
    {
        if (IsFunctionKey(binding.VirtualKey))
        {
            return true;
        }

        return NormalizeModifiers(binding.Modifiers) != 0;
    }

    private static bool IsFunctionKey(uint virtualKey) =>
        virtualKey is >= FunctionKeyMin and <= FunctionKeyMax;

    private static uint NormalizeModifiers(uint modifiers) =>
        modifiers & (ModAlt | ModControl | ModShift | ModWin);

    private static bool IsKeyDown(VirtualKey key)
    {
        return InputKeyboardSource.GetKeyStateForCurrentThread(key)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
    }

    private static string FormatVirtualKey(uint virtualKey)
    {
        if (IsFunctionKey(virtualKey))
        {
            return $"F{virtualKey - FunctionKeyMin + 1}";
        }

        if (virtualKey is >= 0x30 and <= 0x39)
        {
            return ((char)virtualKey).ToString();
        }

        if (virtualKey is >= 0x41 and <= 0x5A)
        {
            return ((char)virtualKey).ToString();
        }

        return virtualKey switch
        {
            0x20 => "Space",
            0x0D => "Enter",
            0x1B => "Esc",
            0x09 => "Tab",
            0x2E => "Delete",
            0x24 => "Home",
            0x23 => "End",
            0x21 => "Page Up",
            0x22 => "Page Down",
            0x25 => "Left",
            0x26 => "Up",
            0x27 => "Right",
            0x28 => "Down",
            _ => $"VK{virtualKey:X}"
        };
    }
}
