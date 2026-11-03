namespace CameraService.Core;

// Full range NV12 planes of a frame for the JPEG encoder, converted into an own buffer unless the frame is full range NV12.
// Used by one thread.
public sealed class Nv12Planes
{
    private byte[] buffer = [];

    // False for an unsupported format, the planes are valid until the next call (or while the frame is valid)
    public bool TryGet(in FrameBuffer frame, out ReadOnlySpan<byte> y, out int yStride, out ReadOnlySpan<byte> uv, out int uvStride)
    {
        if ((frame.PixelFormat == VideoPixelFormat.Nv12) && (frame.ColorRange == VideoColorRange.Full))
        {
            // Passed without copy
            y = frame.GetPlaneSpan(frame.Plane0);
            yStride = frame.Plane0.Stride;
            uv = frame.GetPlaneSpan(frame.Plane1);
            uvStride = frame.Plane1.Stride;
            return true;
        }

        if (!IsSupported(frame.PixelFormat))
        {
            y = default;
            uv = default;
            yStride = 0;
            uvStride = 0;
            return false;
        }

        var stride = GetStride(frame.Width);
        var size = GetSize(stride, frame.Height);
        if (buffer.Length != size)
        {
            buffer = GC.AllocateUninitializedArray<byte>(size);
        }

        var destinationY = buffer.AsSpan(0, stride * frame.Height);
        var destinationUv = buffer.AsSpan(stride * frame.Height);
        Convert(in frame, destinationY, destinationUv, stride);
        y = destinationY;
        uv = destinationUv;
        yStride = stride;
        uvStride = stride;
        return true;
    }

    public static bool IsSupported(VideoPixelFormat format) => format is VideoPixelFormat.Nv12 or VideoPixelFormat.Yuy2;

    // Writes the frame (a supported format) as full range NV12 into two planes of the stride
    public static void Convert(in FrameBuffer frame, Span<byte> y, Span<byte> uv, int stride)
    {
        var width = frame.Width;
        var height = frame.Height;
        switch (frame.PixelFormat)
        {
            case VideoPixelFormat.Nv12 when frame.ColorRange == VideoColorRange.Full:
                CopyPlane(frame.GetPlaneSpan(frame.Plane0), frame.Plane0.Stride, y, stride, width, height);
                CopyPlane(frame.GetPlaneSpan(frame.Plane1), frame.Plane1.Stride, uv, stride, GetStride(width), (height + 1) / 2);
                break;
            case VideoPixelFormat.Nv12:
                // YUV without range information is treated as video range
                ImageConverter.Nv12ToFullRange(frame.GetPlaneSpan(frame.Plane0), frame.Plane0.Stride, frame.GetPlaneSpan(frame.Plane1), frame.Plane1.Stride, y, stride, uv, stride, width, height);
                break;
            case VideoPixelFormat.Yuy2:
                var range = frame.ColorRange == VideoColorRange.Full ? YuvRange.Full : YuvRange.Limited;
                ImageConverter.Yuy2ToNv12(frame.GetPlaneSpan(frame.Plane0), frame.Plane0.Stride, y, stride, uv, stride, width, height, range);
                break;
            default:
                throw new ArgumentException("Format not supported.", nameof(frame));
        }
    }

    // Stride of both planes (the width rounded up to even)
    public static int GetStride(int width) => ((width + 1) / 2) * 2;

    // Bytes of both planes
    public static int GetSize(int stride, int height) => (stride * height) + (stride * ((height + 1) / 2));

    private static void CopyPlane(ReadOnlySpan<byte> source, int sourceStride, Span<byte> destination, int destinationStride, int rowBytes, int rows)
    {
        if ((sourceStride < rowBytes) || (source.Length < ((rows - 1) * sourceStride) + rowBytes))
        {
            throw new ArgumentException("Invalid source plane.", nameof(source));
        }

        if ((destinationStride < rowBytes) || (destination.Length < ((rows - 1) * destinationStride) + rowBytes))
        {
            throw new ArgumentException("Invalid destination plane.", nameof(destination));
        }

        for (var row = 0; row < rows; row++)
        {
            source.Slice(row * sourceStride, rowBytes).CopyTo(destination.Slice(row * destinationStride, rowBytes));
        }
    }
}
