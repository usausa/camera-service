namespace CameraService.Core;

public interface IJpegEncoderFactory
{
    // quality: 1-100
    IJpegEncoder Create(int quality);
}
