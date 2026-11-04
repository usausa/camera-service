namespace CameraService.Host.Services;

using CameraService.Host.Testing;

// Separate from the other tests of the camera, so that the test waiting for the retry delay (5 seconds) runs in parallel
public sealed class CameraChannelStartFailureTests
{
    // The failure of a capture that did not start does not count for the next capture
    [Fact]
    public Task FailureWhileStartingDoesNotCloseNextCamera() =>
        ServiceTestServer.RunAsync(null, static async (server, cancellationToken) =>
        {
            // Arrange (the lease keeps the camera in use)
            server.Camera.FailsNextStart = true;
            using var lease = server.Channel.Acquire();
            await ServiceTestServer.WaitUntilAsync(() => server.Channel.State == CameraState.Failed, cancellationToken).ConfigureAwait(true);

            // Act (opened again after the first delay, then more than one step of the timer)
            await ServiceTestServer.WaitUntilAsync(() => server.Camera.CreatedCount == 2, cancellationToken).ConfigureAwait(true);
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(true);

            // Assert
            Assert.Equal(CameraState.Open, server.Channel.State);
            Assert.False(server.Channel.IsFailing);
            Assert.False(server.Camera.Capture!.IsDisposed);
            Assert.Equal(2, server.Camera.CreatedCount);
        });
}
