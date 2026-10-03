using Windows.Win32;

namespace SnapShot;

/// <summary>Private window messages sent to the host window.</summary>
internal static class AppMessages
{
    /// <summary>PrintScreen was pressed; posted by the keyboard hook.</summary>
    public const uint CaptureRequested = PInvoke.WM_APP + 1;

    /// <summary>Escape was pressed while a selection is open; posted by the keyboard hook.</summary>
    public const uint CancelRequested = PInvoke.WM_APP + 2;

    /// <summary>Mouse activity on the tray icon.</summary>
    public const uint TrayCallback = PInvoke.WM_APP + 3;

    /// <summary>A screenshot has been saved (or failed to) on the worker thread.</summary>
    public const uint SaveCompleted = PInvoke.WM_APP + 4;
}
