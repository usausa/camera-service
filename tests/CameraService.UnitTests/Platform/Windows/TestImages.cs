namespace CameraService.Platform.Windows;

using System.Runtime.InteropServices.WindowsRuntime;

using global::Windows.Graphics.Imaging;
using global::Windows.Storage.Streams;

internal static class TestImages
{
    public const int BytesPerPixel = 4;

    // JPEG of one color (full range YCbCr)
    public static byte[] CreateJpeg(int width, int height, byte luma, byte u, byte v)
    {
        var uvStride = ((width + 1) / 2) * 2;
        var y = Enumerable.Repeat(luma, width * height).ToArray();
        var uv = new byte[uvStride * ((height + 1) / 2)];
        for (var i = 0; i < uv.Length; i += 2)
        {
            uv[i] = u;
            uv[i + 1] = v;
        }

        using var encoder = new JpegEncoder(90);
        var destination = new byte[encoder.GetMaxLength(width, height)];
        Assert.True(encoder.TryEncodeNv12(y, width, uv, uvStride, width, height, destination, out var written));
        return destination[..written];
    }

    // Decoded by the decoder of Windows Runtime, independent of the code under test
    public static async Task<(int Width, int Height, byte[] Bgra)> DecodeAsync(byte[] jpeg)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var stream = new InMemoryRandomAccessStream();
        await stream.WriteAsync(jpeg.AsBuffer()).AsTask(cancellationToken).ConfigureAwait(true);
        stream.Seek(0);
        var decoder = await BitmapDecoder.CreateAsync(stream).AsTask(cancellationToken).ConfigureAwait(true);
        using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore).AsTask(cancellationToken).ConfigureAwait(true);
        var bgra = new byte[bitmap.PixelWidth * bitmap.PixelHeight * BytesPerPixel];
        bitmap.CopyToBuffer(bgra.AsBuffer());
        return (bitmap.PixelWidth, bitmap.PixelHeight, bgra);
    }
}
