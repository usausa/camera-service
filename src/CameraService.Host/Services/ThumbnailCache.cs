namespace CameraService.Host.Services;

using CameraService.Core;

using Microsoft.Extensions.Caching.Memory;

// Thumbnails of the saved images, made on the first request and kept in memory up to a total size (the least used are dropped).
// An own cache, because a size limit applies to every entry of a cache.
public sealed class ThumbnailCache : IDisposable
{
    // About twice the width of a card in the lists, for high density displays
    private const int Width = 320;

    private const long SizeLimit = 64L * 1024 * 1024;

    private readonly IThumbnailGenerator generator;

    private readonly MemoryCache cache = new(new MemoryCacheOptions { SizeLimit = SizeLimit });

    public ThumbnailCache(IThumbnailGenerator generator)
    {
        this.generator = generator;
    }

    public void Dispose() => cache.Dispose();

    // Null when the image cannot be decoded or was deleted after it was found (a replaced file has a new key)
    public async ValueTask<byte[]?> GetAsync(FileInfo file, CancellationToken cancellationToken)
    {
        var key = (file.FullName, file.LastWriteTimeUtc.Ticks, file.Length);
        if (cache.TryGetValue(key, out byte[]? thumbnail))
        {
            return thumbnail;
        }

        byte[] image;
        try
        {
            image = await File.ReadAllBytesAsync(file.FullName, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }

        thumbnail = generator.Generate(image, Width);
        if (thumbnail is not null)
        {
            cache.Set(key, thumbnail, new MemoryCacheEntryOptions { Size = thumbnail.Length });
        }

        return thumbnail;
    }
}
