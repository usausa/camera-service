namespace CameraService.Host.Settings;

public sealed class StorageSettingTests
{
    private const string ServiceFolder = "/opt/camera-service";

    // A relative path starts from the state directory when systemd runs the service with StateDirectory
    [Theory]
    [InlineData(null, ServiceFolder)]
    [InlineData("", ServiceFolder)]
    [InlineData("/var/lib/camera-service", "/var/lib/camera-service")]
    [InlineData("/var/lib/camera-service:/var/lib/other", "/var/lib/camera-service")]
    public void BaseFolderIsStateDirectory(string? stateDirectory, string expected)
    {
        // Act
        var folder = StorageSetting.GetBaseFolder(stateDirectory, ServiceFolder);

        // Assert
        Assert.Equal(expected, folder);
    }
}
