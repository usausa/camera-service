namespace CameraService.Platform.Linux;

using CameraService.Core;

// Cameras of Video for Linux (V4L2)
public sealed class LinuxCameraProvider : ICameraProvider
{
    public Task<IReadOnlyList<VideoDevice>> GetDevicesAsync() => VideoDeviceCatalog.GetDevicesAsync();

    public IVideoCapture CreateCapture(string deviceId) => new VideoCapture(deviceId);
}
