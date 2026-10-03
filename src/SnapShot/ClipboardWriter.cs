using System;
using System.Threading;
using Avig;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Memory;
using Windows.Win32.System.Ole;

namespace SnapShot;

/// <summary>Puts text on the clipboard.</summary>
internal static unsafe class ClipboardWriter
{
    // Another program may hold the clipboard open for a moment; clipboard managers do it on every
    // change. A few short retries get past that without a noticeable delay.
    private const int Attempts = 5;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(50);

    /// <summary>Replaces the clipboard contents with <paramref name="text"/>.</summary>
    /// <param name="owner">Window that owns the clipboard data.</param>
    /// <param name="text">Text to copy.</param>
    /// <returns>Success, or why the clipboard could not be written.</returns>
    public static Result SetText(HWND owner, string text)
    {
        for (int attempt = 1; attempt <= Attempts; attempt++)
        {
            if (PInvoke.OpenClipboard(owner))
            {
                return WriteToOpenClipboard(text);
            }

            Thread.Sleep(RetryDelay);
        }

        return Result.Unavailable("Klippbordet används av ett annat program.");
    }

    private static Result WriteToOpenClipboard(string text)
    {
        try
        {
            PInvoke.EmptyClipboard();

            nuint size = (nuint)((text.Length + 1) * sizeof(char));
            HGLOBAL memory = PInvoke.GlobalAlloc(GLOBAL_ALLOC_FLAGS.GMEM_MOVEABLE, size);

            if (memory.IsNull)
            {
                return Result.Failure("Det fanns inte minne för att kopiera sökvägen.");
            }

            Span<char> target = new(PInvoke.GlobalLock(memory), text.Length + 1);
            text.AsSpan().CopyTo(target);
            target[text.Length] = '\0';
            PInvoke.GlobalUnlock(memory);

            if (PInvoke.SetClipboardData((uint)CLIPBOARD_FORMAT.CF_UNICODETEXT, (HANDLE)memory.Value).IsNull)
            {
                // Ownership only passes to the system when SetClipboardData succeeds.
                PInvoke.GlobalFree(memory);

                return Result.Failure("Sökvägen gick inte att lägga på klippbordet.");
            }

            return Result.Success();
        }
        finally
        {
            PInvoke.CloseClipboard();
        }
    }
}
