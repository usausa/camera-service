namespace CameraService.Host.Endpoints;

using System.Text.Json;

using CameraService.Host.Testing;

public sealed class DeviceEndpointsTests
{
    [Fact]
    public Task DevicesShowsCamerasWithFormats() =>
        ServiceTestServer.RunAsync(null, static async (server, cancellationToken) =>
        {
            // Act
            var json = await server.Client.GetStringAsync(server.GetUri("/api/devices"), cancellationToken).ConfigureAwait(true);
            using var document = JsonDocument.Parse(json);

            // Assert
            Assert.Collection(
                document.RootElement.EnumerateArray(),
                static x => AssertDevice(x, TestCameraProvider.DeviceId, TestCameraProvider.DeviceName),
                static x => AssertDevice(x, TestCameraProvider.SecondDeviceId, TestCameraProvider.SecondDeviceName));
        });

    private static void AssertDevice(JsonElement device, string id, string name)
    {
        Assert.Equal(id, device.GetProperty("id").GetString());
        Assert.Equal(name, device.GetProperty("name").GetString());
        Assert.Equal("YUY2 320x240 30fps", Assert.Single(device.GetProperty("formats").EnumerateArray()).GetString());
    }
}
