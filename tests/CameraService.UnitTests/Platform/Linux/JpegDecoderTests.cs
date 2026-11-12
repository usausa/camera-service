namespace CameraService.Platform.Linux;

using CameraService.Core;

public sealed class JpegDecoderTests
{
    // Orange, Y 149 Cb 80 Cr 181 in full range BT.601
    private const byte Blue = 64;
    private const byte Green = 128;
    private const byte Red = 224;

    private const int Luma = 149;
    private const int Cb = 80;
    private const int Cr = 181;

    [Theory]
    [InlineData(TurboJpeg.Subsampling444)]
    [InlineData(TurboJpeg.Subsampling422)]
    [InlineData(TurboJpeg.Subsampling420)]
    [InlineData(TurboJpeg.Subsampling440)]
    public void SubsamplingIsDecodedInto420(int subsampling)
    {
        TestImages.SkipUnlessInstalled();

        // Arrange
        var jpeg = TestImages.CreateJpeg(64, 48, Blue, Green, Red, subsampling);
        using var decoder = new JpegDecoder();

        // Act
        using var image = decoder.Decode(jpeg, 1);

        // Assert
        Assert.NotNull(image);
        JpegTestData.AssertColor(image, Luma, Cb, Cr);
    }

    [Fact]
    public void GrayHasNeutralChroma()
    {
        TestImages.SkipUnlessInstalled();

        // Arrange
        var jpeg = TestImages.CreateJpeg(64, 48, Blue, Green, Red, TurboJpeg.SubsamplingGray);
        using var decoder = new JpegDecoder();

        // Act
        using var image = decoder.Decode(jpeg, 1);

        // Assert
        Assert.NotNull(image);
        JpegTestData.AssertColor(image, Luma, 128, 128);
    }

    // The size of the JPEG divided by the scale, rounded up
    [Theory]
    [InlineData(640, 480, 2, 320, 240)]
    [InlineData(640, 480, 8, 80, 60)]
    [InlineData(33, 17, 2, 17, 9)]
    [InlineData(33, 17, 8, 5, 3)]
    public void ImageIsScaledWhileDecoding(int width, int height, int scale, int scaledWidth, int scaledHeight)
    {
        TestImages.SkipUnlessInstalled();

        // Arrange
        var jpeg = TestImages.CreateJpeg(width, height, Blue, Green, Red, TurboJpeg.Subsampling420);
        using var decoder = new JpegDecoder();

        // Act
        using var image = decoder.Decode(jpeg, scale);

        // Assert
        Assert.NotNull(image);
        Assert.Equal(scaledWidth, image.Width);
        Assert.Equal(scaledHeight, image.Height);
        JpegTestData.AssertColor(image, Luma, Cb, Cr);
    }

    [Fact]
    public void SizeIsRead()
    {
        TestImages.SkipUnlessInstalled();

        // Arrange
        var jpeg = TestImages.CreateJpeg(33, 17, Blue, Green, Red, TurboJpeg.Subsampling422);
        using var decoder = new JpegDecoder();

        // Act
        var result = decoder.TryReadSize(jpeg, out var width, out var height);

        // Assert
        Assert.True(result);
        Assert.Equal(33, width);
        Assert.Equal(17, height);
    }

    // The decoder warns about the missing data and fills the rest of the image
    [Fact]
    public void TruncatedDataIsDecoded()
    {
        TestImages.SkipUnlessInstalled();

        // Arrange (the end of the scan is missing)
        using var encoder = new JpegEncoder(90);
        var jpeg = JpegTestData.Encode(encoder, JpegTestData.CreateGradient(640, 480), 640, 480);
        using var decoder = new JpegDecoder();

        // Act
        using var image = decoder.Decode(jpeg.AsSpan(0, jpeg.Length / 2), 1);

        // Assert
        Assert.NotNull(image);
    }

    [Fact]
    public void InvalidDataIsNotDecoded()
    {
        TestImages.SkipUnlessInstalled();

        // Arrange
        byte[] data = [0xFF, 0xD8, 0x00, 0x01, 0x02, 0x03];
        using var decoder = new JpegDecoder();

        // Act
        var result = decoder.TryReadSize(data, out _, out _);
        using var image = decoder.Decode(data, 1);

        // Assert
        Assert.False(result);
        Assert.Null(image);
    }

    [Fact]
    public void OtherScaleIsNotDecoded()
    {
        TestImages.SkipUnlessInstalled();

        // Arrange
        var jpeg = TestImages.CreateJpeg(16, 16, Blue, Green, Red, TurboJpeg.Subsampling420);
        using var decoder = new JpegDecoder();

        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => decoder.Decode(jpeg, 3));
    }

    [Fact]
    public void ImageOfFactoryHasFullSize()
    {
        TestImages.SkipUnlessInstalled();

        // Arrange
        var jpeg = TestImages.CreateJpeg(33, 17, Blue, Green, Red, TurboJpeg.Subsampling422);
        using var decoder = new JpegDecoderFactory().Create();

        // Act
        using var image = decoder.Decode(jpeg);

        // Assert
        Assert.NotNull(image);
        Assert.Equal((33, 17), (image.Width, image.Height));
        JpegTestData.AssertColor(image, Luma, Cb, Cr);
    }

    // The tables of a frame of an MJPEG camera without them are those of the encoder
    [Fact]
    public void StandardTablesDecodeFrameWithoutTables()
    {
        TestImages.SkipUnlessInstalled();

        // Arrange
        using var encoder = new JpegEncoder(80);
        var jpeg = JpegTestData.Encode(encoder, JpegTestData.CreateGradient(64, 48), 64, 48);
        var frame = JpegTestData.RemoveHuffmanTables(jpeg);
        var destination = new byte[frame.Length + MjpegFrame.MaxAddedLength];

        // Act
        var written = MjpegFrame.CopyAsJpeg(frame, destination);

        // Assert (the same image)
        Assert.Equal(TestImages.Decode(jpeg).Bgra, TestImages.Decode(destination[..written]).Bgra);
    }
}
