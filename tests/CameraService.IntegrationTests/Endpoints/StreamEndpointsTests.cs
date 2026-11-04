namespace CameraService.Host.Endpoints;

using System.Net;

using CameraService.Host.Testing;

public sealed class StreamEndpointsTests
{
    private const string StreamPath = "/cameras/test/stream.mjpg";

    private const string SnapshotPath = "/cameras/test/snapshot.jpg";

    [Fact]
    public Task MjpegSendsJpegFrames() =>
        ServiceTestServer.RunAsync(null, static async (server, cancellationToken) =>
        {
            // Act
            using var response = await server.Client.GetAsync(server.GetUri(StreamPath), HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(true);
            var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(true);
            var reader = new MjpegReader(stream);
            var parts = await reader.ReadPartsAsync(3, cancellationToken).ConfigureAwait(true);

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("multipart/x-mixed-replace", response.Content.Headers.ContentType?.MediaType);
            Assert.All(parts, static x => Assert.Equal((TestCameraProvider.Width, TestCameraProvider.Height), MjpegReader.GetJpegSize(x)));
        });

    // The viewer reads the same stream with fetch, a browser may refuse a multipart response for anything other than an image
    [Fact]
    public Task RawMjpegHasOtherContentType() =>
        ServiceTestServer.RunAsync(null, static async (server, cancellationToken) =>
        {
            // Act
            using var response = await server.Client.GetAsync(server.GetUri($"{StreamPath}?raw"), HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(true);
            var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(true);
            var reader = new MjpegReader(stream);
            var parts = await reader.ReadPartsAsync(3, cancellationToken).ConfigureAwait(true);

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("application/octet-stream", response.Content.Headers.ContentType?.MediaType);
            Assert.All(parts, static x => Assert.Equal((TestCameraProvider.Width, TestCameraProvider.Height), MjpegReader.GetJpegSize(x)));
        });

    [Fact]
    public Task SnapshotReturnsJpeg() =>
        ServiceTestServer.RunAsync(null, static async (server, cancellationToken) =>
        {
            // Act
            using var response = await server.Client.GetAsync(server.GetUri(SnapshotPath), cancellationToken).ConfigureAwait(true);
            var jpeg = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(true);

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("image/jpeg", response.Content.Headers.ContentType?.MediaType);
            Assert.Equal((TestCameraProvider.Width, TestCameraProvider.Height), MjpegReader.GetJpegSize(jpeg));
        });

    [Fact]
    public Task ClientsShareOneCamera() =>
        ServiceTestServer.RunAsync(null, static async (server, cancellationToken) =>
        {
            // Arrange
            using var first = await server.Client.GetAsync(server.GetUri(StreamPath), HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(true);
            using var second = await server.Client.GetAsync(server.GetUri(StreamPath), HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(true);
            var firstStream = await first.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(true);
            var firstReader = new MjpegReader(firstStream);
            var secondStream = await second.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(true);
            var secondReader = new MjpegReader(secondStream);

            // Act
            var firstParts = await firstReader.ReadPartsAsync(2, cancellationToken).ConfigureAwait(true);
            var secondParts = await secondReader.ReadPartsAsync(2, cancellationToken).ConfigureAwait(true);

            // Assert
            Assert.Equal(2, firstParts.Count);
            Assert.Equal(2, secondParts.Count);
            Assert.Equal(1, server.Camera.CreatedCount);
            Assert.Equal(2, server.Channel.Leases);
        });

    [Theory]
    [InlineData("/cameras/missing/stream.mjpg")]
    [InlineData("/cameras/missing/snapshot.jpg")]
    public Task UnknownCameraIsNotFound(string path) =>
        ServiceTestServer.RunAsync(null, async (server, cancellationToken) =>
        {
            // Act
            using var response = await server.Client.GetAsync(server.GetUri(path), cancellationToken).ConfigureAwait(true);

            // Assert
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal(0, server.Camera.CreatedCount);
        });
}
