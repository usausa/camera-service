namespace CameraService.Host.Endpoints;

using System.Net;

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
