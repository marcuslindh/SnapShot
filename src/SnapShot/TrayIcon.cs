using System;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.WindowsAndMessaging;

namespace SnapShot;

/// <summary>The icon in the notification area, its menu and its balloon notifications.</summary>
/// <param name="owner">Window that receives <see cref="AppMessages.TrayCallback"/>.</param>
internal sealed unsafe class TrayIcon(HWND owner) : IDisposable
{
    /// <summary>Menu command: take a screenshot.</summary>
    public const int CommandCapture = 1;

    /// <summary>Menu command: open the screenshot folder.</summary>
    public const int CommandOpenFolder = 2;

    /// <summary>Menu command: toggle starting with Windows.</summary>
    public const int CommandAutostart = 3;

    /// <summary>Menu command: exit.</summary>
    public const int CommandExit = 4;

    private const uint IconId = 1;
    private const string ToolTip = "SnapShot – tryck PrintScreen";

    // The icon group the SDK embeds from <ApplicationIcon> gets the same id as IDI_APPLICATION.
    private const ushort ApplicationIconResourceId = 32512;

    private readonly HWND _owner = owner;
    private readonly HICON _icon = LoadApplicationIcon();
    private bool _added;

    /// <summary>Adds the icon; also called again when Explorer restarts and has forgotten it.</summary>
    /// <returns>Whether the notification area accepted the icon.</returns>
    public bool Add()
    {
        NOTIFYICONDATAW data = CreateData();
        data.uFlags = NOTIFY_ICON_DATA_FLAGS.NIF_ICON | NOTIFY_ICON_DATA_FLAGS.NIF_MESSAGE | NOTIFY_ICON_DATA_FLAGS.NIF_TIP;
        data.uCallbackMessage = AppMessages.TrayCallback;
        data.hIcon = _icon;
        CopyText(data.szTip.AsSpan(), ToolTip);

        _added = PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_ADD, &data);

        return _added;
    }

    /// <summary>Shows a balloon notification from the icon.</summary>
    /// <param name="title">Bold first line.</param>
    /// <param name="text">Message.</param>
    /// <param name="isError">Whether to show the error symbol.</param>
    public void ShowNotification(string title, string text, bool isError)
    {
        NOTIFYICONDATAW data = CreateData();
        data.uFlags = NOTIFY_ICON_DATA_FLAGS.NIF_INFO;
        data.dwInfoFlags = (isError ? NOTIFY_ICON_INFOTIP_FLAGS.NIIF_ERROR : NOTIFY_ICON_INFOTIP_FLAGS.NIIF_INFO)
            | NOTIFY_ICON_INFOTIP_FLAGS.NIIF_NOSOUND;
        CopyText(data.szInfoTitle.AsSpan(), title);
        CopyText(data.szInfo.AsSpan(), text);

        PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_MODIFY, &data);
    }

    /// <summary>Shows the menu at the mouse position and waits for a choice.</summary>
    /// <param name="autostartEnabled">Whether "Starta med Windows" is ticked.</param>
    /// <returns>The chosen command, or 0 if the menu was dismissed.</returns>
    public int ShowMenu(bool autostartEnabled)
    {
        HMENU menu = PInvoke.CreatePopupMenu();

        try
        {
            AppendItem(menu, CommandCapture, "Ta skärmklipp", MENU_ITEM_FLAGS.MF_STRING);
            AppendItem(menu, CommandOpenFolder, "Öppna mappen", MENU_ITEM_FLAGS.MF_STRING);
            AppendItem(menu, 0, string.Empty, MENU_ITEM_FLAGS.MF_SEPARATOR);
            AppendItem(menu, CommandAutostart, "Starta med Windows", autostartEnabled ? MENU_ITEM_FLAGS.MF_CHECKED : MENU_ITEM_FLAGS.MF_STRING);
            AppendItem(menu, 0, string.Empty, MENU_ITEM_FLAGS.MF_SEPARATOR);
            AppendItem(menu, CommandExit, "Avsluta", MENU_ITEM_FLAGS.MF_STRING);

            PInvoke.GetCursorPos(out System.Drawing.Point cursor);

            // Without this the menu does not close when the user clicks somewhere else.
            PInvoke.SetForegroundWindow(_owner);

            int command = PInvoke.TrackPopupMenu(
                menu,
                TRACK_POPUP_MENU_FLAGS.TPM_RETURNCMD | TRACK_POPUP_MENU_FLAGS.TPM_RIGHTBUTTON | TRACK_POPUP_MENU_FLAGS.TPM_NONOTIFY,
                cursor.X,
                cursor.Y,
                0,
                _owner,
                (RECT*)null);

            PInvoke.PostMessage(_owner, PInvoke.WM_NULL, default, default);

            return command;
        }
        finally
        {
            PInvoke.DestroyMenu(menu);
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_added)
        {
            NOTIFYICONDATAW data = CreateData();
            PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_DELETE, &data);
            _added = false;
        }

        PInvoke.DestroyIcon(_icon);
    }

    private static HICON LoadApplicationIcon()
    {
        HINSTANCE module = (HINSTANCE)PInvoke.GetModuleHandle(default(PCWSTR)).Value;
        int width = PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_CXSMICON);
        int height = PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_CYSMICON);

        HANDLE icon = PInvoke.LoadImage(module, (PCWSTR)(char*)ApplicationIconResourceId, GDI_IMAGE_TYPE.IMAGE_ICON, width, height, IMAGE_FLAGS.LR_DEFAULTCOLOR);

        if (icon.IsNull)
        {
            icon = PInvoke.LoadImage(HINSTANCE.Null, PInvoke.IDI_APPLICATION, GDI_IMAGE_TYPE.IMAGE_ICON, width, height, IMAGE_FLAGS.LR_SHARED);
        }

        return (HICON)icon.Value;
    }

    private static void AppendItem(HMENU menu, int command, string text, MENU_ITEM_FLAGS flags)
    {
        fixed (char* characters = text)
        {
            PInvoke.AppendMenu(menu, flags, (nuint)command, characters);
        }
    }

    private static void CopyText(Span<char> target, string text)
    {
        int length = Math.Min(text.Length, target.Length - 1);
        text.AsSpan(0, length).CopyTo(target);
        target[length] = '\0';
    }

    private NOTIFYICONDATAW CreateData()
    {
        return new NOTIFYICONDATAW
        {
            cbSize = (uint)sizeof(NOTIFYICONDATAW),
            hWnd = _owner,
            uID = IconId,
        };
    }
}
