using System;
using System.Collections.Generic;
using System.IO;
using Avig;
using Microsoft.Extensions.Time.Testing;
using SnapShot.Core;
using Xunit;

namespace SnapShot.Core.Tests;

public sealed class FileNameGeneratorTests
{
    private const string Folder = @"C:\Screenshots";

    private readonly FileNameGenerator _generator;

    public FileNameGeneratorTests()
    {
        FakeTimeProvider timeProvider = new(new DateTimeOffset(2026, 10, 3, 16, 45, 12, TimeSpan.Zero));
        timeProvider.SetLocalTimeZone(TimeZoneInfo.Utc);
        _generator = new FileNameGenerator(timeProvider);
    }

    [Fact]
    public void CreatePath_FreeName_UsesLocalTime()
    {
        Result<string> result = _generator.CreatePath(Folder, Settings.DefaultFileNamePattern, static _ => false);

        Assert.True(result.IsSuccess(out string? path));
        Assert.Equal(Path.Combine(Folder, "Screenshot_2026-10-03_16-45-12.png"), path);
    }

    [Fact]
    public void CreatePath_NameTaken_AddsSuffix()
    {
        HashSet<string> taken = new(StringComparer.OrdinalIgnoreCase)
        {
            Path.Combine(Folder, "Screenshot_2026-10-03_16-45-12.png"),
            Path.Combine(Folder, "Screenshot_2026-10-03_16-45-12_2.png"),
        };

        Result<string> result = _generator.CreatePath(Folder, Settings.DefaultFileNamePattern, taken.Contains);

        Assert.True(result.IsSuccess(out string? path));
        Assert.Equal(Path.Combine(Folder, "Screenshot_2026-10-03_16-45-12_3.png"), path);
    }

    [Fact]
    public void CreatePath_EveryNameTaken_Fails()
    {
        Result<string> result = _generator.CreatePath(Folder, Settings.DefaultFileNamePattern, static _ => true);

        Assert.True(result.IsFailure(out string? _, out ResultStatus status));
        Assert.Equal(ResultStatus.Conflict, status);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Screenshot_{0:yyyy")]
    [InlineData("Screenshot_{1}")]
    [InlineData("Screen:shot")]
    public void CreatePath_UnusablePattern_IsInvalid(string pattern)
    {
        Result<string> result = _generator.CreatePath(Folder, pattern, static _ => false);

        Assert.True(result.IsFailure(out string? _, out ResultStatus status));
        Assert.Equal(ResultStatus.Invalid, status);
    }
}
