namespace CameraService.Platform.Windows;

using CameraService.Core;

// WIC JPEG encoders
public sealed class JpegEncoderFactory : IJpegEncoderFactory
{
    public IJpegEncoder Create(int quality) => new JpegEncoder(quality);
}
