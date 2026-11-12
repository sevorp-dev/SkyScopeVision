using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using SkyScope.Core;

namespace SkyScope.Services;

public class ReleaseInfo
{
    [JsonPropertyName("tag_name")] public string Tag { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("html_url")] public string PageUrl { get; set; } = "";
    [JsonPropertyName("assets")] public List<ReleaseAsset> Assets { get; set; } = new();

    public string? SetupUrl =>
        Assets.FirstOrDefault(a => a.Name.Contains("Setup", StringComparison.OrdinalIgnoreCase)
                                   && a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))?.Url;
}

public class ReleaseAsset
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("browser_download_url")] public string Url { get; set; } = "";
}

// Проверяет релизы sevorp-dev/SkyScopeVision на GitHub и качает установщик.
public class UpdateChecker
{
    private const string ApiUrl = "https://api.github.com/repos/sevorp-dev/SkyScopeVision/releases/latest";
    private static readonly HttpClient Http = CreateClient();

    private readonly SettingsStore _store;
    private readonly Version _current;

    public event Action<ReleaseInfo>? UpdateFound;

    public UpdateChecker(SettingsStore store, Version? currentOverride = null)
    {
        _store = store;
        _current = currentOverride ?? AssemblyVersion();
    }

    public static Version AssemblyVersion()
    {
        var info = Assembly.GetEntryAssembly()
            ?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "0.0.0";
        return Version.TryParse(info.Split('+')[0], out var v) ? v : new Version(0, 0, 0);
    }

    public async Task<ReleaseInfo?> CheckAsync()
    {
        try
        {
            var json = await Http.GetStringAsync(ApiUrl);
            var release = JsonSerializer.Deserialize<ReleaseInfo>(json);
            if (release is null || !Version.TryParse(release.Tag.TrimStart('v', 'V'), out var latest))
                return null;
            if (latest <= _current || release.Tag == _store.Current.App.SkippedRelease)
                return null;
            UpdateFound?.Invoke(release);
            return release;
        }
        catch
        {
            return null; // офлайн или сеть недоступна — не мешаем работе
        }
    }

    public async Task<string?> DownloadSetupAsync(ReleaseInfo release, IProgress<double>? progress = null)
    {
        if (release.SetupUrl is null) return null;
        var path = Path.Combine(Path.GetTempPath(), $"SkyScope-Setup-{release.Tag}.exe");
        using var rsp = await Http.GetAsync(release.SetupUrl, HttpCompletionOption.ResponseHeadersRead);
        rsp.EnsureSuccessStatusCode();
        var total = rsp.Content.Headers.ContentLength ?? -1;
        await using var src = await rsp.Content.ReadAsStreamAsync();
        await using var dst = File.Create(path);
        var buf = new byte[81920];
        long done = 0;
        int read;
        while ((read = await src.ReadAsync(buf)) > 0)
        {
            await dst.WriteAsync(buf.AsMemory(0, read));
            done += read;
            if (total > 0) progress?.Report(done / (double)total);
        }
        return path;
    }

    public static void LaunchInstaller(string path)
    {
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("SkyScope-UpdateChecker");
        return c;
    }
}
