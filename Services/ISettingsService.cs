using System;
using DawnCapture.Models;

namespace DawnCapture.Services;

public interface ISettingsService
{
    AppSettings Current { get; }

    event EventHandler? SettingsChanged;

    void Load();

    bool Save();

    /// <summary>
    /// Restores every preference to the built-in defaults. Returns false and
    /// leaves the previous settings in place when the new values cannot be saved.
    /// Recordings and other user data are not affected.
    /// </summary>
    bool ResetToDefaults();
}
