namespace CameraService.Core;

// Cameras of the platform (replaced by a test camera in the tests)
public interface ICameraProvider
{
    Task<IReadOnlyList<VideoDevice>> GetDevicesAsync();

    IVideoCapture CreateCapture(string deviceId);
}
