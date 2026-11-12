namespace CameraService.Platform.Windows;

public sealed class JpegEncoderTests
{
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
        var (y, uv) = JpegTestData.CreateGradient(width, height);
        using var encoder = new JpegEncoder(Quality);
        var destination = new byte[encoder.GetMaxLength(width, height)];

        // Act
        var result = encoder.TryEncodeNv12(y, width, uv, JpegTestData.GetUvStride(width), width, height, destination, out var written);

        // Assert
        Assert.True(result);
        Assert.Equal([0xFF, 0xD8], destination[..2]);
        Assert.Equal([0xFF, 0xD9], destination[(written - 2)..written]);

        var (decodedWidth, decodedHeight, actual) = await TestImages.DecodeAsync(destination[..written]).ConfigureAwait(true);
        Assert.Equal(width, decodedWidth);
        Assert.Equal(height, decodedHeight);
        Assert.True(JpegTestData.CalculatePsnr(JpegTestData.ToBgra(y, uv, width, height), actual) > 30);
    }

    [Fact]
    public void ReturnsFalseWhenDestinationIsTooSmall()
    {
        // Arrange
        const int width = 640;
        const int height = 480;
        var (y, uv) = JpegTestData.CreateGradient(width, height);
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
}
