// Copyright (c) 2026 SHAO Liming <lmshao@163.com>
// SPDX-License-Identifier: MIT

using CommunityToolkit.Mvvm.ComponentModel;

namespace DawnCapture.Models;

public partial class AudioWaveformBar : ObservableObject
{
    public AudioWaveformBar(double height)
    {
        _height = height;
    }

    [ObservableProperty]
    private double _height;
}
