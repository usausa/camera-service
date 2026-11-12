namespace CameraService.Core;

using System.Buffers;

// Full range NV12 image in a buffer of the shared pool, so that it can be drawn on and encoded after the frame is released.
// Both planes have the same stride.
public sealed class Nv12Image : IDisposable
{
    private byte[]? buffer;

    public int Width { get; }

    public int Height { get; }

    public int Stride { get; }

    public Span<byte> Y => GetBuffer().AsSpan(0, Stride * Height);

    public Span<byte> Uv => GetBuffer().AsSpan(Stride * Height, Stride * ((Height + 1) / 2));

    public Nv12Image(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        Width = width;
        Height = height;
        Stride = Nv12Planes.GetStride(width);
        buffer = ArrayPool<byte>.Shared.Rent(Nv12Planes.GetSize(Stride, height));
    }

    public void Dispose()
    {
        var rented = Interlocked.Exchange(ref buffer, null);
        if (rented is not null)
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    // A copy of the frame, null for an unsupported format
    public static Nv12Image? FromFrame(in FrameBuffer frame)
    {
        if (!Nv12Planes.IsSupported(frame.PixelFormat))
        {
            return null;
        }

        var image = new Nv12Image(frame.Width, frame.Height);
        try
        {
            Nv12Planes.Convert(in frame, image.Y, image.Uv, image.Stride);
            return image;
        }
        catch
        {
            image.Dispose();
            throw;
        }
    }

    // Writes the chroma from Cb and Cr samples whose plane has the size of the image or a half of it in either direction (rounded up).
    // The samples of a row are step bytes apart (1 for separate planes, 2 for interleaved CbCr).
    // Each sample of 4:2:0 covers 2x2 pixels: the average of the source samples that cover them (one sample when the source is already 4:2:0).
    public void WriteChroma(ReadOnlySpan<byte> cb, ReadOnlySpan<byte> cr, int stride, int step, int sourceWidth, int sourceHeight)
    {
        if ((sourceWidth != Width) && (sourceWidth != (Width + 1) / 2))
        {
            throw new ArgumentOutOfRangeException(nameof(sourceWidth));
        }

        if ((sourceHeight != Height) && (sourceHeight != (Height + 1) / 2))
        {
            throw new ArgumentOutOfRangeException(nameof(sourceHeight));
        }

        var last = ((sourceHeight - 1) * stride) + ((sourceWidth - 1) * step);
        if ((cb.Length <= last) || (cr.Length <= last))
        {
            throw new ArgumentException("Invalid chroma plane.", nameof(cb));
        }

        var horizontal = sourceWidth == Width ? 1 : 2;
        var vertical = sourceHeight == Height ? 1 : 2;
        var destination = Uv;
        var columns = (Width + 1) / 2;
        var rows = (Height + 1) / 2;
        for (var row = 0; row < rows; row++)
        {
            var top = Math.Min((row * 2) / vertical, sourceHeight - 1) * stride;
            var bottom = Math.Min(((row * 2) + 1) / vertical, sourceHeight - 1) * stride;
            var line = destination.Slice(row * Stride, columns * 2);
            for (var column = 0; column < columns; column++)
            {
                var left = Math.Min((column * 2) / horizontal, sourceWidth - 1) * step;
                var right = Math.Min(((column * 2) + 1) / horizontal, sourceWidth - 1) * step;
                line[column * 2] = Average(cb, top, bottom, left, right);
                line[(column * 2) + 1] = Average(cr, top, bottom, left, right);
            }
        }
    }

    private static byte Average(ReadOnlySpan<byte> plane, int top, int bottom, int left, int right) =>
        (byte)((plane[top + left] + plane[top + right] + plane[bottom + left] + plane[bottom + right] + 2) / 4);

    private byte[] GetBuffer()
    {
        var current = buffer;
        ObjectDisposedException.ThrowIf(current is null, this);
        return current;
    }
}
