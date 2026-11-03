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
