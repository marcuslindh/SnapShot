using System;
using System.Globalization;
using System.IO;
using Avig;

namespace SnapShot.Core;

/// <summary>
/// Picks the path a new screenshot is saved to: a timestamped name in the output folder, with a
/// numeric suffix when two screenshots land in the same second.
/// </summary>
public sealed class FileNameGenerator(TimeProvider timeProvider)
{
    /// <summary>File extension of every screenshot.</summary>
    public const string Extension = ".png";

    // Far beyond anything a person can produce within one second; reaching it means something
    // else is filling the folder and looping on would only hide that.
    private const int MaximumSuffix = 1000;

    private const int FirstSuffix = 2;

    private readonly TimeProvider _timeProvider = timeProvider;

    /// <summary>
    /// Returns a path in <paramref name="folder"/> that does not exist yet.
    /// </summary>
    /// <param name="folder">Folder the screenshot is saved in.</param>
    /// <param name="fileNamePattern">
    /// Composite format string for the name without extension; <c>{0}</c> is the local time.
    /// </param>
    /// <param name="fileExists">Tells whether a path is already taken.</param>
    /// <returns>The full path, or why no path could be produced.</returns>
    public Result<string> CreatePath(string folder, string fileNamePattern, Func<string, bool> fileExists)
    {
        ArgumentNullException.ThrowIfNull(fileExists);

        if (string.IsNullOrWhiteSpace(folder))
        {
            return Result<string>.Invalid("Ingen mapp för skärmklipp är angiven.");
        }

        if (!TryFormatName(fileNamePattern, out string baseName))
        {
            return Result<string>.Invalid($"Filnamnsmönstret \"{fileNamePattern}\" går inte att använda.");
        }

        string candidate = Path.Combine(folder, baseName + Extension);
        int suffix = FirstSuffix;

        while (fileExists(candidate))
        {
            if (suffix > MaximumSuffix)
            {
                return Result<string>.Conflict($"Det finns redan {MaximumSuffix} filer som heter {baseName}.");
            }

            candidate = Path.Combine(folder, string.Create(CultureInfo.InvariantCulture, $"{baseName}_{suffix}{Extension}"));
            suffix++;
        }

        return candidate;
    }

    private bool TryFormatName(string fileNamePattern, out string baseName)
    {
        baseName = string.Empty;

        if (string.IsNullOrWhiteSpace(fileNamePattern))
        {
            return false;
        }

        try
        {
            baseName = string.Format(CultureInfo.InvariantCulture, fileNamePattern, _timeProvider.GetLocalNow());
        }
        catch (FormatException)
        {
            return false;
        }

        return (baseName.Length > 0) && (baseName.IndexOfAny(Path.GetInvalidFileNameChars()) < 0);
    }
}
