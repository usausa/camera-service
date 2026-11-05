namespace CameraService.Host.Services;

using System.Buffers;
using System.Diagnostics;
using System.Runtime.InteropServices;

using CameraService.Core;
using CameraService.Host.Settings;

using Mofucat.JobScheduler;

// Saves a frame of a camera on its schedule as {folder}\yyyyMMdd\HHmmss.jpg of the scheduled time, with the time of the frame burned in.
// The camera is opened a little before the time (Prepare). The first frame captured at the scheduled time or later is copied on the capture thread,
// then drawn on and encoded on the thread of the job.
public sealed class SnapshotWriter : ISchedulerJob, IDisposable
{
    private const string TemporaryExtension = ".tmp";

    private const string ProbeFileName = "write-test.tmp";

    // Where the time of the schedule is searched before the time of a run: the first holds a schedule of seconds, the last a run that waited for long
    private static readonly TimeSpan[] ScheduleWindows = [TimeSpan.FromSeconds(2), TimeSpan.FromMinutes(2), TimeSpan.FromHours(2)];

    private readonly ILogger log;

    private readonly CameraChannel channel;

    private readonly IJpegEncoderFactory encoderFactory;

    private readonly LocalTimeProvider timeProvider;

    private readonly SnapshotSetting setting;

    private readonly CronExpression schedule;

    // Includes opening the camera and the warm-up when the camera was not opened before
    private readonly TimeSpan captureTimeout;

    // Completed by a frame on the capture thread
    private CaptureRequest? request;

    private CameraLease? preparedLease;

    // UTC ticks of the scheduled time of the last run
    private long lastRunTicks;

    private int running;

    private int failures;

    private long savedCount;

    public string Id => channel.Id;

    public string Folder { get; }

    public string Schedule => setting.Schedule;

    // The camera is opened this long before the scheduled time
    public TimeSpan PrepareTime { get; }

    public SavedSnapshot? LastSaved { get; private set; }

    // Reason of the last failure, null after a save
    public string? ErrorMessage { get; private set; }

    // Reason why the folder could not be written at the start, null after a save
    public string? FolderError { get; private set; }

    public long SavedCount => Interlocked.Read(ref savedCount);

    public SnapshotWriter(ILogger log, CameraChannel channel, IJpegEncoderFactory encoderFactory, LocalTimeProvider timeProvider, SnapshotSetting setting, CameraSetting cameraSetting, string folder)
    {
        this.log = log;
        this.channel = channel;
        this.encoderFactory = encoderFactory;
        this.timeProvider = timeProvider;
        this.setting = setting;
        schedule = CronExpression.Parse(setting.Schedule);
        captureTimeout = cameraSetting.FirstFrameTimeout;
        Folder = folder;
        PrepareTime = cameraSetting.PrepareTime;
        channel.FrameCaptured += OnFrameCaptured;
    }

    public void Dispose()
    {
        channel.FrameCaptured -= OnFrameCaptured;
        ReleasePrepared();
    }

    //--------------------------------------------------------------------------------
    // Lease
    //--------------------------------------------------------------------------------

    // Opens the camera for the run at the time (called until the run takes the lease)
    public void Prepare(DateTimeOffset time)
    {
        if ((Volatile.Read(ref preparedLease) is not null) || HasStarted(time))
        {
            return;
        }

        var lease = channel.Acquire();
        if (Interlocked.CompareExchange(ref preparedLease, lease, null) is not null)
        {
            lease.Dispose();
            return;
        }

        // The run started meanwhile and did not see this lease: the camera would stay open until the next run
        if (HasStarted(time))
        {
            ReleasePrepared();
        }
    }

    // Lets the camera close when the jobs stop
    public void ReleasePrepared() => Interlocked.Exchange(ref preparedLease, null)?.Dispose();

    private bool HasStarted(DateTimeOffset time) => Volatile.Read(ref lastRunTicks) >= time.UtcTicks;

    //--------------------------------------------------------------------------------
    // Folder
    //--------------------------------------------------------------------------------

