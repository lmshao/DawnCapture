using System;
using DawnCapture.Models;

namespace DawnCapture.Services;

public interface ISettingsService
{
    AppSettings Current { get; }

    event EventHandler? SettingsChanged;

    void Load();

    void Save();
}
