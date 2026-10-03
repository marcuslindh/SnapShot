using System;
using Microsoft.Win32;

namespace SnapShot;

/// <summary>Starting with Windows, through the current user's Run key.</summary>
internal static class Autostart
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "SnapShot";

    private static string Command => $"\"{Environment.ProcessPath}\"";

    /// <summary>Gets a value indicating whether this executable starts with Windows.</summary>
    public static bool IsEnabled
    {
        get
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath);

            return string.Equals(key?.GetValue(ValueName) as string, Command, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>Turns starting with Windows on or off.</summary>
    /// <param name="enabled">Whether it should be on.</param>
    public static void Set(bool enabled)
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath);

        if (enabled)
        {
            key.SetValue(ValueName, Command);
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
