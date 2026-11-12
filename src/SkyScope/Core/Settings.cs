using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace SkyScope.Core;

public enum ScopeShape
{
    Circle,
    Square,
    Rectangle,
    RoundedSquare,
}

public enum ScopePosition
{
    Center,
    Top,
    Bottom,
    Left,
    Right,
    Custom,
}

public class HotkeyMap
{
    public string Toggle { get; set; } = "Ctrl+Shift+Z";
    public string ZoomIn { get; set; } = "Ctrl+T";
    public string ZoomOut { get; set; } = "Ctrl+G";
    public string Reset { get; set; } = "Ctrl+R";
}

public class ScopeStyle
{
    public ScopeShape Shape { get; set; } = ScopeShape.Circle;
    public int Radius { get; set; } = 150;
    public string BorderColor { get; set; } = "#FF7D1F";
    public double BorderWidth { get; set; } = 3;
    public double BorderOpacity { get; set; } = 1;
    public bool DimOutside { get; set; }
    public double DimOpacity { get; set; } = 0.55;
    public bool ShowZoomBadge { get; set; } = true;
    /// Положение окна вывода на мониторе.
    public ScopePosition Position { get; set; } = ScopePosition.Center;
    /// Кастомное смещение центра окна относительно центра монитора (логические px).
    public int CustomOffsetX { get; set; }
    public int CustomOffsetY { get; set; }
}

public class ZoomSettings
{
    public double Level { get; set; } = 2;
    public double Step { get; set; } = 0.5;
    public double Min { get; set; } = 1.25;
    public double Max { get; set; } = 8;
    public bool Smooth { get; set; } = true;
}

public class AccessibilitySettings
{
    public bool ReduceMotion { get; set; }
}

public class Profile
{
    public string Id { get; set; } = "default";
    public string Name { get; set; } = "По умолчанию";
    public ScopeStyle Scope { get; set; } = new();
    public ZoomSettings Zoom { get; set; } = new();
    public AccessibilitySettings Accessibility { get; set; } = new();
}

public class AppSettings
{
    public bool RunAtStartup { get; set; }
    public bool ShowTrayIcon { get; set; } = true;
    public int CaptureFps { get; set; } = 60;
}

public class Settings
{
    public int Version { get; set; } = 1;
    public HotkeyMap Hotkeys { get; set; } = new();
    public string ActiveProfileId { get; set; } = "default";
    public ObservableCollection<Profile> Profiles { get; set; } = [new()];
    public AppSettings App { get; set; } = new();

    [JsonIgnore]
    public Profile ActiveProfile =>
        Profiles.FirstOrDefault(p => p.Id == ActiveProfileId) ?? Profiles[0];

    public static Settings Defaults() => new();

    public Profile CreateProfile(string? name)
    {
        var source = ActiveProfile;
        var profile = new Profile
        {
            Id = $"p_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds():x}_{Guid.NewGuid():N}"[..16],
            Name = string.IsNullOrWhiteSpace(name) ? $"Профиль {Profiles.Count + 1}" : name.Trim()[..Math.Min(40, name.Trim().Length)],
            Scope = System.Text.Json.JsonSerializer.Deserialize<ScopeStyle>(System.Text.Json.JsonSerializer.Serialize(source.Scope))!,
            Zoom = System.Text.Json.JsonSerializer.Deserialize<ZoomSettings>(System.Text.Json.JsonSerializer.Serialize(source.Zoom))!,
            Accessibility = System.Text.Json.JsonSerializer.Deserialize<AccessibilitySettings>(System.Text.Json.JsonSerializer.Serialize(source.Accessibility))!,
        };
        Profiles.Add(profile);
        ActiveProfileId = profile.Id;
        return profile;
    }

    public bool DeleteProfile(string id)
    {
        if (Profiles.Count <= 1) return false;
        var profile = Profiles.FirstOrDefault(p => p.Id == id);
        if (profile is null) return false;
        Profiles.Remove(profile);
        if (ActiveProfileId == id) ActiveProfileId = Profiles[0].Id;
        return true;
    }
}
