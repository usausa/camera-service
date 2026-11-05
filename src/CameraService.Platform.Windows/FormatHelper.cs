namespace CameraService.Platform.Windows;

using CameraService.Core;

using global::Windows.Graphics.Imaging;
using global::Windows.Media.Capture.Frames;
using global::Windows.Media.MediaProperties;

internal static class FormatHelper
{
    public static VideoPixelFormat FromSubtype(string subtype) => subtype.ToUpperInvariant() switch
    {
        "NV12" => VideoPixelFormat.Nv12,
        "YUY2" => VideoPixelFormat.Yuy2,
        "BGRA8" => VideoPixelFormat.Bgra8,
        _ => VideoPixelFormat.Unknown
    };

    public static VideoPixelFormat FromBitmapPixelFormat(BitmapPixelFormat format) => format switch
    {
        BitmapPixelFormat.Nv12 => VideoPixelFormat.Nv12,
        BitmapPixelFormat.Yuy2 => VideoPixelFormat.Yuy2,
        BitmapPixelFormat.Bgra8 => VideoPixelFormat.Bgra8,
        _ => VideoPixelFormat.Unknown
    };

    public static double ToFrameRate(MediaRatio ratio) =>
        ratio.Denominator == 0 ? 0 : (double)ratio.Numerator / ratio.Denominator;

    public static VideoColorRange GetColorRange(MediaFrameFormat format) =>
        format.Properties.TryGetValue(MediaFoundation.NominalRange, out var value) && (value is uint range)
            ? range switch
            {
                MediaFoundation.NominalRangeFull => VideoColorRange.Full,
                MediaFoundation.NominalRangeVideo => VideoColorRange.Limited,
                _ => VideoColorRange.Unknown
            }
            : VideoColorRange.Unknown;

    public static VideoFormat ToVideoFormat(MediaFrameFormat format) =>
        new(FromSubtype(format.Subtype), (int)format.VideoFormat.Width, (int)format.VideoFormat.Height, ToFrameRate(format.FrameRate));
}
