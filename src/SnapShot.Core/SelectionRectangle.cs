using System;
using System.Runtime.InteropServices;

namespace SnapShot.Core;

/// <summary>
/// The area the user dragged out, in physical pixels relative to the top left corner of the
/// monitor the drag started on.
/// </summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct SelectionRectangle(int Left, int Top, int Width, int Height)
{
    /// <summary>
    /// Smallest width and height that counts as a selection. Anything smaller is treated as an
    /// accidental click rather than a screenshot nobody can read.
    /// </summary>
    public const int MinimumSize = 4;

    /// <summary>Gets the column just past the right edge.</summary>
    public int Right => Left + Width;

    /// <summary>Gets the row just past the bottom edge.</summary>
    public int Bottom => Top + Height;

    /// <summary>Gets a value indicating whether the selection is big enough to save.</summary>
    public bool IsLargeEnough => (Width >= MinimumSize) && (Height >= MinimumSize);

    /// <summary>
    /// Builds a rectangle from the point where the drag started and the point where it is now,
    /// whichever direction the mouse moved, clipped to the monitor.
    /// </summary>
    /// <param name="startLeft">Horizontal position where the drag started.</param>
    /// <param name="startTop">Vertical position where the drag started.</param>
    /// <param name="currentLeft">Current horizontal position of the mouse.</param>
    /// <param name="currentTop">Current vertical position of the mouse.</param>
    /// <param name="boundsWidth">Width of the monitor.</param>
    /// <param name="boundsHeight">Height of the monitor.</param>
    /// <returns>A rectangle with non-negative size that lies inside the monitor.</returns>
    public static SelectionRectangle FromDrag(
        int startLeft,
        int startTop,
        int currentLeft,
        int currentTop,
        int boundsWidth,
        int boundsHeight)
    {
        // The mouse is captured during the drag, so it can report positions outside the window.
        int left = Math.Clamp(Math.Min(startLeft, currentLeft), 0, boundsWidth);
        int top = Math.Clamp(Math.Min(startTop, currentTop), 0, boundsHeight);
        int right = Math.Clamp(Math.Max(startLeft, currentLeft), 0, boundsWidth);
        int bottom = Math.Clamp(Math.Max(startTop, currentTop), 0, boundsHeight);

        return new SelectionRectangle(left, top, right - left, bottom - top);
    }
}
