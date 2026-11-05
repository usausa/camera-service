namespace CameraService.Host.Application.HealthChecks;

using CameraService.Core;
using CameraService.Host.Services;
using CameraService.Host.Settings;

using Microsoft.Extensions.Diagnostics.HealthChecks;

// Unhealthy while a folder of the saved images cannot be written (checked at the start, cleared by a save),
// degraded while the free space of its drive is below Retention:MinFreeSpace
public sealed class StorageHealthCheck : IHealthCheck
{
    private readonly CameraManager manager;

    private readonly RetentionSetting setting;

    public StorageHealthCheck(CameraManager manager, RetentionSetting setting)
    {
        this.manager = manager;
        this.setting = setting;
    }

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var writers = manager.Cameras.Select(static x => x.Snapshot).OfType<SnapshotWriter>().ToList();
        var errors = writers.Where(static x => x.FolderError is not null).Select(static x => $"{x.Id}: {x.FolderError}").ToList();
        if (errors.Count > 0)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy(String.Join(" ", errors)));
        }

        if (setting.MinFreeSpace > 0)
        {
            var low = new List<string>();
            foreach (var volume in writers.Select(static x => SnapshotStorage.GetVolume(x.Folder)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (SnapshotStorage.GetFreeSpace(volume) is { } free && (free < setting.MinFreeSpace * SnapshotStorage.GigaBytes))
                {
                    low.Add(String.Create(CultureInfo.InvariantCulture, $"{volume}: {free / SnapshotStorage.GigaBytes:F1}GB free."));
                }
            }

            if (low.Count > 0)
            {
                return Task.FromResult(HealthCheckResult.Degraded(String.Join(" ", low)));
            }
        }

        return Task.FromResult(HealthCheckResult.Healthy());
    }
}
