namespace CameraService.Host.Endpoints;

using System.Net;
using System.Net.Http.Json;

using CameraService.Host.Testing;

public sealed class ImageEndpointsTests
{
    [Fact]
    public Task DatesAreNewestFirst() =>
        ServiceTestServer.RunAsync(null, SaveImages, static async (server, cancellationToken) =>
        {
            // Act
            var dates = await server.Client.GetFromJsonAsync<string[]>(server.GetUri("/api/images/test"), cancellationToken).ConfigureAwait(true);

            // Assert (only the folders of a date)
            Assert.Equal(["20260102", "20260101"], dates!);
        });

    [Fact]
    public Task TimesAreOldestFirst() =>
        ServiceTestServer.RunAsync(null, SaveImages, static async (server, cancellationToken) =>
        {
            // Act
            var times = await server.Client.GetFromJsonAsync<string[]>(server.GetUri("/api/images/test/20260101"), cancellationToken).ConfigureAwait(true);

            // Assert (only the images of a time)
            Assert.Equal(["000000", "000100"], times!);
        });

    [Fact]
    public Task ImageIsCachedByTag() =>
        ServiceTestServer.RunAsync(null, SaveImages, static async (server, cancellationToken) =>
        {
            // Act
            var uri = server.GetUri("/images/test/20260101/000100.jpg");
            using var response = await server.Client.GetAsync(uri, cancellationToken).ConfigureAwait(true);
            var jpeg = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(true);
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.IfNoneMatch.Add(response.Headers.ETag!);
            using var again = await server.Client.SendAsync(request, cancellationToken).ConfigureAwait(true);

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("image/jpeg", response.Content.Headers.ContentType?.MediaType);
            Assert.Equal(TimeSpan.FromDays(1), response.Headers.CacheControl?.MaxAge);
            Assert.Equal((640, 480), MjpegReader.GetJpegSize(jpeg));
            Assert.Equal(HttpStatusCode.NotModified, again.StatusCode);
        });

    [Fact]
    public Task ThumbnailIsScaledDown() =>
        ServiceTestServer.RunAsync(null, SaveImages, static async (server, cancellationToken) =>
        {
            // Act
            var uri = server.GetUri("/images/test/20260101/000000/thumbnail.jpg");
            using var response = await server.Client.GetAsync(uri, cancellationToken).ConfigureAwait(true);
            var jpeg = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(true);
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.IfNoneMatch.Add(response.Headers.ETag!);
            using var again = await server.Client.SendAsync(request, cancellationToken).ConfigureAwait(true);

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("image/jpeg", response.Content.Headers.ContentType?.MediaType);
            Assert.Equal((320, 240), MjpegReader.GetJpegSize(jpeg));
            Assert.Equal(HttpStatusCode.NotModified, again.StatusCode);
        });

    // Only a configured camera, a date of 8 digits and a time of 6 digits are accepted
    [Theory]
    [InlineData("/api/images/missing")]
    [InlineData("/api/images/test/2026010")]
    [InlineData("/api/images/test/20261301")]
    [InlineData("/images/missing/20260101/000000.jpg")]
    [InlineData("/images/test/2026-01-01/000000.jpg")]
    [InlineData("/images/test/20260101/0000.jpg")]
    [InlineData("/images/test/20260101/000200.jpg")]
    [InlineData("/images/test/20260101/..%5C..%5C20260102%5C120000.jpg")]
    [InlineData("/images/test/20260101/000200/thumbnail.jpg")]
    public Task InvalidPathIsNotFound(string path) =>
        ServiceTestServer.RunAsync(null, SaveImages, async (server, cancellationToken) =>
        {
            // Act
            using var response = await server.Client.GetAsync(server.GetUri(path), cancellationToken).ConfigureAwait(true);

            // Assert
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        });

    private static void SaveImages(string storage)
    {
        var jpeg = TestJpeg.Create(640, 480);
        TestJpeg.Save(storage, "20260101", "000100", jpeg);
        TestJpeg.Save(storage, "20260101", "000000", jpeg);
        TestJpeg.Save(storage, "20260102", "120000", jpeg);
        File.WriteAllBytes(Path.Combine(storage, ServiceTestServer.CameraId, "20260101", "notes.jpg"), jpeg);
        Directory.CreateDirectory(Path.Combine(storage, ServiceTestServer.CameraId, "notes"));
    }
}
