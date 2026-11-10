using System.IO;
using System.Text.Json;

namespace SkyScope.Core;

public class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _path;
    private Settings _settings;
    private bool _saveScheduled;

    public event Action<Settings>? Changed;

    public SettingsStore()
    {
        _path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SkyScope", "settings.json");
        _settings = Load();
    }

    public Settings Current => _settings;

    private Settings Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                var json = File.ReadAllText(_path);
                var loaded = JsonSerializer.Deserialize<Settings>(json);
                if (loaded is not null && loaded.Profiles.Count > 0)
                {
                    if (!loaded.Profiles.Any(p => p.Id == loaded.ActiveProfileId))
                        loaded.ActiveProfileId = loaded.Profiles[0].Id;
                    return loaded;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Settings load failed: {ex.Message}");
        }
        return Settings.Defaults();
    }

    public void Save()
    {
        if (_saveScheduled) return;
        _saveScheduled = true;
        Task.Run(async () =>
        {
            await Task.Delay(150);
            _saveScheduled = false;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                var json = JsonSerializer.Serialize(_settings, JsonOptions);
                await File.WriteAllTextAsync(_path, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Settings save failed: {ex.Message}");
            }
        });
    }

    public void Update(Action<Settings> mutate)
    {
        mutate(_settings);
        Save();
        Changed?.Invoke(_settings);
    }
}
