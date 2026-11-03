namespace CameraService.Host.Endpoints;

using CameraService.Core;
using CameraService.Host.Services;

using Microsoft.Net.Http.Headers;

// Saved images: the dates and the times of a camera, an image and its thumbnail.
// Only a configured camera, 8 digits of a date and 6 digits of a time are accepted, so no other path can be read.
public static class ImageEndpoints
{
    // The images do not change, the browser checks them again after a day (a replaced file has a new tag)
    private const string CacheControl = "private, max-age=86400";

    private const string JpegContentType = "image/jpeg";

    public static void MapImageEndpoints(this WebApplication app)
    {
        app.MapGet("/api/images/{camera}", HandleDates);
        app.MapGet("/api/images/{camera}/{date}", HandleTimes);
        app.MapGet("/images/{camera}/{date}/{time}.jpg", HandleImage);
        app.MapGet("/images/{camera}/{date}/{time}/thumbnail.jpg", HandleThumbnailAsync);
    }

    // yyyyMMdd, newest first
    private static IResult HandleDates(string camera, CameraManager manager, ImageStore store)
    {
        var found = manager.Find(camera);
        return found is null
            ? Results.NotFound()
            : Results.Ok(store.GetDates(found).Select(SnapshotStorage.FormatDate).ToArray());
    }

    // HHmmss, oldest first
    private static IResult HandleTimes(string camera, string date, CameraManager manager, ImageStore store)
    {
        var found = manager.Find(camera);
        return (found is null) || !SnapshotStorage.TryParseDate(date, out var day)
            ? Results.NotFound()
            : Results.Ok(store.GetTimes(found, day).Select(SnapshotStorage.FormatTime).ToArray());
    }

    private static IResult HandleImage(HttpContext context, string camera, string date, string time, CameraManager manager, ImageStore store)
    {
        var file = FindImage(camera, date, time, manager, store);
        if (file is null)
        {
            return Results.NotFound();
        }

        context.Response.Headers.CacheControl = CacheControl;
        return Results.File(file.FullName, JpegContentType, lastModified: file.LastWriteTimeUtc, entityTag: CreateTag(file, string.Empty));
    }

    private static async Task<IResult> HandleThumbnailAsync(HttpContext context, string camera, string date, string time, CameraManager manager, ImageStore store, ThumbnailCache thumbnails)
    {
        var file = FindImage(camera, date, time, manager, store);
        var thumbnail = file is null ? null : await thumbnails.GetAsync(file, context.RequestAborted);
        if (thumbnail is null)
        {
            return Results.NotFound();
        }

        context.Response.Headers.CacheControl = CacheControl;
        return Results.File(thumbnail, JpegContentType, lastModified: file!.LastWriteTimeUtc, entityTag: CreateTag(file, "-thumbnail"));
    }

    private static FileInfo? FindImage(string camera, string date, string time, CameraManager manager, ImageStore store)
    {
        var found = manager.Find(camera);
        return (found is not null) && SnapshotStorage.TryParseDate(date, out var day) && SnapshotStorage.TryParseTime(time, out var clock)
            ? store.FindImage(found, day, clock)
            : null;
    }

    // Changes when the file is replaced
    private static EntityTagHeaderValue CreateTag(FileInfo file, string suffix) =>
        new(String.Create(CultureInfo.InvariantCulture, $"\"{file.LastWriteTimeUtc.Ticks:x}-{file.Length:x}{suffix}\""));
}
