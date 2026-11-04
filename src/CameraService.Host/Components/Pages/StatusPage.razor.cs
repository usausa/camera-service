namespace CameraService.Host.Components.Pages;

using System.Diagnostics;

using CameraService.Core;
using CameraService.Host.Services;
using CameraService.Host.Settings;

using Microsoft.AspNetCore.Components;

// State of the cameras and of the storage, updated every second while the page is shown
public sealed partial class StatusPage : IDisposable
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(1);

    private readonly CancellationTokenSource disposed = new();

    private IReadOnlyList<StatusRow> rows = [];

    //--------------------------------------------------------------------------------
    // Property
    //--------------------------------------------------------------------------------

    [Inject]
    public required CameraManager Manager { get; set; }

    [Inject]
    public required ImageStore Store { get; set; }

    [Inject]
    public required RetentionSetting Retention { get; set; }

    private string RetentionText =>
        String.Join(
            ", ",
            Retention.Days > 0 ? String.Create(CultureInfo.InvariantCulture, $"{Retention.Days} days are kept") : "no limit of days",
            Retention.MinFreeSpace > 0 ? String.Create(CultureInfo.InvariantCulture, $"{Retention.MinFreeSpace} GB of free space is kept") : "no limit of free space");

    //--------------------------------------------------------------------------------
    // Lifecycle
    //--------------------------------------------------------------------------------

    protected override void OnInitialized()
    {
        // The newest images from the folders once, then from the saves
        rows = [.. Manager.Cameras.Select(x => new StatusRow(x, Store.FindLatest(x)))];
        Update();
        _ = RefreshAsync(disposed.Token);
    }

    public void Dispose()
    {
        disposed.Cancel();
        disposed.Dispose();
    }

    //--------------------------------------------------------------------------------
    // Refresh
    //--------------------------------------------------------------------------------

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var timer = new PeriodicTimer(RefreshInterval);
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                await InvokeAsync(() =>
                {
                    Update();
                    StateHasChanged();
                });
            }
        }
        catch (OperationCanceledException)
        {
            // The page is closed
        }
    }

    private void Update()
    {
        foreach (var row in rows)
        {
            row.Update();
        }
    }

    // The frame rates and the bitrates are counted between the updates
    private sealed class StatusRow
    {
        private const double BitsPerMegabit = 1_000_000;

        private long timestamp;

        private long capturedFrames;

        private long jpegFrames;

        private long jpegBytes;

        private long h264Frames;

        private long h264Bytes;

        public Camera Camera { get; }

        public (DateOnly Date, TimeOnly Time)? Latest { get; private set; }

        public double CaptureRate { get; private set; }

        public double JpegRate { get; private set; }

        // Mbps
        public double JpegBitrate { get; private set; }

        public double H264Rate { get; private set; }

        // Mbps
        public double H264Bitrate { get; private set; }

        public long? FreeSpace { get; private set; }

        public StatusRow(Camera camera, (DateOnly Date, TimeOnly Time)? latest)
        {
            Camera = camera;
            Latest = latest;
        }

        public void Update()
        {
            var now = Stopwatch.GetTimestamp();
            var captured = Camera.Channel.CapturedFrames;
            var jpegFrameCount = Camera.Jpeg.EncodedFrames;
            var jpegByteCount = Camera.Jpeg.EncodedBytes;
            var h264FrameCount = Camera.H264?.EncodedFrames ?? 0;
            var h264ByteCount = Camera.H264?.EncodedBytes ?? 0;
            if (timestamp != 0)
            {
                var seconds = Stopwatch.GetElapsedTime(timestamp, now).TotalSeconds;
                CaptureRate = (captured - capturedFrames) / seconds;
                JpegRate = (jpegFrameCount - jpegFrames) / seconds;
                JpegBitrate = (jpegByteCount - jpegBytes) * 8 / BitsPerMegabit / seconds;
                H264Rate = (h264FrameCount - h264Frames) / seconds;
                H264Bitrate = (h264ByteCount - h264Bytes) * 8 / BitsPerMegabit / seconds;
            }

            timestamp = now;
            capturedFrames = captured;
            jpegFrames = jpegFrameCount;
            jpegBytes = jpegByteCount;
            h264Frames = h264FrameCount;
            h264Bytes = h264ByteCount;
            if (Camera.Snapshot?.LastSaved is { } saved)
            {
                Latest = (DateOnly.FromDateTime(saved.ScheduledTime.DateTime), TimeOnly.FromDateTime(saved.ScheduledTime.DateTime));
            }

            FreeSpace = SnapshotStorage.GetFreeSpace(Camera.Folder);
        }
    }
}
