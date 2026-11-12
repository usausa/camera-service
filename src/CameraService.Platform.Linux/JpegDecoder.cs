namespace CameraService.Platform.Linux;

using System.Runtime.InteropServices;

using CameraService.Core;

// TurboJPEG decoder into full range NV12, scaled down while decoding (1/2, 1/4 or 1/8 of the size).
// JPEG of YCbCr whose chroma has the size of the luma or a half of it in either direction, or of gray. Used by one thread.
internal sealed unsafe class JpegDecoder : IJpegDecoder
{
    private const int PlaneCount = 3;

    private const byte NeutralChroma = 128;

    private IntPtr handle = TurboJpeg.CreateDecompressor();

    // The Y plane, then the Cb and Cr planes, as TurboJPEG writes them
    private byte[] planes = [];

    public void Dispose()
    {
        if (handle != IntPtr.Zero)
        {
            _ = TurboJpeg.Destroy(handle);
            handle = IntPtr.Zero;
        }
    }

    // Size of the image divided by the scale, as the decoder: rounded up
    public static int GetScaledSize(int size, int scale) => (size + scale - 1) / scale;

    // Size of the image, false when the data is not a JPEG
    public bool TryReadSize(ReadOnlySpan<byte> jpeg, out int width, out int height)
    {
        ObjectDisposedException.ThrowIf(handle == IntPtr.Zero, this);
        return TryReadHeader(jpeg, out width, out height, out _, out _);
    }

    public Nv12Image? Decode(ReadOnlySpan<byte> jpeg) => Decode(jpeg, 1);

    // The image divided by the scale (1, 2, 4 or 8), null when the data cannot be decoded.
    // A warning (for example data cut short) still gives the image.
    public Nv12Image? Decode(ReadOnlySpan<byte> jpeg, int scale)
    {
        ObjectDisposedException.ThrowIf(handle == IntPtr.Zero, this);
        if (scale is not (1 or 2 or 4 or 8))
        {
            throw new ArgumentOutOfRangeException(nameof(scale));
        }

        if (!TryReadHeader(jpeg, out var jpegWidth, out var jpegHeight, out var subsampling, out var colorspace) || !IsSupported(subsampling, colorspace))
        {
            return null;
        }

        var width = GetScaledSize(jpegWidth, scale);
        var height = GetScaledSize(jpegHeight, scale);
        var gray = subsampling == TurboJpeg.SubsamplingGray;
        var lumaWidth = TurboJpeg.PlaneWidth(0, width, subsampling);
        var lumaSize = lumaWidth * TurboJpeg.PlaneHeight(0, height, subsampling);
        var chromaWidth = gray ? 0 : TurboJpeg.PlaneWidth(1, width, subsampling);
        var chromaHeight = gray ? 0 : TurboJpeg.PlaneHeight(1, height, subsampling);
        var chromaSize = chromaWidth * chromaHeight;
        if (planes.Length < lumaSize + (chromaSize * 2))
        {
            planes = GC.AllocateUninitializedArray<byte>(lumaSize + (chromaSize * 2));
        }

        int result;
        fixed (byte* source = jpeg)
        {
            fixed (byte* destination = planes)
            {
                var pointers = stackalloc byte*[PlaneCount];
                pointers[0] = destination;
                pointers[1] = gray ? null : destination + lumaSize;
                pointers[2] = gray ? null : destination + lumaSize + chromaSize;
                var strides = stackalloc int[PlaneCount];
                strides[0] = lumaWidth;
                strides[1] = chromaWidth;
                strides[2] = chromaWidth;
                result = TurboJpeg.DecompressToYuvPlanes(handle, source, new CULong((nuint)jpeg.Length), pointers, width, strides, height, 0);
            }
        }

        if ((result != 0) && !TurboJpeg.IsWarning(handle))
        {
            return null;
        }

        var image = new Nv12Image(width, height);
        var y = image.Y;
        for (var row = 0; row < height; row++)
        {
            planes.AsSpan(row * lumaWidth, width).CopyTo(y.Slice(row * image.Stride, width));
        }

        if (gray)
        {
            image.Uv.Fill(NeutralChroma);
        }
        else
        {
            image.WriteChroma(planes.AsSpan(lumaSize, chromaSize), planes.AsSpan(lumaSize + chromaSize, chromaSize), chromaWidth, 1, chromaWidth, chromaHeight);
        }

        return image;
    }

    private static bool IsSupported(int subsampling, int colorspace) =>
        colorspace switch
        {
            TurboJpeg.ColorspaceYCbCr => subsampling is TurboJpeg.Subsampling444 or TurboJpeg.Subsampling422 or TurboJpeg.Subsampling420 or TurboJpeg.Subsampling440,
            TurboJpeg.ColorspaceGray => subsampling == TurboJpeg.SubsamplingGray,
            _ => false
        };

    private bool TryReadHeader(ReadOnlySpan<byte> jpeg, out int width, out int height, out int subsampling, out int colorspace)
    {
        int result;
        var imageWidth = 0;
        var imageHeight = 0;
        var imageSubsampling = -1;
        var imageColorspace = -1;
        fixed (byte* source = jpeg)
        {
            result = TurboJpeg.DecompressHeader(handle, source, new CULong((nuint)jpeg.Length), &imageWidth, &imageHeight, &imageSubsampling, &imageColorspace);
        }

        width = imageWidth;
        height = imageHeight;
        subsampling = imageSubsampling;
        colorspace = imageColorspace;
        return ((result == 0) || TurboJpeg.IsWarning(handle)) && (width > 0) && (height > 0);
    }
}
