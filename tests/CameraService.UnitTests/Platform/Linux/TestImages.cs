namespace CameraService.Platform.Linux;

using System.Runtime.InteropServices;

// JPEG of TurboJPEG with its own color conversion, independent of the planes of the code under test
internal static partial class TestImages
{
    private const string Library = "libturbojpeg.so.0";

    // TJPF_BGRA
    private const int PixelFormatBgra = 8;

    private const int Quality = 90;

    public static void SkipUnlessInstalled() => Assert.SkipUnless(TurboJpeg.IsAvailable, $"{Library} is not installed");

    // JPEG of one color in the subsampling (TJSAMP)
    public static byte[] CreateJpeg(int width, int height, byte blue, byte green, byte red, int subsampling)
    {
        var bgra = new byte[width * height * JpegTestData.BytesPerPixel];
        for (var i = 0; i < bgra.Length; i += JpegTestData.BytesPerPixel)
        {
            bgra[i] = blue;
            bgra[i + 1] = green;
            bgra[i + 2] = red;
            bgra[i + 3] = 255;
        }

        var handle = TurboJpeg.CreateCompressor();
        var jpeg = IntPtr.Zero;
        try
        {
            var size = default(CULong);
            Assert.Equal(0, Compress(handle, bgra, width, 0, height, PixelFormatBgra, ref jpeg, ref size, subsampling, Quality, 0));
            var result = new byte[(int)size.Value];
            Marshal.Copy(jpeg, result, 0, result.Length);
            return result;
        }
        finally
        {
            Free(jpeg);
            _ = TurboJpeg.Destroy(handle);
        }
    }

    // Decoded into BGRA
    public static (int Width, int Height, byte[] Bgra) Decode(byte[] jpeg)
    {
        var handle = TurboJpeg.CreateDecompressor();
        try
        {
            var size = new CULong((nuint)jpeg.Length);
            Assert.Equal(0, DecompressHeader(handle, jpeg, size, out var width, out var height, out _, out _));
            var bgra = new byte[width * height * JpegTestData.BytesPerPixel];
            Assert.Equal(0, Decompress(handle, jpeg, size, bgra, width, 0, height, PixelFormatBgra, 0));
            return (width, height, bgra);
        }
        finally
        {
            _ = TurboJpeg.Destroy(handle);
        }
    }

    [LibraryImport(Library, EntryPoint = "tjCompress2")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static partial int Compress(IntPtr handle, byte[] source, int width, int pitch, int height, int pixelFormat, ref IntPtr jpeg, ref CULong jpegSize, int subsampling, int quality, int flags);

    [LibraryImport(Library, EntryPoint = "tjDecompressHeader3")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static partial int DecompressHeader(IntPtr handle, byte[] jpeg, CULong jpegSize, out int width, out int height, out int subsampling, out int colorspace);

    [LibraryImport(Library, EntryPoint = "tjDecompress2")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static partial int Decompress(IntPtr handle, byte[] jpeg, CULong jpegSize, [Out] byte[] destination, int width, int pitch, int height, int pixelFormat, int flags);

    [LibraryImport(Library, EntryPoint = "tjFree")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static partial void Free(IntPtr buffer);
}
