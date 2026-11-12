namespace CameraService.Core;

using System.Runtime.InteropServices;

public sealed class Nv12ImageTests
{
    private const int Width = 32;

    private const int Height = 4;

    [Fact]
    public void Yuy2IsConvertedToFullRange()
    {
        // Arrange (limited range black: Y 16, chroma 128)
        var source = GC.AllocateArray<byte>(Width * Height * 2, true);
        for (var i = 0; i < source.Length; i += 2)
        {
            source[i] = 16;
            source[i + 1] = 128;
        }

        var frame = CreateFrame(source, VideoPixelFormat.Yuy2, VideoColorRange.Limited, new FramePlane(0, Width, Height, Width * 2), default);

        // Act
        using var image = Nv12Image.FromFrame(in frame);

        // Assert
        Assert.NotNull(image);
        Assert.Equal(Width, image.Stride);
        Assert.All(image.Y.ToArray(), static x => Assert.Equal(0, x));
        Assert.All(image.Uv.ToArray(), static x => Assert.Equal(128, x));
    }

    [Fact]
    public void FullRangeNv12IsCopied()
    {
        // Arrange
        var source = GC.AllocateArray<byte>(Width * Height * 3 / 2, true);
        TestData.CreateRandom(source.Length, 1).CopyTo(source, 0);
        var frame = CreateFrame(source, VideoPixelFormat.Nv12, VideoColorRange.Full, new FramePlane(0, Width, Height, Width), new FramePlane(Width * Height, Width, Height / 2, Width));

        // Act (the image is drawn on, the frame must not change)
        using var image = Nv12Image.FromFrame(in frame);
        var expected = source.ToArray();
        image!.Y.Clear();

        // Assert
        Assert.Equal(expected, source);
        Assert.Equal(expected.AsSpan(Width * Height).ToArray(), image.Uv.ToArray());
    }

    [Fact]
    public void Bgra8IsNotSupported()
    {
        // Arrange
        var source = GC.AllocateArray<byte>(Width * Height * 4, true);
        var frame = CreateFrame(source, VideoPixelFormat.Bgra8, VideoColorRange.Unknown, new FramePlane(0, Width, Height, Width * 4), default);

        // Act
        using var image = Nv12Image.FromFrame(in frame);

        // Assert
        Assert.Null(image);
    }

    [Fact]
    public void ChromaOf420IsInterleaved()
    {
        // Arrange (separate planes of 2x2 samples)
        using var image = new Nv12Image(4, 4);

        // Act
        image.WriteChroma([100, 110, 120, 130], [140, 150, 160, 170], 2, 1, 2, 2);

        // Assert
        Assert.Equal([100, 140, 110, 150, 120, 160, 130, 170], image.Uv.ToArray());
    }

    [Fact]
    public void InterleavedChromaIsCopied()
    {
        // Arrange (CbCr pairs, 2 bytes of padding in each line)
        using var image = new Nv12Image(4, 4);
        byte[] chroma = [100, 140, 110, 150, 0, 0, 120, 160, 130, 170, 0, 0];

        // Act
        image.WriteChroma(chroma, chroma.AsSpan(1), 6, 2, 2, 2);

        // Assert
        Assert.Equal([100, 140, 110, 150, 120, 160, 130, 170], image.Uv.ToArray());
    }

    [Fact]
    public void ChromaOf444IsAveraged()
    {
        // Arrange (4x2 samples)
        using var image = new Nv12Image(4, 2);
        byte[] cb = [120, 122, 124, 126, 128, 130, 132, 134];
        byte[] cr = [140, 142, 144, 146, 148, 150, 152, 154];

        // Act
        image.WriteChroma(cb, cr, 4, 1, 4, 2);

        // Assert (2x2 samples each)
        Assert.Equal([125, 145, 129, 149], image.Uv.ToArray());
    }

    [Fact]
    public void ChromaOf422IsAveragedVertically()
    {
        // Arrange (2x2 samples for 4x2 pixels)
        using var image = new Nv12Image(4, 2);

        // Act
        image.WriteChroma([100, 110, 120, 130], [140, 150, 160, 170], 2, 1, 2, 2);

        // Assert
        Assert.Equal([110, 150, 120, 160], image.Uv.ToArray());
    }

    [Fact]
    public void LastChromaOfOddSizeIsRepeated()
    {
        // Arrange (3x3 samples for 3x3 pixels, the stride of the image is 4)
        using var image = new Nv12Image(3, 3);
        byte[] cb = [100, 110, 120, 130, 140, 150, 160, 170, 180];

        // Act
        image.WriteChroma(cb, cb, 3, 1, 3, 3);

        // Assert (the last column and row cover one sample)
        Assert.Equal([120, 120, 135, 135, 165, 165, 180, 180], image.Uv.ToArray());
    }

    [Fact]
    public void ChromaOfOtherSizeIsNotWritten()
    {
        // Arrange
        using var image = new Nv12Image(4, 4);
        var chroma = new byte[16];

        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => image.WriteChroma(chroma, chroma, 4, 1, 3, 2));
    }

    [Fact]
    public void DisposedImageIsNotUsed()
    {
        // Arrange
        var image = new Nv12Image(Width, Height);

        // Act (the second call does nothing)
        image.Dispose();
        image.Dispose();

        // Assert
        Assert.Throws<ObjectDisposedException>(() => image.Y.Length);
    }

    //--------------------------------------------------------------------------------
    // Helper
    //--------------------------------------------------------------------------------

    // The source must be pinned
    private static FrameBuffer CreateFrame(byte[] source, VideoPixelFormat format, VideoColorRange range, FramePlane plane0, FramePlane plane1) =>
        new(Marshal.UnsafeAddrOfPinnedArrayElement(source, 0), source.Length, format, range, Width, Height, plane0, plane1, 1, TimeSpan.Zero, TimeSpan.Zero);
}
