namespace CameraService.Core;

using System.Buffers;

public static class JpegEncoderExtensions
{
    // The image as a JPEG of its size, null when the encoder does not write it
    public static byte[]? Encode(this IJpegEncoder encoder, Nv12Image image)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(encoder.GetMaxLength(image.Width, image.Height));
        try
        {
            return encoder.TryEncodeNv12(image.Y, image.Stride, image.Uv, image.Stride, image.Width, image.Height, buffer, out var written)
                ? buffer.AsSpan(0, written).ToArray()
                : null;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
