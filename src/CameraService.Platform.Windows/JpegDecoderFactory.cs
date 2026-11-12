namespace CameraService.Platform.Windows;

using CameraService.Core;

// WIC JPEG decoders
public sealed class JpegDecoderFactory : IJpegDecoderFactory
{
    public IJpegDecoder Create() => new JpegDecoder();
}
