namespace SnapShot.Core;

/// <summary>User settings, stored as JSON in the user's application data folder.</summary>
/// <remarks>
/// The properties have setters rather than <c>init</c> on purpose: the JSON source generator
/// treats init-only properties like constructor parameters and sets the ones missing from the
/// file to null, which loses the defaults below.
/// </remarks>
public sealed record Settings
{
    /// <summary>Default file name pattern; <c>{0}</c> is the local time.</summary>
    public const string DefaultFileNamePattern = "Screenshot_{0:yyyy-MM-dd_HH-mm-ss}";

    /// <summary>
    /// Gets or sets the folder screenshots are saved in. When empty, Windows' own screenshot
    /// folder is used, so moving that folder in Windows moves SnapShot's along with it.
    /// </summary>
    public string? OutputFolder { get; set; }

    /// <summary>Gets or sets the composite format string for the file name, without extension.</summary>
    public string FileNamePattern { get; set; } = DefaultFileNamePattern;

    /// <summary>Gets or sets a value indicating whether a sound plays when a screenshot is saved.</summary>
    public bool PlaySound { get; set; }

    /// <summary>Gets or sets a value indicating whether a notification is shown when a screenshot is saved.</summary>
    public bool ShowNotification { get; set; } = true;

    /// <summary>Returns the folder to save in: the configured one, or else Windows' screenshot folder.</summary>
    /// <param name="windowsScreenshotFolder">Windows' screenshot folder.</param>
    /// <returns>The folder to save in.</returns>
    public string ResolveOutputFolder(string windowsScreenshotFolder)
    {
        return string.IsNullOrWhiteSpace(OutputFolder) ? windowsScreenshotFolder : OutputFolder;
    }
}
