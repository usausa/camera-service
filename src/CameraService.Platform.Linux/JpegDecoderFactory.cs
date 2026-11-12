namespace CameraService.Platform.Linux;

using CameraService.Core;

// TurboJPEG decoders
public sealed class JpegDecoderFactory : IJpegDecoderFactory
{
    public IJpegDecoder Create() => new JpegDecoder();
}
