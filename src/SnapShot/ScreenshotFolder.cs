using System;
using System.IO;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;

namespace SnapShot;

/// <summary>Windows' own screenshot folder, the one Win+PrintScreen saves to.</summary>
internal static unsafe class ScreenshotFolder
{
    private const string FallbackFolderName = "Screenshots";

    /// <summary>
    /// Returns the folder as Windows has it now, so a folder the user has moved (for example
    /// into OneDrive) is followed.
    /// </summary>
    /// <returns>The folder; <c>Pictures\Screenshots</c> if Windows has none registered.</returns>
    public static string GetWindowsFolder()
    {
        HRESULT result = PInvoke.SHGetKnownFolderPath(PInvoke.FOLDERID_Screenshots, KNOWN_FOLDER_FLAG.KF_FLAG_DEFAULT, hToken: null, out PWSTR path);

        try
        {
            if (result.Succeeded)
            {
                return path.ToString();
            }
        }
        finally
        {
            // Windows allocates the string even when the call fails.
            PInvoke.CoTaskMemFree(path);
        }

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), FallbackFolderName);
    }
}
