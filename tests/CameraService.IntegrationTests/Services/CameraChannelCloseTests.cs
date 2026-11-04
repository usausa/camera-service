namespace CameraService.Host.Services;

using CameraService.Host.Testing;

// Separate from the other tests of the camera, so that the test waiting for the retry delay (5 seconds) runs in parallel.
// The test server sets the idle time to 1 second.
public sealed class CameraChannelCloseTests
{
    [Fact]
    public Task CameraThatFailsToCloseOpensAgain() =>
        ServiceTestServer.RunAsync(null, static async (server, cancellationToken) =>
        {
            // Arrange (a client keeps the camera in use)
            using var stream = await server.Client.GetAsync(server.GetUri("/cameras/test/stream.mjpg"), HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(true);
            await ServiceTestServer.WaitUntilAsync(() => server.Channel.State == CameraState.Open, cancellationToken).ConfigureAwait(true);
            var first = server.Camera.Capture!;
            first.CloseFails = true;

            // Act
            first.Fail();
            await ServiceTestServer.WaitUntilAsync(() => server.Channel.State == CameraState.Failed, cancellationToken).ConfigureAwait(true);
            await ServiceTestServer.WaitUntilAsync(() => (server.Camera.CreatedCount == 2) && !server.Channel.IsFailing, cancellationToken).ConfigureAwait(true);

            // Assert
            Assert.True(first.IsDisposed);
            Assert.Equal(CameraState.Open, server.Channel.State);
            Assert.Contains(server.Logs.Entries, static x => x.Message.StartsWith("Camera close failed.", StringComparison.Ordinal));
        });

    // Nobody uses the camera after the still image, so it stays failed until it is used
    [Fact]
    public Task CameraThatFailsToCloseAfterIdleTimeIsFailed() =>
        ServiceTestServer.RunAsync(null, static async (server, cancellationToken) =>
        {
            // Arrange
            using var response = await server.Client.GetAsync(server.GetUri("/cameras/test/snapshot.jpg"), cancellationToken).ConfigureAwait(true);
            var capture = server.Camera.Capture!;
            capture.CloseFails = true;

            // Act
            await ServiceTestServer.WaitUntilAsync(() => server.Channel.State != CameraState.Open, cancellationToken).ConfigureAwait(true);

            // Assert
            Assert.True(capture.IsDisposed);
            Assert.Equal(CameraState.Failed, server.Channel.State);
            Assert.True(server.Channel.IsFailing);
            Assert.Equal("Test close failure.", server.Manager.Find(ServiceTestServer.CameraId)!.ErrorMessage);
            Assert.Equal(1, server.Camera.CreatedCount);
        });
}
