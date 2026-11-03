namespace CameraService.Host.Settings;

public sealed class StorageSetting
{
    // Base folder of the saved images, relative to the folder of the service
    [Required]
    public string Path { get; set; } = "Images";
}
