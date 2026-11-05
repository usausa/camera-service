namespace CameraService.Host.Services;

using CameraService.Host.Testing;

using Microsoft.Extensions.Logging;

// Separate from the other tests of the camera, so that the test waiting for the retry delay (5 seconds) runs in parallel
public sealed class CameraChannelStepFailureTests
{
    // An exception that no camera is known to throw: the camera does not stay in the step, or try again on each tick of the timer
    [Fact]
    public Task UnexpectedExceptionIsHandledAsFailure() =>
        ServiceTestServer.RunAsync(null, static async (server, cancellationToken) =>
        {
            // Arrange (the lease keeps the camera in use)
            server.Camera.CreateFails = true;
            using var lease = server.Channel.Acquire();

            // Act
            await ServiceTestServer.WaitUntilAsync(() => server.Channel.State == CameraState.Failed, cancellationToken).ConfigureAwait(true);
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(true);
            var attempts = server.Camera.CreateAttempts;
            var state = server.Channel.State;
            var error = server.Channel.ErrorMessage;
            var failing = server.Channel.IsFailing;
            server.Camera.CreateFails = false;
            await ServiceTestServer.WaitUntilAsync(() => server.Channel.State == CameraState.Open, cancellationToken).ConfigureAwait(true);

            // Assert (opened after the retry delay when the exception is gone)
            Assert.Equal(1, attempts);
            Assert.Equal(CameraState.Failed, state);
            Assert.Contains(TestCameraProvider.CreateError, error, StringComparison.Ordinal);
            Assert.True(failing);
            Assert.Single(server.Logs.Entries, static x => (x.Level == LogLevel.Error) && x.Message.StartsWith("Camera step failed. id=[test]", StringComparison.Ordinal));
        });
}
