namespace CameraService.Host.Settings;

public sealed class CameraSetting
{
    // Seconds a camera stays open after the last user
    [Range(0, 3600)]
    public int IdleTime { get; set; } = 10;

    // Milliseconds of the frames not used after a camera starts (the exposure settles)
    [Range(0, 10000)]
    public int WarmupTime { get; set; } = 1000;

    // Seconds without a frame until a camera is opened again
    [Range(1, 60)]
    public int FrameTimeout { get; set; } = 5;
}
