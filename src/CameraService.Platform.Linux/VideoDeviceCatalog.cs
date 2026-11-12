namespace CameraService.Platform.Linux;

using CameraService.Core;

using LinuxDotNet.Video4Linux2;

using VideoFormat = CameraService.Core.VideoFormat;

public static class VideoDeviceCatalog
{
    public static Task<IReadOnlyList<VideoDevice>> GetDevicesAsync() => Task.Run(GetDevices);

    private static IReadOnlyList<VideoDevice> GetDevices()
    {
        var devices = new List<VideoDevice>();
        foreach (var info in VideoInfo.GetAllVideo())
        {
            // The other nodes of a camera (for example the metadata) have no formats of the streams
            var formats = ToVideoFormats(info.SupportedFormats.SelectMany(static format =>
                format.SupportedResolutions.SelectMany(resolution =>
                    format.GetFrameIntervals(resolution).Select(interval => (format.PixelFormat, resolution, interval)))));
            if (info.IsVideoCapture && (formats.Count > 0))
            {
                devices.Add(new VideoDevice(GetDeviceId(info.ById, info.Device), info.Name, formats));
            }
        }

        return devices;
    }

    // The formats the streams can use, by pixel format, size and frame rate
    internal static IReadOnlyList<VideoFormat> ToVideoFormats(IEnumerable<(PixelFormat PixelFormat, Resolution Resolution, FrameInterval Interval)> entries) =>
        [.. entries
            .Select(static x => new VideoFormat(ToPixelFormat(x.PixelFormat), x.Resolution.Width, x.Resolution.Height, x.Interval.FramesPerSecond))
            .Where(static x => (x.PixelFormat != VideoPixelFormat.Unknown) && (x.FrameRate > 0))
            .Distinct()
            .OrderBy(static x => x.PixelFormat)
            .ThenBy(static x => x.Width * x.Height)
            .ThenBy(static x => x.FrameRate)];

    internal static VideoPixelFormat ToPixelFormat(PixelFormat format) => format switch
    {
        PixelFormat.YUYV => VideoPixelFormat.Yuy2,
        PixelFormat.NV12 => VideoPixelFormat.Nv12,
        PixelFormat.MJPG => VideoPixelFormat.Mjpeg,
        _ => VideoPixelFormat.Unknown
    };

    // The link in /dev/v4l/by-id names the camera by its USB ids, /dev/videoN can change when the cameras are plugged in another order
    internal static string GetDeviceId(string byId, string device) => byId.Length > 0 ? byId : device;
}
