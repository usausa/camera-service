namespace CameraService.Host.Testing;

using System.Diagnostics;
using System.Runtime.InteropServices;

using CameraService.Core;

// Cameras of the tests: two devices whose captures send a moving gradient of YUY2 at the frame rate of the format
internal sealed class TestCameraProvider : ICameraProvider
{
    public const string DeviceName = "Test camera";

    public const string DeviceId = "test-camera";

    public const string SecondDeviceName = "Test camera 2";

    public const string SecondDeviceId = "test-camera-2";

    public const string VirtualDeviceName = "Virtual camera";

    public const string CreateError = "Test create failure.";

    public const int Width = 320;

    public const int Height = 240;

    public const int FrameRate = 30;

    private static readonly VideoFormat Format = new(VideoPixelFormat.Yuy2, Width, Height, FrameRate);

    private static readonly VideoDevice[] Devices = [new(DeviceId, DeviceName, [Format]), new(SecondDeviceId, SecondDeviceName, [Format])];

    private static readonly VideoDevice VirtualDevice = new("virtual-camera", VirtualDeviceName, [new VideoFormat(VideoPixelFormat.Nv12, 1280, 720, FrameRate)]);

    private TestCapture? capture;

    private int createdCount;

    private int createAttempts;

    // The capture created last (null while no camera has been opened)
    public TestCapture? Capture => Volatile.Read(ref capture);

    // Number of the captures created (each open of the camera creates one)
    public int CreatedCount => Volatile.Read(ref createdCount);

    // Number of the calls to create a capture
    public int CreateAttempts => Volatile.Read(ref createAttempts);

    // The next captures are not created, with an exception that no camera is known to throw
    public bool CreateFails { get; set; }

    // The search for the devices waits for this task, so that a test can see a camera in the Opening state
    public Task DevicesHold { get; set; } = Task.CompletedTask;

    // The next captures open without an error and send nothing
    public bool SendsNoFrames { get; set; }

    // The next capture fails while it starts
    public bool FailsNextStart { get; set; }

    // A device with another format comes first
    public bool HasVirtualDevice { get; set; }

    public async Task<IReadOnlyList<VideoDevice>> GetDevicesAsync()
    {
        await DevicesHold.ConfigureAwait(false);
        return HasVirtualDevice ? [VirtualDevice, .. Devices] : Devices;
    }

    public IVideoCapture CreateCapture(string deviceId)
    {
        Interlocked.Increment(ref createAttempts);
        if (CreateFails)
        {
            throw new ArgumentException(CreateError, nameof(deviceId));
        }

        var created = new TestCapture();
        if (SendsNoFrames)
        {
            created.Pause();
        }

        created.StartFails = FailsNextStart;
        FailsNextStart = false;
        Volatile.Write(ref capture, created);
        Interlocked.Increment(ref createdCount);
        return created;
    }
}

// Raises the frames on a dedicated thread in the same way as VideoCapture
internal sealed class TestCapture : IVideoCapture
{
    private readonly FrameCapturedEventArgs eventArgs = new();

    private readonly ManualResetEventSlim stopRequested = new();

    // Pinned, the frames point to it
    private byte[] buffer = [];

    private Thread? thread;

    private volatile bool paused;

    private int disposed;

    public event EventHandler<FrameCapturedEventArgs>? FrameCaptured;

    public event EventHandler<VideoCaptureFailedEventArgs>? Failed;

    public VideoFormat Format { get; private set; }

    public bool IsDisposed => Volatile.Read(ref disposed) != 0;

    // Closing throws after the capture is released, as a capture whose device was removed
    public bool CloseFails { get; set; }

    // Starting raises the failure and throws, as a capture whose device cannot start
    public bool StartFails { get; set; }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        stopRequested.Set();
        thread?.Join();
        stopRequested.Dispose();
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return CloseFails ? ValueTask.FromException(new InvalidOperationException("Test close failure.")) : ValueTask.CompletedTask;
    }

    public Task OpenAsync(VideoFormat format)
    {
        if (format.PixelFormat != VideoPixelFormat.Yuy2)
        {
            throw new InvalidOperationException("Format not supported.");
        }

        Format = format;
        buffer = GC.AllocateArray<byte>(format.Width * format.Height * 2, true);
        return Task.CompletedTask;
    }

    public Task StartAsync()
    {
        if (StartFails)
        {
            Fail();
            return Task.FromException(new InvalidOperationException("Test start failure."));
        }

        thread = new Thread(Run)
        {
            IsBackground = true,
            Name = "Test camera"
        };
        thread.Start();
        return Task.CompletedTask;
    }

    // Stops sending frames, as a camera that hangs
    public void Pause() => paused = true;

    // Raises the failure of the capture (for example when the device is removed)
    public void Fail() => Failed?.Invoke(this, new VideoCaptureFailedEventArgs("Test failure.", 0x80004005));

    private void Run()
    {
        var pointer = Marshal.UnsafeAddrOfPinnedArrayElement(buffer, 0);
        var plane = new FramePlane(0, Format.Width, Format.Height, Format.Width * 2);
        var interval = Stopwatch.Frequency / Format.FrameRate;
        var start = Stopwatch.GetTimestamp();
        long sequence = 0;
        while (true)
        {
            Fill(sequence);
            sequence++;
            if (!paused)
            {
                var now = CaptureClock.Now;
                eventArgs.Frame = new FrameBuffer(pointer, buffer.Length, VideoPixelFormat.Yuy2, VideoColorRange.Limited, Format.Width, Format.Height, plane, default, sequence, now, now);
                try
                {
                    FrameCaptured?.Invoke(this, eventArgs);
                }
                finally
                {
                    eventArgs.Frame = default;
                }
            }

            // Waits for the time of the next frame
            var wait = Stopwatch.GetElapsedTime(Stopwatch.GetTimestamp(), start + (long)(sequence * interval));
            if (stopRequested.Wait(wait > TimeSpan.Zero ? wait : TimeSpan.Zero))
            {
                return;
            }
        }
    }

    // Diagonal gradient moving by 4 per frame, gray chroma (Y0 U Y1 V)
    private void Fill(long index)
    {
        var shift = (int)(index * 4);
        var width = Format.Width;
        for (var y = 0; y < Format.Height; y++)
        {
            var row = buffer.AsSpan(y * width * 2, width * 2);
            for (var x = 0; x < width; x++)
            {
                row[x * 2] = (byte)(x + y + shift);
                row[(x * 2) + 1] = 128;
            }
        }
    }
}
