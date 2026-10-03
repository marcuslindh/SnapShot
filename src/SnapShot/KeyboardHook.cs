using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.WindowsAndMessaging;

namespace SnapShot;

/// <summary>
/// Low-level keyboard hook that takes PrintScreen away from Windows, and Escape while a selection
/// is open.
/// </summary>
/// <remarks>
/// RegisterHotKey cannot be used: it does not reliably fire for PrintScreen and cannot stop
/// Windows from also acting on the key. Windows removes a low-level hook whose callback is slow,
/// so the callback only posts a message and returns; the work happens in the message loop.
/// Escape goes through the hook too, because Windows does not always let a background process
/// give its overlay keyboard focus.
/// </remarks>
internal static unsafe class KeyboardHook
{
    private static readonly LRESULT Handled = new(1);

    private static HWND s_target;
    private static HHOOK s_hook;
    private static volatile bool s_selectionOpen;

    /// <summary>Gets or sets a value indicating whether Escape should cancel a selection.</summary>
    public static bool SelectionOpen
    {
        get => s_selectionOpen;
        set => s_selectionOpen = value;
    }

    /// <summary>Installs the hook; messages are posted to <paramref name="target"/>.</summary>
    /// <param name="target">Window that receives <see cref="AppMessages.CaptureRequested"/> and <see cref="AppMessages.CancelRequested"/>.</param>
    public static void Install(HWND target)
    {
        s_target = target;
        s_hook = PInvoke.SetWindowsHookEx(WINDOWS_HOOK_ID.WH_KEYBOARD_LL, &Callback, PInvoke.GetModuleHandle(default(PCWSTR)), 0);

        if (s_hook.IsNull)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Tangentbordshooken gick inte att installera.");
        }
    }

    /// <summary>Removes the hook.</summary>
    public static void Uninstall()
    {
        if (!s_hook.IsNull)
        {
            PInvoke.UnhookWindowsHookEx(s_hook);
            s_hook = HHOOK.Null;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static LRESULT Callback(int code, WPARAM wParam, LPARAM lParam)
    {
        if (code < 0)
        {
            return PInvoke.CallNextHookEx(HHOOK.Null, code, wParam, lParam);
        }

        KBDLLHOOKSTRUCT* info = (KBDLLHOOKSTRUCT*)lParam.Value;
        uint message = (uint)wParam.Value;
        bool keyDown = (message == PInvoke.WM_KEYDOWN) || (message == PInvoke.WM_SYSKEYDOWN);

        if (info->vkCode == (uint)VIRTUAL_KEY.VK_SNAPSHOT)
        {
            if (keyDown)
            {
                PInvoke.PostMessage(s_target, AppMessages.CaptureRequested, default, default);
            }

            // Swallow both down and up, so neither Snipping Tool nor the clipboard copy happens.
            return Handled;
        }

        if (s_selectionOpen && (info->vkCode == (uint)VIRTUAL_KEY.VK_ESCAPE))
        {
            if (keyDown)
            {
                PInvoke.PostMessage(s_target, AppMessages.CancelRequested, default, default);
            }

            return Handled;
        }

        return PInvoke.CallNextHookEx(HHOOK.Null, code, wParam, lParam);
    }
}
