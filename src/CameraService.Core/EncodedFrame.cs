namespace CameraService.Core;

using System.Diagnostics;

// Encoded once and shared by all clients, returned to the pool when all references are released
public sealed class EncodedFrame
{
    private readonly FramePool pool;

    private byte[] buffer = [];

    private int length;

    private int references;

    // Publish order
    public long Index { get; private set; }

    // Stopwatch timestamp
    public long PublishTimestamp { get; private set; }

    // H.264 frame that does not depend on the previous frames
    public bool IsKeyFrame { get; private set; }

    public ReadOnlyMemory<byte> Data => buffer.AsMemory(0, length);

    internal EncodedFrame(FramePool pool)
    {
        this.pool = pool;
    }

    // Space for the data
    public Span<byte> GetBuffer(int capacity)
    {
        if (buffer.Length < capacity)
        {
            buffer = GC.AllocateUninitializedArray<byte>(capacity);
        }

        return buffer;
    }

    public void Commit(long index, int dataLength) => Commit(index, dataLength, false);

    public void Commit(long index, int dataLength, bool keyFrame)
    {
        Index = index;
        length = dataLength;
        IsKeyFrame = keyFrame;
        PublishTimestamp = Stopwatch.GetTimestamp();
    }

    // Rented with one reference
    internal void Initialize() => references = 1;

    internal void AddReference() => Interlocked.Increment(ref references);

    public void Release()
    {
        var count = Interlocked.Decrement(ref references);
        Debug.Assert(count >= 0, "Released more than referenced.");
        if (count == 0)
        {
            pool.Return(this);
        }
    }
}

public sealed class FramePool
{
    private readonly Lock sync = new();

    private readonly Stack<EncodedFrame> frames = new();

    private int created;

    // Number of frames allocated
    public int Created => Volatile.Read(ref created);

    public EncodedFrame Rent()
    {
        EncodedFrame? frame;
        lock (sync)
        {
            frames.TryPop(out frame);
        }

        if (frame is null)
        {
            frame = new EncodedFrame(this);
            Interlocked.Increment(ref created);
        }

        frame.Initialize();
        return frame;
    }

    // Drops the frames that are not in use (the camera closed), the frames in use come back later
    public void Clear()
    {
        lock (sync)
        {
            Interlocked.Add(ref created, -frames.Count);
            frames.Clear();
        }
    }

    internal void Return(EncodedFrame frame)
    {
        lock (sync)
        {
            frames.Push(frame);
        }
    }
}
