using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using Avig;
using Microsoft.Win32;
using SnapShot.Core;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace SnapShot;

/// <summary>
/// The hidden host window and everything hanging off it: the tray icon, the keyboard hook, the
/// selection overlays and the hand-off to the thread that saves the file.
/// </summary>
internal sealed unsafe class SnapShotApp : IDisposable
{
    private const string ClassName = "SnapShot.Host";
    private const nuint MenuCaptureTimerId = 1;

    // Taking the screenshot straight from the menu would catch the menu on its way out. A short
    // pause lets the area under it repaint first.
    private const uint MenuCaptureDelayMilliseconds = 250;

    // When the app starts with Windows the notification area may not be ready yet, and adding the
    // icon fails without the TaskbarCreated broadcast that would otherwise bring it back. The app
    // would then run with no icon at all, so adding is retried for a while.
    private const nuint TrayRetryTimerId = 2;
    private const uint TrayRetryIntervalMilliseconds = 1000;
    private const int MaximumTrayAttempts = 120;

    private const string SnippingKeyPath = @"Control Panel\Keyboard";
    private const string SnippingValueName = "PrintScreenKeyForSnippingEnabled";

    private static SnapShotApp? s_current;

    private readonly SettingsStore _settingsStore = new(SettingsStore.DefaultFilePath);
    private readonly ScreenshotWriter _writer = new(new FileNameGenerator(TimeProvider.System));
    private readonly ConcurrentQueue<SaveOutcome> _outcomes = new();
    private readonly List<OverlayWindow> _overlays = [];
    private readonly HWND _window;
    private readonly uint _taskbarCreatedMessage;
    private readonly TrayIcon _trayIcon;
    private Settings _settings;
    private int _trayAttempts;

    private SnapShotApp()
    {
        _window = CreateHostWindow();
        _taskbarCreatedMessage = PInvoke.RegisterWindowMessage("TaskbarCreated");

        _trayIcon = new TrayIcon(_window);
        AddTrayIcon();

        KeyboardHook.Install(_window);

        _settings = LoadSettings();
        WarnIfSnippingToolOwnsPrintScreen();
    }

    /// <summary>
    /// Creates the host window, tray icon and keyboard hook. There is one per process: the window
    /// procedure is a static function pointer and finds the instance through a static field.
    /// </summary>
    /// <returns>The running application.</returns>
    public static SnapShotApp Start()
    {
        s_current = new SnapShotApp();

        return s_current;
    }

    /// <summary>Runs the message loop until the user exits.</summary>
    /// <returns>The process exit code.</returns>
    public static int Run()
    {
        MSG message;

        while (true)
        {
            int result = PInvoke.GetMessage(&message, HWND.Null, 0, 0).Value;

            if (result == 0)
            {
                return (int)message.wParam.Value;
            }

            if (result == -1)
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "Meddelandeloopen avbröts.");
            }

