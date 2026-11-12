namespace CameraService.Platform.Windows;

using System.Runtime.InteropServices.WindowsRuntime;

using global::Windows.Graphics.Imaging;
using global::Windows.Storage.Streams;

internal static class TestImages
{
    // JPEG of one color (full range YCbCr)
    public static byte[] CreateJpeg(int width, int height, byte luma, byte u, byte v)
    {
        using var encoder = new JpegEncoder(90);
        return JpegTestData.Encode(encoder, JpegTestData.CreateSolid(width, height, luma, u, v), width, height);
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
        var bgra = new byte[bitmap.PixelWidth * bitmap.PixelHeight * JpegTestData.BytesPerPixel];
        bitmap.CopyToBuffer(bgra.AsBuffer());
        return (bitmap.PixelWidth, bitmap.PixelHeight, bgra);
    }
}
