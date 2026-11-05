namespace CameraService.Host.Endpoints;

using System.Net;

using CameraService.Host.Testing;

using Microsoft.Extensions.Options;

public sealed class StartupTests
{
    [Fact]
    public Task HealthIsHealthy() =>
        ServiceTestServer.RunAsync(null, static async (server, cancellationToken) =>
        {
            // Act
            var health = await server.Client.GetStringAsync(server.GetUri("/health"), cancellationToken).ConfigureAwait(true);

            // Assert (no camera is opened for it)
            Assert.Equal("Healthy", health);
            Assert.Equal(0, server.Camera.CreatedCount);
        });

    // Only the disposal of the test server stops the server, so it still answers after the stop has started.
    [Fact]
    public Task RequestAfterStopStartedIsRefused() =>
        ServiceTestServer.RunAsync(null, static async (server, cancellationToken) =>
        {
            // Act
            server.Lifetime.StopApplication();
            using var response = await server.Client.GetAsync(server.GetUri("/health"), cancellationToken).ConfigureAwait(true);

            // Assert
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.True(response.Headers.ConnectionClose);
        });

    // The Ids are compared without upper and lower case
    [Fact]
    public Task DuplicateCameraIdStopsStart() =>
        Assert.ThrowsAsync<OptionsValidationException>(static () =>
            ServiceTestServer.RunAsync(
                new Dictionary<string, string?> { ["Cameras:1:Id"] = "TEST", ["Cameras:1:Device"] = TestCameraProvider.DeviceName },
                static (_, _) => Task.CompletedTask));

    // The streams encode only YUY2 and NV12
    [Fact]
    public Task UnsupportedFormatStopsStart() =>
        Assert.ThrowsAsync<OptionsValidationException>(static () =>
            ServiceTestServer.RunAsync(
                new Dictionary<string, string?> { ["Cameras:0:Format"] = "Bgra8" },
                static (_, _) => Task.CompletedTask));

    [Theory]
    [InlineData("Cameras:0:Snapshot:Schedule", "* * *")]
    [InlineData("Cameras:0:Snapshot:Timestamp:Format", "yyyy/MM/dd ddd")]
    [InlineData("Retention:Schedule", "0 25 * * *")]
    [InlineData("Storage:Path", "")]
    public Task InvalidSavingStopsStart(string key, string value) =>
        Assert.ThrowsAsync<OptionsValidationException>(() =>
            ServiceTestServer.RunAsync(
                new Dictionary<string, string?> { [key] = value },
                static (_, _) => Task.CompletedTask));
}
