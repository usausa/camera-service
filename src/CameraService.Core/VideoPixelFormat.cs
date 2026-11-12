namespace CameraService.Core;

public enum VideoPixelFormat
{
    Unknown,
    Nv12,
    Yuy2,
    Bgra8,
    // A JPEG of each frame, full range
    Mjpeg
}