    // Written at the start, so that a folder that cannot be written is found in the log at once
    public void CheckFolder()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            var probe = Path.Combine(Folder, ProbeFileName);
            File.WriteAllBytes(probe, []);
            File.Delete(probe);
            FolderError = null;
            log.InfoSnapshotFolder(Id, Folder, SnapshotStorage.GetFreeSpace(Folder) / SnapshotStorage.GigaBytes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            FolderError = ex.Message;
            log.ErrorSnapshotFolder(ex, Id, Folder);
        }
    }

    //--------------------------------------------------------------------------------
    // Job
    //--------------------------------------------------------------------------------

    public async ValueTask ExecuteAsync(DateTimeOffset time, CancellationToken cancellationToken)
    {
        // Skipped while the previous run waits for the camera
        if (Interlocked.Exchange(ref running, 1) != 0)
        {
            log.DebugSnapshotSkipped(Id, time);
            return;
        }

        try
        {
            // Before the lease is taken: a Prepare for this run that comes later sees it
            var scheduled = GetScheduledTime(schedule, time);
            Volatile.Write(ref lastRunTicks, scheduled.UtcTicks);
            using var lease = Interlocked.Exchange(ref preparedLease, null) ?? channel.Acquire();
            await SaveAsync(scheduled, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Volatile.Write(ref running, 0);
        }
    }

    // The last time of the schedule at or before the time of the run
    internal static DateTimeOffset GetScheduledTime(CronExpression schedule, DateTimeOffset time)
    {
        foreach (var window in ScheduleWindows)
        {
            var from = time - window;
            DateTimeOffset? found = null;
            while ((schedule.GetNextOccurrence(from) is { } next) && (next <= time))
            {
                found = next;
                from = next;
            }

            if (found is { } scheduled)
            {
                return scheduled;
            }
        }

        return time;
    }

    private async Task SaveAsync(DateTimeOffset time, CancellationToken cancellationToken)
    {
        var start = Stopwatch.GetTimestamp();

        // Not waited for while the camera waits for the retry after a failure
        if (channel.State == CameraState.Failed)
        {
            Fail(channel.ErrorMessage ?? "Camera failed.", null);
            return;
        }

        CapturedImage captured;
        try
        {
            var result = await CaptureAsync(time, cancellationToken).ConfigureAwait(false);
            if (result is null)
            {
                Fail(channel.ErrorMessage ?? "No frame.", null);
                return;
            }

            captured = result.Value;
        }
        catch (InvalidOperationException ex)
        {
            Fail(ex.Message, ex);
            return;
        }

        using var image = captured.Image;
        try
        {
            var timestamp = setting.Timestamp;
            if (timestamp.Enable)
            {
                var scale = timestamp.Scale > 0 ? timestamp.Scale : TextOverlay.GetScale(image.Height);
                TextOverlay.Draw(image, captured.Time.ToString(timestamp.Format, CultureInfo.InvariantCulture), timestamp.Position, scale);
            }

            using var encoder = encoderFactory.Create(setting.Quality);
            var buffer = ArrayPool<byte>.Shared.Rent(encoder.GetMaxLength(image.Width, image.Height));
            try
            {
                if (!encoder.TryEncodeNv12(image.Y, image.Stride, image.Uv, image.Stride, image.Width, image.Height, buffer, out var written))
                {
                    throw new InvalidOperationException("JPEG encoding failed.");
                }

                var scheduledTime = timeProvider.ToLocal(time);
                var path = await WriteAsync(scheduledTime.DateTime, buffer.AsMemory(0, written), cancellationToken).ConfigureAwait(false);
                Succeed(new SavedSnapshot(path, scheduledTime, captured.Time));
                log.DebugSnapshotSaved(Id, path, written, Stopwatch.GetElapsedTime(start).TotalMilliseconds);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or COMException or ArgumentException or InvalidOperationException)
        {
            Fail(ex.Message, ex);
        }
    }

    // The first frame captured at the time or later, null when no frame comes in time
    private async Task<CapturedImage?> CaptureAsync(DateTimeOffset time, CancellationToken cancellationToken)
    {
        // The run starts a little after the time, and a frame arrives some time after its capture
        var elapsed = timeProvider.GetUtcNow() - time;
        var current = new CaptureRequest(CaptureClock.Now - (elapsed > TimeSpan.Zero ? elapsed : TimeSpan.Zero));
        Volatile.Write(ref request, current);
        var source = current.Source;
        try
        {
            return await source.Task.WaitAsync(captureTimeout, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is TimeoutException or OperationCanceledException)
        {
            Interlocked.CompareExchange(ref request, null, current);

            // A frame that came meanwhile is returned to the pool
            if (!source.TrySetCanceled(cancellationToken) && source.Task.IsCompletedSuccessfully)
            {
                var late = await source.Task.ConfigureAwait(false);
                late.Image.Dispose();
            }

            if (ex is OperationCanceledException)
            {
                throw;
            }

            return null;
        }
    }

    // Written to a temporary file first, so that a reader never sees a part of an image
    private async Task<string> WriteAsync(DateTime time, ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
    {
        var folder = Path.Combine(Folder, SnapshotStorage.GetDateFolderName(time));
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, SnapshotStorage.GetFileName(time));
        var temporary = path + TemporaryExtension;
        await File.WriteAllBytesAsync(temporary, data, cancellationToken).ConfigureAwait(false);
        File.Move(temporary, path, true);
        return path;
    }

    private void Succeed(SavedSnapshot saved)
    {
        LastSaved = saved;
        Interlocked.Increment(ref savedCount);
        var failed = Interlocked.Exchange(ref failures, 0);
        if (failed > 0)
        {
            log.InfoSnapshotRecovered(Id, failed);
        }

        ErrorMessage = null;
        FolderError = null;
    }

    // Logged once until a save succeeds again
    private void Fail(string message, Exception? ex)
    {
        if (Interlocked.Increment(ref failures) == 1)
        {
            log.WarnSnapshotFailed(ex, Id, message);
        }

        ErrorMessage = message;
    }

    //--------------------------------------------------------------------------------
    // Capture thread
    //--------------------------------------------------------------------------------

    private void OnFrameCaptured(object? sender, FrameCapturedEventArgs e)
    {
        var frame = e.Frame;
        var current = Volatile.Read(ref request);
        if ((current is null) || (frame.CaptureTime < current.NotBefore) || (Interlocked.CompareExchange(ref request, null, current) != current))
        {
            return;
        }

        var source = current.Source;
        Nv12Image? image;
        try
        {
            image = Nv12Image.FromFrame(in frame);
        }
        catch (ArgumentException ex)
        {
            source.TrySetException(new InvalidOperationException("Unexpected frame layout.", ex));
            return;
        }

        if (image is null)
        {
            source.TrySetException(new InvalidOperationException("Format not supported."));
            return;
        }

        // Local time of the capture
        var time = timeProvider.GetLocalNow() - (CaptureClock.Now - frame.CaptureTime);
        if (!source.TrySetResult(new CapturedImage(image, time)))
        {
            image.Dispose();
        }
    }

    // Completed by the first frame captured at NotBefore (capture clock) or later
    private sealed class CaptureRequest
    {
        public TaskCompletionSource<CapturedImage> Source { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TimeSpan NotBefore { get; }

        public CaptureRequest(TimeSpan notBefore)
        {
            NotBefore = notBefore;
        }
    }

    private readonly record struct CapturedImage(Nv12Image Image, DateTimeOffset Time);
}
