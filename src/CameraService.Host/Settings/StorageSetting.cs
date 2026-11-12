namespace CameraService.Host.Settings;

public sealed class StorageSetting
{
    // Base folder of the saved images, relative to BaseFolder
    [Required]
    public string Path { get; set; } = "Images";

    // Folder a relative path starts from: the state directory when systemd runs the service with StateDirectory, otherwise the folder of the service
    public static string BaseFolder => GetBaseFolder(Environment.GetEnvironmentVariable("STATE_DIRECTORY"), AppContext.BaseDirectory);

    // systemd separates more than one state directory with ':'
    internal static string GetBaseFolder(string? stateDirectory, string serviceFolder) =>
        String.IsNullOrEmpty(stateDirectory) ? serviceFolder : stateDirectory.Split(':')[0];
}
