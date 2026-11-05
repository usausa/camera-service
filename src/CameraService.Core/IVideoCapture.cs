namespace CameraService.Core;

// Capture of a camera (VideoCapture), so that a consumer can be given other frames (for example in a test)
public interface IVideoCapture : IAsyncDisposable, IDisposable
{
    // Raised on the capture thread, the frame is valid only while the handler runs
    event EventHandler<FrameCapturedEventArgs>? FrameCaptured;

    event EventHandler<VideoCaptureFailedEventArgs>? Failed;

    Task OpenAsync(VideoFormat format);

    Task StartAsync();
}
