namespace CameraService.Host.Settings;

public sealed class CameraSetting
{
    // Time to open a camera
    private static readonly TimeSpan PrepareAllowance = TimeSpan.FromSeconds(2);

    // Time to open a camera, also with a slow search of the devices
    private static readonly TimeSpan OpenAllowance = TimeSpan.FromSeconds(10);

    // Seconds a camera stays open after the last user
    [Range(0, 3600)]
    public int IdleTime { get; set; } = 10;

    // Milliseconds of the frames not used after a camera starts (the exposure settles)
    [Range(0, 10000)]
    public int WarmupTime { get; set; } = 1000;

    // Seconds without a frame until a camera is opened again
    [Range(1, 60)]
    public int FrameTimeout { get; set; } = 5;

    // How long before a save the camera is opened
    public TimeSpan PrepareTime => TimeSpan.FromMilliseconds(WarmupTime) + PrepareAllowance;

    // The longest wait for the first frame of a closed camera
    public TimeSpan FirstFrameTimeout => TimeSpan.FromMilliseconds(WarmupTime) + OpenAllowance;
}
