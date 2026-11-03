namespace CameraService.Core;

using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

// YUV (BT.601) to full range NV12 (JFIF, used by JPEG)
public static unsafe class ImageConverter
{
    private const int VectorBytes = 16;

    //--------------------------------------------------------------------------------
    // Full range NV12
    //--------------------------------------------------------------------------------

    // Chroma of two rows is averaged (4:2:2 to 4:2:0)
    public static void Yuy2ToNv12(ReadOnlySpan<byte> yuy2, int yuy2Stride, Span<byte> y, int yStride, Span<byte> uv, int uvStride, int width, int height, YuvRange range) =>
        Yuy2ToNv12(yuy2, yuy2Stride, y, yStride, uv, uvStride, width, height, range, Ssse3.IsSupported);

    internal static void Yuy2ToNv12(ReadOnlySpan<byte> yuy2, int yuy2Stride, Span<byte> y, int yStride, Span<byte> uv, int uvStride, int width, int height, YuvRange range, bool simd)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        var rowBytes = ((width + 1) / 2) * 4;
        var uvRowBytes = ((width + 1) / 2) * 2;
        if ((yuy2Stride < rowBytes) || (yuy2.Length < RequiredLength(yuy2Stride, rowBytes, height)))
        {
            throw new ArgumentException("Invalid source.", nameof(yuy2));
        }

        if ((yStride < width) || (y.Length < RequiredLength(yStride, width, height)))
        {
            throw new ArgumentException("Invalid Y plane.", nameof(y));
        }

        if ((uvStride < uvRowBytes) || (uv.Length < RequiredLength(uvStride, uvRowBytes, (height + 1) / 2)))
        {
            throw new ArgumentException("Invalid UV plane.", nameof(uv));
        }

