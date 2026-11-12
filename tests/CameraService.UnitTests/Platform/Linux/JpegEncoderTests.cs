namespace CameraService.Platform.Linux;

public sealed class JpegEncoderTests
{
    private const int Quality = 80;

    [Theory]
    [InlineData(16, 16)]
    [InlineData(64, 48)]
    [InlineData(33, 17)]
    [InlineData(640, 480)]
    [InlineData(1920, 1080)]
    public void EncodesNv12(int width, int height)
    {
        TestImages.SkipUnlessInstalled();

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

        var (decodedWidth, decodedHeight, actual) = TestImages.Decode(destination[..written]);
        Assert.Equal(width, decodedWidth);
        Assert.Equal(height, decodedHeight);
        Assert.True(JpegTestData.CalculatePsnr(JpegTestData.ToBgra(y, uv, width, height), actual) > 30);
    }

    [Fact]
    public void PlanesWithPaddingAreEncoded()
    {
        TestImages.SkipUnlessInstalled();

        // Arrange (each line of both planes has 16 more bytes)
        const int width = 33;
        const int height = 17;
        const int padding = 16;
        var (y, uv) = JpegTestData.CreateGradient(width, height);
        var uvStride = JpegTestData.GetUvStride(width);
        var paddedY = Pad(y, width, height, padding);
        var paddedUv = Pad(uv, uvStride, (height + 1) / 2, padding);
        using var encoder = new JpegEncoder(Quality);
        var expected = JpegTestData.Encode(encoder, (y, uv), width, height);
        var destination = new byte[encoder.GetMaxLength(width, height)];

        // Act
        var result = encoder.TryEncodeNv12(paddedY, width + padding, paddedUv, uvStride + padding, width, height, destination, out var written);

        // Assert (the same data as without the padding)
        Assert.True(result);
        Assert.Equal(expected, destination[..written]);
    }

    [Fact]
    public void ReturnsFalseWhenDestinationIsTooSmall()
    {
        TestImages.SkipUnlessInstalled();

        // Arrange
        const int width = 640;
        const int height = 480;
        var (y, uv) = JpegTestData.CreateGradient(width, height);
        using var encoder = new JpegEncoder(Quality);
        var destination = new byte[encoder.GetMaxLength(width, height) - 1];

        // Act
        var result = encoder.TryEncodeNv12(y, width, uv, width, width, height, destination, out var written);

        // Assert
        Assert.False(result);
        Assert.Equal(0, written);
    }

    [Fact]
    public void ThrowsWhenUvPlaneIsTooSmall()
    {
        TestImages.SkipUnlessInstalled();

        using var encoder = new JpegEncoder(Quality);
        var y = new byte[16 * 16];
        var uv = new byte[(16 * 8) - 1];
        var destination = new byte[encoder.GetMaxLength(16, 16)];

        Assert.Throws<ArgumentException>(() => encoder.TryEncodeNv12(y, 16, uv, 16, 16, 16, destination, out _));
    }

    [Fact]
    public void ThrowsWhenDisposed()
    {
        TestImages.SkipUnlessInstalled();

        var encoder = new JpegEncoder(Quality);
        var destination = new byte[encoder.GetMaxLength(16, 16)];
        encoder.Dispose();
        var y = new byte[16 * 16];
        var uv = new byte[16 * 8];

        Assert.Throws<ObjectDisposedException>(() => encoder.TryEncodeNv12(y, 16, uv, 16, 16, 16, destination, out _));
    }

    [Fact]
    public void OddLumaIsExtendedByLastColumnAndRow()
    {
        // Arrange (3x3 pixels in lines of 4 bytes)
        byte[] y = [130, 131, 132, 0, 140, 141, 142, 0, 150, 151, 152, 0];
        var padded = new byte[16];

        // Act
        JpegEncoder.PadLuma(y, 4, padded, 3, 3, 4, 4);

        // Assert
        Assert.Equal([130, 131, 132, 132, 140, 141, 142, 142, 150, 151, 152, 152, 150, 151, 152, 152], padded);
    }

    [Fact]
    public void InterleavedChromaIsSplit()
    {
        // Arrange (2x2 samples, 2 bytes of padding in each line)
        byte[] uv = [100, 140, 110, 150, 0, 0, 120, 160, 130, 170, 0, 0];
        var cb = new byte[4];
        var cr = new byte[4];

        // Act
        JpegEncoder.SplitChroma(uv, 6, cb, cr, 2, 2);

        // Assert
        Assert.Equal([100, 110, 120, 130], cb);
        Assert.Equal([140, 150, 160, 170], cr);
    }

    //--------------------------------------------------------------------------------
    // Helper
    //--------------------------------------------------------------------------------

    private static byte[] Pad(byte[] plane, int rowBytes, int rows, int padding)
    {
        var padded = new byte[(rowBytes + padding) * rows];
        for (var row = 0; row < rows; row++)
        {
            plane.AsSpan(row * rowBytes, rowBytes).CopyTo(padded.AsSpan(row * (rowBytes + padding)));
        }

        return padded;
    }
}
