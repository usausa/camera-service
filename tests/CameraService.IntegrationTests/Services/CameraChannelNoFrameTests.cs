namespace CameraService.Host.Services;

using System.Net;

using CameraService.Host.Testing;

// Separate from the other tests of the camera, so that the tests waiting for the retry delays (5 and 10 seconds) run in parallel.
// The test server sets the frame timeout to 1 second.
public sealed class CameraChannelNoFrameTests
{
    // Opening works, so only the missing frames tell that the camera does not work
    [Fact]
    public Task CameraThatSendsNoFramesStaysFailedAndWaitsLonger() =>
        ServiceTestServer.RunAsync(null, static async (server, cancellationToken) =>
        {
            // Arrange (the lease keeps the camera in use)
            server.Camera.SendsNoFrames = true;
            using var lease = server.Channel.Acquire();
            await ServiceTestServer.WaitUntilAsync(() => server.Channel.State == CameraState.Failed, cancellationToken).ConfigureAwait(true);

            // Act (open again after the first delay, 5 seconds)
            await ServiceTestServer.WaitUntilAsync(() => (server.Camera.CreatedCount == 2) && (server.Channel.State == CameraState.Open), cancellationToken).ConfigureAwait(true);
            var errorWhileOpen = server.Manager.Find(ServiceTestServer.CameraId)!.ErrorMessage;
            using var healthWhileOpen = await server.Client.GetAsync(server.GetUri("/health"), cancellationToken).ConfigureAwait(true);

            // Failed again: the next delay is 10 seconds, so the camera is not opened again after 6 seconds
            await ServiceTestServer.WaitUntilAsync(() => server.Channel.State == CameraState.Failed, cancellationToken).ConfigureAwait(true);
            await Task.Delay(TimeSpan.FromSeconds(6), cancellationToken).ConfigureAwait(true);

            // Assert
            Assert.Equal("Camera sent no frame.", errorWhileOpen);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, healthWhileOpen.StatusCode);
            Assert.Equal(2, server.Camera.CreatedCount);
        });
}
