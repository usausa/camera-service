namespace CameraService.Platform.Linux;

using CameraService.Core;

// TurboJPEG encoders
public sealed class JpegEncoderFactory : IJpegEncoderFactory
{
    public IJpegEncoder Create(int quality) => new JpegEncoder(quality);
}
