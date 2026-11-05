namespace CameraService.Host.Testing;

using System.Collections.Concurrent;

using CameraService.Core;

// Encoders that write one byte, the first frames fail as the test says
internal sealed class TestJpegEncoderFactory : IJpegEncoderFactory
{
    private readonly ConcurrentQueue<TestJpegEncoder> encoders = new();

    private int failures;

    // Frames that fail from the start
    public int Failures
    {
        set => Volatile.Write(ref failures, value);
    }

    public IReadOnlyList<TestJpegEncoder> Encoders => [.. encoders];

    public IJpegEncoder Create(int quality)
    {
        var encoder = new TestJpegEncoder(this);
        encoders.Enqueue(encoder);
        return encoder;
    }

    public bool TakeFailure() => Interlocked.Decrement(ref failures) >= 0;
}

internal sealed class TestJpegEncoder : IJpegEncoder
{
    private readonly TestJpegEncoderFactory factory;

    public bool IsDisposed { get; private set; }

    public TestJpegEncoder(TestJpegEncoderFactory factory)
    {
        this.factory = factory;
    }

    public void Dispose() => IsDisposed = true;

    public int GetMaxLength(int width, int height) => 1;

    public bool TryEncodeNv12(ReadOnlySpan<byte> y, int yStride, ReadOnlySpan<byte> uv, int uvStride, int width, int height, Span<byte> destination, out int written)
    {
        if (factory.TakeFailure())
        {
            throw new InvalidOperationException("Test encode failure.");
        }

        destination[0] = 0xFF;
        written = 1;
        return true;
    }
}
