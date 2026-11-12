namespace CameraService.Core;

// JPEG decoder of the platform, used by one thread
public interface IJpegDecoder : IDisposable
{
    // Full range NV12 of the image, null when the data cannot be decoded
    Nv12Image? Decode(ReadOnlySpan<byte> jpeg);
}
