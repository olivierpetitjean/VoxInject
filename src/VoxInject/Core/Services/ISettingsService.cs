using VoxInject.Core.Models;

namespace VoxInject.Core.Services;

public interface ISettingsService
{
    AppSettings Current { get; }

    void Save(AppSettings settings);
    void Reload();

    event EventHandler<AppSettings>? SettingsChanged;
}
