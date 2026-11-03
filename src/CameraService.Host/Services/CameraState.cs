namespace CameraService.Host.Services;

public enum CameraState
{
    Closed,
    Opening,
    Open,
    // Waiting for the retry after a failure
    Failed
}
