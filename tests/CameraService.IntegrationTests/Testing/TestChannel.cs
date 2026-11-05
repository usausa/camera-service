namespace CameraService.Host.Testing;

using CameraService.Host.Services;
using CameraService.Host.Settings;

using Microsoft.Extensions.Logging.Abstractions;

internal static class TestChannel
{
    // A channel without a server, for the tests of the classes that use it. Without the timer of the manager, only Acquire runs its steps.
    public static CameraChannel Create(TestCameraProvider camera) =>
        new(
            NullLogger.Instance,
            camera,
            new CameraDeviceSetting
            {
                Id = ServiceTestServer.CameraId,
                Device = TestCameraProvider.DeviceName,
                Width = TestCameraProvider.Width,
                Height = TestCameraProvider.Height,
                Fps = TestCameraProvider.FrameRate
            },
            new CameraSetting { WarmupTime = 0 });
}
