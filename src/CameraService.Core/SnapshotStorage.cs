namespace CameraService.Core;

using System.Globalization;

// Folders and names of the saved images ({camera folder}\yyyyMMdd\HHmmss.jpg), and the date folders to delete
public static class SnapshotStorage
{
    public const string Extension = ".jpg";

    // Bytes of the free space values in the settings and the logs
    public const double GigaBytes = 1024d * 1024 * 1024;

    private const string DateFormat = "yyyyMMdd";

    private const string TimeFormat = "HHmmss";

    // The storage path is relative to the base folder (the folder of the service), the camera path (the camera id when empty) is relative to the storage path.
    // One folder has one form, so that two folders can be compared.
    public static string GetCameraFolder(string baseFolder, string storagePath, string cameraPath, string cameraId) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(cameraPath.Length > 0 ? cameraPath : cameraId, Path.GetFullPath(storagePath, baseFolder)));

    public static string GetDateFolderName(DateTime time) => FormatDate(DateOnly.FromDateTime(time));

    public static string GetFileName(DateTime time) => FormatTime(TimeOnly.FromDateTime(time)) + Extension;

    public static string GetImagePath(string cameraFolder, DateOnly date, TimeOnly time) => Path.Combine(cameraFolder, FormatDate(date), FormatTime(time) + Extension);

    public static string FormatDate(DateOnly date) => date.ToString(DateFormat, CultureInfo.InvariantCulture);

    public static string FormatTime(TimeOnly time) => time.ToString(TimeFormat, CultureInfo.InvariantCulture);

    // 8 digits of a valid date
    public static bool TryParseDate(string value, out DateOnly date)
    {
        if ((value.Length != DateFormat.Length) || value.AsSpan().ContainsAnyExceptInRange('0', '9'))
        {
            date = default;
            return false;
        }

        return DateOnly.TryParseExact(value, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    }

    // 6 digits of a valid time
    public static bool TryParseTime(string value, out TimeOnly time)
    {
        if ((value.Length != TimeFormat.Length) || value.AsSpan().ContainsAnyExceptInRange('0', '9'))
        {
            time = default;
            return false;
        }

        return TimeOnly.TryParseExact(value, TimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out time);
    }

    // The date folders of a camera folder by date, none when the folder does not exist
    public static IReadOnlyList<DateFolder> FindDateFolders(string cameraFolder)
    {
        var folders = new List<DateFolder>();
        if (!Directory.Exists(cameraFolder))
        {
            return folders;
        }

        foreach (var path in Directory.EnumerateDirectories(cameraFolder))
        {
            if (TryParseDate(Path.GetFileName(path), out var date))
            {
                folders.Add(new DateFolder(path, date));
            }
        }

        folders.Sort(static (x, y) => x.Date.CompareTo(y.Date));
        return folders;
    }

    // The times of the images in a date folder (HHmmss.jpg), none when the folder does not exist
    public static IReadOnlyList<TimeOnly> FindTimes(string dateFolder)
    {
        var times = new List<TimeOnly>();
        if (!Directory.Exists(dateFolder))
        {
            return times;
        }

        foreach (var path in Directory.EnumerateFiles(dateFolder, "*" + Extension))
        {
            if (TryParseTime(Path.GetFileNameWithoutExtension(path), out var time))
            {
                times.Add(time);
            }
        }

        times.Sort();
        return times;
    }

    // Older than the days to keep (today is the first day), never when days is 0
    public static bool IsExpired(DateOnly date, DateOnly today, int days) => (days > 0) && (date <= today.AddDays(-days));

    // The folders that can be deleted for free space, oldest first (today and later are kept)
    public static IReadOnlyList<DateFolder> GetDeletable(IEnumerable<DateFolder> folders, DateOnly today) =>
        [.. folders.Where(x => x.Date < today).OrderBy(static x => x.Date).ThenBy(static x => x.Path, StringComparer.OrdinalIgnoreCase)];

    // Root of the drive of the path ("D:\", or the share of a network path)
    public static string GetVolume(string path) => Path.GetPathRoot(Path.GetFullPath(path)) ?? path;

    // Free space of the drive of the path, null when it is unknown (for example a network path)
    public static long? GetFreeSpace(string path)
    {
        var root = GetVolume(path);
        try
        {
            return new DriveInfo(root).AvailableFreeSpace;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
