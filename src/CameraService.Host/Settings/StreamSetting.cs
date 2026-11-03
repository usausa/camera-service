namespace CameraService.Host.Settings;

public sealed class StreamSetting
{
    // Maximum frames per second of the stream (the frames of the camera are thinned out)
    [Range(1, 60)]
    public int Fps { get; set; } = 10;

    // JPEG quality of the stream and the snapshot
    [Range(1, 100)]
    public int Quality { get; set; } = 80;
}
