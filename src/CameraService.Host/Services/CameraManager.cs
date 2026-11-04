namespace CameraService.Host.Services;

using System.Runtime.InteropServices;

using CameraService.Core;
using CameraService.Host.Settings;

// The configured cameras, the timer runs the steps (open, close, retry, watchdog) of each camera and the idle release of its H.264 encoder
public sealed class CameraManager : IHostedService, IAsyncDisposable
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(500);

    // Opening a camera takes about 1 second, the warm-up follows
    private static readonly TimeSpan OpenAllowance = TimeSpan.FromSeconds(2);

    private readonly ILogger<CameraManager> log;

    private readonly ICameraProvider provider;

    private readonly Camera[] cameras;

    private Timer? timer;

    private Task? enumerationTask;

    public IReadOnlyList<Camera> Cameras => cameras;

    public CameraManager(
        ILogger<CameraManager> log,
        ILoggerFactory loggerFactory,
        ICameraProvider provider,
        IJpegEncoderFactory encoderFactory,
        IH264EncoderFactory h264EncoderFactory,
        LocalTimeProvider timeProvider,
        CameraSetting cameraSetting,
        StreamSetting streamSetting,
        H264Setting h264Setting,
        StorageSetting storageSetting,
        IOptions<List<CameraDeviceSetting>> devices)
    {
        this.log = log;
        this.provider = provider;
        var channelLog = loggerFactory.CreateLogger<CameraChannel>();
        var publisherLog = loggerFactory.CreateLogger<JpegPublisher>();
        var h264Log = loggerFactory.CreateLogger<H264Publisher>();
        var snapshotLog = loggerFactory.CreateLogger<SnapshotWriter>();
        var prepareTime = TimeSpan.FromMilliseconds(cameraSetting.WarmupTime) + OpenAllowance;
        cameras = [.. devices.Value.Select(x =>
        {
            var channel = new CameraChannel(channelLog, provider, x, cameraSetting);
            var h264 = h264Setting.Enable ? new H264Publisher(h264Log, channel, h264EncoderFactory, h264Setting) : null;
            var folder = SnapshotStorage.GetCameraFolder(AppContext.BaseDirectory, storageSetting.Path, x.Snapshot.Path, x.Id);
            var snapshot = x.Snapshot.Enable ? new SnapshotWriter(snapshotLog, channel, encoderFactory, timeProvider, x.Snapshot, folder, prepareTime) : null;
            return new Camera(x, channel, new JpegPublisher(publisherLog, channel, encoderFactory, streamSetting), h264, folder, snapshot);
        })];
    }

    // Can be called more than once (the container disposes the singleton for each registration)
    public async ValueTask DisposeAsync()
    {
        if (timer is not null)
        {
            await timer.DisposeAsync().ConfigureAwait(false);
        }

        foreach (var camera in cameras)
        {
            await camera.DisposeAsync().ConfigureAwait(false);
        }
    }

    public Camera? Find(string id) => Array.Find(cameras, x => String.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));

    //--------------------------------------------------------------------------------
    // Hosted service
    //--------------------------------------------------------------------------------

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // The devices are written to the log for the settings (it takes a while, so the start does not wait)
        enumerationTask = LogDevicesAsync();
        timer = new Timer(OnTick, null, TickInterval, TickInterval);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (timer is not null)
        {
            await timer.DisposeAsync().ConfigureAwait(false);
        }

        foreach (var camera in cameras)
        {
            await camera.Channel.StopAsync().ConfigureAwait(false);
        }

        if (enumerationTask is not null)
        {
            try
            {
                await enumerationTask.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // A search for the devices that hangs does not keep the service from stopping
            }
        }
    }

    private void OnTick(object? state)
    {
        foreach (var camera in cameras)
        {
            camera.Channel.Tick();
            camera.H264?.Tick();
        }
    }

    private async Task LogDevicesAsync()
    {
        try
        {
            var devices = await provider.GetDevicesAsync().ConfigureAwait(false);
            foreach (var device in devices)
            {
                log.InfoDeviceFound(device.Name, device.Id, String.Join(", ", device.Formats));
            }
        }
        catch (Exception ex) when (ex is COMException or UnauthorizedAccessException)
        {
            log.ErrorDeviceEnumerationFailed(ex);
        }
    }
}
