using System;
using System.Buffers;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.IO.Hashing;

namespace SnapShot.Core;

/// <summary>
/// Writes 32-bit BGRA pixels, the layout a Windows DIB section uses, as an 8-bit RGB PNG.
/// </summary>
/// <remarks>
/// Hand-written because the framework's image encoders live in System.Drawing and WPF, neither
/// of which survives Native AOT. Every row uses the Sub filter: screenshots are mostly flat
/// areas and text, where the difference to the pixel to the left is usually zero.
/// </remarks>
public static class PngEncoder
{
    /// <summary>Bytes per pixel in the source buffer (B, G, R, unused).</summary>
    public const int SourceBytesPerPixel = 4;

    private const int OutputBytesPerPixel = 3;
    private const byte BitDepth = 8;
    private const byte ColorTypeRgb = 2;
    private const byte FilterTypeSub = 1;
    private const int HeaderLength = 13;

    private static ReadOnlySpan<byte> Signature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private static ReadOnlySpan<byte> HeaderType => "IHDR"u8;

    private static ReadOnlySpan<byte> DataType => "IDAT"u8;

    private static ReadOnlySpan<byte> EndType => "IEND"u8;

    /// <summary>Encodes a region of a BGRA buffer and writes the PNG to <paramref name="output"/>.</summary>
    /// <param name="pixels">Pixels, starting at the top left pixel of the region.</param>
    /// <param name="width">Width of the region in pixels.</param>
    /// <param name="height">Height of the region in pixels.</param>
    /// <param name="stride">Bytes from the start of one row to the start of the next.</param>
    /// <param name="output">Stream the PNG is written to.</param>
    public static void Encode(ReadOnlySpan<byte> pixels, int width, int height, int stride, Stream output)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentOutOfRangeException.ThrowIfLessThan(stride, width * SourceBytesPerPixel);

        int lastRowEnd = ((height - 1) * stride) + (width * SourceBytesPerPixel);

        if (pixels.Length < lastRowEnd)
        {
            throw new ArgumentException("The buffer is smaller than the region it is said to hold.", nameof(pixels));
        }

        output.Write(Signature);
        WriteHeader(width, height, output);
        WriteData(pixels, width, height, stride, output);
        WriteChunk(EndType, [], output);
    }

    private static void WriteHeader(int width, int height, Stream output)
    {
        Span<byte> header = stackalloc byte[HeaderLength];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header[4..], height);
        header[8] = BitDepth;
        header[9] = ColorTypeRgb;

        // Bytes 10-12: deflate compression, adaptive filtering, no interlacing - all zero.
        WriteChunk(HeaderType, header, output);
    }

    private static void WriteData(ReadOnlySpan<byte> pixels, int width, int height, int stride, Stream output)
    {
        int rowLength = width * OutputBytesPerPixel;
        byte[] rgbRow = ArrayPool<byte>.Shared.Rent(rowLength);
        byte[] filteredRow = ArrayPool<byte>.Shared.Rent(rowLength + 1);

        try
        {
            using MemoryStream compressed = new();

            using (ZLibStream zlib = new(compressed, CompressionLevel.Optimal, leaveOpen: true))
            {
                for (int row = 0; row < height; row++)
                {
                    ReadOnlySpan<byte> source = pixels.Slice(row * stride, width * SourceBytesPerPixel);
                    ConvertToRgb(source, rgbRow.AsSpan(0, rowLength));
                    ApplySubFilter(rgbRow.AsSpan(0, rowLength), filteredRow.AsSpan(0, rowLength + 1));
                    zlib.Write(filteredRow, 0, rowLength + 1);
                }
            }

            WriteChunk(DataType, compressed.GetBuffer().AsSpan(0, (int)compressed.Length), output);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rgbRow);
            ArrayPool<byte>.Shared.Return(filteredRow);
        }
    }

    private static void ConvertToRgb(ReadOnlySpan<byte> bgra, Span<byte> rgb)
    {
        int target = 0;

        for (int source = 0; source < bgra.Length; source += SourceBytesPerPixel)
        {
            rgb[target] = bgra[source + 2];
            rgb[target + 1] = bgra[source + 1];
            rgb[target + 2] = bgra[source];
            target += OutputBytesPerPixel;
        }
    }

    private static void ApplySubFilter(ReadOnlySpan<byte> rgb, Span<byte> filtered)
    {
        filtered[0] = FilterTypeSub;
        rgb[..OutputBytesPerPixel].CopyTo(filtered[1..]);

        for (int index = OutputBytesPerPixel; index < rgb.Length; index++)
        {
            filtered[index + 1] = unchecked((byte)(rgb[index] - rgb[index - OutputBytesPerPixel]));
        }
    }

    private static void WriteChunk(ReadOnlySpan<byte> type, ReadOnlySpan<byte> data, Stream output)
    {
        Span<byte> number = stackalloc byte[sizeof(uint)];

        BinaryPrimitives.WriteInt32BigEndian(number, data.Length);
        output.Write(number);
        output.Write(type);
        output.Write(data);

        Crc32 crc = new();
        crc.Append(type);
        crc.Append(data);
        BinaryPrimitives.WriteUInt32BigEndian(number, crc.GetCurrentHashAsUInt32());
        output.Write(number);
    }
}
