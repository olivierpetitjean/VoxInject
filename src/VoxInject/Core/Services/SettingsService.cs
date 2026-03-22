using System.Text.Json;
using System.Text.Json.Serialization;
using VoxInject.Core.Models;

namespace VoxInject.Core.Services;

public sealed class SettingsService : ISettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented          = true,
        Converters             = { new JsonStringEnumConverter() },
        PropertyNameCaseInsensitive = true
    };

    private readonly string _settingsPath;

    private AppSettings _current = new();

    public AppSettings Current => _current;

    public event EventHandler<AppSettings>? SettingsChanged;

    public SettingsService()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "VoxInject", "settings.json"))
    { }

    // Overload for tests
    public SettingsService(string settingsPath)
    {
        _settingsPath = settingsPath;
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
        Reload();
    }

    public void Reload()
    {
        if (!File.Exists(_settingsPath))
        {
            _current = new AppSettings();
            return;
        }

        try
        {
            var json = File.ReadAllText(_settingsPath);
            _current = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }
        catch (JsonException)
        {
            // Corrupted settings — fall back to defaults silently
            _current = new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(_settingsPath, json);
        _current = settings;

        SettingsChanged?.Invoke(this, _current);
    }
}
