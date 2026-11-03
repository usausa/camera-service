namespace CameraService.Core;

using System.Runtime.Intrinsics.X86;

public sealed class ImageConverterTests
{
    //--------------------------------------------------------------------------------
    // YUY2
    //--------------------------------------------------------------------------------

    [Theory]
    [InlineData(2, 1, 0)]
    [InlineData(2, 2, 0)]
    [InlineData(16, 2, 0)]
    [InlineData(18, 3, 0)]
    [InlineData(33, 5, 3)]
    [InlineData(48, 4, 8)]
    [InlineData(640, 480, 0)]
    [InlineData(642, 7, 32)]
    public void Yuy2ToNv12SimdMatchesScalar(int width, int height, int padding)
    {
        Assert.SkipUnless(Ssse3.IsSupported, "SSSE3 is not supported.");

        // Arrange
        var sourceStride = (((width + 1) / 2) * 4) + padding;
        var yStride = width + padding;
        var uvStride = (((width + 1) / 2) * 2) + padding;
        var source = TestData.CreateRandom(sourceStride * height, 3);

        foreach (var range in Enum.GetValues<YuvRange>())
        {
            var expectedY = new byte[yStride * height];
            var expectedUv = new byte[uvStride * ((height + 1) / 2)];
            var actualY = new byte[expectedY.Length];
            var actualUv = new byte[expectedUv.Length];

            // Act
            ImageConverter.Yuy2ToNv12(source, sourceStride, expectedY, yStride, expectedUv, uvStride, width, height, range, false);
            ImageConverter.Yuy2ToNv12(source, sourceStride, actualY, yStride, actualUv, uvStride, width, height, range, true);

            // Assert
            Assert.Equal(expectedY, actualY);
            Assert.Equal(expectedUv, actualUv);
        }
    }

    [Theory]
    [InlineData(16, 128, 0, 128)]
    [InlineData(235, 16, 255, 1)]
    [InlineData(126, 240, 128, 255)]
    [InlineData(0, 0, 0, 0)]
    [InlineData(255, 255, 255, 255)]
    public void Yuy2ToNv12ExpandsLimitedRange(byte y, byte chroma, byte expectedY, byte expectedChroma)
    {
        // Arrange
        const int width = 32;
        const int height = 2;
        var source = Enumerable.Range(0, width * 2 * height).Select(i => (i & 1) == 0 ? y : chroma).ToArray();
        var yPlane = new byte[width * height];
        var uvPlane = new byte[width];

        // Act
        ImageConverter.Yuy2ToNv12(source, width * 2, yPlane, width, uvPlane, width, width, height, YuvRange.Limited);

        // Assert
        Assert.All(yPlane, x => Assert.Equal(expectedY, x));
        Assert.All(uvPlane, x => Assert.Equal(expectedChroma, x));
    }

    [Fact]
    public void Yuy2ToNv12AveragesChromaOfTwoRows()
    {
        // Arrange
        const int width = 32;
        const int height = 2;
        var source = new byte[width * 2 * height];
        for (var row = 0; row < height; row++)
        {
            for (var x = 0; x < width; x += 2)
            {
                var offset = (row * width * 2) + (x * 2);
                source[offset] = (byte)x;
                source[offset + 1] = row == 0 ? (byte)100 : (byte)201;
                source[offset + 2] = (byte)(x + 1);
                source[offset + 3] = row == 0 ? (byte)50 : (byte)60;
            }
        }

        var yPlane = new byte[width * height];
        var uvPlane = new byte[width];

        // Act
        ImageConverter.Yuy2ToNv12(source, width * 2, yPlane, width, uvPlane, width, width, height, YuvRange.Full);

        // Assert
        for (var x = 0; x < width; x++)
        {
            Assert.Equal(x, yPlane[x]);
            Assert.Equal(x, yPlane[width + x]);
        }

        for (var x = 0; x < width; x += 2)
        {
            Assert.Equal(151, uvPlane[x]);
            Assert.Equal(55, uvPlane[x + 1]);
        }
    }

    //--------------------------------------------------------------------------------
    // NV12
    //--------------------------------------------------------------------------------

    [Theory]
    [InlineData(1, 1, 0)]
    [InlineData(16, 2, 0)]
    [InlineData(33, 5, 3)]
    [InlineData(640, 480, 0)]
    [InlineData(642, 7, 32)]
    public void Nv12ToFullRangeSimdMatchesScalar(int width, int height, int padding)
    {
        Assert.SkipUnless(Ssse3.IsSupported, "SSSE3 is not supported.");

        // Arrange
        var yStride = width + padding;
        var uvStride = (((width + 1) / 2) * 2) + padding;
        var uvLength = uvStride * ((height + 1) / 2);
        var y = TestData.CreateRandom(yStride * height, 4);
        var uv = TestData.CreateRandom(uvLength, 5);
        var expectedY = new byte[y.Length];
        var expectedUv = new byte[uvLength];
        var actualY = new byte[y.Length];
        var actualUv = new byte[uvLength];

        // Act
        ImageConverter.Nv12ToFullRange(y, yStride, uv, uvStride, expectedY, yStride, expectedUv, uvStride, width, height, false);
        ImageConverter.Nv12ToFullRange(y, yStride, uv, uvStride, actualY, yStride, actualUv, uvStride, width, height, true);

        // Assert
        Assert.Equal(expectedY, actualY);
        Assert.Equal(expectedUv, actualUv);
    }

    [Theory]
    [InlineData(16, 128, 0, 128)]
    [InlineData(235, 16, 255, 1)]
    [InlineData(126, 240, 128, 255)]
    public void Nv12ToFullRangeExpandsKnownValue(byte y, byte chroma, byte expectedY, byte expectedChroma)
    {
        // Arrange
        const int width = 32;
        const int height = 2;
        var yPlane = Enumerable.Repeat(y, width * height).ToArray();
        var uvPlane = Enumerable.Repeat(chroma, width).ToArray();
        var destinationY = new byte[yPlane.Length];
        var destinationUv = new byte[uvPlane.Length];

        // Act
        ImageConverter.Nv12ToFullRange(yPlane, width, uvPlane, width, destinationY, width, destinationUv, width, width, height);

        // Assert
        Assert.All(destinationY, x => Assert.Equal(expectedY, x));
        Assert.All(destinationUv, x => Assert.Equal(expectedChroma, x));
    }

    //--------------------------------------------------------------------------------
    // Validation
    //--------------------------------------------------------------------------------

    [Fact]
    public void Yuy2ToNv12ThrowsWhenUvPlaneIsTooSmall()
    {
        var source = new byte[16 * 2 * 2];
        var y = new byte[16 * 2];
        var uv = new byte[16 - 1];

        Assert.Throws<ArgumentException>(() => ImageConverter.Yuy2ToNv12(source, 16 * 2, y, 16, uv, 16, 16, 2, YuvRange.Limited));
    }

    [Fact]
    public void Nv12ToFullRangeThrowsWhenDestinationIsTooSmall()
    {
        var y = new byte[16 * 2];
        var uv = new byte[16];
        var destinationY = new byte[(16 * 2) - 1];
        var destinationUv = new byte[16];

        Assert.Throws<ArgumentException>(() => ImageConverter.Nv12ToFullRange(y, 16, uv, 16, destinationY, 16, destinationUv, 16, 16, 2));
    }
}
