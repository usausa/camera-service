namespace CameraService.Platform.Linux;

using CameraService.Core;

using LinuxDotNet.Video4Linux2;

using VideoFormat = CameraService.Core.VideoFormat;

public sealed class VideoCaptureTests
{
    [Theory]
    [InlineData(VideoPixelFormat.Yuy2, PixelFormat.YUYV)]
    [InlineData(VideoPixelFormat.Nv12, PixelFormat.NV12)]
    [InlineData(VideoPixelFormat.Mjpeg, PixelFormat.MJPG)]
    public void PixelFormatIsMapped(VideoPixelFormat format, PixelFormat expected)
    {
        // Act
        var result = VideoCapture.ToV4L2Format(format);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void OtherPixelFormatIsNotMapped()
    {
        // Act
        var result = VideoCapture.ToV4L2Format(VideoPixelFormat.Bgra8);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void MjpegHasNoPlanes()
    {
        // Act (a JPEG of each frame)
        var (plane0, plane1) = VideoCapture.GetPlanes(VideoPixelFormat.Mjpeg, 1280, 720, 0);

        // Assert
        Assert.Equal(default, plane0);
        Assert.Equal(default, plane1);
    }

    [Fact]
    public void Yuy2HasOnePlane()
    {
        // Act (the driver pads the lines)
        var (plane0, plane1) = VideoCapture.GetPlanes(VideoPixelFormat.Yuy2, 640, 480, 1344);

        // Assert
        Assert.Equal(new FramePlane(0, 640, 480, 1344), plane0);
        Assert.Equal(default, plane1);
    }

    [Fact]
    public void ChromaOfNv12FollowsLuma()
    {
        // Act
        var (plane0, plane1) = VideoCapture.GetPlanes(VideoPixelFormat.Nv12, 1280, 721, 1344);

        // Assert
        Assert.Equal(new FramePlane(0, 1280, 721, 1344), plane0);
        Assert.Equal(new FramePlane(1344 * 721, 1280, 361, 1344), plane1);
    }

    [Theory]
    [InlineData(10_000, 10_020)]
    [InlineData(10_020, 10_020)]
    public void CaptureTimeIsTimeOfDriver(int timestamp, int arrival)
    {
        // Act
        var time = VideoCapture.GetCaptureTime(TimeSpan.FromMilliseconds(timestamp), TimeSpan.FromMilliseconds(arrival));

        // Assert
        Assert.Equal(TimeSpan.FromMilliseconds(timestamp), time);
    }

    // No time, a time after the arrival, or a time too far before it (another clock)
    [Theory]
    [InlineData(0, 10_020)]
    [InlineData(10_030, 10_020)]
    [InlineData(9_020, 10_020)]
    public void CaptureTimeIsArrivalWhenTimeOfDriverIsOnOtherClock(int timestamp, int arrival)
    {
        // Act
        var time = VideoCapture.GetCaptureTime(TimeSpan.FromMilliseconds(timestamp), TimeSpan.FromMilliseconds(arrival));

        // Assert
        Assert.Equal(TimeSpan.FromMilliseconds(arrival), time);
    }

    // Not passed to the device
    [Fact]
    public async Task OtherPixelFormatIsNotOpened()
    {
        // Arrange
        var capture = new VideoCapture("/dev/video0");
        await using (capture.ConfigureAwait(true))
        {
            // Act & Assert
            await Assert.ThrowsAsync<InvalidOperationException>(() => capture.OpenAsync(new VideoFormat(VideoPixelFormat.Bgra8, 640, 480, 30))).ConfigureAwait(true);
        }
    }

    [Fact]
    public async Task MissingDeviceIsNotOpened()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "Devices of Linux");

        // Arrange
        var capture = new VideoCapture("/dev/video-missing");
        await using (capture.ConfigureAwait(true))
        {
            // Act & Assert
            await Assert.ThrowsAsync<InvalidOperationException>(() => capture.OpenAsync(new VideoFormat(VideoPixelFormat.Yuy2, 640, 480, 30))).ConfigureAwait(true);
        }
    }

    [Fact]
    public async Task CaptureIsNotStartedBeforeOpening()
    {
        // Arrange
        var capture = new VideoCapture("/dev/video0");
        await using (capture.ConfigureAwait(true))
        {
            // Act & Assert
            await Assert.ThrowsAsync<InvalidOperationException>(capture.StartAsync).ConfigureAwait(true);
        }
    }

    [Fact]
    public void CaptureThatIsNotOpenedIsDisposed()
    {
        // Arrange
        using var capture = new VideoCapture("/dev/video0");

        // Act (called again at the end of the test)
        var exception = Record.Exception(capture.Dispose);

        // Assert
        Assert.Null(exception);
    }
}
