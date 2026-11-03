namespace CameraService.Core;

public sealed class TextOverlayTests
{
    private const int Width = 64;

    private const int Height = 32;

    private const byte BackgroundLuma = 100;

    private const byte BackgroundU = 90;

    private const byte BackgroundV = 160;

    [Theory]
    [InlineData("2026/11/03 12:34:56", true)]
    [InlineData("2026-11-03 12:34:56.789 +09:00", true)]
    [InlineData("12:34 PM", false)]
    [InlineData("Tue", false)]
    public void SupportedCharacters(string text, bool expected)
    {
        // Act
        var result = TextOverlay.IsSupported(text);

        // Assert
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(100, 1)]
    [InlineData(240, 1)]
    [InlineData(480, 2)]
    [InlineData(720, 3)]
    [InlineData(1080, 4)]
    public void ScaleFollowsHeight(int height, int expected)
    {
        // Act
        var scale = TextOverlay.GetScale(height);

        // Assert
        Assert.Equal(expected, scale);
    }

    [Fact]
    public void TextIsWhiteOnBlackOutline()
    {
        // Arrange
        using var image = CreateImage();

        // Act ("1" at 4 + outline 1: the glyph starts at (5, 5), its top pixel is the third column)
        TextOverlay.Draw(image, "1", TextPosition.TopLeft, 1);

        // Assert
        Assert.Equal(255, GetLuma(image, 7, 5));
        Assert.Equal(255, GetLuma(image, 6, 6));
        Assert.Equal(255, GetLuma(image, 8, 11));
        Assert.Equal(0, GetLuma(image, 7, 4));
        Assert.Equal(0, GetLuma(image, 6, 5));
        Assert.Equal(0, GetLuma(image, 9, 12));
        Assert.Equal(BackgroundLuma, GetLuma(image, 4, 4));
        Assert.Equal(BackgroundLuma, GetLuma(image, 40, 20));
    }

    [Fact]
    public void ChromaUnderTextIsGray()
    {
        // Arrange
        using var image = CreateImage();

        // Act
        TextOverlay.Draw(image, "1", TextPosition.TopLeft, 1);

        // Assert (the chroma of the 2x2 pixels that include the text, or only the outline)
        Assert.Equal((128, 128), GetChroma(image, 7, 5));
        Assert.Equal((128, 128), GetChroma(image, 9, 12));
        Assert.Equal((BackgroundU, BackgroundV), GetChroma(image, 2, 2));
        Assert.Equal((BackgroundU, BackgroundV), GetChroma(image, 40, 20));
    }

    [Fact]
    public void BottomRightKeepsMargin()
    {
        // Arrange
        using var image = CreateImage();

        // Act (the box of "1" is 7x9 with the outline, 4 pixels from the right and bottom edges)
        TextOverlay.Draw(image, "1", TextPosition.BottomRight, 1);

        // Assert
        Assert.Equal(255, GetLuma(image, 56, 20));
        Assert.Equal(255, GetLuma(image, 57, 26));
        Assert.Equal(0, GetLuma(image, 58, 27));
        Assert.Equal(BackgroundLuma, GetLuma(image, 60, 28));
    }

    [Fact]
    public void ScaleEnlargesGlyphs()
    {
        // Arrange
        using var image = CreateImage();

        // Act (scale 2: margin 8, outline 1, each pixel of the glyph is 2x2)
        TextOverlay.Draw(image, "1", TextPosition.TopLeft, 2);

        // Assert
        Assert.Equal(255, GetLuma(image, 13, 9));
        Assert.Equal(255, GetLuma(image, 14, 10));
        Assert.Equal(0, GetLuma(image, 12, 9));
        Assert.Equal(0, GetLuma(image, 13, 8));
    }

    [Fact]
    public void TextIsClippedByImage()
    {
        // Arrange
        using var image = CreateImage();

        // Act (wider and higher than the image)
        TextOverlay.Draw(image, "0000000000", TextPosition.BottomRight, 4);

        // Assert
        Assert.Contains((byte)255, image.Y.ToArray());
    }

    [Fact]
    public void UnsupportedCharacterIsRejected()
    {
        // Arrange
        using var image = CreateImage();

        // Act & Assert
        Assert.Throws<ArgumentException>(() => TextOverlay.Draw(image, "12:34 PM", TextPosition.TopLeft, 1));
    }

    //--------------------------------------------------------------------------------
    // Helper
    //--------------------------------------------------------------------------------

    private static Nv12Image CreateImage()
    {
        var image = new Nv12Image(Width, Height);
        image.Y.Fill(BackgroundLuma);
        var uv = image.Uv;
        for (var i = 0; i < uv.Length; i += 2)
        {
            uv[i] = BackgroundU;
            uv[i + 1] = BackgroundV;
        }

        return image;
    }

    private static byte GetLuma(Nv12Image image, int x, int y) => image.Y[(y * image.Stride) + x];

    private static (byte U, byte V) GetChroma(Nv12Image image, int x, int y)
    {
        var offset = ((y / 2) * image.Stride) + ((x / 2) * 2);
        var uv = image.Uv;
        return (uv[offset], uv[offset + 1]);
    }
}
