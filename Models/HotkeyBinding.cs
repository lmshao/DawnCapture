// Copyright (c) 2026 SHAO Liming <lmshao@163.com>
// SPDX-License-Identifier: MIT

using System;

namespace DawnCapture.Models;

public sealed class HotkeyBinding : IEquatable<HotkeyBinding>
{
    public uint Modifiers { get; set; }

    public uint VirtualKey { get; set; }

    public static HotkeyBinding ToggleRecordingDefault => new() { VirtualKey = 0x78 };

    public static HotkeyBinding TogglePauseDefault => new() { VirtualKey = 0x79 };

    public bool IsEmpty => VirtualKey == 0;

    public bool Equals(HotkeyBinding? other)
    {
        if (other is null)
        {
            return false;
        }

        return Modifiers == other.Modifiers && VirtualKey == other.VirtualKey;
    }

    public override bool Equals(object? obj) => obj is HotkeyBinding other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Modifiers, VirtualKey);
}
