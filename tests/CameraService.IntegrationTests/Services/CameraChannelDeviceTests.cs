namespace CameraService.Host.Services;

using CameraService.Host.Testing;

public sealed class CameraChannelDeviceTests
{
    // The first device of a PC can be a virtual camera without the format
    [Fact]
    public Task CameraWithoutSettingUsesFirstDeviceWithFormat() =>
        ServiceTestServer.RunAsync(
            new Dictionary<string, string?> { ["Cameras:0:Device"] = string.Empty },
            static async (server, cancellationToken) =>
            {
                // Arrange
                server.Camera.HasVirtualDevice = true;

                // Act
                using var lease = server.Channel.Acquire();
                await ServiceTestServer.WaitUntilAsync(() => server.Channel.State is CameraState.Open or CameraState.Failed, cancellationToken).ConfigureAwait(true);

                // Assert
                Assert.Equal(CameraState.Open, server.Channel.State);
                Assert.Equal(TestCameraProvider.DeviceName, server.Channel.Device?.Name);
            });

    [Fact]
    public Task DeviceIdSelectsDevice() =>
        ServiceTestServer.RunAsync(
            new Dictionary<string, string?> { ["Cameras:0:Device"] = string.Empty, ["Cameras:0:DeviceId"] = TestCameraProvider.SecondDeviceId },
            static async (server, cancellationToken) =>
            {
                // Act
                using var lease = server.Channel.Acquire();
                await ServiceTestServer.WaitUntilAsync(() => server.Channel.State is CameraState.Open or CameraState.Failed, cancellationToken).ConfigureAwait(true);

                // Assert
                Assert.Equal(TestCameraProvider.SecondDeviceId, server.Channel.Device?.Id);
            });

    // A configured device is not replaced by another one
    [Fact]
    public Task NamedDeviceWithoutFormatFails() =>
        ServiceTestServer.RunAsync(
            new Dictionary<string, string?> { ["Cameras:0:Device"] = TestCameraProvider.VirtualDeviceName },
            static async (server, cancellationToken) =>
            {
                // Arrange
                server.Camera.HasVirtualDevice = true;

                // Act
                using var lease = server.Channel.Acquire();
                await ServiceTestServer.WaitUntilAsync(() => server.Channel.State == CameraState.Failed, cancellationToken).ConfigureAwait(true);

                // Assert (the error tells what the device has)
                Assert.Equal("Format YUY2 320x240 30fps not supported. Virtual camera: NV12 1280x720 30fps.", server.Channel.ErrorMessage);
                Assert.Equal(0, server.Camera.CreatedCount);
            });

    [Fact]
    public Task FormatOfNoDeviceFails() =>
        ServiceTestServer.RunAsync(
            new Dictionary<string, string?> { ["Cameras:0:Device"] = string.Empty, ["Cameras:0:Width"] = "640", ["Cameras:0:Height"] = "480" },
            static async (server, cancellationToken) =>
            {
                // Act
                using var lease = server.Channel.Acquire();
                await ServiceTestServer.WaitUntilAsync(() => server.Channel.State == CameraState.Failed, cancellationToken).ConfigureAwait(true);

                // Assert
                Assert.Equal("Format YUY2 640x480 30fps not supported. Test camera: YUY2 320x240 30fps. Test camera 2: YUY2 320x240 30fps.", server.Channel.ErrorMessage);
                Assert.Equal(0, server.Camera.CreatedCount);
            });
}
