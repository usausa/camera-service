namespace CameraService.Host.Endpoints;

using System.Globalization;
using System.Net;

using CameraService.Host.Settings;
using CameraService.Host.Testing;

// Separate from the other tests of the endpoints, so that the test waiting for the warm-up (10 seconds) runs in parallel
public sealed class SnapshotWarmupTests
{
    // The largest value of the setting
    private const int LongWarmupTime = 10000;

    [Fact]
    public Task SnapshotWaitsForLongWarmup() =>
        ServiceTestServer.RunAsync(
            new Dictionary<string, string?> { ["Camera:WarmupTime"] = LongWarmupTime.ToString(CultureInfo.InvariantCulture) },
            static async (server, cancellationToken) =>
            {
                // Act
                using var response = await server.Client.GetAsync(server.GetUri("/cameras/test/snapshot.jpg"), cancellationToken).ConfigureAwait(true);

                // Assert
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            });

    [Fact]
    public void TimesIncludeWarmup()
    {
        // Arrange
        var setting = new CameraSetting { WarmupTime = LongWarmupTime };

        // Act
        var prepareTime = setting.PrepareTime;
        var timeout = setting.FirstFrameTimeout;

        // Assert
        Assert.True(prepareTime > TimeSpan.FromMilliseconds(LongWarmupTime));
        Assert.True(timeout > prepareTime);
    }
}
