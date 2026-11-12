namespace CameraService.Platform.Linux;

public sealed class ThumbnailGeneratorTests
{
    private const int Width = 320;

    private readonly ThumbnailGenerator generator = new();

    [Theory]
    [InlineData(640, 480, TurboJpeg.Subsampling420, 320, 240)]
    [InlineData(1280, 720, TurboJpeg.Subsampling420, 320, 180)]
    [InlineData(160, 120, TurboJpeg.Subsampling420, 160, 120)]
    [InlineData(640, 480, TurboJpeg.Subsampling422, 320, 240)]
    public void ImageIsScaledDownWhileDecoding(int width, int height, int subsampling, int expectedWidth, int expectedHeight)
    {
        TestImages.SkipUnlessInstalled();

        // Arrange (orange)
        var jpeg = TestImages.CreateJpeg(width, height, 64, 128, 224, subsampling);

        // Act
        var thumbnail = generator.Generate(jpeg, Width);

        // Assert (the color is kept)
        Assert.NotNull(thumbnail);
        var (decodedWidth, decodedHeight, bgra) = TestImages.Decode(thumbnail);
        Assert.Equal(expectedWidth, decodedWidth);
        Assert.Equal(expectedHeight, decodedHeight);
        var reference = TestImages.Decode(jpeg);
        for (var c = 0; c < 3; c++)
        {
            Assert.InRange(bgra[c] - reference.Bgra[c], -4, 4);
        }
    }

    [Fact]
    public void LargeImageUsesNearestScale()
    {
        TestImages.SkipUnlessInstalled();

        // Arrange
        var jpeg = TestImages.CreateJpeg(1920, 1080, 128, 128, 128, TurboJpeg.Subsampling420);

        // Act
        var thumbnail = generator.Generate(jpeg, Width);

        // Assert (1/4 of the size, 1/8 is smaller than the width)
        Assert.NotNull(thumbnail);
        var (decodedWidth, decodedHeight, _) = TestImages.Decode(thumbnail);
        Assert.Equal((480, 270), (decodedWidth, decodedHeight));
    }

    [Fact]
    public void InvalidDataHasNoThumbnail()
    {
        TestImages.SkipUnlessInstalled();

        // Act
        var thumbnail = generator.Generate([0xFF, 0xD8, 0x00, 0x01, 0x02, 0x03], Width);

        // Assert
        Assert.Null(thumbnail);
    }

    // The largest division that keeps the width
    [Theory]
    [InlineData(640, 2)]
    [InlineData(639, 2)]
    [InlineData(1280, 4)]
    [InlineData(1920, 4)]
    [InlineData(2560, 8)]
    [InlineData(638, 1)]
    [InlineData(160, 1)]
    public void ScaleKeepsWidth(int sourceWidth, int expected)
    {
        // Act
        var scale = ThumbnailGenerator.GetScale(sourceWidth, Width);

        // Assert
        Assert.Equal(expected, scale);
    }
}
