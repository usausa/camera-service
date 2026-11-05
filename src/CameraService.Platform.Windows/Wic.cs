namespace CameraService.Platform.Windows;

using System.Runtime.InteropServices;

// WIC through the COM vtable: the imaging factory, the memory stream and the shared definitions
internal static unsafe partial class Wic
{
    // IWICImagingFactory
    public const int CreateDecoderFromStreamSlot = 4;
    public const int CreateEncoderSlot = 8;
    public const int CreateStreamSlot = 14;
    // IWICStream (IStream)
    public const int SeekSlot = 5;
    public const int InitializeFromMemorySlot = 16;

    public const uint StreamSeekCurrent = 1;

    private const uint ClsctxInprocServer = 1;

    public static readonly Guid PixelFormat24BppBgr = new("6fddc324-4e03-4bfe-b185-3d77768dc90c");
    public static readonly Guid PixelFormat8BppY = new("91b4db54-2df9-42f0-b449-2909bb3df88e");
    public static readonly Guid PixelFormat16BppCbCr = new("ff95ba6e-11e0-4263-bb45-01721f3460a4");

    private static readonly Guid ClsidImagingFactory = new("317d06e8-5f24-433d-bdf7-79ce68d8abc2");
    private static readonly Guid IidImagingFactory = new("ec5ec8a9-c395-4314-9c77-54d7a935ff70");

    // IWICImagingFactory, released by the caller
    public static IntPtr CreateFactory()
    {
        Marshal.ThrowExceptionForHR(CoCreateInstance(in ClsidImagingFactory, IntPtr.Zero, ClsctxInprocServer, in IidImagingFactory, out var factory));
        return factory;
    }

    // IWICImagingFactory.CreateStream, IWICStream.InitializeFromMemory (the memory must stay fixed while the stream is used)
    public static int CreateMemoryStream(IntPtr factory, byte* buffer, int length, out IntPtr stream)
    {
        IntPtr created;
        var result = ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int>)Com.Vtbl(factory)[CreateStreamSlot])(factory, &created);
        if (result < 0)
        {
            stream = IntPtr.Zero;
            return result;
        }

        result = ((delegate* unmanaged[Stdcall]<IntPtr, byte*, uint, int>)Com.Vtbl(created)[InitializeFromMemorySlot])(created, buffer, (uint)length);
        if (result < 0)
        {
            Com.Release(created);
            stream = IntPtr.Zero;
            return result;
        }

        stream = created;
        return result;
    }

    [LibraryImport("ole32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int CoCreateInstance(in Guid clsid, IntPtr outer, uint context, in Guid iid, out IntPtr pointer);

    // WICBitmapPlane
    [StructLayout(LayoutKind.Sequential)]
    public struct BitmapPlane
    {
        public Guid Format;
        public byte* Buffer;
        public uint Stride;
        public uint BufferSize;
    }
}
