namespace CameraService.Platform.Windows;

using System.Runtime.InteropServices;

using CameraService.Core;

// WIC JPEG encoder called through the COM vtable.
// Full range NV12 is passed as YCbCr planes without color conversion, and the data is written to the given buffer.
public sealed unsafe class JpegEncoder : IJpegEncoder
{
    // IWICBitmapEncoder
    private const int EncoderInitializeSlot = 3;
    private const int CreateNewFrameSlot = 10;
    private const int EncoderCommitSlot = 11;
    // IPropertyBag2
    private const int WriteSlot = 4;
    // IWICBitmapFrameEncode
    private const int FrameInitializeSlot = 3;
    private const int SetSizeSlot = 4;
    private const int SetResolutionSlot = 5;
    private const int SetPixelFormatSlot = 6;
    private const int FrameCommitSlot = 12;
    // IWICPlanarBitmapFrameEncode
    private const int WritePixelsSlot = 3;

    private const int EncoderNoCache = 2;
    private const ushort VariantSingle = 4;
    private const ushort VariantByte = 17;
    private const byte Subsampling420 = 1;
    private const int PlaneCount = 2;
    private const double Dpi = 96;
    private const int HeaderAllowance = 4096;

    private static readonly Guid IidPlanarFrameEncode = new("f928b7b8-2221-40c1-b72e-7e82f1974d1a");
    private static readonly Guid ContainerFormatJpeg = new("19e4a5aa-5662-4fc5-a0c0-1758028e1057");

    private readonly float quality;

    private IntPtr factory;

    public JpegEncoder(int quality)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(quality, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(quality, 100);
        this.quality = quality / 100f;
        factory = Wic.CreateFactory();
    }

    public void Dispose()
    {
        if (factory != IntPtr.Zero)
        {
            Marshal.Release(factory);
            factory = IntPtr.Zero;
        }
    }

    // Upper bound of the encoded size
    public int GetMaxLength(int width, int height) => (width * height * 3) + HeaderAllowance;

    // Encodes full range NV12 as 4:2:0 JPEG, returns false when the destination is too small
    public bool TryEncodeNv12(ReadOnlySpan<byte> y, int yStride, ReadOnlySpan<byte> uv, int uvStride, int width, int height, Span<byte> destination, out int written)
    {
        ObjectDisposedException.ThrowIf(factory == IntPtr.Zero, this);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        var uvRowBytes = ((width + 1) / 2) * 2;
        var uvRows = (height + 1) / 2;
        if ((yStride < width) || (y.Length < ((height - 1) * yStride) + width))
        {
            throw new ArgumentException("Invalid Y plane.", nameof(y));
        }

        if ((uvStride < uvRowBytes) || (uv.Length < ((uvRows - 1) * uvStride) + uvRowBytes))
        {
            throw new ArgumentException("Invalid UV plane.", nameof(uv));
        }

        int result;
        int length;
        fixed (byte* yPointer = y)
        {
            fixed (byte* uvPointer = uv)
            {
                fixed (byte* destinationPointer = destination)
                {
                    var planes = stackalloc Wic.BitmapPlane[PlaneCount];
                    planes[0] = new Wic.BitmapPlane { Format = Wic.PixelFormat8BppY, Buffer = yPointer, Stride = (uint)yStride, BufferSize = (uint)y.Length };
                    planes[1] = new Wic.BitmapPlane { Format = Wic.PixelFormat16BppCbCr, Buffer = uvPointer, Stride = (uint)uvStride, BufferSize = (uint)uv.Length };
                    result = Encode(planes, width, height, destinationPointer, destination.Length, out length);
                }
            }
        }

        if ((result >= 0) && IsComplete(destination, length))
        {
            written = length;
            return true;
        }

        // A full buffer is reported as an error or as truncated data
        if (destination.Length < GetMaxLength(width, height))
        {
            written = 0;
            return false;
        }

        Marshal.ThrowExceptionForHR(result);
        throw new InvalidOperationException("JPEG encoding failed.");
    }

    private int Encode(Wic.BitmapPlane* planes, int width, int height, byte* destination, int capacity, out int length)
    {
        length = 0;
        var stream = IntPtr.Zero;
        var encoder = IntPtr.Zero;
        var frame = IntPtr.Zero;
        var options = IntPtr.Zero;
        var planar = IntPtr.Zero;
        try
        {
            // IWICImagingFactory.CreateStream, IWICStream.InitializeFromMemory
            var result = Wic.CreateMemoryStream(factory, destination, capacity, out stream);
            if (result < 0)
            {
                return result;
            }

            // IWICImagingFactory.CreateEncoder, IWICBitmapEncoder.Initialize, IWICBitmapEncoder.CreateNewFrame
            var container = ContainerFormatJpeg;
            result = ((delegate* unmanaged[Stdcall]<IntPtr, Guid*, Guid*, IntPtr*, int>)Com.Vtbl(factory)[Wic.CreateEncoderSlot])(factory, &container, null, &encoder);
            if (result < 0)
            {
                return result;
            }

            result = ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int, int>)Com.Vtbl(encoder)[EncoderInitializeSlot])(encoder, stream, EncoderNoCache);
            if (result < 0)
            {
                return result;
            }

