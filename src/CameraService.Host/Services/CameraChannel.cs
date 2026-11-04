namespace CameraService.Host.Services;

using System.Diagnostics;
using System.Runtime.InteropServices;

using CameraService.Core;
using CameraService.Host.Settings;

// A configured camera, open while it is used (leases) and closed after the idle time.
// Opening, closing, the retry after a failure and the frame watchdog run from Tick (the timer of CameraManager), one at a time.
// The frames are raised on the capture thread after the warm-up time.
public sealed class CameraChannel : IAsyncDisposable
{
    private const double FrameRateTolerance = 0.5;

    private static readonly TimeSpan[] RetryDelays = [TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60)];

    // A driver that hangs does not keep the steps of the camera, or the stop of the service, waiting
    private static readonly TimeSpan OperationTimeout = TimeSpan.FromSeconds(15);

    private readonly ILogger log;

    private readonly ICameraProvider provider;

    private readonly CameraDeviceSetting setting;

    private readonly TimeSpan idleTime;

    private readonly TimeSpan warmupTime;

    private readonly TimeSpan frameTimeout;

    // One step (open, close) at a time
    private readonly SemaphoreSlim gate = new(1, 1);

    private IVideoCapture? capture;

    private VideoFormat resolvedFormat;

    private int leases;

    // Stopwatch timestamps
    private long idleTimestamp;

    private long openedTimestamp;

    private long lastFrameTimestamp;

    private long retryTimestamp;

    // Failures since the last frame: a camera that opens and then fails, or sends nothing, goes on with the longer delays
    private int failures;

    // Set by the capture (MediaCapture.Failed)
    private int failed;

    private bool stopped;

    private int disposed;

    private long capturedFrames;

    private long openedFrames;

    // Raised on the capture thread after the warm-up, the frame is valid only while the handler runs
    public event EventHandler<FrameCapturedEventArgs>? FrameCaptured;

    // Raised after the capture is released (no frame handler runs)
    public event EventHandler? Closed;

    public string Id => setting.Id;

    public string Name => setting.DisplayName;

    public CameraState State { get; private set; }

    public string? ErrorMessage { get; private set; }

    // The camera failed and has sent no frame since (also while it tries to open again)
    public bool IsFailing => Volatile.Read(ref failures) > 0;

    // Found when the camera opens first, searched again after a failure
    public VideoDevice? Device { get; private set; }

    public VideoFormat Format => setting.ToVideoFormat();

    public int Leases => Volatile.Read(ref leases);

    public long CapturedFrames => Interlocked.Read(ref capturedFrames);

    public CameraChannel(ILogger log, ICameraProvider provider, CameraDeviceSetting setting, CameraSetting cameraSetting)
    {
        this.log = log;
        this.provider = provider;
        this.setting = setting;
        idleTime = TimeSpan.FromSeconds(cameraSetting.IdleTime);
        warmupTime = TimeSpan.FromMilliseconds(cameraSetting.WarmupTime);
        frameTimeout = TimeSpan.FromSeconds(cameraSetting.FrameTimeout);
    }

    // Can be called more than once (the container disposes the singletons)
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) == 0)
        {
            await StopAsync().ConfigureAwait(false);
            gate.Dispose();
        }
    }

    public async Task StopAsync()
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            stopped = true;
            await CloseAsync(false).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    //--------------------------------------------------------------------------------
    // Lease
    //--------------------------------------------------------------------------------

    // The camera opens when the first lease is taken, and closes after the idle time when the last one is released
    public CameraLease Acquire()
    {
        if (Interlocked.Increment(ref leases) == 1)
        {
            // Opened without waiting for the timer
            Tick();
        }

        return new CameraLease(this);
    }

    internal void Release()
    {
        Volatile.Write(ref idleTimestamp, Stopwatch.GetTimestamp());
        Interlocked.Decrement(ref leases);
    }

    //--------------------------------------------------------------------------------
    // Step
    //--------------------------------------------------------------------------------

    // Skipped while a step runs (the next tick checks again)
    public void Tick()
    {
        if (gate.Wait(0))
        {
            _ = RunAsync();
        }
    }

    private async Task RunAsync()
    {
        try
        {
            while (!stopped)
            {
                var now = Stopwatch.GetTimestamp();
                if (capture is null)
                {
                    // Not retried while nobody uses the camera
                    if ((Volatile.Read(ref leases) == 0) || (now < Volatile.Read(ref retryTimestamp)))
                    {
                        break;
                    }

                    await OpenAsync().ConfigureAwait(false);
                }
                else if (Interlocked.Exchange(ref failed, 0) != 0)
                {
                    await CloseAsync(true).ConfigureAwait(false);
                }
                else if (Stopwatch.GetElapsedTime(Volatile.Read(ref lastFrameTimestamp), now) > frameTimeout)
                {
                    log.WarnCameraFrameTimeout(Id, (int)frameTimeout.TotalSeconds);
                    ErrorMessage = "Camera sent no frame.";
                    await CloseAsync(true).ConfigureAwait(false);
                }
                else if ((Volatile.Read(ref leases) == 0) && (Stopwatch.GetElapsedTime(Volatile.Read(ref idleTimestamp), now) >= idleTime))
                {
                    await CloseAsync(false).ConfigureAwait(false);
                }
                else
                {
                    break;
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Not a failure known to come from a camera, tried again in the same way so that the camera does not stay in a step
            log.ErrorCameraStepFailed(ex, Id);
            Device = null;
            ScheduleRetry();
            ErrorMessage = ex.Message;
            State = CameraState.Failed;
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task OpenAsync()
    {
        State = CameraState.Opening;
        var start = Stopwatch.GetTimestamp();
        IVideoCapture? video = null;
        try
        {
            if (Device is null)
            {
                await ResolveAsync().WaitAsync(OperationTimeout).ConfigureAwait(false);
            }

            var device = Device!;
            video = provider.CreateCapture(device.Id);

            // A failure of an earlier capture (while it started, or just before it closed) does not count for this one
            Volatile.Write(ref failed, 0);
            video.FrameCaptured += OnFrameCaptured;
            video.Failed += OnFailed;
            await video.OpenAsync(resolvedFormat).WaitAsync(OperationTimeout).ConfigureAwait(false);

            // The warm-up and the watchdog count from the start of the frames
            var started = Stopwatch.GetTimestamp();
            Volatile.Write(ref openedTimestamp, started);
            Volatile.Write(ref lastFrameTimestamp, started);
            Interlocked.Exchange(ref openedFrames, 0);
            await video.StartAsync().WaitAsync(OperationTimeout).ConfigureAwait(false);

            capture = video;
            video = null;
            Volatile.Write(ref retryTimestamp, 0);
            State = CameraState.Open;
            log.InfoCameraOpened(Id, device.Name, resolvedFormat.ToString(), Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        }
        catch (Exception ex) when (ex is COMException or UnauthorizedAccessException or InvalidOperationException or TimeoutException)
        {
            // The device may have been changed or plugged in again
            Device = null;
            var delay = ScheduleRetry();
            ErrorMessage = ex.Message;
            State = CameraState.Failed;
            log.WarnCameraOpenFailed(ex, Id, delay.TotalSeconds);
        }
        finally
        {
            if (video is not null)
            {
                video.FrameCaptured -= OnFrameCaptured;
                video.Failed -= OnFailed;
                await DisposeCaptureAsync(video).ConfigureAwait(false);
            }
        }
    }

    private async Task ResolveAsync()
    {
        var devices = await provider.GetDevicesAsync().ConfigureAwait(false);
        var device = FindDevice(devices) ?? throw new InvalidOperationException("Camera not found.");
        var format = setting.ToVideoFormat();
        var found = device.Formats.FirstOrDefault(x =>
            (x.PixelFormat == format.PixelFormat) &&
            (x.Width == format.Width) &&
            (x.Height == format.Height) &&
            (Math.Abs(x.FrameRate - format.FrameRate) < FrameRateTolerance));
        if (found.Width == 0)
        {
            throw new InvalidOperationException("Format not supported.");
        }

        Device = device;
        resolvedFormat = found;
    }

    private VideoDevice? FindDevice(IReadOnlyList<VideoDevice> devices)
    {
        if (setting.DeviceId.Length > 0)
        {
            return devices.FirstOrDefault(x => String.Equals(x.Id, setting.DeviceId, StringComparison.OrdinalIgnoreCase));
        }

        if (setting.Device.Length > 0)
        {
            return devices.FirstOrDefault(x => String.Equals(x.Name, setting.Device, StringComparison.OrdinalIgnoreCase));
        }

        return devices.Count > 0 ? devices[0] : null;
    }

    // failure: opened again after the retry delay while the camera is used
    private async Task CloseAsync(bool failure)
    {
        var video = capture;
        if (video is null)
        {
            return;
        }

        capture = null;
        video.FrameCaptured -= OnFrameCaptured;
        video.Failed -= OnFailed;
        var error = await DisposeCaptureAsync(video).ConfigureAwait(false);

        // A capture that did not close may hold the device: the camera counts as failed until it sends frames again
        if (failure || (error is not null))
        {
            ErrorMessage ??= error;
            Device = null;
            ScheduleRetry();
            State = CameraState.Failed;
        }
        else
        {
            State = CameraState.Closed;
        }

        Closed?.Invoke(this, EventArgs.Empty);
        log.InfoCameraClosed(Id, Interlocked.Read(ref openedFrames));
    }

    // Waits for the running frame handler. Returns the reason when the capture could not be closed (for example the device was removed), otherwise null.
    private async Task<string?> DisposeCaptureAsync(IVideoCapture video)
    {
        try
        {
            await video.DisposeAsync().AsTask().WaitAsync(OperationTimeout).ConfigureAwait(false);
            return null;
        }
        catch (Exception ex) when (ex is COMException or UnauthorizedAccessException or InvalidOperationException or TimeoutException)
        {
            log.ErrorCameraCloseFailed(ex, Id);
            return ex.Message;
        }
    }

    private TimeSpan ScheduleRetry()
    {
        var count = Interlocked.Increment(ref failures);
        var delay = RetryDelays[Math.Min(count - 1, RetryDelays.Length - 1)];
        Volatile.Write(ref retryTimestamp, Stopwatch.GetTimestamp() + (long)(delay.TotalSeconds * Stopwatch.Frequency));
        return delay;
    }

    //--------------------------------------------------------------------------------
    // Capture thread
    //--------------------------------------------------------------------------------

    private void OnFrameCaptured(object? sender, FrameCapturedEventArgs e)
    {
        var now = Stopwatch.GetTimestamp();
        Volatile.Write(ref lastFrameTimestamp, now);
        Interlocked.Increment(ref capturedFrames);
        Interlocked.Increment(ref openedFrames);

        // The camera works again when it sends a frame
        if (Volatile.Read(ref failures) != 0)
        {
            ErrorMessage = null;
            Interlocked.Exchange(ref failures, 0);
        }

        if (Stopwatch.GetElapsedTime(Volatile.Read(ref openedTimestamp), now) < warmupTime)
        {
            return;
        }

        FrameCaptured?.Invoke(this, e);
    }

    // Closed and opened again by the next step
    private void OnFailed(object? sender, VideoCaptureFailedEventArgs e)
    {
        log.ErrorCameraFailed(Id, e.Message, e.Code);
        ErrorMessage = e.Message;
        Volatile.Write(ref failed, 1);
    }
}

// Keeps the camera open until disposed
public sealed class CameraLease : IDisposable
{
    private CameraChannel? channel;

    internal CameraLease(CameraChannel channel)
    {
        this.channel = channel;
    }

    public void Dispose() => Interlocked.Exchange(ref channel, null)?.Release();
}
