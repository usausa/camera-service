namespace CameraService.Host.Settings;

using CameraService.Core;

// One camera of the Cameras list
public sealed class CameraDeviceSetting
{
    // Used in the URLs and the folder names
    [Required]
    [RegularExpression("^[A-Za-z0-9_-]+$")]
    public string Id { get; set; } = string.Empty;

    // Display name, the Id when empty
    public string Name { get; set; } = string.Empty;

    // Name of the device, the first device with the format when both Device and DeviceId are empty
    public string Device { get; set; } = string.Empty;

    // Identifies one of the devices with the same name (shown in the log and /api/devices)
    public string DeviceId { get; set; } = string.Empty;

    // The streams encode only these formats
    [AllowedValues(VideoPixelFormat.Yuy2, VideoPixelFormat.Nv12)]
    public VideoPixelFormat Format { get; set; } = VideoPixelFormat.Yuy2;

    [Range(1, 8192)]
    public int Width { get; set; } = 640;

    [Range(1, 8192)]
    public int Height { get; set; } = 480;

    [Range(1, 240)]
    public int Fps { get; set; } = 30;

    public SnapshotSetting Snapshot { get; set; } = new();

    public string DisplayName => Name.Length > 0 ? Name : Id;

    public VideoFormat ToVideoFormat() => new(Format, Width, Height, Fps);
}
