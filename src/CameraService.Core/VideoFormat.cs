namespace CameraService.Core;

using System.Globalization;

public readonly record struct VideoFormat(VideoPixelFormat PixelFormat, int Width, int Height, double FrameRate)
{
    private const double FrameRateTolerance = 0.5;

    public bool Matches(VideoFormat other) =>
        (PixelFormat == other.PixelFormat) &&
        (Width == other.Width) &&
        (Height == other.Height) &&
        (Math.Abs(FrameRate - other.FrameRate) < FrameRateTolerance);

    public override string ToString() =>
        String.Create(CultureInfo.InvariantCulture, $"{PixelFormat.ToString().ToUpperInvariant()} {Width}x{Height} {FrameRate:0.##}fps");
}
