namespace CameraService.Platform.Linux;

using CameraService.Core;

// Thumbnails with TurboJPEG: the decoder scales down (1/2 to 1/8) while it decodes into YCbCr planes,
// and the planes are encoded again without color conversion
public sealed class ThumbnailGenerator : IThumbnailGenerator
{
    private const int Quality = 80;

    private static readonly int[] Scales = [8, 4, 2];

    public byte[]? Generate(ReadOnlySpan<byte> image, int width)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        using var decoder = new JpegDecoder();
        if (!decoder.TryReadSize(image, out var sourceWidth, out _))
        {
            return null;
        }

        using var decoded = decoder.Decode(image, GetScale(sourceWidth, width));
        if (decoded is null)
        {
            return null;
        }

        using var encoder = new JpegEncoder(Quality);
        return encoder.Encode(decoded);
    }

    // The largest scale of the decoder (8, 4 or 2) that keeps at least the width, 1 for a smaller image
    internal static int GetScale(int sourceWidth, int width)
    {
        foreach (var scale in Scales)
        {
            if (JpegDecoder.GetScaledSize(sourceWidth, scale) >= width)
            {
                return scale;
            }
        }

        return 1;
    }
}
