using System;
using System.IO;
using Avig;

namespace SnapShot.Core;

/// <summary>Saves a captured region as a PNG file in the output folder.</summary>
public sealed class ScreenshotWriter(FileNameGenerator fileNameGenerator)
{
    private const string TemporaryExtension = ".tmp";

    private readonly FileNameGenerator _fileNameGenerator = fileNameGenerator;

    /// <summary>Encodes the pixels and writes them to a new file.</summary>
    /// <param name="pixels">BGRA pixels of the region, tightly packed.</param>
    /// <param name="width">Width of the region in pixels.</param>
    /// <param name="height">Height of the region in pixels.</param>
    /// <param name="folder">Folder to save in; created if it does not exist.</param>
    /// <param name="fileNamePattern">Composite format string for the name; <c>{0}</c> is the local time.</param>
    /// <returns>The full path of the new file, or why it could not be saved.</returns>
    public Result<string> Save(ReadOnlySpan<byte> pixels, int width, int height, string folder, string fileNamePattern)
    {
        try
        {
            Result<string> path = _fileNameGenerator.CreatePath(folder, fileNamePattern, File.Exists);

            if (path.IsFailure(out string? error, out ResultStatus status, out string? finalPath))
            {
                return Result<string>.Failure(error, status);
            }

            Directory.CreateDirectory(folder);

            // The output folder is synced by OneDrive. Writing under a temporary name and renaming
            // when done keeps it from picking up a half-written file.
            string temporaryPath = finalPath + TemporaryExtension;

            using (FileStream stream = new(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                PngEncoder.Encode(pixels, width, height, width * PngEncoder.SourceBytesPerPixel, stream);
            }

            File.Move(temporaryPath, finalPath, overwrite: false);

            return finalPath;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Result<string>.Failure($"Skärmklippet gick inte att spara: {exception.Message}");
        }
    }
}
