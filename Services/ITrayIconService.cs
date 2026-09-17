// Copyright (c) 2026 SHAO Liming <lmshao@163.com>
// SPDX-License-Identifier: MIT

using System;

namespace DawnCapture.Services;

public interface ITrayIconService : IDisposable
{
    void Attach(DawnCapture.MainWindow mainWindow);
}
