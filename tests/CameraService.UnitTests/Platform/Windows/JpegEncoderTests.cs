namespace CameraService.Platform.Windows;

public sealed class JpegEncoderTests
{
    private const int BytesPerPixel = TestImages.BytesPerPixel;

    private const int Quality = 80;

    [Theory]
    [InlineData(16, 16)]
    [InlineData(64, 48)]
    [InlineData(33, 17)]
    [InlineData(640, 480)]
    [InlineData(1920, 1080)]
    public async Task EncodesNv12(int width, int height)
    {
        // Arrange
        var uvStride = ((width + 1) / 2) * 2;
        var (y, uv) = CreateGradient(width, height);
        using var encoder = new JpegEncoder(Quality);
        var destination = new byte[encoder.GetMaxLength(width, height)];

        // Act
        var result = encoder.TryEncodeNv12(y, width, uv, uvStride, width, height, destination, out var written);

        // Assert
        Assert.True(result);
        Assert.Equal([0xFF, 0xD8], destination[..2]);
        Assert.Equal([0xFF, 0xD9], destination[(written - 2)..written]);

        var (decodedWidth, decodedHeight, actual) = await TestImages.DecodeAsync(destination[..written]).ConfigureAwait(true);
        Assert.Equal(width, decodedWidth);
        Assert.Equal(height, decodedHeight);
        Assert.True(CalculatePsnr(ToBgra(y, uv, width, height), actual) > 30);
    }

    [Fact]
    public void ReturnsFalseWhenDestinationIsTooSmall()
    {
        // Arrange
        const int width = 640;
        const int height = 480;
        var (y, uv) = CreateGradient(width, height);
        using var encoder = new JpegEncoder(Quality);
        var destination = new byte[100];

        // Act
        var result = encoder.TryEncodeNv12(y, width, uv, width, width, height, destination, out var written);

        // Assert
        Assert.False(result);
        Assert.Equal(0, written);
    }

    [Fact]
    public void ThrowsWhenUvPlaneIsTooSmall()
    {
        using var encoder = new JpegEncoder(Quality);
        var y = new byte[16 * 16];
        var uv = new byte[(16 * 8) - 1];
        var destination = new byte[encoder.GetMaxLength(16, 16)];

        Assert.Throws<ArgumentException>(() => encoder.TryEncodeNv12(y, 16, uv, 16, 16, 16, destination, out _));
    }

    [Fact]
    public void ThrowsWhenDisposed()
    {
        var encoder = new JpegEncoder(Quality);
        var destination = new byte[encoder.GetMaxLength(16, 16)];
        encoder.Dispose();
        var y = new byte[16 * 16];
        var uv = new byte[16 * 8];

        Assert.Throws<ObjectDisposedException>(() => encoder.TryEncodeNv12(y, 16, uv, 16, 16, 16, destination, out _));
    }

    //--------------------------------------------------------------------------------
    // Helper
    //--------------------------------------------------------------------------------

    private static (byte[] Y, byte[] Uv) CreateGradient(int width, int height)
    {
        var uvStride = ((width + 1) / 2) * 2;
        var uvRows = (height + 1) / 2;
        var y = new byte[width * height];
        var uv = new byte[uvStride * uvRows];
        for (var row = 0; row < height; row++)
        {
            for (var x = 0; x < width; x++)
            {
                y[(row * width) + x] = (byte)(32 + ((x * 160) / width) + ((row * 32) / height));
            }
        }

        for (var row = 0; row < uvRows; row++)
        {
            for (var x = 0; x < uvStride; x += 2)
            {
                uv[(row * uvStride) + x] = (byte)(80 + ((row * 96) / uvRows));
                uv[(row * uvStride) + x + 1] = (byte)(176 - ((x * 96) / uvStride));
            }
        }

        return (y, uv);
    }

    // Full range BT.601 (JFIF), the reference of the decoded image
    private static byte[] ToBgra(byte[] y, byte[] uv, int width, int height)
    {
        var uvStride = ((width + 1) / 2) * 2;
        var bgra = new byte[width * height * BytesPerPixel];
        for (var row = 0; row < height; row++)
        {
            for (var x = 0; x < width; x++)
            {
                var luma = y[(row * width) + x];
                var chroma = ((row / 2) * uvStride) + ((x / 2) * 2);
                var u = uv[chroma] - 128;
                var v = uv[chroma + 1] - 128;
                var offset = ((row * width) + x) * BytesPerPixel;
                bgra[offset] = Clamp(luma + (1.772 * u));
                bgra[offset + 1] = Clamp(luma - (0.344136 * u) - (0.714136 * v));
                bgra[offset + 2] = Clamp(luma + (1.402 * v));
                bgra[offset + 3] = 255;
            }
        }

        return bgra;
    }

    private static byte Clamp(double value) => (byte)Math.Clamp(Math.Round(value), 0, 255);

    // RGB channels only
    private static double CalculatePsnr(byte[] expected, byte[] actual)
    {
        double sum = 0;
        var count = 0;
        for (var i = 0; i < expected.Length; i += BytesPerPixel)
        {
            for (var c = 0; c < 3; c++)
            {
                var difference = expected[i + c] - actual[i + c];
                sum += difference * difference;
                count++;
            }
        }

        var mse = sum / count;
        return mse == 0 ? double.PositiveInfinity : 10 * Math.Log10(255.0 * 255.0 / mse);
    }
}
