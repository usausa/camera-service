namespace CameraService.Host.Application;

using System.Net;

internal static partial class Log
{
    // Startup

    [LoggerMessage(Level = LogLevel.Information, Message = "Service start.")]
    public static partial void InfoServiceStart(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Runtime: os=[{osDescription}], framework=[{frameworkDescription}], rid=[{runtimeIdentifier}]")]
    public static partial void InfoServiceSettingsRuntime(this ILogger logger, string osDescription, string frameworkDescription, string runtimeIdentifier);

    [LoggerMessage(Level = LogLevel.Information, Message = "Environment: version=[{version}], directory=[{directory}]")]
    public static partial void InfoServiceSettingsEnvironment(this ILogger logger, Version? version, string directory);

    [LoggerMessage(Level = LogLevel.Information, Message = "Camera: idleTime=[{idleTime}], warmupTime=[{warmupTime}], frameTimeout=[{frameTimeout}]")]
    public static partial void InfoServiceSettingsCamera(this ILogger logger, int idleTime, int warmupTime, int frameTimeout);

    [LoggerMessage(Level = LogLevel.Information, Message = "Stream: fps=[{fps}], quality=[{quality}]")]
    public static partial void InfoServiceSettingsStream(this ILogger logger, int fps, int quality);

    [LoggerMessage(Level = LogLevel.Information, Message = "H264: enable=[{enable}], bitrate=[{bitrate}], keyFrameInterval=[{keyFrameInterval}], preferHardware=[{preferHardware}]")]
    public static partial void InfoServiceSettingsH264(this ILogger logger, bool enable, int bitrate, int keyFrameInterval, bool preferHardware);

    [LoggerMessage(Level = LogLevel.Information, Message = "Storage: path=[{path}]")]
    public static partial void InfoServiceSettingsStorage(this ILogger logger, string path);

    [LoggerMessage(Level = LogLevel.Information, Message = "Retention: schedule=[{schedule}], days=[{days}], minFreeSpace=[{minFreeSpace}GB]")]
    public static partial void InfoServiceSettingsRetention(this ILogger logger, string schedule, int days, int minFreeSpace);

    [LoggerMessage(Level = LogLevel.Information, Message = "Camera setting: id=[{id}], device=[{device}], deviceId=[{deviceId}], format=[{format}]")]
    public static partial void InfoServiceSettingsCameraDevice(this ILogger logger, string id, string device, string deviceId, string format);

    [LoggerMessage(Level = LogLevel.Information, Message = "Snapshot setting: id=[{id}], enable=[{enable}], schedule=[{schedule}], quality=[{quality}], timestamp=[{timestamp}]")]
    public static partial void InfoServiceSettingsSnapshot(this ILogger logger, string id, bool enable, string schedule, int quality, string timestamp);

    [LoggerMessage(Level = LogLevel.Warning, Message = "No camera is configured.")]
    public static partial void WarnNoCamera(this ILogger logger);

    // Device

    [LoggerMessage(Level = LogLevel.Information, Message = "Device found. name=[{name}], id=[{id}], formats=[{formats}]")]
    public static partial void InfoDeviceFound(this ILogger logger, string name, string id, string formats);

    [LoggerMessage(Level = LogLevel.Error, Message = "Device enumeration failed.")]
    public static partial void ErrorDeviceEnumerationFailed(this ILogger logger, Exception ex);

    // Camera

    [LoggerMessage(Level = LogLevel.Information, Message = "Camera opened. id=[{id}], device=[{device}], format=[{format}], elapsed=[{elapsed:F0}ms]")]
    public static partial void InfoCameraOpened(this ILogger logger, string id, string device, string format, double elapsed);

    [LoggerMessage(Level = LogLevel.Information, Message = "Camera closed. id=[{id}], frames=[{frames}]")]
    public static partial void InfoCameraClosed(this ILogger logger, string id, long frames);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Camera open failed. id=[{id}], retry=[{retry}s]")]
    public static partial void WarnCameraOpenFailed(this ILogger logger, Exception ex, string id, double retry);

    [LoggerMessage(Level = LogLevel.Error, Message = "Camera failed. id=[{id}], message=[{message}], code=[{code}]")]
    public static partial void ErrorCameraFailed(this ILogger logger, string id, string message, uint code);

    [LoggerMessage(Level = LogLevel.Error, Message = "Camera close failed. id=[{id}]")]
    public static partial void ErrorCameraCloseFailed(this ILogger logger, Exception ex, string id);

    [LoggerMessage(Level = LogLevel.Error, Message = "Camera step failed. id=[{id}]")]
    public static partial void ErrorCameraStepFailed(this ILogger logger, Exception ex, string id);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Camera sent no frame. id=[{id}], timeout=[{timeout}s]")]
    public static partial void WarnCameraFrameTimeout(this ILogger logger, string id, int timeout);

    [LoggerMessage(Level = LogLevel.Error, Message = "Encode failed. id=[{id}]")]
    public static partial void ErrorEncodeFailed(this ILogger logger, Exception ex, string id);

    // H.264

    [LoggerMessage(Level = LogLevel.Information, Message = "H.264 encoder created. id=[{id}], name=[{name}], hardware=[{hardware}], size=[{width}x{height}], fullRange=[{fullRange}], fps=[{fps}], bitrate=[{bitrate}], keyFrameInterval=[{keyFrameInterval}], elapsed=[{elapsed:F1}ms]")]
    public static partial void InfoH264EncoderCreated(this ILogger logger, string id, string name, bool hardware, int width, int height, bool fullRange, int fps, int bitrate, int keyFrameInterval, double elapsed);

    [LoggerMessage(Level = LogLevel.Error, Message = "H.264 encoder create failed. id=[{id}], size=[{width}x{height}]")]
    public static partial void ErrorH264EncoderCreateFailed(this ILogger logger, Exception ex, string id, int width, int height);

    [LoggerMessage(Level = LogLevel.Information, Message = "H.264 encoder released. id=[{id}], name=[{name}]")]
    public static partial void InfoH264EncoderReleased(this ILogger logger, string id, string name);

    [LoggerMessage(Level = LogLevel.Error, Message = "H.264 encoder failed. id=[{id}], name=[{name}], code=[0x{code:X8}]")]
    public static partial void ErrorH264EncoderFailed(this ILogger logger, string id, string name, int code);

    // Schedule

    [LoggerMessage(Level = LogLevel.Information, Message = "Job scheduled. name=[{name}], schedule=[{schedule}], next=[{next}]")]
    public static partial void InfoJobScheduled(this ILogger logger, string name, string schedule, DateTimeOffset? next);

    [LoggerMessage(Level = LogLevel.Error, Message = "Job failed. name=[{name}]")]
    public static partial void ErrorJobFailed(this ILogger logger, Exception ex, string name);

    // Snapshot

    [LoggerMessage(Level = LogLevel.Information, Message = "Snapshot folder. id=[{id}], path=[{path}], free=[{free:F1}GB]")]
    public static partial void InfoSnapshotFolder(this ILogger logger, string id, string path, double? free);

    [LoggerMessage(Level = LogLevel.Error, Message = "Snapshot folder is not writable. id=[{id}], path=[{path}]")]
    public static partial void ErrorSnapshotFolder(this ILogger logger, Exception ex, string id, string path);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Snapshot saved. id=[{id}], path=[{path}], size=[{size}], elapsed=[{elapsed:F0}ms]")]
    public static partial void DebugSnapshotSaved(this ILogger logger, string id, string path, int size, double elapsed);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Snapshot skipped while the previous one runs. id=[{id}], time=[{time}]")]
    public static partial void DebugSnapshotSkipped(this ILogger logger, string id, DateTimeOffset time);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Snapshot failed. id=[{id}], message=[{message}]")]
    public static partial void WarnSnapshotFailed(this ILogger logger, Exception? ex, string id, string message);

    [LoggerMessage(Level = LogLevel.Information, Message = "Snapshot saved again. id=[{id}], failures=[{failures}]")]
    public static partial void InfoSnapshotRecovered(this ILogger logger, string id, int failures);

    // Retention

    [LoggerMessage(Level = LogLevel.Information, Message = "Images deleted. path=[{path}]")]
    public static partial void InfoImagesDeleted(this ILogger logger, string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Images deleted for free space. path=[{path}], free=[{free:F1}GB]")]
    public static partial void WarnImagesDeletedForSpace(this ILogger logger, string path, double free);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Free space is low. volume=[{volume}], free=[{free:F1}GB], minimum=[{minimum}GB]")]
    public static partial void WarnFreeSpaceLow(this ILogger logger, string volume, double free, int minimum);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Free space became unknown, no more images are deleted. volume=[{volume}]")]
    public static partial void WarnFreeSpaceUnknown(this ILogger logger, string volume);

    [LoggerMessage(Level = LogLevel.Error, Message = "Images delete failed. path=[{path}]")]
    public static partial void ErrorImagesDeleteFailed(this ILogger logger, Exception ex, string path);

    [LoggerMessage(Level = LogLevel.Information, Message = "Retention completed. deleted=[{deleted}], elapsed=[{elapsed:F0}ms]")]
    public static partial void InfoRetentionCompleted(this ILogger logger, int deleted, double elapsed);

    // Client

    [LoggerMessage(Level = LogLevel.Information, Message = "Client connected. camera=[{camera}], type=[{type}], address=[{address}], port=[{port}]")]
    public static partial void InfoClientConnected(this ILogger logger, string camera, string type, IPAddress? address, int port);

    [LoggerMessage(Level = LogLevel.Information, Message = "Client disconnected. camera=[{camera}], type=[{type}], address=[{address}], port=[{port}], sent=[{sent}], skipped=[{skipped}]")]
    public static partial void InfoClientDisconnected(this ILogger logger, string camera, string type, IPAddress? address, int port, long sent, long skipped);
}
