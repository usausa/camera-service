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

    private byte[] GetBuffer()
    {
        var current = buffer;
        ObjectDisposedException.ThrowIf(current is null, this);
        return current;
    }
}
