namespace CameraService.Host.Services;

using CameraService.Core;

// The saved images of the cameras ({camera folder}\yyyyMMdd\HHmmss.jpg) for the pages and the end points.
// A folder that cannot be read has no images (the error is in the log of the saving).
public sealed class ImageStore
{
    // Newest first
    public IReadOnlyList<DateOnly> GetDates(Camera camera)
    {
        try
        {
            return [.. SnapshotStorage.FindDateFolders(camera.Folder).Select(static x => x.Date).Reverse()];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    // Oldest first
    public IReadOnlyList<TimeOnly> GetTimes(Camera camera, DateOnly date)
    {
        try
        {
            return SnapshotStorage.FindTimes(Path.Combine(camera.Folder, SnapshotStorage.FormatDate(date)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    // Null when the image does not exist
    public FileInfo? FindImage(Camera camera, DateOnly date, TimeOnly time)
    {
        var file = new FileInfo(SnapshotStorage.GetImagePath(camera.Folder, date, time));
        return file.Exists ? file : null;
    }

    // The newest image, null when there is none
    public (DateOnly Date, TimeOnly Time)? FindLatest(Camera camera)
    {
        foreach (var date in GetDates(camera))
        {
            var times = GetTimes(camera, date);
            if (times.Count > 0)
            {
                return (date, times[^1]);
            }
        }

        return null;
    }
}
