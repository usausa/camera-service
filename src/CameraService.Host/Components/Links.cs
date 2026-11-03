namespace CameraService.Host.Components;

using CameraService.Core;

// Paths of the pages and of the end points used by the pages (relative to the base of the page)
public static class Links
{
    public static string Camera(string id) => $"cameras/{Uri.EscapeDataString(id)}";

    public static string Images(string id) => $"{Camera(id)}/images";

    public static string Images(string id, DateOnly date) => $"{Images(id)}/{SnapshotStorage.FormatDate(date)}";

    public static string Stream(string id) => $"{Camera(id)}/stream.mjpg";

    // The version makes the browser load a new image
    public static string Snapshot(string id, long version) => String.Create(CultureInfo.InvariantCulture, $"{Camera(id)}/snapshot.jpg?v={version}");

    public static string Image(string id, DateOnly date, TimeOnly time) => $"{ImageBase(id, date, time)}.jpg";

    public static string Thumbnail(string id, DateOnly date, TimeOnly time) => $"{ImageBase(id, date, time)}/thumbnail.jpg";

    public static string DownloadName(string id, DateOnly date, TimeOnly time) => $"{id}_{SnapshotStorage.FormatDate(date)}_{SnapshotStorage.FormatTime(time)}.jpg";

    private static string ImageBase(string id, DateOnly date, TimeOnly time) =>
        $"images/{Uri.EscapeDataString(id)}/{SnapshotStorage.FormatDate(date)}/{SnapshotStorage.FormatTime(time)}";
}
