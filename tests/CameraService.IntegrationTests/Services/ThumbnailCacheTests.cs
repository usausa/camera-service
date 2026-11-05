namespace CameraService.Host.Services;

using CameraService.Host.Testing;
using CameraService.Platform.Windows;

public sealed class ThumbnailCacheTests
{
    // The image was found, then deleted by the retention (the image, or the folder of the date) before it was read
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DeletedImageHasNoThumbnail(bool deleteFolder)
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var folder = Path.Combine(Path.GetTempPath(), "CameraService.IntegrationTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var path = Path.Combine(folder, "000000.jpg");
            await File.WriteAllBytesAsync(path, TestJpeg.Create(640, 480), cancellationToken).ConfigureAwait(true);
            var file = new FileInfo(path);
            Assert.True(file.Exists);
            File.Delete(path);
            if (deleteFolder)
            {
                Directory.Delete(folder);
            }

            using var cache = new ThumbnailCache(new ThumbnailGenerator());

            // Act
            var thumbnail = await cache.GetAsync(file, cancellationToken).ConfigureAwait(true);

            // Assert
            Assert.Null(thumbnail);
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, true);
            }
        }
    }

    [Fact]
    public async Task ThumbnailIsKeptInMemory()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var path = Path.Combine(Path.GetTempPath(), "CameraService.IntegrationTests", Guid.NewGuid().ToString("N") + ".jpg");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try
        {
            await File.WriteAllBytesAsync(path, TestJpeg.Create(640, 480), cancellationToken).ConfigureAwait(true);
            var file = new FileInfo(path);
            using var cache = new ThumbnailCache(new ThumbnailGenerator());

            // Act
            var first = await cache.GetAsync(file, cancellationToken).ConfigureAwait(true);
            File.Delete(path);
            var second = await cache.GetAsync(file, cancellationToken).ConfigureAwait(true);

            // Assert
            Assert.NotNull(first);
            Assert.Same(first, second);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
