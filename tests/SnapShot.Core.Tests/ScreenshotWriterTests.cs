using System;
using System.IO;
using Avig;
using Microsoft.Extensions.Time.Testing;
using SnapShot.Core;
using Xunit;

namespace SnapShot.Core.Tests;

public sealed class ScreenshotWriterTests : IDisposable
{
    private const int Width = 4;
    private const int Height = 4;

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "SnapShotTests", Guid.NewGuid().ToString("N"));

    private readonly ScreenshotWriter _writer = new(new FileNameGenerator(new FakeTimeProvider()));

    private readonly byte[] _pixels = new byte[Width * Height * PngEncoder.SourceBytesPerPixel];

    [Fact]
    public void Save_CreatesFolderAndPng_WithoutLeavingTemporaryFile()
    {
        Result<string> result = _writer.Save(_pixels, Width, Height, _folder, Settings.DefaultFileNamePattern);

        Assert.True(result.IsSuccess(out string? path));
        Assert.True(File.Exists(path));
        Assert.Equal(".png", Path.GetExtension(path));
        Assert.Single(Directory.GetFiles(_folder));
    }

    [Fact]
    public void Save_TwiceInSameSecond_GivesTwoFiles()
    {
        Result<string> first = _writer.Save(_pixels, Width, Height, _folder, Settings.DefaultFileNamePattern);
        Result<string> second = _writer.Save(_pixels, Width, Height, _folder, Settings.DefaultFileNamePattern);

        Assert.True(first.IsSuccess(out string? firstPath));
        Assert.True(second.IsSuccess(out string? secondPath));
        Assert.NotEqual(firstPath, secondPath);
    }

    [Fact]
    public void Save_NoFolder_IsInvalid()
    {
        Result<string> result = _writer.Save(_pixels, Width, Height, string.Empty, Settings.DefaultFileNamePattern);

        Assert.True(result.IsFailure(out string? _, out ResultStatus status));
        Assert.Equal(ResultStatus.Invalid, status);
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }
}
