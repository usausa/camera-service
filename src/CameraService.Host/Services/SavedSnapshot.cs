namespace CameraService.Host.Services;

// A saved image: the file is named by the scheduled time, the capture time of the frame is burned in (both in the local time)
public sealed record SavedSnapshot(string Path, DateTimeOffset ScheduledTime, DateTimeOffset CaptureTime);
