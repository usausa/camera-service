namespace CameraService.Platform.Windows;

using System.Buffers;
using System.Runtime.InteropServices;

using CameraService.Core;

// Thumbnails with WIC called through the COM vtable: the JPEG decoder scales down (1/2 to 1/8) while it decodes into YCbCr planes
// (IWICPlanarBitmapSourceTransform), and the planes are encoded again without color conversion.
// The scaled chroma comes at the size of the luma, it is averaged into 4:2:0 for the encoder.
public sealed unsafe class ThumbnailGenerator : IThumbnailGenerator
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
    private const int Quality = 80;

    private static readonly Guid IidPlanarSourceTransform = new("3aff9cce-be95-4303-b927-e7d16ff4a613");

    public byte[]? Generate(ReadOnlySpan<byte> image, int width)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        Nv12Image? decoded;
        fixed (byte* source = image)
        {
            decoded = Decode(source, image.Length, width);
        }

        if (decoded is null)
        {
            return null;
        }

        using (decoded)
        {
            using var encoder = new JpegEncoder(Quality);
            var buffer = ArrayPool<byte>.Shared.Rent(encoder.GetMaxLength(decoded.Width, decoded.Height));
            try
            {
                return encoder.TryEncodeNv12(decoded.Y, decoded.Stride, decoded.Uv, decoded.Stride, decoded.Width, decoded.Height, buffer, out var written)
                    ? buffer.AsSpan(0, written).ToArray()
                    : null;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
    }

    // Planes of the nearest scale the decoder supports, null when the data cannot be decoded so
    private static Nv12Image? Decode(byte* source, int length, int width)
    {
        var factory = Wic.CreateFactory();
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
                (((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, Guid*, int, IntPtr*, int>)Wic.Vtbl(factory)[Wic.CreateDecoderFromStreamSlot])(factory, stream, null, DecodeMetadataCacheOnDemand, &decoder) < 0) ||
                (((delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr*, int>)Wic.Vtbl(decoder)[GetFrameSlot])(decoder, 0, &frame) < 0) ||
                (((delegate* unmanaged[Stdcall]<IntPtr, uint*, uint*, int>)Wic.Vtbl(frame)[GetSizeSlot])(frame, &sourceWidth, &sourceHeight) < 0) ||
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
            if ((((delegate* unmanaged[Stdcall]<IntPtr, uint*, uint*, int, int, Guid*, PlaneDescription*, uint, int*, int>)Wic.Vtbl(transform)[DoesSupportTransformSlot])(transform, &scaledWidth, &scaledHeight, TransformRotate0, PlanarOptionsDefault, formats, descriptions, PlaneCount, &supported) < 0) ||
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
            var chroma = ArrayPool<byte>.Shared.Rent(chromaStride * chromaHeight);
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
                        planes[1] = new Wic.BitmapPlane { Format = Wic.PixelFormat16BppCbCr, Buffer = uv, Stride = (uint)chromaStride, BufferSize = (uint)(chromaStride * chromaHeight) };
                        result = ((delegate* unmanaged[Stdcall]<IntPtr, void*, uint, uint, int, int, Wic.BitmapPlane*, uint, int>)Wic.Vtbl(transform)[PlanarCopyPixelsSlot])(transform, null, scaledWidth, scaledHeight, TransformRotate0, PlanarOptionsDefault, planes, PlaneCount);
                    }
                }

                if (result < 0)
                {
                    image.Dispose();
                    return null;
                }

                AverageChroma(chroma, chromaStride, chromaWidth, chromaHeight, image);
                return image;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(chroma);
            }
        }
        finally
        {
            Wic.Release(transform);
            Wic.Release(frame);
            Wic.Release(decoder);
            Wic.Release(stream);
            Wic.Release(factory);
        }
    }

    // Each sample of 4:2:0 covers 2x2 pixels: the average of the source samples that cover them (one sample when the source is already 4:2:0)
    private static void AverageChroma(ReadOnlySpan<byte> source, int sourceStride, int sourceWidth, int sourceHeight, Nv12Image image)
    {
        var horizontal = sourceWidth == image.Width ? 1 : 2;
        var vertical = sourceHeight == image.Height ? 1 : 2;
        var destination = image.Uv;
        var columns = (image.Width + 1) / 2;
        var rows = (image.Height + 1) / 2;
        for (var row = 0; row < rows; row++)
        {
            var top = source[(Math.Min((row * 2) / vertical, sourceHeight - 1) * sourceStride)..];
            var bottom = source[(Math.Min(((row * 2) + 1) / vertical, sourceHeight - 1) * sourceStride)..];
            var line = destination[(row * image.Stride)..];
            for (var column = 0; column < columns; column++)
            {
                var left = Math.Min((column * 2) / horizontal, sourceWidth - 1) * 2;
                var right = Math.Min(((column * 2) + 1) / horizontal, sourceWidth - 1) * 2;
                line[column * 2] = (byte)((top[left] + top[right] + bottom[left] + bottom[right] + 2) / 4);
                line[(column * 2) + 1] = (byte)((top[left + 1] + top[right + 1] + bottom[left + 1] + bottom[right + 1] + 2) / 4);
            }
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
