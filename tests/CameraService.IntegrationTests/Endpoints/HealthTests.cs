namespace CameraService.Host.Endpoints;

using System.Net;

using CameraService.Host.Services;
using CameraService.Host.Testing;

public sealed class HealthTests
{
    private const string HealthPath = "/health";

    [Fact]
    public Task FailedSavingIsUnhealthy() =>
        ServiceTestServer.RunAsync(
            new Dictionary<string, string?>
            {
                ["Cameras:0:Device"] = "Missing camera",
                ["Cameras:0:Snapshot:Enable"] = "true",
                ["Cameras:0:Snapshot:Schedule"] = "* * * * * *"
            },
            static async (server, cancellationToken) =>
            {
                // Act
                await ServiceTestServer.WaitUntilAsync(() => server.Snapshot.ErrorMessage is not null, cancellationToken).ConfigureAwait(true);
                using var response = await server.Client.GetAsync(server.GetUri(HealthPath), cancellationToken).ConfigureAwait(true);

                // Assert
                Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
                Assert.Equal("Unhealthy", await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(true));
            });

    // Without an error of the saving (no saving here), only the state of the camera tells that it failed.
    // It takes 5 seconds until the camera tries to open again.
    [Fact]
    public Task CameraThatFailedIsUnhealthyUntilItSendsFramesAgain() =>
        ServiceTestServer.RunAsync(null, static async (server, cancellationToken) =>
        {
            // Arrange (a client keeps the camera in use)
            using var stream = await server.Client.GetAsync(server.GetUri("/cameras/test/stream.mjpg"), HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(true);
            await ServiceTestServer.WaitUntilAsync(() => server.Channel.State == CameraState.Open, cancellationToken).ConfigureAwait(true);
            using var healthy = await server.Client.GetAsync(server.GetUri(HealthPath), cancellationToken).ConfigureAwait(true);
            var hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            server.Camera.DevicesHold = hold.Task;

            // Act
            HttpStatusCode whileFailed;
            HttpStatusCode whileOpening;
            try
            {
                server.Camera.Capture!.Fail();
                await ServiceTestServer.WaitUntilAsync(() => server.Channel.State == CameraState.Failed, cancellationToken).ConfigureAwait(true);
                using var failed = await server.Client.GetAsync(server.GetUri(HealthPath), cancellationToken).ConfigureAwait(true);
                whileFailed = failed.StatusCode;

                await ServiceTestServer.WaitUntilAsync(() => server.Channel.State == CameraState.Opening, cancellationToken).ConfigureAwait(true);
                using var opening = await server.Client.GetAsync(server.GetUri(HealthPath), cancellationToken).ConfigureAwait(true);
                whileOpening = opening.StatusCode;
            }
            finally
            {
                // Also after an error: stopping the server waits for the camera that is opening
                hold.SetResult();
            }

            await ServiceTestServer.WaitUntilAsync(() => !server.Channel.IsFailing, cancellationToken).ConfigureAwait(true);
            using var opened = await server.Client.GetAsync(server.GetUri(HealthPath), cancellationToken).ConfigureAwait(true);

            // Assert
            Assert.Equal(HttpStatusCode.OK, healthy.StatusCode);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, whileFailed);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, whileOpening);
            Assert.Equal(HttpStatusCode.OK, opened.StatusCode);
        });

    // The folder cannot be created (the name has a character that Windows does not accept)
    [Fact]
    public Task FolderThatCannotBeWrittenIsUnhealthy() =>
        ServiceTestServer.RunAsync(
            new Dictionary<string, string?>
            {
                ["Storage:Path"] = "invalid<name",
                ["Cameras:0:Snapshot:Enable"] = "true",
                ["Cameras:0:Snapshot:Schedule"] = "0 0 1 1 *"
            },
            static async (server, cancellationToken) =>
            {
                // Act
                await ServiceTestServer.WaitUntilAsync(() => server.Snapshot.FolderError is not null, cancellationToken).ConfigureAwait(true);
                using var response = await server.Client.GetAsync(server.GetUri(HealthPath), cancellationToken).ConfigureAwait(true);

                // Assert
                Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
                Assert.Equal("Unhealthy", await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(true));
            });

    // More free space than any drive has
    [Fact]
    public Task LowFreeSpaceIsDegraded() =>
        ServiceTestServer.RunAsync(
            new Dictionary<string, string?>
            {
                ["Retention:MinFreeSpace"] = "1000000",
                ["Cameras:0:Snapshot:Enable"] = "true",
                ["Cameras:0:Snapshot:Schedule"] = "0 0 1 1 *"
            },
            static async (server, cancellationToken) =>
            {
                // Act
                using var response = await server.Client.GetAsync(server.GetUri(HealthPath), cancellationToken).ConfigureAwait(true);

                // Assert (still answered with 200)
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.Equal("Degraded", await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(true));
            });
}
