namespace CameraService.Host.Settings;

// Live view in H.264 (WebSocket), the encoder of a camera runs only while its clients are connected
public sealed class H264Setting
{
    public bool Enable { get; set; } = true;

    // kbps (constant bitrate), 0: by the frame size (4000 up to 640x480, 8000 up to 1280x720, 16000 above)
    [Range(0, 100_000)]
    public int Bitrate { get; set; }

    // Seconds between key frames (a client that connects or skips a frame requests one sooner)
    [Range(1, 60)]
    public int KeyFrameInterval { get; set; } = 2;

    // False: the software encoder only
    public bool PreferHardware { get; set; } = true;

    // kbps for the frame size: Bitrate, or by the frame size when it is not set
    public int ResolveBitrate(int width, int height)
    {
        if (Bitrate > 0)
        {
            return Bitrate;
        }

        var pixels = width * height;
        if (pixels <= 640 * 480)
        {
            return 4000;
        }

        return pixels <= 1280 * 720 ? 8000 : 16000;
    }
}
