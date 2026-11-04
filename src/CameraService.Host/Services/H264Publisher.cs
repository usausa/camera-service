namespace CameraService.Host.Services;

using System.Diagnostics;
using System.Numerics;

using CameraService.Core;
using CameraService.Host.Settings;

// Encodes the frames of a camera to H.264 while WebSocket clients are connected.
// The capture thread passes the frame to the encoder, and the output callback (encoder thread) publishes the access unit with its header.
// The encoder is created in the background for the first client (again when the frame format changes),
// and released when no client has been connected for the idle time (checked by the timer of the manager, also while the camera is closed).
// An encoder that fails (to be created, or later) is released and tried again after the retry interval.
public sealed class H264Publisher : IAsyncDisposable
{
    private const int MinimumCapacity = 16 * 1024;

    // Frames passed to the encoder and not output yet (the encoder holds a few frames at most)
    private const int PendingCount = 8;

    private const int BitsPerKilobit = 1000;

    // NVENC takes about 0.7 seconds to be created, so it is kept for a while
    private static readonly TimeSpan IdleTime = TimeSpan.FromSeconds(10);

    // The device may come back or free its sessions
    private static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(10);

    // A slow client that skips frames must not make every frame a key frame
    private static readonly TimeSpan KeyFrameRequestInterval = TimeSpan.FromMilliseconds(500);

    private readonly ILogger log;

    private readonly CameraChannel channel;

    private readonly IH264EncoderFactory encoderFactory;

    private readonly H264Setting setting;

    private readonly FramePool pool = new();

    // Guards the encoder against the release while a frame is passed
    private readonly Lock encoderSync = new();

    private readonly Lock pendingSync = new();

    private readonly PendingFrame[] pendingFrames = new PendingFrame[PendingCount];

    private IH264Encoder? encoder;

    // Format of the encoder (also while it is created)
    private EncoderFormat encoderFormat;

    // Creation or release of the encoder
    private Task? replaceTask;

    // Format the encoder failed with, tried again after the retry interval
    private EncoderFormat failedFormat;

    // Stopwatch timestamp
    private long failedTimestamp;

    // Canceled when an encoder fails, then replaced for the next clients
    private CancellationTokenSource failure = new();

    private bool stopped;

    private long releasedDroppedFrames;

    // Stopwatch timestamp
    private long lastClientTimestamp;

    private int pendingIndex;

    // Stopwatch timestamp
    private long lastKeyFrameRequest;

    private long encodedFrames;

    private long encodedBytes;

    private long keyFrames;

    public FrameHub Hub { get; } = new();

    // Encoder in use, null while none is created (before the first client, after the idle time or a failure)
    public string? EncoderName { get; private set; }

    public bool IsHardware { get; private set; }

    // Why the last encoder could not be created or stopped working, until an encoder is created (the last state while no client is connected)
    public string? ErrorMessage { get; private set; }

    public long EncodedFrames => Interlocked.Read(ref encodedFrames);

    // Access units without the headers
    public long EncodedBytes => Interlocked.Read(ref encodedBytes);

    public long KeyFrames => Interlocked.Read(ref keyFrames);

    // Frames not encoded because the encoder was busy
    public long DroppedFrames
    {
        get
        {
            lock (encoderSync)
            {
                return releasedDroppedFrames + (encoder?.DroppedFrames ?? 0);
            }
        }
    }

    public H264Publisher(ILogger log, CameraChannel channel, IH264EncoderFactory encoderFactory, H264Setting setting)
    {
        this.log = log;
        this.channel = channel;
        this.encoderFactory = encoderFactory;
        this.setting = setting;
        channel.FrameCaptured += OnFrameCaptured;
    }

    // Waits for the running creation, which releases its encoder
    public async ValueTask DisposeAsync()
    {
        channel.FrameCaptured -= OnFrameCaptured;
        IH264Encoder? current;
        Task? task;
        lock (encoderSync)
        {
            stopped = true;
            current = encoder;
            encoder = null;
            ClearEncoderName();
            task = replaceTask;
        }

        if (task is not null)
        {
            await task.ConfigureAwait(false);
        }

        current?.Dispose();
        Hub.Clear();
        failure.Dispose();
    }

    // A new client tries an encoder that failed without waiting for the retry interval.
    // The token is canceled when the next encoder cannot be created or stops working: the connected clients get no frames from it.
    public CancellationToken PrepareClient()
    {
        lock (encoderSync)
        {
            failedFormat = default;
            return failure.Token;
        }
    }

