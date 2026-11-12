namespace CameraService.Platform.Linux;

using CameraService.Core;

using LinuxDotNet.Video4Linux2;

using VideoFormat = CameraService.Core.VideoFormat;

public sealed class VideoDeviceCatalogTests
{
    [Fact]
    public void FormatsOfStreamsAreListed()
    {
        // Arrange (the streams do not use gray, and a frame rate of 0 is not a format)
        (PixelFormat, Resolution, FrameInterval)[] entries =
        [
            (PixelFormat.YUYV, new Resolution(640, 480), new FrameInterval(1, 30)),
            (PixelFormat.YUYV, new Resolution(640, 480), new FrameInterval(1, 15)),
            (PixelFormat.YUYV, new Resolution(320, 240), new FrameInterval(1, 30)),
            (PixelFormat.NV12, new Resolution(1280, 720), new FrameInterval(1001, 30000)),
            (PixelFormat.MJPG, new Resolution(1920, 1080), new FrameInterval(1, 30)),
            (PixelFormat.GREY, new Resolution(640, 480), new FrameInterval(1, 30)),
            (PixelFormat.YUYV, new Resolution(640, 360), new FrameInterval(0, 0)),
            (PixelFormat.YUYV, new Resolution(640, 480), new FrameInterval(1, 30))
        ];

        // Act
        var formats = VideoDeviceCatalog.ToVideoFormats(entries);

        // Assert (by pixel format, size and frame rate, without duplicates)
        Assert.Equal(
            [
                new VideoFormat(VideoPixelFormat.Nv12, 1280, 720, 30000d / 1001),
                new VideoFormat(VideoPixelFormat.Yuy2, 320, 240, 30),
                new VideoFormat(VideoPixelFormat.Yuy2, 640, 480, 15),
                new VideoFormat(VideoPixelFormat.Yuy2, 640, 480, 30),
                new VideoFormat(VideoPixelFormat.Mjpeg, 1920, 1080, 30)
            ],
            formats);
    }

    [Theory]
    [InlineData(PixelFormat.YUYV, VideoPixelFormat.Yuy2)]
    [InlineData(PixelFormat.NV12, VideoPixelFormat.Nv12)]
    [InlineData(PixelFormat.MJPG, VideoPixelFormat.Mjpeg)]
    [InlineData(PixelFormat.UYVY, VideoPixelFormat.Unknown)]
    [InlineData(PixelFormat.H264, VideoPixelFormat.Unknown)]
    public void PixelFormatIsMapped(PixelFormat format, VideoPixelFormat expected)
    {
        // Act
        var result = VideoDeviceCatalog.ToPixelFormat(format);

        // Assert
        Assert.Equal(expected, result);
    }

    // The link by the USB ids, or the device when the camera has none
    [Theory]
    [InlineData("/dev/v4l/by-id/usb-Camera_0001-video-index0", "/dev/video0", "/dev/v4l/by-id/usb-Camera_0001-video-index0")]
    [InlineData("", "/dev/video2", "/dev/video2")]
    public void DeviceIdIsLinkById(string byId, string device, string expected)
    {
        // Act
        var id = VideoDeviceCatalog.GetDeviceId(byId, device);

        // Assert
        Assert.Equal(expected, id);
    }
}
