using System;
using System.IO;
using System.Text.Json;
using Avig;

namespace SnapShot.Core;

/// <summary>Reads and writes <see cref="Settings"/> as a JSON file.</summary>
public sealed class SettingsStore(string filePath)
{
    private readonly string _filePath = filePath;

    /// <summary>Gets the default location: <c>%APPDATA%\SnapShot\settings.json</c>.</summary>
    public static string DefaultFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SnapShot",
        "settings.json");

    /// <summary>
    /// Reads the settings. A missing file is not an error: it is created with the defaults so
    /// there is something to edit.
    /// </summary>
    /// <returns>The settings, or why the file could not be read.</returns>
    public Result<Settings> Load()
    {
        if (!File.Exists(_filePath))
        {
            Settings defaults = new();
            Result saved = Save(defaults);

            if (saved.IsFailure(out string? error, out ResultStatus status))
            {
                return Result<Settings>.Failure(error, status);
            }

            return defaults;
        }

        try
        {
            using FileStream stream = File.OpenRead(_filePath);
            Settings? settings = JsonSerializer.Deserialize(stream, SettingsJsonContext.Default.Settings);

            if (settings is null)
            {
                return Result<Settings>.Invalid($"{_filePath} innehåller inga inställningar.");
            }

            return settings;
        }
        catch (JsonException exception)
        {
            return Result<Settings>.Invalid($"{_filePath} är inte giltig JSON: {exception.Message}");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Result<Settings>.Failure($"{_filePath} gick inte att läsa: {exception.Message}");
        }
    }

    /// <summary>Writes the settings, creating the folder if needed.</summary>
    /// <param name="settings">Settings to write.</param>
    /// <returns>Success, or why the file could not be written.</returns>
    public Result Save(Settings settings)
    {
        try
        {
            string? folder = Path.GetDirectoryName(_filePath);

            if (!string.IsNullOrEmpty(folder))
            {
                Directory.CreateDirectory(folder);
            }

            using FileStream stream = File.Create(_filePath);
            JsonSerializer.Serialize(stream, settings, SettingsJsonContext.Default.Settings);

            return Result.Success();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Result.Failure($"{_filePath} gick inte att skriva: {exception.Message}");
        }
    }
}
