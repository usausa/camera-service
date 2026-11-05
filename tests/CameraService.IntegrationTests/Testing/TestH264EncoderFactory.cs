namespace CameraService.Host.Testing;

using CameraService.Core;

// Encoders that need no device and behave as the test says
internal sealed class TestH264EncoderFactory : IH264EncoderFactory
{
    public const string CreateError = "No encoder.";

    private int attempts;

    public bool Fails { get; set; }

    // The encoders stop working with the first frame
    public bool Faults { get; set; }

    // The encoders reject the frames (an unexpected buffer layout)
    public bool RejectsFrames { get; set; }

    // Create waits for it, after Attempts has counted the call
    public ManualResetEventSlim? CreateHold { get; set; }

    public int Attempts => Volatile.Read(ref attempts);

    // The encoder created last
    public TestH264Encoder? Encoder { get; private set; }

    public IH264Encoder Create(H264EncoderOptions options, H264OutputCallback callback)
    {
        Interlocked.Increment(ref attempts);
        CreateHold?.Wait();
        if (Fails)
        {
            throw new InvalidOperationException(CreateError);
        }

        Encoder = new TestH264Encoder(this, callback);
        return Encoder;
    }
}

// Each frame becomes a key frame of a few bytes, on the thread of the caller
internal sealed class TestH264Encoder : IH264Encoder
{
    private readonly TestH264EncoderFactory factory;

    private readonly H264OutputCallback callback;

    public string Name => "Test";

    public bool IsHardware => false;

    public long DroppedFrames { get; set; }

    public bool IsFaulted { get; private set; }

    public int FaultCode => unchecked((int)0x80004005);

    public bool IsDisposed { get; private set; }

    public TestH264Encoder(TestH264EncoderFactory factory, H264OutputCallback callback)
    {
        this.factory = factory;
        this.callback = callback;
    }

    public void Dispose() => IsDisposed = true;

    public void RequestKeyFrame()
    {
    }

    public bool Encode(ReadOnlySpan<byte> y, int yStride, ReadOnlySpan<byte> uv, int uvStride, long timestamp) => EncodeFrame(timestamp);

    public bool EncodeYuy2(ReadOnlySpan<byte> yuy2, int stride, long timestamp) => EncodeFrame(timestamp);

    private bool EncodeFrame(long timestamp)
    {
        if (factory.RejectsFrames)
        {
            throw new ArgumentException("Test layout.");
        }

        if (factory.Faults)
        {
            IsFaulted = true;
            return false;
        }

        callback([0, 0, 0, 1, 0x65], true, timestamp);
        return true;
    }
}
