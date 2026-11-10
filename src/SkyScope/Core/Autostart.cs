using Microsoft.Win32;

namespace SkyScope.Core;

public static class Autostart
{
    private const string KeyName = "SkyScope";
    private const string RunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunPath);
        return key?.GetValue(KeyName) is not null;
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunPath, writable: true);
        if (key is null) return;
        if (enabled)
        {
            var exe = Environment.ProcessPath;
            if (exe is not null)
                key.SetValue(KeyName, $"\"{exe}\" --minimized");
        }
        else
        {
            key.DeleteValue(KeyName, throwOnMissingValue: false);
        }
    }
}
