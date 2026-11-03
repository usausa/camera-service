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
            var device = Assert.Single(document.RootElement.EnumerateArray());
            Assert.Equal(TestCameraProvider.DeviceId, device.GetProperty("id").GetString());
            Assert.Equal(TestCameraProvider.DeviceName, device.GetProperty("name").GetString());
            Assert.Equal("YUY2 320x240 30fps", Assert.Single(device.GetProperty("formats").EnumerateArray()).GetString());
        });
}