    // Timer of the manager: releases the encoder when no client has been connected for the idle time
    public void Tick()
    {
        if (Hub.HasClients)
        {
            Interlocked.Exchange(ref lastClientTimestamp, Stopwatch.GetTimestamp());
            return;
        }

        lock (encoderSync)
        {
            if (!stopped && (encoder is not null) && replaceTask is not { IsCompleted: false } &&
                (Stopwatch.GetElapsedTime(Interlocked.Read(ref lastClientTimestamp)) > IdleTime))
            {
                Replace(default);
            }
        }
    }

    //--------------------------------------------------------------------------------
    // Key frame
    //--------------------------------------------------------------------------------

    // Requested by a client that waits for a key frame (a new client or a client that skipped a frame),
    // a request within the interval from the previous one is ignored (the client requests again with the next frame)
    public void RequestKeyFrame()
    {
        var last = Interlocked.Read(ref lastKeyFrameRequest);
        var now = Stopwatch.GetTimestamp();
        if ((last != 0) && (Stopwatch.GetElapsedTime(last, now) < KeyFrameRequestInterval))
        {
            return;
        }

        if (Interlocked.CompareExchange(ref lastKeyFrameRequest, now, last) == last)
        {
            // A new encoder starts with a key frame
            Volatile.Read(ref encoder)?.RequestKeyFrame();
        }
    }

    //--------------------------------------------------------------------------------
    // Encoder
    //--------------------------------------------------------------------------------

    // In the lock: releases the encoder and creates one for the format (default: none) in the background
    private void Replace(EncoderFormat format)
    {
        var previous = encoder;
        if (previous is not null)
        {
            releasedDroppedFrames += previous.DroppedFrames;
        }

        encoder = null;
        ClearEncoderName();
        encoderFormat = format;
        replaceTask = Task.Run(() => ReplaceEncoder(previous, format));
    }

    // In the lock: no encoder in use
    private void ClearEncoderName()
    {
        EncoderName = null;
        IsHardware = false;
    }

