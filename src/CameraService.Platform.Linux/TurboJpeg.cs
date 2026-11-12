namespace CameraService.Platform.Linux;

using System.Runtime.InteropServices;

// TurboJPEG of libjpeg-turbo (libturbojpeg.so.0 of the OS), the functions of the 2.x API that 3.x also has
internal static partial class TurboJpeg
{
    // TJSAMP
    public const int Subsampling444 = 0;
    public const int Subsampling422 = 1;
    public const int Subsampling420 = 2;
    public const int SubsamplingGray = 3;
    public const int Subsampling440 = 4;

    // TJCS
    public const int ColorspaceYCbCr = 1;
    public const int ColorspaceGray = 2;

    // TJFLAG_NOREALLOC: the output is written to the given buffer of BufferSize bytes
    public const int NoReallocation = 1024;

    private const string Library = "libturbojpeg.so.0";

    // TJERR_WARNING: the data has errors, but the output is complete
    private const int ErrorWarning = 0;

    private static readonly Lazy<bool> Loaded = new(static () => NativeLibrary.TryLoad(Library, out _));

    public static bool IsAvailable => Loaded.Value;

    public static IntPtr CreateCompressor() => Check(IsAvailable ? InitCompress() : IntPtr.Zero);

    public static IntPtr CreateDecompressor() => Check(IsAvailable ? InitDecompress() : IntPtr.Zero);

    // Message of the last error of the handle
    public static string GetErrorMessage(IntPtr handle) => Marshal.PtrToStringUTF8(GetErrorString(handle)) ?? "TurboJPEG failed.";

    // The last error of the handle was a warning
    public static bool IsWarning(IntPtr handle) => GetErrorCode(handle) == ErrorWarning;

    private static IntPtr Check(IntPtr handle) =>
        handle != IntPtr.Zero ? handle : throw new InvalidOperationException(IsAvailable ? "TurboJPEG initialization failed." : $"{Library} is not installed.");

    [LibraryImport(Library, EntryPoint = "tjInitCompress")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static partial IntPtr InitCompress();

    [LibraryImport(Library, EntryPoint = "tjInitDecompress")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static partial IntPtr InitDecompress();

    [LibraryImport(Library, EntryPoint = "tjDestroy")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    public static partial int Destroy(IntPtr handle);

    [LibraryImport(Library, EntryPoint = "tjBufSize")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    public static partial CULong BufferSize(int width, int height, int subsampling);

    [LibraryImport(Library, EntryPoint = "tjPlaneWidth")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    public static partial int PlaneWidth(int component, int width, int subsampling);

    [LibraryImport(Library, EntryPoint = "tjPlaneHeight")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    public static partial int PlaneHeight(int component, int height, int subsampling);

    [LibraryImport(Library, EntryPoint = "tjCompressFromYUVPlanes")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    public static partial int CompressFromYuvPlanes(IntPtr handle, byte** planes, int width, int* strides, int height, int subsampling, byte** jpeg, CULong* jpegSize, int quality, int flags);

    [LibraryImport(Library, EntryPoint = "tjDecompressHeader3")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    public static partial int DecompressHeader(IntPtr handle, byte* jpeg, CULong jpegSize, int* width, int* height, int* subsampling, int* colorspace);

    [LibraryImport(Library, EntryPoint = "tjDecompressToYUVPlanes")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    public static partial int DecompressToYuvPlanes(IntPtr handle, byte* jpeg, CULong jpegSize, byte** planes, int width, int* strides, int height, int flags);

    [LibraryImport(Library, EntryPoint = "tjGetErrorStr2")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static partial IntPtr GetErrorString(IntPtr handle);

    [LibraryImport(Library, EntryPoint = "tjGetErrorCode")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static partial int GetErrorCode(IntPtr handle);
}
