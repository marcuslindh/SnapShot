using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.IO.Hashing;
using System.Text;
using SnapShot.Core;
using Xunit;

namespace SnapShot.Core.Tests;

public sealed class PngEncoderTests
{
    private const int Width = 3;
    private const int Height = 2;
    private const int Stride = 16;
    private const int SignatureLength = 8;
    private const int ChunkOverhead = 12;
    private const int TypeLength = 4;
    private const int RgbBytesPerPixel = 3;

    // BGRA, with one padding pixel per row so the stride differs from the width.
    private static readonly byte[] Pixels =
    [
        0, 0, 255, 255, 0, 255, 0, 255, 255, 0, 0, 255, 9, 9, 9, 9,
        10, 20, 30, 255, 40, 50, 60, 255, 70, 80, 90, 255, 9, 9, 9, 9,
    ];

    private static readonly byte[] ExpectedRgb =
    [
        255, 0, 0, 0, 255, 0, 0, 0, 255,
        30, 20, 10, 60, 50, 40, 90, 80, 70,
    ];

    [Fact]
    public void Encode_StartsWithSignature()
    {
        byte[] png = EncodeSample();

        Assert.Equal([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A], png[..SignatureLength]);
    }

    [Fact]
    public void Encode_EveryChunkHasValidCrc_AndEndsWithIend()
    {
        byte[] png = EncodeSample();
        string lastType = string.Empty;
        int offset = SignatureLength;

        while (offset < png.Length)
        {
            int length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(offset));
            byte[] typeAndData = png.AsSpan(offset + TypeLength, TypeLength + length).ToArray();
            uint storedCrc = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(offset + (2 * TypeLength) + length));

            Assert.Equal(Crc32.HashToUInt32(typeAndData), storedCrc);

            lastType = Encoding.ASCII.GetString(typeAndData, 0, TypeLength);
            offset += ChunkOverhead + length;
        }

        Assert.Equal(png.Length, offset);
        Assert.Equal("IEND", lastType);
    }

    [Fact]
    public void Encode_ImageData_DecodesToOriginalPixels()
    {
        byte[] png = EncodeSample();
        byte[] filtered = Decompress(FindChunk(png, "IDAT"));
        byte[] rgb = Unfilter(filtered);

        Assert.Equal(ExpectedRgb, rgb);
    }

    [Fact]
    public void Encode_BufferTooSmall_Throws()
    {
        using MemoryStream output = new();

        Assert.Throws<ArgumentException>(() => PngEncoder.Encode(Pixels.AsSpan(0, 20), Width, Height, Stride, output));
    }

    private static byte[] EncodeSample()
    {
        using MemoryStream output = new();
        PngEncoder.Encode(Pixels, Width, Height, Stride, output);

        return output.ToArray();
    }

    private static byte[] FindChunk(byte[] png, string type)
    {
        int offset = SignatureLength;

        while (offset < png.Length)
        {
            int length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(offset));

            if (string.Equals(Encoding.ASCII.GetString(png, offset + TypeLength, TypeLength), type, StringComparison.Ordinal))
            {
                return png.AsSpan(offset + (2 * TypeLength), length).ToArray();
            }

            offset += ChunkOverhead + length;
        }

        throw new InvalidOperationException($"No {type} chunk.");
    }

    private static byte[] Decompress(byte[] compressed)
    {
        using MemoryStream input = new(compressed);
        using ZLibStream zlib = new(input, CompressionMode.Decompress);
        using MemoryStream output = new();
        zlib.CopyTo(output);

        return output.ToArray();
    }

    private static byte[] Unfilter(byte[] filtered)
    {
        int rowLength = Width * RgbBytesPerPixel;
        byte[] rgb = new byte[rowLength * Height];

        for (int row = 0; row < Height; row++)
        {
            int source = row * (rowLength + 1);
            int target = row * rowLength;

            Assert.Equal(1, filtered[source]);

            for (int index = 0; index < rowLength; index++)
            {
                byte left = (index >= RgbBytesPerPixel) ? rgb[target + index - RgbBytesPerPixel] : (byte)0;
                rgb[target + index] = unchecked((byte)(filtered[source + 1 + index] + left));
            }
        }

        return rgb;
    }
}
