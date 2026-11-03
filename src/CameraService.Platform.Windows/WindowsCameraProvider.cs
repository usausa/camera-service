namespace CameraService.Platform.Windows;

using CameraService.Core;

// Cameras of MediaFrameReader
public sealed class WindowsCameraProvider : ICameraProvider
{
    public Task<IReadOnlyList<VideoDevice>> GetDevicesAsync() => VideoDeviceCatalog.GetDevicesAsync();

    public IVideoCapture CreateCapture(string deviceId) => new VideoCapture(deviceId);
}
