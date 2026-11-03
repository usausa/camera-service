namespace CameraService.Core;

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

public sealed class Nv12PlanesTests
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
        var planes = new Nv12Planes();

        // Act
        var result = planes.TryGet(in frame, out var y, out var yStride, out var uv, out var uvStride);

        // Assert
        Assert.True(result);
        Assert.Equal(Width, yStride);
        Assert.Equal(Width, uvStride);
        Assert.All(y[..(Width * Height)].ToArray(), static x => Assert.Equal(0, x));
        Assert.All(uv[..(Width * Height / 2)].ToArray(), static x => Assert.Equal(128, x));
    }

    [Fact]
    public void FullRangeNv12IsPassedWithoutCopy()
    {
        // Arrange
        var source = GC.AllocateArray<byte>(Width * Height * 3 / 2, true);
        var frame = CreateFrame(source, VideoPixelFormat.Nv12, VideoColorRange.Full, new FramePlane(0, Width, Height, Width), new FramePlane(Width * Height, Width, Height / 2, Width));
        var planes = new Nv12Planes();

        // Act
        var result = planes.TryGet(in frame, out var y, out _, out var uv, out _);

        // Assert (the planes point to the frame)
        Assert.True(result);
        Assert.True(Unsafe.AreSame(ref MemoryMarshal.GetReference(y), ref source[0]));
        Assert.True(Unsafe.AreSame(ref MemoryMarshal.GetReference(uv), ref source[Width * Height]));
    }

    [Fact]
    public void Bgra8IsNotSupported()
    {
        // Arrange
        var source = GC.AllocateArray<byte>(Width * Height * 4, true);
        var frame = CreateFrame(source, VideoPixelFormat.Bgra8, VideoColorRange.Unknown, new FramePlane(0, Width, Height, Width * 4), default);
        var planes = new Nv12Planes();

        // Act
        var result = planes.TryGet(in frame, out _, out _, out _, out _);

        // Assert
        Assert.False(result);
    }

    //--------------------------------------------------------------------------------
    // Helper
    //--------------------------------------------------------------------------------

    // The source must be pinned
    private static FrameBuffer CreateFrame(byte[] source, VideoPixelFormat format, VideoColorRange range, FramePlane plane0, FramePlane plane1) =>
        new(Marshal.UnsafeAddrOfPinnedArrayElement(source, 0), source.Length, format, range, Width, Height, plane0, plane1, 1, TimeSpan.Zero, TimeSpan.Zero);
}
