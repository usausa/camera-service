namespace CameraService.Host.Services;

using CameraService.Host.Testing;

// Separate from CameraChannelTests, so that the tests waiting for the retry delay (5 seconds) run in parallel
public sealed class CameraChannelRetryTests
{
    private const string StreamPath = "/cameras/test/stream.mjpg";

    [Fact]
    public Task CameraOpensAgainWhenFramesStop() =>
        ServiceTestServer.RunAsync(null, static async (server, cancellationToken) =>
        {
            // Arrange
            using var response = await server.Client.GetAsync(server.GetUri(StreamPath), HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(true);
            var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(true);
            var reader = new MjpegReader(stream);
            await reader.ReadPartsAsync(1, cancellationToken).ConfigureAwait(true);
            var hung = server.Camera.Capture!;

            // Act
            hung.Pause();
            await ServiceTestServer.WaitUntilAsync(() => server.Channel.State == CameraState.Failed, cancellationToken).ConfigureAwait(true);
            await ServiceTestServer.WaitUntilAsync(() => (server.Camera.CreatedCount == 2) && (server.Channel.State == CameraState.Open), cancellationToken).ConfigureAwait(true);
            var parts = await reader.ReadPartsAsync(2, cancellationToken).ConfigureAwait(true);

            // Assert
            Assert.True(hung.IsDisposed);
            Assert.Equal(2, parts.Count);
        });

    [Fact]
    public Task CameraIsNotOpenedAgainWithoutUsers() =>
        ServiceTestServer.RunAsync(null, static async (server, cancellationToken) =>
        {
            // Arrange (the only client leaves while the camera waits for the retry)
            using (var response = await server.Client.GetAsync(server.GetUri(StreamPath), HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(true))
            {
                var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(true);
                var reader = new MjpegReader(stream);
                await reader.ReadPartsAsync(1, cancellationToken).ConfigureAwait(true);
                server.Camera.Capture!.Fail();
                await ServiceTestServer.WaitUntilAsync(() => server.Channel.State == CameraState.Failed, cancellationToken).ConfigureAwait(true);
            }

            // Act (longer than the first retry delay)
            await ServiceTestServer.WaitUntilAsync(() => server.Channel.Leases == 0, cancellationToken).ConfigureAwait(true);
            await Task.Delay(TimeSpan.FromSeconds(6), cancellationToken).ConfigureAwait(true);

            // Assert
            Assert.Equal(1, server.Camera.CreatedCount);
            Assert.Equal(CameraState.Failed, server.Channel.State);
        });
}
