namespace CameraService.Platform.Windows;

using System.Runtime.InteropServices;

// COM called through the vtable
internal static unsafe class Com
{
    public static void** Vtbl(IntPtr pointer) => *(void***)pointer;

    public static void Release(IntPtr pointer)
    {
        if (pointer != IntPtr.Zero)
        {
            Marshal.Release(pointer);
        }
    }
}