            PInvoke.TranslateMessage(&message);
            PInvoke.DispatchMessage(&message);
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        KeyboardHook.Uninstall();
        CloseOverlays();
        _trayIcon.Dispose();
        PInvoke.DestroyWindow(_window);
    }

    private static HWND CreateHostWindow()
    {
        HINSTANCE module = (HINSTANCE)PInvoke.GetModuleHandle(default(PCWSTR)).Value;

        fixed (char* className = ClassName)
        {
            WNDCLASSEXW windowClass = new()
            {
                cbSize = (uint)sizeof(WNDCLASSEXW),
                lpfnWndProc = &WindowProcedure,
                hInstance = module,
                lpszClassName = className,
            };

            if (PInvoke.RegisterClassEx(&windowClass) == 0)
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "Fönsterklassen gick inte att registrera.");
            }

            // A hidden top-level window rather than a message-only one: only top-level windows
            // receive the TaskbarCreated broadcast that says the tray icon must be added again.
            HWND window = PInvoke.CreateWindowEx(
                WINDOW_EX_STYLE.WS_EX_TOOLWINDOW,
                className,
                className,
                WINDOW_STYLE.WS_OVERLAPPED,
                0,
                0,
                0,
                0,
                HWND.Null,
                HMENU.Null,
                module,
                lpParam: null);

            if (window.IsNull)
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "Värdfönstret gick inte att skapa.");
            }

            return window;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static LRESULT WindowProcedure(HWND window, uint message, WPARAM wParam, LPARAM lParam)
    {
        if ((s_current is not null) && s_current.HandleMessage(message, wParam, lParam))
        {
            return default;
        }

        return PInvoke.DefWindowProc(window, message, wParam, lParam);
    }

    private bool HandleMessage(uint message, WPARAM wParam, LPARAM lParam)
    {
        if (message == _taskbarCreatedMessage)
        {
            _trayAttempts = 0;
            AddTrayIcon();

            return true;
        }

        switch (message)
        {
            case AppMessages.CaptureRequested:
                StartSelection();

                return true;

            case AppMessages.CancelRequested:
                CloseOverlays();

                return true;

            case AppMessages.TrayCallback:
                HandleTrayMouse((uint)(lParam.Value & 0xFFFF));

                return true;

            case AppMessages.SaveCompleted:
                ReportOutcomes();

                return true;

            case PInvoke.WM_TIMER when wParam.Value == MenuCaptureTimerId:
                PInvoke.KillTimer(_window, MenuCaptureTimerId);
                StartSelection();

                return true;

            case PInvoke.WM_TIMER when wParam.Value == TrayRetryTimerId:
                AddTrayIcon();

                return true;

            case PInvoke.WM_DESTROY:
                PInvoke.PostQuitMessage(0);

                return true;

            default:
                return false;
        }
    }

    private void AddTrayIcon()
    {
        _trayAttempts++;

        if (_trayIcon.Add() || (_trayAttempts >= MaximumTrayAttempts))
        {
            PInvoke.KillTimer(_window, TrayRetryTimerId);

            return;
        }

        PInvoke.SetTimer(_window, TrayRetryTimerId, TrayRetryIntervalMilliseconds, lpTimerFunc: null);
    }

    private void HandleTrayMouse(uint mouseMessage)
    {
        if ((mouseMessage != PInvoke.WM_LBUTTONUP) && (mouseMessage != PInvoke.WM_RBUTTONUP))
        {
            return;
        }

        switch (_trayIcon.ShowMenu(Autostart.IsEnabled))
        {
            case TrayIcon.CommandCapture:
                PInvoke.SetTimer(_window, MenuCaptureTimerId, MenuCaptureDelayMilliseconds, lpTimerFunc: null);
                break;

            case TrayIcon.CommandOpenFolder:
                OpenOutputFolder();
                break;

            case TrayIcon.CommandAutostart:
                Autostart.Set(!Autostart.IsEnabled);
                break;

            case TrayIcon.CommandExit:
                PInvoke.DestroyWindow(_window);
                break;

            default:
                break;
        }
    }

    private void StartSelection()
    {
        if (_overlays.Count > 0)
        {
            return;
        }

        _settings = LoadSettings();

        // Every monitor is captured before any overlay appears, so menus and tooltips that were
        // open when the key was pressed end up in the picture.
        List<ScreenCapture> captures = [];

        try
        {
            foreach (RECT bounds in ScreenCapture.GetMonitorBounds())
            {
                captures.Add(ScreenCapture.Capture(bounds));
            }

            while (captures.Count > 0)
            {
                ScreenCapture capture = captures[^1];
                captures.RemoveAt(captures.Count - 1);
                _overlays.Add(new OverlayWindow(capture, OnSelected, CloseOverlays));
            }
        }
        catch (Win32Exception exception)
        {
            captures.ForEach(static capture => capture.Dispose());
            CloseOverlays();
            _trayIcon.ShowNotification("Skärmklippet gick inte att ta", exception.Message, isError: true);

            return;
        }

        KeyboardHook.SelectionOpen = true;
    }

    private void CloseOverlays()
    {
        KeyboardHook.SelectionOpen = false;
        _overlays.ForEach(static overlay => overlay.Dispose());
        _overlays.Clear();
    }

    private void OnSelected(byte[] pixels, SelectionRectangle selection)
    {
        CloseOverlays();

        // Encoding a large area takes long enough to stall the message loop, and with it the
        // keyboard hook, which Windows then removes. The file is written on a pool thread.
        SaveJob job = new(pixels, selection.Width, selection.Height, GetOutputFolder(), _settings);
        ThreadPool.QueueUserWorkItem(Save, job, preferLocal: false);
    }

    private void Save(SaveJob job)
    {
        Result<string> saved = _writer.Save(job.Pixels, job.Width, job.Height, job.Folder, job.Settings.FileNamePattern);
        SaveOutcome outcome;

        if (saved.IsFailure(out string? error, out ResultStatus _, out string? path))
        {
            outcome = new SaveOutcome("Skärmklippet sparades inte", error, IsError: true, job.Settings);
        }
        else if (ClipboardWriter.SetText(_window, path).IsFailure(out string? clipboardError, out ResultStatus _))
        {
            outcome = new SaveOutcome("Sparad, men sökvägen kopierades inte", $"{clipboardError}\n{path}", IsError: true, job.Settings);
        }
        else
        {
            outcome = new SaveOutcome("Sparad och sökväg kopierad", Path.GetFileName(path), IsError: false, job.Settings);
        }

        _outcomes.Enqueue(outcome);
        PInvoke.PostMessage(_window, AppMessages.SaveCompleted, default, default);
    }

    private void ReportOutcomes()
    {
        while (_outcomes.TryDequeue(out SaveOutcome? outcome))
        {
            if (outcome.Settings.PlaySound && !outcome.IsError)
            {
                PInvoke.MessageBeep(MESSAGEBOX_STYLE.MB_OK);
            }

            // Errors are always shown; turning notifications off must not hide a lost screenshot.
            if (outcome.Settings.ShowNotification || outcome.IsError)
            {
                _trayIcon.ShowNotification(outcome.Title, outcome.Text, outcome.IsError);
            }
        }
    }

    private Settings LoadSettings()
    {
        Result<Settings> loaded = _settingsStore.Load();

        if (loaded.IsFailure(out string? error, out ResultStatus _, out Settings? settings))
        {
            _trayIcon.ShowNotification("Inställningarna kunde inte läsas – standardvärden används", error, isError: true);

            return new Settings();
        }

        return settings;
    }

    private string GetOutputFolder()
    {
        return _settings.ResolveOutputFolder(ScreenshotFolder.GetWindowsFolder());
    }

    private void OpenOutputFolder()
    {
        string folder = GetOutputFolder();

        try
        {
            Directory.CreateDirectory(folder);
            using Process? explorer = Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or Win32Exception)
        {
            _trayIcon.ShowNotification("Mappen gick inte att öppna", exception.Message, isError: true);
        }
    }

    private void WarnIfSnippingToolOwnsPrintScreen()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(SnippingKeyPath);

        if (key?.GetValue(SnippingValueName) is int value && value != 0)
        {
            _trayIcon.ShowNotification(
                "PrintScreen är också kopplat till Skärmklippsverktyget",
                "Öppnas båda: stäng av det under Inställningar › Hjälpmedel › Tangentbord.",
                isError: false);
        }
    }

    private sealed record SaveJob(byte[] Pixels, int Width, int Height, string Folder, Settings Settings);

    private sealed record SaveOutcome(string Title, string Text, bool IsError, Settings Settings);
}
