namespace CameraService.Platform.Linux;

using System.Runtime.InteropServices;

using CameraService.Core;

using V4L2 = LinuxDotNet.Video4Linux2;

// Capture of Video for Linux: the frames come on the capture thread of the library, the buffer of a frame is valid while the handler runs.
// YUYV and NV12 are video range, a frame of MJPEG is a JPEG (full range).
public sealed class VideoCapture : IVideoCapture
{
    // A time of the driver further from the arrival is not on the clock of the frames
    private static readonly TimeSpan MaxCaptureDelay = TimeSpan.FromSeconds(1);

    private readonly FrameCapturedEventArgs frameEventArgs = new();

    private readonly string deviceId;

    private V4L2.VideoCapture? capture;

    private VideoPixelFormat pixelFormat;

    private VideoColorRange colorRange;

    private int width;

    private int height;

    private FramePlane plane0;

    private FramePlane plane1;

    private long sequence;

    public event EventHandler<FrameCapturedEventArgs>? FrameCaptured;

    public event EventHandler<VideoCaptureFailedEventArgs>? Failed;

    public VideoCapture(string deviceId)
    {
        this.deviceId = deviceId;
    }

    // Closing waits for the capture thread
    public void Dispose() => Close();

    public ValueTask DisposeAsync() => new(Task.Run(Close));

    public Task OpenAsync(VideoFormat format) => Task.Run(() => Open(format));

    public Task StartAsync() => Task.Run(Start);

    internal static V4L2.PixelFormat? ToV4L2Format(VideoPixelFormat format) => format switch
    {
        VideoPixelFormat.Yuy2 => V4L2.PixelFormat.YUYV,
        VideoPixelFormat.Nv12 => V4L2.PixelFormat.NV12,
        VideoPixelFormat.Mjpeg => V4L2.PixelFormat.MJPG,
        _ => null
    };

    // YUYV has one plane, NV12 the luma and then the interleaved chroma, both with the stride of the driver. MJPEG has no planes.
    internal static (FramePlane Plane0, FramePlane Plane1) GetPlanes(VideoPixelFormat format, int width, int height, int stride) => format switch
    {
        VideoPixelFormat.Nv12 => (new FramePlane(0, width, height, stride), new FramePlane(stride * height, width, (height + 1) / 2, stride)),
        VideoPixelFormat.Mjpeg => (default, default),
        _ => (new FramePlane(0, width, height, stride), default)
    };

    // The time the driver gave the frame (the monotonic clock, as CaptureClock), or the arrival when the time is not on that clock
    internal static TimeSpan GetCaptureTime(TimeSpan timestamp, TimeSpan arrival) =>
        (timestamp > TimeSpan.Zero) && (timestamp <= arrival) && (arrival - timestamp < MaxCaptureDelay) ? timestamp : arrival;

    private void Open(VideoFormat format)
    {
        if (capture is not null)
        {
            throw new InvalidOperationException("Already opened.");
        }

        var deviceFormat = ToV4L2Format(format.PixelFormat) ?? throw new InvalidOperationException("Format not supported.");
        var created = new V4L2.VideoCapture(deviceId);
        try
        {
            // The library fails when the driver takes another pixel format, and the driver takes the nearest size it has
            if (!created.Open(format.Width, format.Height, deviceFormat))
            {
                throw new InvalidOperationException("Failed to open the device.");
            }

            if ((created.Width != format.Width) || (created.Height != format.Height))
            {
                throw new InvalidOperationException("Format not supported.");
            }

            // A request: the driver takes the nearest rate it has
            _ = created.SetFrameRate((int)Math.Round(format.FrameRate));

            pixelFormat = format.PixelFormat;
            colorRange = pixelFormat == VideoPixelFormat.Mjpeg ? VideoColorRange.Full : VideoColorRange.Limited;
            width = created.Width;
            height = created.Height;
            (plane0, plane1) = GetPlanes(pixelFormat, width, height, created.BytesPerLine);
            created.FrameCaptured += OnFrameCaptured;
            created.CaptureFailed += OnCaptureFailed;
            capture = created;
        }
        catch
        {
            created.Dispose();
            throw;
        }
    }

    private void Start()
    {
        var current = capture ?? throw new InvalidOperationException("Not opened.");
        if (!current.IsCapturing && !current.StartCapture())
        {
            throw new InvalidOperationException("Failed to start capture.");
        }
    }

    private void Close()
    {
        if (capture is null)
        {
            return;
        }

        capture.FrameCaptured -= OnFrameCaptured;
        capture.CaptureFailed -= OnCaptureFailed;
        try
        {
            // The device is closed after the capture thread ends, a thread that does not end keeps it
            if (!capture.StopCapture())
            {
                throw new InvalidOperationException("Failed to stop capture.");
            }

            capture.Dispose();
        }
        finally
        {
            capture = null;
        }
    }

    // Capture thread of the library
    private void OnFrameCaptured(V4L2.FrameBuffer frame)
    {
        // A frame the driver could not complete
        if (frame.IsError || frame.IsEmpty)
        {
            return;
        }

        try
        {
            var arrivalTime = CaptureClock.Now;
            var data = frame.Span;
            fixed (byte* pointer = data)
            {
                frameEventArgs.Frame = new FrameBuffer(
                    (IntPtr)pointer,
                    data.Length,
                    pixelFormat,
                    colorRange,
                    width,
                    height,
                    plane0,
                    plane1,
                    ++sequence,
                    GetCaptureTime(frame.Timestamp, arrivalTime),
                    arrivalTime);
                FrameCaptured?.Invoke(this, frameEventArgs);
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // An exception would end the capture thread of the library and the process
        }
        finally
        {
            frameEventArgs.Frame = default;
        }
    }

    private void OnCaptureFailed(int error)
    {
        try
        {
            Failed?.Invoke(this, new VideoCaptureFailedEventArgs(Marshal.GetPInvokeErrorMessage(error), (uint)error));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
        }
    }
}