            result = ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, IntPtr*, int>)Com.Vtbl(encoder)[CreateNewFrameSlot])(encoder, &frame, &options);
            if (result < 0)
            {
                return result;
            }

            // IPropertyBag2.Write, IWICBitmapFrameEncode.Initialize / SetSize / SetResolution / SetPixelFormat
            result = WriteOptions(options);
            if (result < 0)
            {
                return result;
            }

            result = ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int>)Com.Vtbl(frame)[FrameInitializeSlot])(frame, options);
            if (result < 0)
            {
                return result;
            }

            result = ((delegate* unmanaged[Stdcall]<IntPtr, uint, uint, int>)Com.Vtbl(frame)[SetSizeSlot])(frame, (uint)width, (uint)height);
            if (result < 0)
            {
                return result;
            }

            result = ((delegate* unmanaged[Stdcall]<IntPtr, double, double, int>)Com.Vtbl(frame)[SetResolutionSlot])(frame, Dpi, Dpi);
            if (result < 0)
            {
                return result;
            }

            // The interleaved format decides the accepted planar formats
            var pixelFormat = Wic.PixelFormat24BppBgr;
            result = ((delegate* unmanaged[Stdcall]<IntPtr, Guid*, int>)Com.Vtbl(frame)[SetPixelFormatSlot])(frame, &pixelFormat);
            if (result < 0)
            {
                return result;
            }

            // IWICPlanarBitmapFrameEncode.WritePixels
            result = Marshal.QueryInterface(frame, in IidPlanarFrameEncode, out planar);
            if (result < 0)
            {
                return result;
            }

            result = ((delegate* unmanaged[Stdcall]<IntPtr, uint, Wic.BitmapPlane*, uint, int>)Com.Vtbl(planar)[WritePixelsSlot])(planar, (uint)height, planes, PlaneCount);
            if (result < 0)
            {
                return result;
            }

            // IWICBitmapFrameEncode.Commit, IWICBitmapEncoder.Commit
            result = ((delegate* unmanaged[Stdcall]<IntPtr, int>)Com.Vtbl(frame)[FrameCommitSlot])(frame);
            if (result < 0)
            {
                return result;
            }

            result = ((delegate* unmanaged[Stdcall]<IntPtr, int>)Com.Vtbl(encoder)[EncoderCommitSlot])(encoder);
            if (result < 0)
            {
                return result;
            }

            // IStream.Seek (current position = encoded length)
            ulong position;
            result = ((delegate* unmanaged[Stdcall]<IntPtr, long, uint, ulong*, int>)Com.Vtbl(stream)[Wic.SeekSlot])(stream, 0, Wic.StreamSeekCurrent, &position);
            if (result >= 0)
            {
                length = (int)position;
            }

            return result;
        }
        finally
        {
            Com.Release(planar);
            Com.Release(options);
            Com.Release(frame);
            Com.Release(encoder);
            Com.Release(stream);
        }
    }

    private int WriteOptions(IntPtr options)
    {
        var names = stackalloc PropertyBagEntry[2];
        var values = stackalloc Variant[2];
        fixed (char* qualityName = "ImageQuality")
        {
            fixed (char* subsamplingName = "JpegYCrCbSubsampling")
            {
                names[0] = new PropertyBagEntry { Name = qualityName };
                names[1] = new PropertyBagEntry { Name = subsamplingName };
                values[0] = new Variant { Type = VariantSingle, Single = quality };
                values[1] = new Variant { Type = VariantByte, Byte = Subsampling420 };
                return ((delegate* unmanaged[Stdcall]<IntPtr, uint, PropertyBagEntry*, Variant*, int>)Com.Vtbl(options)[WriteSlot])(options, 2, names, values);
            }
        }
    }

    // Ends with the EOI marker
    private static bool IsComplete(Span<byte> destination, int length) =>
        (length >= 4) && (length <= destination.Length) && (destination[length - 2] == 0xFF) && (destination[length - 1] == 0xD9);

    // PROPBAG2
    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyBagEntry
    {
        public uint Type;
        public ushort VariantType;
        public ushort ClipboardFormat;
        public uint Hint;
        public char* Name;
        public Guid Clsid;
    }

    // VARIANT (VT_R4 / VT_UI1 only)
    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct Variant
    {
        [FieldOffset(0)]
        public ushort Type;

        [FieldOffset(8)]
        public float Single;

        [FieldOffset(8)]
        public byte Byte;
    }
}
