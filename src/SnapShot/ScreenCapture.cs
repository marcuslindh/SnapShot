using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SnapShot.Core;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;

namespace SnapShot;

/// <summary>A frozen image of one monitor, plus a darkened copy shown behind the selection.</summary>
internal sealed unsafe class ScreenCapture : IDisposable
{
    private const int BytesPerPixel = PngEncoder.SourceBytesPerPixel;
    private const ushort BitsPerPixel = 32;

    // Halves every colour channel of a BGRA pixel in one operation: shift right, then clear the
    // bit that crossed over from the neighbouring channel.
    private const uint HalfBrightnessMask = 0x7F7F7F7F;

    private ScreenCapture(RECT bounds, HBITMAP bitmap, byte* bits, HBITMAP dimmedBitmap)
    {
        Bounds = bounds;
        Bitmap = bitmap;
        Bits = bits;
        DimmedBitmap = dimmedBitmap;
    }

    /// <summary>Gets the monitor's position on the virtual screen, in physical pixels.</summary>
    public RECT Bounds { get; }

    /// <summary>Gets the width in pixels.</summary>
    public int Width => Bounds.Width;

    /// <summary>Gets the height in pixels.</summary>
    public int Height => Bounds.Height;

    /// <summary>Gets the captured image.</summary>
    public HBITMAP Bitmap { get; private set; }

    /// <summary>Gets the darkened copy of the captured image.</summary>
    public HBITMAP DimmedBitmap { get; private set; }

    private byte* Bits { get; }

    private int Stride => Width * BytesPerPixel;

    /// <summary>Returns the bounds of every monitor, in physical pixels.</summary>
    /// <returns>One rectangle per monitor.</returns>
    public static IReadOnlyList<RECT> GetMonitorBounds()
    {
        List<RECT> bounds = [];
        GCHandle handle = GCHandle.Alloc(bounds);

        try
        {
            PInvoke.EnumDisplayMonitors(HDC.Null, (RECT*)null, &CollectMonitor, (LPARAM)GCHandle.ToIntPtr(handle));
        }
        finally
        {
            handle.Free();
        }

        return bounds;
    }

    /// <summary>Copies what is on screen inside <paramref name="bounds"/>.</summary>
    /// <param name="bounds">Area of the virtual screen to capture.</param>
    /// <returns>The capture.</returns>
    public static ScreenCapture Capture(RECT bounds)
    {
        HDC screen = PInvoke.GetDC(HWND.Null);
        HDC memory = PInvoke.CreateCompatibleDC(screen);

        try
        {
            HBITMAP bitmap = CreateBitmap(screen, bounds.Width, bounds.Height, out byte* bits);
            HBITMAP dimmed;
            byte* dimmedBits;

            try
            {
                dimmed = CreateBitmap(screen, bounds.Width, bounds.Height, out dimmedBits);
            }
            catch (Win32Exception)
            {
                PInvoke.DeleteObject(bitmap);
                throw;
            }

            HGDIOBJ previous = PInvoke.SelectObject(memory, bitmap);

            // CAPTUREBLT includes layered windows such as tooltips and menus.
            PInvoke.BitBlt(memory, 0, 0, bounds.Width, bounds.Height, screen, bounds.left, bounds.top, ROP_CODE.SRCCOPY | ROP_CODE.CAPTUREBLT);
            PInvoke.SelectObject(memory, previous);

            // GDI may batch the copy; the pixels must be in place before they are read directly.
            PInvoke.GdiFlush();
            Darken(bits, dimmedBits, bounds.Width * bounds.Height);

            return new ScreenCapture(bounds, bitmap, bits, dimmed);
        }
        finally
        {
            PInvoke.DeleteDC(memory);
            _ = PInvoke.ReleaseDC(HWND.Null, screen);
        }
    }

    /// <summary>Copies the pixels inside <paramref name="selection"/> into a tightly packed BGRA buffer.</summary>
    /// <param name="selection">Area to copy, relative to the monitor.</param>
    /// <returns>The pixels, row by row.</returns>
    public byte[] CopyRegion(SelectionRectangle selection)
    {
        int rowLength = selection.Width * BytesPerPixel;
        byte[] pixels = new byte[rowLength * selection.Height];
        ReadOnlySpan<byte> source = new(Bits, Stride * Height);

        for (int row = 0; row < selection.Height; row++)
        {
            int sourceOffset = ((selection.Top + row) * Stride) + (selection.Left * BytesPerPixel);
            source.Slice(sourceOffset, rowLength).CopyTo(pixels.AsSpan(row * rowLength, rowLength));
        }

        return pixels;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (!Bitmap.IsNull)
        {
            PInvoke.DeleteObject(Bitmap);
            Bitmap = HBITMAP.Null;
        }

        if (!DimmedBitmap.IsNull)
        {
            PInvoke.DeleteObject(DimmedBitmap);
            DimmedBitmap = HBITMAP.Null;
        }
    }

    private static HBITMAP CreateBitmap(HDC reference, int width, int height, out byte* bits)
    {
        BITMAPINFO info = default;
        info.bmiHeader.biSize = (uint)sizeof(BITMAPINFOHEADER);
        info.bmiHeader.biWidth = width;

        // Negative height: rows top to bottom, the same order as the PNG.
        info.bmiHeader.biHeight = -height;
        info.bmiHeader.biPlanes = 1;
        info.bmiHeader.biBitCount = BitsPerPixel;
        info.bmiHeader.biCompression = (uint)BI_COMPRESSION.BI_RGB;

        void* pointer;
        HBITMAP bitmap = PInvoke.CreateDIBSection(reference, &info, DIB_USAGE.DIB_RGB_COLORS, &pointer, HANDLE.Null, 0);

        if (bitmap.IsNull)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Skärmbilden fick inte plats i minnet.");
        }

        bits = (byte*)pointer;

        return bitmap;
    }

    private static void Darken(byte* source, byte* target, int pixelCount)
    {
        ReadOnlySpan<uint> from = new(source, pixelCount);
        Span<uint> to = new(target, pixelCount);

        for (int index = 0; index < from.Length; index++)
        {
            to[index] = (from[index] >> 1) & HalfBrightnessMask;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static BOOL CollectMonitor(HMONITOR monitor, HDC deviceContext, RECT* bounds, LPARAM state)
    {
        List<RECT> collected = (List<RECT>)GCHandle.FromIntPtr(state.Value).Target!;
        collected.Add(*bounds);

        return true;
    }
}
