using SnapShot.Core;
using Xunit;

namespace SnapShot.Core.Tests;

public sealed class SettingsTests
{
    private const string WindowsFolder = @"C:\Users\Someone\Pictures\Screenshots";

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ResolveOutputFolder_NotConfigured_UsesWindowsFolder(string? configured)
    {
        Settings settings = new() { OutputFolder = configured };

        Assert.Equal(WindowsFolder, settings.ResolveOutputFolder(WindowsFolder));
    }

    [Fact]
    public void ResolveOutputFolder_Configured_WinsOverWindowsFolder()
    {
        Settings settings = new() { OutputFolder = @"D:\Klipp" };

        Assert.Equal(@"D:\Klipp", settings.ResolveOutputFolder(WindowsFolder));
    }
}
