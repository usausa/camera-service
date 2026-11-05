namespace CameraService.Host.Components;

using System.Net;

using CameraService.Host.Testing;

// The first response of the pages (rendered on the server before the script connects). The pages open no camera for it.
public sealed class PagesTests
{
    [Theory]
    [InlineData("/", "No saved image")]
    [InlineData("/cameras/test", "Live")]
    [InlineData("/cameras/test/images", "No image is saved.")]
    [InlineData("/cameras/test/images/20260101", "No image is saved.")]
    [InlineData("/status", "Format")]
    [InlineData("/cameras/missing", "The camera is not found.")]
    [InlineData("/cameras/missing/images", "The camera is not found.")]
    public Task PageIsRendered(string path, string text) =>
        ServiceTestServer.RunAsync(null, async (server, cancellationToken) =>
        {
            // Act
            var html = await GetPageAsync(server, path, cancellationToken).ConfigureAwait(true);

            // Assert
            Assert.Contains(text, html, StringComparison.Ordinal);
            Assert.Equal(0, server.Camera.CreatedCount);
        });

    [Fact]
    public Task CamerasPageShowsNewestImage() =>
        ServiceTestServer.RunAsync(null, SaveImages, static async (server, cancellationToken) =>
        {
            // Act
            var html = await GetPageAsync(server, "/", cancellationToken).ConfigureAwait(true);

            // Assert
            Assert.Contains("images/test/20260102/093000/thumbnail.jpg", html, StringComparison.Ordinal);
            Assert.DoesNotContain("No saved image", html, StringComparison.Ordinal);
        });

    [Fact]
    public Task ImagesPageShowsNewestHour() =>
        ServiceTestServer.RunAsync(null, SaveImages, static async (server, cancellationToken) =>
        {
            // Act
            var html = await GetPageAsync(server, "/cameras/test/images/20260101", cancellationToken).ConfigureAwait(true);

            // Assert (the other hours are behind their buttons)
            Assert.Contains("images/test/20260101/120000/thumbnail.jpg", html, StringComparison.Ordinal);
            Assert.DoesNotContain("images/test/20260101/000000/thumbnail.jpg", html, StringComparison.Ordinal);
        });

    private static async Task<string> GetPageAsync(ServiceTestServer server, string path, CancellationToken cancellationToken)
    {
        using var response = await server.Client.GetAsync(server.GetUri(path), cancellationToken).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(true);
    }

    private static void SaveImages(string storage)
    {
        var jpeg = TestJpeg.Create(64, 48);
        TestJpeg.Save(storage, "20260101", "000000", jpeg);
        TestJpeg.Save(storage, "20260101", "120000", jpeg);
        TestJpeg.Save(storage, "20260102", "093000", jpeg);
    }
}
