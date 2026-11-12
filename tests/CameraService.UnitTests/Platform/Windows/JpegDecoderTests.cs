namespace CameraService.Platform.Windows;

using CameraService.Core;

public sealed class JpegDecoderTests
{
    private const byte Luma = 150;
    private const byte U = 70;
    private const byte V = 200;

    [Theory]
    [InlineData(64, 48)]
    [InlineData(33, 17)]
    public void ImageIsDecodedAtFullSize(int width, int height)
    {
        // Arrange
        var jpeg = TestImages.CreateJpeg(width, height, Luma, U, V);
        using var decoder = new JpegDecoderFactory().Create();

        // Act
        using var image = decoder.Decode(jpeg);

        // Assert
        Assert.NotNull(image);
        Assert.Equal(width, image.Width);
        Assert.Equal(height, image.Height);
        JpegTestData.AssertColor(image, Luma, U, V);
    }

    [Fact]
    public void InvalidDataIsNotDecoded()
    {
        // Arrange
        using var decoder = new JpegDecoder();

        // Act
        using var image = decoder.Decode([0xFF, 0xD8, 0x00, 0x01, 0x02, 0x03]);

        // Assert
        Assert.Null(image);
    }

    // The tables of a frame of an MJPEG camera without them are those of the encoder
    [Fact]
    public async Task StandardTablesDecodeFrameWithoutTables()
    {
        // Arrange
        using var encoder = new JpegEncoder(80);
        var jpeg = JpegTestData.Encode(encoder, JpegTestData.CreateGradient(64, 48), 64, 48);
        var frame = JpegTestData.RemoveHuffmanTables(jpeg);
        var destination = new byte[frame.Length + MjpegFrame.MaxAddedLength];

        // Act
        var written = MjpegFrame.CopyAsJpeg(frame, destination);

        // Assert (the same image)
        var (_, _, expected) = await TestImages.DecodeAsync(jpeg).ConfigureAwait(true);
        var (_, _, actual) = await TestImages.DecodeAsync(destination[..written]).ConfigureAwait(true);
        Assert.Equal(expected, actual);
    }
}
