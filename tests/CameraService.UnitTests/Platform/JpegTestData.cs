namespace CameraService.Platform;

using CameraService.Core;

// Full range NV12 of the JPEG tests of the platforms, and the reference of the decoded images.
// The stride of the luma is the width, the stride of the chroma the width rounded up to even.
internal static class JpegTestData
{
    public const int BytesPerPixel = 4;

    public static int GetUvStride(int width) => ((width + 1) / 2) * 2;

    public static (byte[] Y, byte[] Uv) CreateGradient(int width, int height)
    {
        var uvStride = GetUvStride(width);
        var uvRows = (height + 1) / 2;
        var y = new byte[width * height];
        var uv = new byte[uvStride * uvRows];
        for (var row = 0; row < height; row++)
        {
            for (var x = 0; x < width; x++)
            {
                y[(row * width) + x] = (byte)(32 + ((x * 160) / width) + ((row * 32) / height));
            }
        }

        for (var row = 0; row < uvRows; row++)
        {
            for (var x = 0; x < uvStride; x += 2)
            {
                uv[(row * uvStride) + x] = (byte)(80 + ((row * 96) / uvRows));
                uv[(row * uvStride) + x + 1] = (byte)(176 - ((x * 96) / uvStride));
            }
        }

        return (y, uv);
    }

    public static (byte[] Y, byte[] Uv) CreateSolid(int width, int height, byte luma, byte u, byte v)
    {
        var y = Enumerable.Repeat(luma, width * height).ToArray();
        var uv = new byte[GetUvStride(width) * ((height + 1) / 2)];
        for (var i = 0; i < uv.Length; i += 2)
        {
            uv[i] = u;
            uv[i + 1] = v;
        }

        return (y, uv);
    }

    public static byte[] Encode(IJpegEncoder encoder, (byte[] Y, byte[] Uv) planes, int width, int height)
    {
        var destination = new byte[encoder.GetMaxLength(width, height)];
        Assert.True(encoder.TryEncodeNv12(planes.Y, width, planes.Uv, GetUvStride(width), width, height, destination, out var written));
        return destination[..written];
    }

    // The JPEG without its Huffman tables (DHT), as a frame of an MJPEG camera
    public static byte[] RemoveHuffmanTables(byte[] jpeg)
    {
        var result = new List<byte>(jpeg.Length);
        result.AddRange(jpeg.AsSpan(0, 2));
        var position = 2;
        while (jpeg[position + 1] != 0xDA)
        {
            var length = 2 + ((jpeg[position + 2] << 8) | jpeg[position + 3]);
            if (jpeg[position + 1] != 0xC4)
            {
                result.AddRange(jpeg.AsSpan(position, length));
            }

            position += length;
        }

        result.AddRange(jpeg.AsSpan(position));
        Assert.True(result.Count < jpeg.Length);
        return [.. result];
    }

    // Full range BT.601 (JFIF), the reference of the decoded image
    public static byte[] ToBgra(byte[] y, byte[] uv, int width, int height)
    {
        var uvStride = GetUvStride(width);
        var bgra = new byte[width * height * BytesPerPixel];
        for (var row = 0; row < height; row++)
        {
            for (var x = 0; x < width; x++)
            {
                var luma = y[(row * width) + x];
                var chroma = ((row / 2) * uvStride) + ((x / 2) * 2);
                var u = uv[chroma] - 128;
                var v = uv[chroma + 1] - 128;
                var offset = ((row * width) + x) * BytesPerPixel;
                bgra[offset] = Clamp(luma + (1.772 * u));
                bgra[offset + 1] = Clamp(luma - (0.344136 * u) - (0.714136 * v));
                bgra[offset + 2] = Clamp(luma + (1.402 * v));
                bgra[offset + 3] = 255;
            }
        }

        return bgra;
    }

    // Each sample of a decoded image of one color is within 2 of the color
    public static void AssertColor(Nv12Image image, int luma, int u, int v)
    {
        const int tolerance = 2;
        var y = image.Y.ToArray();
        var uv = image.Uv.ToArray();
        for (var row = 0; row < image.Height; row++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                Assert.InRange(y[(row * image.Stride) + x], luma - tolerance, luma + tolerance);
            }
        }

        for (var row = 0; row < (image.Height + 1) / 2; row++)
        {
            for (var x = 0; x < (image.Width + 1) / 2; x++)
            {
                var offset = (row * image.Stride) + (x * 2);
                Assert.InRange(uv[offset], u - tolerance, u + tolerance);
                Assert.InRange(uv[offset + 1], v - tolerance, v + tolerance);
            }
        }
    }

    // RGB channels only
    public static double CalculatePsnr(byte[] expected, byte[] actual)
    {
        double sum = 0;
        var count = 0;
        for (var i = 0; i < expected.Length; i += BytesPerPixel)
        {
            for (var c = 0; c < 3; c++)
            {
                var difference = expected[i + c] - actual[i + c];
                sum += difference * difference;
                count++;
            }
        }

        var mse = sum / count;
        return mse == 0 ? double.PositiveInfinity : 10 * Math.Log10(255.0 * 255.0 / mse);
    }

    private static byte Clamp(double value) => (byte)Math.Clamp(Math.Round(value), 0, 255);
}
