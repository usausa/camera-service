namespace CameraService.Platform.Windows;

using CameraService.Core;

public sealed class H264EncoderFactory : IH264EncoderFactory
{
    public IH264Encoder Create(H264EncoderOptions options, H264OutputCallback callback) => new H264Encoder(options, callback);
}
