namespace CameraService.Core;

// JPEG encoder of the platform, used by one thread
public interface IJpegEncoder : IDisposable
{
    // Upper bound of the encoded size
    int GetMaxLength(int width, int height);

    // Encodes full range NV12 as 4:2:0 JPEG, returns false when the destination is too small
    bool TryEncodeNv12(ReadOnlySpan<byte> y, int yStride, ReadOnlySpan<byte> uv, int uvStride, int width, int height, Span<byte> destination, out int written);
}
