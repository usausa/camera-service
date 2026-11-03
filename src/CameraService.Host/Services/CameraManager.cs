namespace CameraService.Host.Services;

using System.Runtime.InteropServices;

using CameraService.Core;
using CameraService.Host.Settings;

// The configured cameras, the timer runs the steps (open, close, retry, watchdog) of each camera
public sealed class CameraManager : IHostedService, IAsyncDisposable
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(500);

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
        CameraSetting cameraSetting,
        StreamSetting streamSetting,
        IOptions<List<CameraDeviceSetting>> devices)
    {
        this.log = log;
        this.provider = provider;
        var channelLog = loggerFactory.CreateLogger<CameraChannel>();
        var publisherLog = loggerFactory.CreateLogger<JpegPublisher>();
        cameras = [.. devices.Value.Select(x =>
        {
            var channel = new CameraChannel(channelLog, provider, x, cameraSetting);
            return new Camera(x, channel, new JpegPublisher(publisherLog, channel, encoderFactory, streamSetting));
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
            await enumerationTask.ConfigureAwait(false);
        }
    }

    private void OnTick(object? state)
    {
        foreach (var camera in cameras)
        {
            camera.Channel.Tick();
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
