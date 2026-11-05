namespace CameraService.Core;

// Cameras of the platform
public interface ICameraProvider
{
    Task<IReadOnlyList<VideoDevice>> GetDevicesAsync();

    IVideoCapture CreateCapture(string deviceId);
}
