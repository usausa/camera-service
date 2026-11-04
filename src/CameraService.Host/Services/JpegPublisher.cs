namespace CameraService.Host.Services;

using System.Runtime.InteropServices;

using CameraService.Core;
using CameraService.Host.Settings;

// Encodes the frames of a camera to JPEG while clients wait (MJPEG and snapshots), at most Stream:Fps frames per second.
// Encoded on the capture thread into one work buffer and copied into a frame of the encoded size,
// so that a frame does not keep a buffer of the largest size. Everything is released when the camera closes.
public sealed class JpegPublisher : IDisposable
{
    private readonly ILogger log;

    private readonly CameraChannel channel;

    private readonly IJpegEncoderFactory encoderFactory;

    private readonly int quality;

    private readonly TimeSpan interval;

    private readonly FramePool pool = new();

    private IJpegEncoder? encoder;

    private Nv12Planes? planes;

    private byte[] work = [];

    private long index;

    private bool encoded;

    private TimeSpan lastCaptureTime;

    private bool failed;

    private long encodedFrames;

    private long encodedBytes;

    public FrameHub Hub { get; } = new();

    public long EncodedFrames => Interlocked.Read(ref encodedFrames);

    public long EncodedBytes => Interlocked.Read(ref encodedBytes);

    public JpegPublisher(ILogger log, CameraChannel channel, IJpegEncoderFactory encoderFactory, StreamSetting setting)
    {
        this.log = log;
        this.channel = channel;
        this.encoderFactory = encoderFactory;
        quality = setting.Quality;
        // A little shorter, so that the frames of the camera are not thinned out more than needed (30 fps to 10 fps is every third)
        interval = TimeSpan.FromSeconds(0.9 / setting.Fps);
        channel.FrameCaptured += OnFrameCaptured;
        channel.Closed += OnClosed;
    }

    public void Dispose()
    {
        channel.FrameCaptured -= OnFrameCaptured;
        channel.Closed -= OnClosed;
        Release();
    }

    // Capture thread
    private void OnFrameCaptured(object? sender, FrameCapturedEventArgs e)
    {
        if (!Hub.HasClients || failed)
        {
            return;
        }

        var frame = e.Frame;
        if (encoded && (frame.CaptureTime - lastCaptureTime < interval))
        {
            return;
        }

        try
        {
            planes ??= new Nv12Planes();
            if (!planes.TryGet(in frame, out var y, out var yStride, out var uv, out var uvStride))
            {
                return;
            }

            encoder ??= encoderFactory.Create(quality);
            var maxLength = encoder.GetMaxLength(frame.Width, frame.Height);
            if (work.Length < maxLength)
            {
                work = GC.AllocateUninitializedArray<byte>(maxLength);
            }

            if (!encoder.TryEncodeNv12(y, yStride, uv, uvStride, frame.Width, frame.Height, work, out var written))
            {
                return;
            }

            encoded = true;
            lastCaptureTime = frame.CaptureTime;
            var jpeg = pool.Rent();
            work.AsSpan(0, written).CopyTo(jpeg.GetBuffer(written));
            jpeg.Commit(++index, written);
            Interlocked.Increment(ref encodedFrames);
            Interlocked.Add(ref encodedBytes, written);
            Hub.Publish(jpeg);
        }
        catch (Exception ex) when (ex is ArgumentException or COMException)
        {
            // Unexpected buffer layout or encoder error, not tried again until the camera opens again
            failed = true;
            log.ErrorEncodeFailed(ex, channel.Id);
        }
    }

    // After the capture is released (no frame handler runs)
    private void OnClosed(object? sender, EventArgs e) => Release();

    private void Release()
    {
        encoder?.Dispose();
        encoder = null;
        planes = null;
        work = [];
        encoded = false;
        failed = false;
        Hub.Clear();
        pool.Clear();
    }
}
