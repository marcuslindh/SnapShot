using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SnapShot.Core;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.WindowsAndMessaging;

namespace SnapShot;

/// <summary>
/// Full-screen window over one monitor that shows the frozen, darkened image and lets the user
/// drag out the area to save. The selected area is shown at full brightness.
/// </summary>
internal sealed unsafe class OverlayWindow : IDisposable
{
    private const string ClassName = "SnapShot.Overlay";
    private const string HintText = "Dra för att markera  ·  Esc avbryter";
    private const int FontPoints = 10;
    private const int PointsPerInch = 72;
    private const uint DefaultDpi = 96;
    private const int FontWeightSemibold = 600;
    private const int LabelPadding = 6;
    private const int LabelGap = 6;
    private const int HintTop = 24;

    private static readonly Dictionary<nint, OverlayWindow> s_windows = [];
    private static bool s_classRegistered;

    private static readonly COLORREF BorderColor = Rgb(37, 99, 235);
    private static readonly COLORREF LabelBackground = Rgb(32, 32, 32);
    private static readonly COLORREF LabelForeground = Rgb(255, 255, 255);

    private readonly ScreenCapture _capture;
    private readonly Action<byte[], SelectionRectangle> _selected;
    private readonly Action _cancelled;

    private HWND _window;
    private HDC _backBuffer;
    private HBITMAP _backBitmap;
    private HDC _imageSource;
    private HDC _dimmedSource;
    private HFONT _font;
    private HBRUSH _borderBrush;
    private HBRUSH _labelBrush;
    private bool _dragging;
    private int _startLeft;
    private int _startTop;
    private SelectionRectangle _selection;

    /// <summary>Creates and shows the overlay.</summary>
    /// <param name="capture">Frozen image of the monitor; the overlay takes ownership.</param>
    /// <param name="selected">Called with the pixels and the area when the user lets go of the mouse.</param>
    /// <param name="cancelled">Called when the user right-clicks or makes a too small selection.</param>
    public OverlayWindow(ScreenCapture capture, Action<byte[], SelectionRectangle> selected, Action cancelled)
    {
        _capture = capture;
        _selected = selected;
        _cancelled = cancelled;

        RegisterClass();

        RECT bounds = capture.Bounds;

        fixed (char* className = ClassName)
        {
            _window = PInvoke.CreateWindowEx(
                WINDOW_EX_STYLE.WS_EX_TOPMOST | WINDOW_EX_STYLE.WS_EX_TOOLWINDOW,
                className,
                default,
                WINDOW_STYLE.WS_POPUP,
                bounds.left,
                bounds.top,
                bounds.Width,
                bounds.Height,
                HWND.Null,
                HMENU.Null,
                (HINSTANCE)PInvoke.GetModuleHandle(default(PCWSTR)).Value,
                lpParam: null);
        }

        if (_window.IsNull)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Markeringsfönstret gick inte att skapa.");
        }

        CreateDrawingResources();
        s_windows[_window] = this;

