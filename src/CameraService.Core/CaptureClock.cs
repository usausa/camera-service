namespace CameraService.Core;

using System.Diagnostics;

// Time base of the capture times of the frames
public static class CaptureClock
{
    private static readonly double TickFrequency = (double)TimeSpan.TicksPerSecond / Stopwatch.Frequency;

    public static TimeSpan Now => FromTimestamp(Stopwatch.GetTimestamp());

    public static TimeSpan FromTimestamp(long timestamp) => new((long)(timestamp * TickFrequency));
}