        var limited = range == YuvRange.Limited;
        fixed (byte* sourcePointer = yuy2)
        {
            fixed (byte* yPointer = y)
            {
                fixed (byte* uvPointer = uv)
                {
                    for (var row = 0; row < height; row += 2)
                    {
                        // The last row of an odd height is paired with itself
                        var next = Math.Min(row + 1, height - 1);
                        var source0 = sourcePointer + (row * yuy2Stride);
                        var source1 = sourcePointer + (next * yuy2Stride);
                        var y0 = yPointer + (row * yStride);
                        var y1 = yPointer + (next * yStride);
                        var chroma = uvPointer + ((row >> 1) * uvStride);

                        var x = simd ? Yuy2ToNv12RowSsse3(source0, source1, y0, y1, chroma, width, limited) : 0;
                        for (; x < width; x += 2)
                        {
                            var pair0 = source0 + (x * 2);
                            var pair1 = source1 + (x * 2);
                            y0[x] = ExpandLuma(pair0[0], limited);
                            y1[x] = ExpandLuma(pair1[0], limited);
                            if (x + 1 < width)
                            {
                                y0[x + 1] = ExpandLuma(pair0[2], limited);
                                y1[x + 1] = ExpandLuma(pair1[2], limited);
                            }

                            chroma[x] = ExpandChroma((pair0[1] + pair1[1] + 1) >> 1, limited);
                            chroma[x + 1] = ExpandChroma((pair0[3] + pair1[3] + 1) >> 1, limited);
                        }
                    }
                }
            }
        }
    }

    private static int Yuy2ToNv12RowSsse3(byte* source0, byte* source1, byte* y0, byte* y1, byte* uv, int width, bool limited)
    {
        // Y0 U0 Y1 V0 Y2 U1 Y3 V1 ...
        var yShuffle = Vector128.Create(0, 2, 4, 6, 8, 10, 12, 14, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);
        var uvShuffle = Vector128.Create(1, 3, 5, 7, 9, 11, 13, 15, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

        var x = 0;
        for (; x <= width - VectorBytes; x += VectorBytes)
        {
            var low0 = Sse2.LoadVector128(source0 + (x * 2));
            var high0 = Sse2.LoadVector128(source0 + (x * 2) + VectorBytes);
            var low1 = Sse2.LoadVector128(source1 + (x * 2));
            var high1 = Sse2.LoadVector128(source1 + (x * 2) + VectorBytes);

            var luma0 = Combine(Ssse3.Shuffle(low0, yShuffle), Ssse3.Shuffle(high0, yShuffle));
            var luma1 = Combine(Ssse3.Shuffle(low1, yShuffle), Ssse3.Shuffle(high1, yShuffle));
            var chroma = Sse2.Average(
                Combine(Ssse3.Shuffle(low0, uvShuffle), Ssse3.Shuffle(high0, uvShuffle)),
                Combine(Ssse3.Shuffle(low1, uvShuffle), Ssse3.Shuffle(high1, uvShuffle)));
            if (limited)
            {
                luma0 = ExpandLuma(luma0);
                luma1 = ExpandLuma(luma1);
                chroma = ExpandChroma(chroma);
            }

            Sse2.Store(y0 + x, luma0);
            Sse2.Store(y1 + x, luma1);
            Sse2.Store(uv + x, chroma);
        }

        return x;
    }

    // Limited range NV12 to full range NV12
    public static void Nv12ToFullRange(ReadOnlySpan<byte> y, int yStride, ReadOnlySpan<byte> uv, int uvStride, Span<byte> destinationY, int destinationYStride, Span<byte> destinationUv, int destinationUvStride, int width, int height) =>
        Nv12ToFullRange(y, yStride, uv, uvStride, destinationY, destinationYStride, destinationUv, destinationUvStride, width, height, Ssse3.IsSupported);

    internal static void Nv12ToFullRange(ReadOnlySpan<byte> y, int yStride, ReadOnlySpan<byte> uv, int uvStride, Span<byte> destinationY, int destinationYStride, Span<byte> destinationUv, int destinationUvStride, int width, int height, bool simd)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        var uvRowBytes = ((width + 1) / 2) * 2;
        var uvRows = (height + 1) / 2;
        if ((yStride < width) || (y.Length < RequiredLength(yStride, width, height)))
        {
            throw new ArgumentException("Invalid Y plane.", nameof(y));
        }

        if ((uvStride < uvRowBytes) || (uv.Length < RequiredLength(uvStride, uvRowBytes, uvRows)))
        {
            throw new ArgumentException("Invalid UV plane.", nameof(uv));
        }

        if ((destinationYStride < width) || (destinationY.Length < RequiredLength(destinationYStride, width, height)))
        {
            throw new ArgumentException("Invalid destination Y plane.", nameof(destinationY));
        }

        if ((destinationUvStride < uvRowBytes) || (destinationUv.Length < RequiredLength(destinationUvStride, uvRowBytes, uvRows)))
        {
            throw new ArgumentException("Invalid destination UV plane.", nameof(destinationUv));
        }

        fixed (byte* yPointer = y)
        {
            fixed (byte* uvPointer = uv)
            {
                fixed (byte* destinationYPointer = destinationY)
                {
                    fixed (byte* destinationUvPointer = destinationUv)
                    {
                        for (var row = 0; row < height; row++)
                        {
                            ExpandRow(yPointer + (row * yStride), destinationYPointer + (row * destinationYStride), width, true, simd);
                        }

                        for (var row = 0; row < uvRows; row++)
                        {
                            ExpandRow(uvPointer + (row * uvStride), destinationUvPointer + (row * destinationUvStride), uvRowBytes, false, simd);
                        }
                    }
                }
            }
        }
    }

    private static void ExpandRow(byte* source, byte* destination, int length, bool luma, bool simd)
    {
        var x = 0;
        if (simd)
        {
            for (; x <= length - VectorBytes; x += VectorBytes)
            {
                var value = Sse2.LoadVector128(source + x);
                Sse2.Store(destination + x, luma ? ExpandLuma(value) : ExpandChroma(value));
            }
        }

        for (; x < length; x++)
        {
            destination[x] = luma ? ExpandLuma(source[x], true) : ExpandChroma(source[x], true);
        }
    }

    //--------------------------------------------------------------------------------
    // Helper
    //--------------------------------------------------------------------------------

    private static int RequiredLength(int stride, int rowBytes, int rows) => ((rows - 1) * stride) + rowBytes;

    // (y - 16) * 255 / 219
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte ExpandLuma(int value, bool limited) =>
        limited ? (byte)Math.Clamp((((value - 16) * 298) + 128) >> 8, 0, 255) : (byte)value;

    // (c - 128) * 255 / 224 + 128
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte ExpandChroma(int value, bool limited) =>
        limited ? (byte)Math.Clamp(((((value - 128) * 291) + 128) >> 8) + 128, 0, 255) : (byte)value;

    // Same rounding as the scalar version: ((x << 7) * scale + 0x4000) >> 15 = (x * scale + 128) >> 8
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<byte> ExpandLuma(Vector128<byte> value)
    {
        var offset = Vector128.Create((short)16);
        var scale = Vector128.Create((short)298);
        var low = Sse2.ShiftLeftLogical(Sse2.Subtract(Sse2.UnpackLow(value, Vector128<byte>.Zero).AsInt16(), offset), 7);
        var high = Sse2.ShiftLeftLogical(Sse2.Subtract(Sse2.UnpackHigh(value, Vector128<byte>.Zero).AsInt16(), offset), 7);
        return Sse2.PackUnsignedSaturate(Ssse3.MultiplyHighRoundScale(low, scale), Ssse3.MultiplyHighRoundScale(high, scale));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<byte> ExpandChroma(Vector128<byte> value)
    {
        var center = Vector128.Create((short)128);
        var scale = Vector128.Create((short)291);
        var low = Sse2.ShiftLeftLogical(Sse2.Subtract(Sse2.UnpackLow(value, Vector128<byte>.Zero).AsInt16(), center), 7);
        var high = Sse2.ShiftLeftLogical(Sse2.Subtract(Sse2.UnpackHigh(value, Vector128<byte>.Zero).AsInt16(), center), 7);
        return Sse2.PackUnsignedSaturate(
            Sse2.Add(Ssse3.MultiplyHighRoundScale(low, scale), center),
            Sse2.Add(Ssse3.MultiplyHighRoundScale(high, scale), center));
    }

    // Low 8 bytes of each
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<byte> Combine(Vector128<byte> low, Vector128<byte> high) =>
        Sse2.UnpackLow(low.AsUInt64(), high.AsUInt64()).AsByte();
}