    private void ReplaceEncoder(IH264Encoder? previous, EncoderFormat format)
    {
        if (previous is not null)
        {
            // Waits for the remaining output
            previous.Dispose();
            log.InfoH264EncoderReleased(channel.Id, previous.Name);
        }

        var width = format.Width;
        var height = format.Height;
        if (width == 0)
        {
            // No more frames of the encoder
            Hub.Clear();
            pool.Clear();
            return;
        }

        var frameRate = Math.Max(1, (int)Math.Round(channel.Format.FrameRate));
        var options = new H264EncoderOptions
        {
            Width = width,
            Height = height,
            FrameRate = frameRate,
            Bitrate = setting.ResolveBitrate(width, height) * BitsPerKilobit,
            KeyFrameInterval = setting.KeyFrameInterval * frameRate,
            PreferHardware = setting.PreferHardware,
            FullRange = format.FullRange
        };
        var start = Stopwatch.GetTimestamp();
        IH264Encoder? created = null;
        try
        {
            created = encoderFactory.Create(options, (data, keyFrame, timestamp) => OnEncoded(data, keyFrame, timestamp, format));
            log.InfoH264EncoderCreated(channel.Id, created.Name, created.IsHardware, width, height, format.FullRange, frameRate, options.Bitrate / BitsPerKilobit, options.KeyFrameInterval, Stopwatch.GetElapsedTime(start).TotalMilliseconds);
            lock (encoderSync)
            {
                if (!stopped)
                {
                    encoder = created;
                    EncoderName = created.Name;
                    IsHardware = created.IsHardware;
                    ErrorMessage = null;
                    failedFormat = default;
                    created = null;
                }
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            log.ErrorH264EncoderCreateFailed(ex, channel.Id, width, height);
            lock (encoderSync)
            {
                SetFailed(format, ex.Message);
            }
        }
        finally
        {
            // Stopped while creating
            created?.Dispose();
        }
    }

    // In the lock: no encoder for the format until the retry interval has passed
    private void SetFailed(EncoderFormat format, string message)
    {
        failedFormat = format;
        failedTimestamp = Stopwatch.GetTimestamp();
        ErrorMessage = message;

        // The connected clients stop waiting (on another thread, not in the lock)
        var failed = failure;
        failure = new CancellationTokenSource();
        _ = failed.CancelAsync();
    }

    //--------------------------------------------------------------------------------
    // Capture thread
    //--------------------------------------------------------------------------------

    // The release after the idle time is done by the timer
    private void OnFrameCaptured(object? sender, FrameCapturedEventArgs e)
    {
        if (!Hub.HasClients)
        {
            return;
        }

        var frame = e.Frame;
        Interlocked.Exchange(ref lastClientTimestamp, Stopwatch.GetTimestamp());
        lock (encoderSync)
        {
            // Not encoded while the encoder is created or released
            if (stopped || replaceTask is { IsCompleted: false })
            {
                return;
            }

            // YUV without range information is treated as video range
            var format = new EncoderFormat(frame.Width, frame.Height, frame.ColorRange == VideoColorRange.Full);
            if ((encoder is null) || (encoderFormat != format))
            {
                if ((encoder is not null) || (failedFormat != format) || (Stopwatch.GetElapsedTime(failedTimestamp) > RetryInterval))
                {
                    Replace(format);
                }

                return;
            }

            Encode(encoder, frame);
        }
    }

    // In the lock. The encoder takes the range of the frame (written to the stream).
    private void Encode(IH264Encoder current, in FrameBuffer frame)
    {
        var format = frame.PixelFormat;
        if ((format != VideoPixelFormat.Nv12) && (format != VideoPixelFormat.Yuy2))
        {
            return;
        }

        try
        {
            // The capture time (100 ns units) is returned with the output and identifies the frame
            var timestamp = frame.CaptureTime.Ticks;
            lock (pendingSync)
            {
                pendingFrames[pendingIndex] = new PendingFrame(timestamp, frame.Sequence);
                pendingIndex = (pendingIndex + 1) % PendingCount;
            }

            if (format == VideoPixelFormat.Yuy2)
            {
                // Converted into the input sample of the encoder
                current.EncodeYuy2(frame.GetPlaneSpan(frame.Plane0), frame.Plane0.Stride, timestamp);
            }
            else
            {
                current.Encode(frame.GetPlaneSpan(frame.Plane0), frame.Plane0.Stride, frame.GetPlaneSpan(frame.Plane1), frame.Plane1.Stride, timestamp);
            }
        }
        catch (ArgumentException ex)
        {
            // Unexpected buffer layout
            log.ErrorEncodeFailed(ex, channel.Id);
            return;
        }

        if (current.IsFaulted)
        {
            // Released, and created again after the retry interval
            log.ErrorH264EncoderFailed(channel.Id, current.Name, current.FaultCode);
            SetFailed(encoderFormat, String.Create(CultureInfo.InvariantCulture, $"H.264 encoder failed. code=[0x{current.FaultCode:X8}]"));
            Replace(default);
        }
    }

    //--------------------------------------------------------------------------------
    // Encoder thread
    //--------------------------------------------------------------------------------

    private void OnEncoded(ReadOnlySpan<byte> data, bool keyFrame, long timestamp, EncoderFormat format)
    {
        var sequence = GetSequence(timestamp);
        var encoded = pool.Rent();
        try
        {
            // The pooled buffers grow to the largest size (key frames)
            var length = H264Header.Length + data.Length;
            var buffer = encoded.GetBuffer((int)BitOperations.RoundUpToPowerOf2((uint)Math.Max(length, MinimumCapacity)));

            // Unix time of the capture, calculated from the capture clock
            var now = CaptureClock.Now;
            var unixNow = (DateTime.UtcNow - DateTime.UnixEpoch).TotalMilliseconds;
            var captureTime = unixNow - (now - TimeSpan.FromTicks(timestamp)).TotalMilliseconds;
            H264Header.Write(buffer, format.Width, format.Height, sequence, keyFrame, format.FullRange, captureTime, unixNow);
            data.CopyTo(buffer[H264Header.Length..]);
            encoded.Commit(Interlocked.Increment(ref encodedFrames), length, keyFrame);
            Hub.Publish(encoded);
            encoded = null;
        }
        finally
        {
            encoded?.Release();
        }

        Interlocked.Add(ref encodedBytes, data.Length);
        if (keyFrame)
        {
            Interlocked.Increment(ref keyFrames);
        }
    }

    // Sequence of the camera frame of the timestamp, 0 when it is not found
    private long GetSequence(long timestamp)
    {
        lock (pendingSync)
        {
            foreach (var pending in pendingFrames)
            {
                if (pending.Timestamp == timestamp)
                {
                    return pending.Sequence;
                }
            }
        }

        return 0;
    }

    private readonly record struct EncoderFormat(int Width, int Height, bool FullRange);

    private readonly record struct PendingFrame(long Timestamp, long Sequence);
}
