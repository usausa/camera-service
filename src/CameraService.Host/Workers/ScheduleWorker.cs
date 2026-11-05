namespace CameraService.Host.Workers;

using CameraService.Core;
using CameraService.Host.Services;
using CameraService.Host.Settings;

using Mofucat.JobScheduler;

// Runs the saving of the images of each camera and the deletion of the old images on their cron schedules (the deletion also at the start).
// The scheduler is created here with the time provider of the local time, so that the cron expressions are in the local time.
// The cameras are opened a little before their saves.
public sealed class ScheduleWorker : BackgroundService
{
    private const string RetentionJobName = "retention";

    private static readonly TimeSpan PrepareInterval = TimeSpan.FromMilliseconds(500);

    private readonly ILogger<ScheduleWorker> log;

    private readonly LocalTimeProvider timeProvider;

    private readonly CameraManager manager;

    private readonly ImageRetention retention;

    private readonly RetentionSetting retentionSetting;

    public ScheduleWorker(
        ILogger<ScheduleWorker> log,
        LocalTimeProvider timeProvider,
        CameraManager manager,
        ImageRetention retention,
        RetentionSetting retentionSetting)
    {
        this.log = log;
        this.timeProvider = timeProvider;
        this.manager = manager;
        this.retention = retention;
        this.retentionSetting = retentionSetting;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var scheduler = new JobScheduler(timeProvider);
        scheduler.JobError += OnJobError;
        var snapshots = new List<(SnapshotWriter Writer, IJobHandle Handle)>();
        Task? startup = null;
        try
        {
            foreach (var camera in manager.Cameras)
            {
                if (camera.Snapshot is { } writer)
                {
                    writer.CheckFolder();
                    snapshots.Add((writer, scheduler.AddJob(writer.Schedule, writer, "snapshot-" + camera.Id, MisfirePolicy.Skip)));
                }
            }

            scheduler.AddJob(retentionSetting.Schedule, retention, RetentionJobName, MisfirePolicy.Skip);
            await scheduler.StartAsync().ConfigureAwait(false);
            foreach (var handle in scheduler.JobHandles)
            {
                log.InfoJobScheduled(handle.Name, handle.CronExpression, handle.NextExecutionTime);
            }

            // The images that expired while the service was stopped
            startup = RunRetentionAsync(stoppingToken);

            using var timer = new PeriodicTimer(PrepareInterval);
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                var now = timeProvider.GetUtcNow();
                foreach (var (writer, handle) in snapshots)
                {
                    if ((handle.NextExecutionTime is { } next) && (next - now <= writer.PrepareTime))
                    {
                        writer.Prepare(next);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Stopping
        }
        finally
        {
            // The running jobs are canceled and waited for
            await scheduler.DisposeAsync().ConfigureAwait(false);
            scheduler.JobError -= OnJobError;
            foreach (var (writer, _) in snapshots)
            {
                writer.ReleasePrepared();
            }

            if (startup is not null)
            {
                await startup.ConfigureAwait(false);
            }
        }
    }

    private async Task RunRetentionAsync(CancellationToken cancellationToken)
    {
        try
        {
            await retention.ExecuteAsync(timeProvider.GetUtcNow(), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Stopping
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log.ErrorJobFailed(ex, RetentionJobName);
        }
    }

    private void OnJobError(object? sender, JobErrorEventArgs e) => log.ErrorJobFailed(e.Exception, e.JobName);
}
