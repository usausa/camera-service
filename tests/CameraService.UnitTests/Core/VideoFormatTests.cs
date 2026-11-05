namespace CameraService.Core;

public sealed class VideoFormatTests
{
    private static readonly VideoFormat Format = new(VideoPixelFormat.Nv12, 1280, 720, 30);

    [Theory]
    [InlineData(30, true)]
    [InlineData(29.97, true)]
    [InlineData(30.4, true)]
    [InlineData(30.5, false)]
    [InlineData(29.5, false)]
    [InlineData(15, false)]
    public void FrameRateIsComparedWithTolerance(double frameRate, bool expected)
    {
        // Act
        var result = Format.Matches(Format with { FrameRate = frameRate });

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void OtherFormatDoesNotMatch()
    {
        // Act
        var pixelFormat = Format.Matches(Format with { PixelFormat = VideoPixelFormat.Yuy2 });
        var width = Format.Matches(Format with { Width = 640 });
        var height = Format.Matches(Format with { Height = 480 });

        // Assert
        Assert.False(pixelFormat);
        Assert.False(width);
        Assert.False(height);
    }
}