        PInvoke.ShowWindow(_window, SHOW_WINDOW_CMD.SW_SHOW);
        PInvoke.SetForegroundWindow(_window);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_window.IsNull)
        {
            return;
        }

        s_windows.Remove(_window);
        PInvoke.DestroyWindow(_window);
        _window = HWND.Null;

        PInvoke.DeleteDC(_backBuffer);
        PInvoke.DeleteObject(_backBitmap);
        PInvoke.DeleteDC(_imageSource);
        PInvoke.DeleteDC(_dimmedSource);
        PInvoke.DeleteObject(_font);
        PInvoke.DeleteObject(_borderBrush);
        PInvoke.DeleteObject(_labelBrush);
        _capture.Dispose();
    }

    private static COLORREF Rgb(byte red, byte green, byte blue)
    {
        return new COLORREF((uint)(red | (green << 8) | (blue << 16)));
    }

    private static void RegisterClass()
    {
        if (s_classRegistered)
        {
            return;
        }

        fixed (char* className = ClassName)
        {
            WNDCLASSEXW windowClass = new()
            {
                cbSize = (uint)sizeof(WNDCLASSEXW),
                lpfnWndProc = &WindowProcedure,
                hInstance = (HINSTANCE)PInvoke.GetModuleHandle(default(PCWSTR)).Value,
                hCursor = PInvoke.LoadCursor(HINSTANCE.Null, PInvoke.IDC_CROSS),
                lpszClassName = className,
            };

            if (PInvoke.RegisterClassEx(&windowClass) == 0)
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "Fönsterklassen gick inte att registrera.");
            }
        }

        s_classRegistered = true;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static LRESULT WindowProcedure(HWND window, uint message, WPARAM wParam, LPARAM lParam)
    {
        if (s_windows.TryGetValue(window, out OverlayWindow? overlay) && overlay.HandleMessage(message, lParam))
        {
            return default;
        }

        return PInvoke.DefWindowProc(window, message, wParam, lParam);
    }

    private static (int Left, int Top) GetPoint(LPARAM lParam)
    {
        // Signed, because a captured mouse can be left of or above the window.
        return ((short)(lParam.Value & 0xFFFF), (short)((lParam.Value >> 16) & 0xFFFF));
    }

    private void CreateDrawingResources()
    {
        HDC windowContext = PInvoke.GetDC(_window);

        try
        {
            _backBuffer = PInvoke.CreateCompatibleDC(windowContext);
            _backBitmap = PInvoke.CreateCompatibleBitmap(windowContext, _capture.Width, _capture.Height);
            PInvoke.SelectObject(_backBuffer, _backBitmap);

            _imageSource = PInvoke.CreateCompatibleDC(windowContext);
            PInvoke.SelectObject(_imageSource, _capture.Bitmap);

            _dimmedSource = PInvoke.CreateCompatibleDC(windowContext);
            PInvoke.SelectObject(_dimmedSource, _capture.DimmedBitmap);
        }
        finally
        {
            _ = PInvoke.ReleaseDC(_window, windowContext);
        }

        int fontHeight = -(int)(FontPoints * GetDpi() / PointsPerInch);

        fixed (char* faceName = "Segoe UI")
        {
            _font = PInvoke.CreateFont(
                fontHeight,
                0,
                0,
                0,
                FontWeightSemibold,
                0,
                0,
                0,
                FONT_CHARSET.DEFAULT_CHARSET,
                FONT_OUTPUT_PRECISION.OUT_DEFAULT_PRECIS,
                FONT_CLIP_PRECISION.CLIP_DEFAULT_PRECIS,
                FONT_QUALITY.CLEARTYPE_QUALITY,
                0,
                faceName);
        }

        PInvoke.SelectObject(_backBuffer, _font);
        PInvoke.SetBkMode(_backBuffer, BACKGROUND_MODE.TRANSPARENT);
        PInvoke.SetTextColor(_backBuffer, LabelForeground);

        _borderBrush = PInvoke.CreateSolidBrush(BorderColor);
        _labelBrush = PInvoke.CreateSolidBrush(LabelBackground);
    }

    private uint GetDpi()
    {
        // Per-monitor DPI needs Windows 10 1607; older systems get the classic 96.
        return OperatingSystem.IsWindowsVersionAtLeast(10, 0, 14393) ? PInvoke.GetDpiForWindow(_window) : DefaultDpi;
    }

    private bool HandleMessage(uint message, LPARAM lParam)
    {
        switch (message)
        {
            case PInvoke.WM_ERASEBKGND:
                // Everything is painted in WM_PAINT; erasing first would only flicker.
                return true;

            case PInvoke.WM_PAINT:
                Paint();

                return true;

            case PInvoke.WM_LBUTTONDOWN:
                StartDrag(lParam);

                return true;

            case PInvoke.WM_MOUSEMOVE:
                ContinueDrag(lParam);

                return true;

            case PInvoke.WM_LBUTTONUP:
                FinishDrag(lParam);

                return true;

            case PInvoke.WM_RBUTTONUP:
                _dragging = false;
                PInvoke.ReleaseCapture();
                _cancelled();

                return true;

            default:
                return false;
        }
    }

    private void StartDrag(LPARAM lParam)
    {
        (_startLeft, _startTop) = GetPoint(lParam);
        _selection = new SelectionRectangle(_startLeft, _startTop, 0, 0);
        _dragging = true;
        PInvoke.SetCapture(_window);
        Invalidate();
    }

    private void ContinueDrag(LPARAM lParam)
    {
        if (!_dragging)
        {
            return;
        }

        UpdateSelection(lParam);
        Invalidate();
    }

    private void FinishDrag(LPARAM lParam)
    {
        if (!_dragging)
        {
            return;
        }

        _dragging = false;
        PInvoke.ReleaseCapture();
        UpdateSelection(lParam);

        if (!_selection.IsLargeEnough)
        {
            _cancelled();

            return;
        }

        _selected(_capture.CopyRegion(_selection), _selection);
    }

    private void UpdateSelection(LPARAM lParam)
    {
        (int left, int top) = GetPoint(lParam);
        _selection = SelectionRectangle.FromDrag(_startLeft, _startTop, left, top, _capture.Width, _capture.Height);
    }

    private void Invalidate()
    {
        PInvoke.InvalidateRect(_window, (RECT*)null, bErase: false);
    }

    private void Paint()
    {
        PAINTSTRUCT paint;
        HDC target = PInvoke.BeginPaint(_window, &paint);
        int width = _capture.Width;
        int height = _capture.Height;

        PInvoke.BitBlt(_backBuffer, 0, 0, width, height, _dimmedSource, 0, 0, ROP_CODE.SRCCOPY);

        if (_selection.Width > 0 && _selection.Height > 0)
        {
            PaintSelection();
        }
        else if (!_dragging)
        {
            PaintHint();
        }

        PInvoke.BitBlt(target, 0, 0, width, height, _backBuffer, 0, 0, ROP_CODE.SRCCOPY);
        PInvoke.EndPaint(_window, &paint);
    }

    private void PaintSelection()
    {
        SelectionRectangle selection = _selection;

        PInvoke.BitBlt(_backBuffer, selection.Left, selection.Top, selection.Width, selection.Height, _imageSource, selection.Left, selection.Top, ROP_CODE.SRCCOPY);

        // Two pixels wide, drawn outside the selection so it never hides a selected pixel.
        RECT inner = new(selection.Left - 1, selection.Top - 1, selection.Right + 1, selection.Bottom + 1);
        RECT outer = new(selection.Left - 2, selection.Top - 2, selection.Right + 2, selection.Bottom + 2);
        _ = PInvoke.FrameRect(_backBuffer, &inner, _borderBrush);
        _ = PInvoke.FrameRect(_backBuffer, &outer, _borderBrush);

        string size = string.Create(CultureInfo.InvariantCulture, $"{selection.Width} × {selection.Height}");
        RECT label = MeasureLabel(size);
        int labelHeight = label.Height;
        int labelTop = selection.Top - labelHeight - LabelGap;

        if (labelTop < 0)
        {
            labelTop = selection.Bottom + LabelGap;
        }

        int labelLeft = Math.Clamp(selection.Left, 0, Math.Max(0, _capture.Width - label.Width));
        DrawLabel(size, new RECT(labelLeft, labelTop, labelLeft + label.Width, labelTop + labelHeight));
    }

    private void PaintHint()
    {
        RECT label = MeasureLabel(HintText);
        int left = (_capture.Width - label.Width) / 2;
        DrawLabel(HintText, new RECT(left, HintTop, left + label.Width, HintTop + label.Height));
    }

    private RECT MeasureLabel(string text)
    {
        RECT bounds = default;

        fixed (char* characters = text)
        {
            _ = PInvoke.DrawText(_backBuffer, characters, text.Length, &bounds, DRAW_TEXT_FORMAT.DT_SINGLELINE | DRAW_TEXT_FORMAT.DT_CALCRECT | DRAW_TEXT_FORMAT.DT_NOPREFIX);
        }

        return new RECT(0, 0, bounds.Width + (2 * LabelPadding), bounds.Height + (2 * LabelPadding));
    }

    private void DrawLabel(string text, RECT box)
    {
        _ = PInvoke.FillRect(_backBuffer, &box, _labelBrush);

        fixed (char* characters = text)
        {
            _ = PInvoke.DrawText(_backBuffer, characters, text.Length, &box, DRAW_TEXT_FORMAT.DT_SINGLELINE | DRAW_TEXT_FORMAT.DT_CENTER | DRAW_TEXT_FORMAT.DT_VCENTER | DRAW_TEXT_FORMAT.DT_NOPREFIX);
        }
    }
}
