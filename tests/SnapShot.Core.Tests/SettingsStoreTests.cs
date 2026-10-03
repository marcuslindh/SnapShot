using System;
using System.IO;
using Avig;
using SnapShot.Core;
using Xunit;

namespace SnapShot.Core.Tests;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "SnapShotTests", Guid.NewGuid().ToString("N"));

    private string FilePath => Path.Combine(_folder, "settings.json");

    [Fact]
    public void Load_MissingFile_ReturnsDefaultsAndCreatesFile()
    {
        SettingsStore store = new(FilePath);

        Result<Settings> result = store.Load();

        Assert.True(result.IsSuccess(out Settings? settings));
        Assert.Equal(new Settings(), settings);
        Assert.True(File.Exists(FilePath));
    }

    [Fact]
    public void SaveThenLoad_RoundTrips()
    {
        SettingsStore store = new(FilePath);
        Settings original = new()
        {
            OutputFolder = @"D:\Klipp",
            FileNamePattern = "Klipp_{0:HHmmss}",
            PlaySound = true,
            ShowNotification = false,
        };

        Result saved = store.Save(original);
        Result<Settings> loaded = store.Load();

        Assert.True(saved.IsSuccess(out string? _, out ResultStatus _));
        Assert.True(loaded.IsSuccess(out Settings? settings));
        Assert.Equal(original, settings);
    }

    [Fact]
    public void Load_UsesCamelCaseNames_AndDefaultsForMissingValues()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(FilePath, """{ "outputFolder": "D:\\Klipp", "playSound": true }""");

        Result<Settings> result = new SettingsStore(FilePath).Load();

        Assert.True(result.IsSuccess(out Settings? settings));
        Assert.Equal(@"D:\Klipp", settings.OutputFolder);
        Assert.True(settings.PlaySound);
        Assert.Equal(Settings.DefaultFileNamePattern, settings.FileNamePattern);
    }

    [Fact]
    public void Load_BrokenJson_IsInvalid()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(FilePath, "{ inte json");

        Result<Settings> result = new SettingsStore(FilePath).Load();

        Assert.True(result.IsFailure(out string? _, out ResultStatus status));
        Assert.Equal(ResultStatus.Invalid, status);
    }

    [Fact]
    public void Load_NullForRequiredValue_IsInvalid()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(FilePath, """{ "fileNamePattern": null }""");

        Result<Settings> result = new SettingsStore(FilePath).Load();

        Assert.True(result.IsFailure(out string? _, out ResultStatus status));
        Assert.Equal(ResultStatus.Invalid, status);
    }

    [Fact]
    public void Load_MissingFile_WritesNoOutputFolder_SoWindowsFolderIsFollowed()
    {
        SettingsStore store = new(FilePath);

        Result<Settings> result = store.Load();

        Assert.True(result.IsSuccess(out Settings? settings));
        Assert.Null(settings.OutputFolder);
        Assert.DoesNotContain("outputFolder", File.ReadAllText(FilePath), StringComparison.Ordinal);
    }

    [Fact]
    public void Load_NullOutputFolder_IsAllowed()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(FilePath, """{ "outputFolder": null }""");

        Result<Settings> result = new SettingsStore(FilePath).Load();

        Assert.True(result.IsSuccess(out Settings? settings));
        Assert.Null(settings.OutputFolder);
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }
}
