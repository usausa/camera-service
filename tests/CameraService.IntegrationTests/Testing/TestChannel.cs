namespace CameraService.Host.Testing;

using CameraService.Core;
using CameraService.Host.Services;
using CameraService.Host.Settings;

using Microsoft.Extensions.Logging.Abstractions;

internal static class TestChannel
{
    // A channel without a server, for the tests of the classes that use it. Without the timer of the manager, only Acquire runs its steps.
    public static CameraChannel Create(TestCameraProvider camera, VideoPixelFormat format = VideoPixelFormat.Yuy2) =>
        new(
            NullLogger.Instance,
            camera,
            new CameraDeviceSetting
            {
                Id = ServiceTestServer.CameraId,
                Device = TestCameraProvider.DeviceName,
                Format = format,
                Width = TestCameraProvider.Width,
                Height = TestCameraProvider.Height,
                Fps = TestCameraProvider.FrameRate
            },
            new CameraSetting { WarmupTime = 0 });
}
