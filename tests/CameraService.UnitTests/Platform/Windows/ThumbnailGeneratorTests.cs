namespace CameraService.Platform.Windows;

public sealed class ThumbnailGeneratorTests
{
    private const int Width = 320;

    private readonly ThumbnailGenerator generator = new();

    [Theory]
    [InlineData(640, 480, 320, 240)]
    [InlineData(1280, 720, 320, 180)]
    [InlineData(160, 120, 160, 120)]
    public async Task ImageIsScaledDownWhileDecoding(int width, int height, int expectedWidth, int expectedHeight)
    {
        // Arrange (orange)
        var jpeg = TestImages.CreateJpeg(width, height, 150, 70, 200);

        // Act
        var thumbnail = generator.Generate(jpeg, Width);

        // Assert (the color is kept)
        Assert.NotNull(thumbnail);
        var (decodedWidth, decodedHeight, bgra) = await TestImages.DecodeAsync(thumbnail).ConfigureAwait(true);
        Assert.Equal(expectedWidth, decodedWidth);
        Assert.Equal(expectedHeight, decodedHeight);
        var reference = await TestImages.DecodeAsync(jpeg).ConfigureAwait(true);
        for (var c = 0; c < 3; c++)
        {
            Assert.InRange(bgra[c] - reference.Bgra[c], -4, 4);
        }
    }

    [Fact]
    public async Task LargeImageUsesNearestScale()
    {
        // Arrange
        var jpeg = TestImages.CreateJpeg(1920, 1080, 128, 128, 128);

        // Act
        var thumbnail = generator.Generate(jpeg, Width);

        // Assert (1/4 or 1/8 of the size)
        Assert.NotNull(thumbnail);
        var (decodedWidth, decodedHeight, _) = await TestImages.DecodeAsync(thumbnail).ConfigureAwait(true);
        Assert.Contains((decodedWidth, decodedHeight), new[] { (480, 270), (240, 135) });
    }

    [Fact]
    public void InvalidDataHasNoThumbnail()
    {
        // Act
        var thumbnail = generator.Generate([0xFF, 0xD8, 0x00, 0x01, 0x02, 0x03], Width);

        // Assert
        Assert.Null(thumbnail);
    }
}
