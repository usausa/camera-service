namespace CameraService.Platform.Linux;

using System.Runtime.InteropServices;

using CameraService.Core;

// TurboJPEG encoder: full range NV12 is passed as YCbCr planes without color conversion (the interleaved chroma is split into Cb and Cr),
// and the data is written to the given buffer
public sealed unsafe class JpegEncoder : IJpegEncoder
{
    private const int PlaneCount = 3;

    private readonly int quality;

    private IntPtr handle;

    // The luma of an odd size extended to even
    private byte[] luma = [];

    // The Cb plane and then the Cr plane
    private byte[] chroma = [];

    public JpegEncoder(int quality)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(quality, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(quality, 100);
        this.quality = quality;
        handle = TurboJpeg.CreateCompressor();
    }

    public void Dispose()
    {
        if (handle != IntPtr.Zero)
        {
            _ = TurboJpeg.Destroy(handle);
            handle = IntPtr.Zero;
        }
    }

    // Upper bound of the encoded size, TurboJPEG may write up to it into the buffer
    public int GetMaxLength(int width, int height) => checked((int)TurboJpeg.BufferSize(width, height, TurboJpeg.Subsampling420).Value);

    // Encodes full range NV12 as 4:2:0 JPEG, returns false when the destination is too small
    public bool TryEncodeNv12(ReadOnlySpan<byte> y, int yStride, ReadOnlySpan<byte> uv, int uvStride, int width, int height, Span<byte> destination, out int written)
    {
        ObjectDisposedException.ThrowIf(handle == IntPtr.Zero, this);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        var chromaWidth = (width + 1) / 2;
        var chromaHeight = (height + 1) / 2;
        if ((yStride < width) || (y.Length < ((height - 1) * yStride) + width))
        {
            throw new ArgumentException("Invalid Y plane.", nameof(y));
        }

        if ((uvStride < chromaWidth * 2) || (uv.Length < ((chromaHeight - 1) * uvStride) + (chromaWidth * 2)))
        {
            throw new ArgumentException("Invalid UV plane.", nameof(uv));
        }

        if (destination.Length < GetMaxLength(width, height))
        {
            written = 0;
            return false;
        }

        // TurboJPEG reads the luma of 4:2:0 to an even width and height
        var lumaPlane = y;
        var lumaStride = yStride;
        var paddedWidth = chromaWidth * 2;
        var paddedHeight = chromaHeight * 2;
        if ((paddedWidth != width) || (paddedHeight != height))
        {
            if (luma.Length < paddedWidth * paddedHeight)
            {
                luma = GC.AllocateUninitializedArray<byte>(paddedWidth * paddedHeight);
            }

            PadLuma(y, yStride, luma, width, height, paddedWidth, paddedHeight);
            lumaPlane = luma;
            lumaStride = paddedWidth;
        }

        var planeSize = chromaWidth * chromaHeight;
        if (chroma.Length < planeSize * 2)
        {
            chroma = GC.AllocateUninitializedArray<byte>(planeSize * 2);
        }

        SplitChroma(uv, uvStride, chroma.AsSpan(0, planeSize), chroma.AsSpan(planeSize, planeSize), chromaWidth, chromaHeight);

        int result;
        var size = new CULong((nuint)destination.Length);
        fixed (byte* yPointer = lumaPlane)
        {
            fixed (byte* chromaPointer = chroma)
            {
                fixed (byte* destinationPointer = destination)
                {
                    var planes = stackalloc byte*[PlaneCount];
                    planes[0] = yPointer;
                    planes[1] = chromaPointer;
                    planes[2] = chromaPointer + planeSize;
                    var strides = stackalloc int[PlaneCount];
                    strides[0] = lumaStride;
                    strides[1] = chromaWidth;
                    strides[2] = chromaWidth;
                    var output = destinationPointer;
                    result = TurboJpeg.CompressFromYuvPlanes(handle, planes, width, strides, height, TurboJpeg.Subsampling420, &output, &size, quality, TurboJpeg.NoReallocation);
                }
            }
        }

        if (result != 0)
        {
            throw new InvalidOperationException(TurboJpeg.GetErrorMessage(handle));
        }

        written = (int)size.Value;
        return true;
    }

    // The luma extended by its last column and row to the padded size
    internal static void PadLuma(ReadOnlySpan<byte> y, int yStride, Span<byte> destination, int width, int height, int paddedWidth, int paddedHeight)
    {
        for (var row = 0; row < paddedHeight; row++)
        {
            var source = y.Slice(Math.Min(row, height - 1) * yStride, width);
            var line = destination.Slice(row * paddedWidth, paddedWidth);
            source.CopyTo(line);
            line[width..].Fill(source[width - 1]);
        }
    }

    // The interleaved chroma of NV12 as the Cb and Cr planes (the width and height of the chroma)
    internal static void SplitChroma(ReadOnlySpan<byte> uv, int uvStride, Span<byte> cb, Span<byte> cr, int width, int height)
    {
        for (var row = 0; row < height; row++)
        {
            var source = uv.Slice(row * uvStride, width * 2);
            var cbRow = cb.Slice(row * width, width);
            var crRow = cr.Slice(row * width, width);
            for (var x = 0; x < width; x++)
            {
                cbRow[x] = source[x * 2];
                crRow[x] = source[(x * 2) + 1];
            }
        }
    }
}
