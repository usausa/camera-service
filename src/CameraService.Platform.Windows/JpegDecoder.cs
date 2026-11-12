namespace CameraService.Platform.Windows;

using System.Buffers;
using System.Runtime.InteropServices;

using CameraService.Core;

// WIC JPEG decoder called through the COM vtable, into full range NV12 without color conversion: the decoder gives YCbCr planes
// (IWICPlanarBitmapSourceTransform), scaled down (1/2 to 1/8) while it decodes when a smaller width is asked for.
// The chroma comes at the size of the luma or a half of it in either direction, it is averaged into 4:2:0. Used by one thread.
internal sealed unsafe class JpegDecoder : IJpegDecoder
{
    // IWICBitmapDecoder
    private const int GetFrameSlot = 13;
    // IWICBitmapSource
    private const int GetSizeSlot = 3;
    // IWICPlanarBitmapSourceTransform
    private const int DoesSupportTransformSlot = 3;
    private const int PlanarCopyPixelsSlot = 4;

    private const int DecodeMetadataCacheOnDemand = 0;
    private const int TransformRotate0 = 0;
    private const int PlanarOptionsDefault = 0;
    private const int PlaneCount = 2;

    private static readonly Guid IidPlanarSourceTransform = new("3aff9cce-be95-4303-b927-e7d16ff4a613");

    private IntPtr factory = Wic.CreateFactory();

    public void Dispose()
    {
        if (factory != IntPtr.Zero)
        {
            Marshal.Release(factory);
            factory = IntPtr.Zero;
        }
    }

    public Nv12Image? Decode(ReadOnlySpan<byte> jpeg) => Decode(jpeg, int.MaxValue);

    // The planes of the nearest scale the decoder supports for the width (at most the size of the image), null when the data cannot be decoded so
    public Nv12Image? Decode(ReadOnlySpan<byte> jpeg, int width)
    {
        ObjectDisposedException.ThrowIf(factory == IntPtr.Zero, this);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        fixed (byte* source = jpeg)
        {
            return Decode(source, jpeg.Length, width);
        }
    }

    private Nv12Image? Decode(byte* source, int length, int width)
    {
        var stream = IntPtr.Zero;
        var decoder = IntPtr.Zero;
        var frame = IntPtr.Zero;
        var transform = IntPtr.Zero;
        try
        {
            // IWICImagingFactory.CreateDecoderFromStream, IWICBitmapDecoder.GetFrame, IWICBitmapSource.GetSize
            uint sourceWidth;
            uint sourceHeight;
            if ((Wic.CreateMemoryStream(factory, source, length, out stream) < 0) ||
                (((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, Guid*, int, IntPtr*, int>)Com.Vtbl(factory)[Wic.CreateDecoderFromStreamSlot])(factory, stream, null, DecodeMetadataCacheOnDemand, &decoder) < 0) ||
                (((delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr*, int>)Com.Vtbl(decoder)[GetFrameSlot])(decoder, 0, &frame) < 0) ||
                (((delegate* unmanaged[Stdcall]<IntPtr, uint*, uint*, int>)Com.Vtbl(frame)[GetSizeSlot])(frame, &sourceWidth, &sourceHeight) < 0) ||
                (Marshal.QueryInterface(frame, in IidPlanarSourceTransform, out transform) < 0))
            {
                return null;
            }

            // IWICPlanarBitmapSourceTransform.DoesSupportTransform changes the size to the nearest one it supports
            var scaledWidth = Math.Min((uint)width, sourceWidth);
            var scaledHeight = Math.Max(1u, (uint)((ulong)sourceHeight * scaledWidth / sourceWidth));
            var formats = stackalloc Guid[PlaneCount];
            formats[0] = Wic.PixelFormat8BppY;
            formats[1] = Wic.PixelFormat16BppCbCr;
            var descriptions = stackalloc PlaneDescription[PlaneCount];
            int supported;
            if ((((delegate* unmanaged[Stdcall]<IntPtr, uint*, uint*, int, int, Guid*, PlaneDescription*, uint, int*, int>)Com.Vtbl(transform)[DoesSupportTransformSlot])(transform, &scaledWidth, &scaledHeight, TransformRotate0, PlanarOptionsDefault, formats, descriptions, PlaneCount, &supported) < 0) ||
                (supported == 0))
            {
                return null;
            }

            // The chroma plane has the size of the luma, or a half of it in either direction
            var chromaWidth = (int)descriptions[1].Width;
            var chromaHeight = (int)descriptions[1].Height;
            if ((descriptions[0].Width != scaledWidth) || (descriptions[0].Height != scaledHeight) ||
                ((chromaWidth != scaledWidth) && (chromaWidth != (scaledWidth + 1) / 2)) ||
                ((chromaHeight != scaledHeight) && (chromaHeight != (scaledHeight + 1) / 2)))
            {
                return null;
            }

            var image = new Nv12Image((int)scaledWidth, (int)scaledHeight);
            var chromaStride = chromaWidth * 2;
            var chromaSize = chromaStride * chromaHeight;
            var chroma = ArrayPool<byte>.Shared.Rent(chromaSize);
            try
            {
                // IWICPlanarBitmapSourceTransform.CopyPixels
                int result;
                fixed (byte* y = image.Y)
                {
                    fixed (byte* uv = chroma)
                    {
                        var planes = stackalloc Wic.BitmapPlane[PlaneCount];
                        planes[0] = new Wic.BitmapPlane { Format = Wic.PixelFormat8BppY, Buffer = y, Stride = (uint)image.Stride, BufferSize = (uint)image.Y.Length };
                        planes[1] = new Wic.BitmapPlane { Format = Wic.PixelFormat16BppCbCr, Buffer = uv, Stride = (uint)chromaStride, BufferSize = (uint)chromaSize };
                        result = ((delegate* unmanaged[Stdcall]<IntPtr, void*, uint, uint, int, int, Wic.BitmapPlane*, uint, int>)Com.Vtbl(transform)[PlanarCopyPixelsSlot])(transform, null, scaledWidth, scaledHeight, TransformRotate0, PlanarOptionsDefault, planes, PlaneCount);
                    }
                }

                if (result < 0)
                {
                    image.Dispose();
                    return null;
                }

                // Interleaved CbCr
                image.WriteChroma(chroma.AsSpan(0, chromaSize), chroma.AsSpan(1, chromaSize - 1), chromaStride, 2, chromaWidth, chromaHeight);
                return image;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(chroma);
            }
        }
        finally
        {
            Com.Release(transform);
            Com.Release(frame);
            Com.Release(decoder);
            Com.Release(stream);
        }
    }

    // WICBitmapPlaneDescription
    [StructLayout(LayoutKind.Sequential)]
    private struct PlaneDescription
    {
        public Guid Format;
        public uint Width;
        public uint Height;
    }
}
