namespace CameraService.Platform.Windows;

using CameraService.Core;

// Thumbnails with WIC: the decoder scales down (1/2 to 1/8) while it decodes into YCbCr planes,
// and the planes are encoded again without color conversion
public sealed class ThumbnailGenerator : IThumbnailGenerator
{
    private const int Quality = 80;

    public byte[]? Generate(ReadOnlySpan<byte> image, int width)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        using var decoder = new JpegDecoder();
        using var decoded = decoder.Decode(image, width);
        if (decoded is null)
        {
            return null;
        }

        using var encoder = new JpegEncoder(Quality);
        return encoder.Encode(decoded);
    }
}
