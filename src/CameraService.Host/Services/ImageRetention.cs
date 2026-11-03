namespace CameraService.Host.Services;

using System.Diagnostics;

using CameraService.Core;
using CameraService.Host.Settings;

using Mofucat.JobScheduler;

// Deletes the date folders of the saved images: older than Retention:Days, and the oldest while the free space of a drive is below Retention:MinFreeSpace.
// Today's folders are never deleted, and only the folders named by a date (yyyyMMdd) are deleted.
public sealed class ImageRetention : ISchedulerJob
{
    private const double GigaBytes = 1024d * 1024 * 1024;

    private readonly ILogger<ImageRetention> log;

    private readonly LocalTimeProvider timeProvider;

    private readonly RetentionSetting setting;

    // Folders of all the cameras, also the ones that do not save now (their old images are deleted too)
    private readonly string[] folders;

    private int running;

    public ImageRetention(ILogger<ImageRetention> log, LocalTimeProvider timeProvider, RetentionSetting setting, CameraManager manager)
    {
        this.log = log;
        this.timeProvider = timeProvider;
        this.setting = setting;
        folders = [.. manager.Cameras.Select(static x => x.Folder).Distinct(StringComparer.OrdinalIgnoreCase)];
    }

    public async ValueTask ExecuteAsync(DateTimeOffset time, CancellationToken cancellationToken)
    {
        // The run at the start and the scheduled one may overlap
        if (Interlocked.Exchange(ref running, 1) != 0)
        {
            return;
        }

        try
        {
            var today = DateOnly.FromDateTime(timeProvider.ToLocal(time).DateTime);
            // Deleting many files takes a while, the scheduler is not blocked by it
            await Task.Run(() => Run(today, cancellationToken), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Volatile.Write(ref running, 0);
        }
    }

    private void Run(DateOnly today, CancellationToken cancellationToken)
    {
        var start = Stopwatch.GetTimestamp();
        var deleted = 0;
        var kept = new List<DateFolder>();
        foreach (var folder in folders)
        {
            IReadOnlyList<DateFolder> dates;
            try
            {
                dates = SnapshotStorage.FindDateFolders(folder);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                log.ErrorImagesDeleteFailed(ex, folder);
                continue;
            }

            foreach (var date in dates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!SnapshotStorage.IsExpired(date.Date, today, setting.Days))
                {
                    kept.Add(date);
                }
                else if (Delete(date.Path))
                {
                    deleted++;
                    log.InfoImagesDeleted(date.Path);
                }
            }
        }

        if (setting.MinFreeSpace > 0)
        {
            foreach (var volume in folders.Select(SnapshotStorage.GetVolume).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                deleted += DeleteForSpace(volume, kept.Where(x => String.Equals(SnapshotStorage.GetVolume(x.Path), volume, StringComparison.OrdinalIgnoreCase)), today, cancellationToken);
            }
        }

        if (deleted > 0)
        {
            log.InfoRetentionCompleted(deleted, Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        }
    }

    // Deletes the oldest days of the drive until the free space is enough
    private int DeleteForSpace(string volume, IEnumerable<DateFolder> dates, DateOnly today, CancellationToken cancellationToken)
    {
        var minimum = setting.MinFreeSpace * GigaBytes;
        var free = SnapshotStorage.GetFreeSpace(volume);
        if (free is null)
        {
            // Unknown (for example a network path)
            return 0;
        }

        var deleted = 0;
        foreach (var date in SnapshotStorage.GetDeletable(dates, today))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (free >= minimum)
            {
                break;
            }

            if (Delete(date.Path))
            {
                deleted++;
                log.WarnImagesDeletedForSpace(date.Path, free.Value / GigaBytes);
            }

            free = SnapshotStorage.GetFreeSpace(volume) ?? 0;
        }

        if (free < minimum)
        {
            log.WarnFreeSpaceLow(volume, free.Value / GigaBytes, setting.MinFreeSpace);
        }

        return deleted;
    }

    private bool Delete(string path)
    {
        try
        {
            Directory.Delete(path, true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log.ErrorImagesDeleteFailed(ex, path);
            return false;
        }
    }
}
