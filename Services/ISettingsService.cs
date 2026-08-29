using DawnCapture.Models;

namespace DawnCapture.Services;

public interface ISettingsService
{
    AppSettings Current { get; }

    void Load();

    void Save();
}
