namespace CameraService.Host.Services;

using System.Net;

using CameraService.Host.Testing;

// The test server sets the idle time and the frame timeout to 1 second (the first retry is 5 seconds after a failure)
public sealed class CameraChannelTests
{
    private const string StreamPath = "/cameras/test/stream.mjpg";

    [Theory]
    [InlineData(1, 5)]
    [InlineData(2, 10)]
    [InlineData(3, 30)]
    [InlineData(4, 60)]
    [InlineData(100, 60)]
    public void RetryDelayGrowsWithFailures(int failures, int seconds)
    {
        // Act
        var delay = CameraChannel.GetRetryDelay(failures);

        // Assert
        Assert.Equal(TimeSpan.FromSeconds(seconds), delay);
    }

    [Fact]
    public Task CameraOpensOnDemandAndClosesAfterIdleTime() =>
        ServiceTestServer.RunAsync(null, static async (server, cancellationToken) =>
        {
            // Arrange (nobody uses the camera yet)
            await Task.Delay(TimeSpan.FromMilliseconds(700), cancellationToken).ConfigureAwait(true);
            var createdBefore = server.Camera.CreatedCount;

            // Act
            using var response = await server.Client.GetAsync(server.GetUri("/cameras/test/snapshot.jpg"), cancellationToken).ConfigureAwait(true);
            var stateAfterSnapshot = server.Channel.State;
            await ServiceTestServer.WaitUntilAsync(() => server.Channel.State == CameraState.Closed, cancellationToken).ConfigureAwait(true);

            // Assert
            Assert.Equal(0, createdBefore);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(CameraState.Open, stateAfterSnapshot);
            Assert.Equal(1, server.Camera.CreatedCount);
            Assert.True(server.Camera.Capture!.IsDisposed);
            Assert.Equal(0, server.Channel.Leases);
        });

    [Fact]
    public Task CameraOpensAgainAfterFailure() =>
        ServiceTestServer.RunAsync(null, static async (server, cancellationToken) =>
        {
            // Arrange (a client keeps the camera open)
            using var response = await server.Client.GetAsync(server.GetUri(StreamPath), HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(true);
            var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(true);
            var reader = new MjpegReader(stream);
            await reader.ReadPartsAsync(1, cancellationToken).ConfigureAwait(true);
            var failed = server.Camera.Capture!;

            // Act
            failed.Fail();
            await ServiceTestServer.WaitUntilAsync(() => server.Channel.State == CameraState.Failed, cancellationToken).ConfigureAwait(true);
            var error = server.Channel.ErrorMessage;
            await ServiceTestServer.WaitUntilAsync(() => (server.Camera.CreatedCount == 2) && (server.Channel.State == CameraState.Open), cancellationToken).ConfigureAwait(true);
            var parts = await reader.ReadPartsAsync(2, cancellationToken).ConfigureAwait(true);

            // Assert (the client gets the frames of the camera opened again)
            Assert.True(failed.IsDisposed);
            Assert.Equal("Test failure.", error);
            Assert.Equal(2, parts.Count);
            Assert.Null(server.Channel.ErrorMessage);
        });
}
