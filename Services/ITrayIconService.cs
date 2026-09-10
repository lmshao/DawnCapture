using System;

namespace DawnCapture.Services;

public interface ITrayIconService : IDisposable
{
    void Attach(DawnCapture.MainWindow mainWindow);
}
