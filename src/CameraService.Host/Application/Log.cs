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

    [LoggerMessage(Level = LogLevel.Information, Message = "Camera setting: id=[{id}], device=[{device}], deviceId=[{deviceId}], format=[{format}]")]
    public static partial void InfoServiceSettingsCameraDevice(this ILogger logger, string id, string device, string deviceId, string format);

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

    [LoggerMessage(Level = LogLevel.Warning, Message = "Camera sent no frame. id=[{id}], timeout=[{timeout}s]")]
    public static partial void WarnCameraFrameTimeout(this ILogger logger, string id, int timeout);

    [LoggerMessage(Level = LogLevel.Error, Message = "Encode failed. id=[{id}]")]
    public static partial void ErrorEncodeFailed(this ILogger logger, Exception ex, string id);

    // Client

    [LoggerMessage(Level = LogLevel.Information, Message = "Client connected. camera=[{camera}], address=[{address}], port=[{port}]")]
    public static partial void InfoClientConnected(this ILogger logger, string camera, IPAddress? address, int port);

    [LoggerMessage(Level = LogLevel.Information, Message = "Client disconnected. camera=[{camera}], address=[{address}], port=[{port}], sent=[{sent}], skipped=[{skipped}]")]
    public static partial void InfoClientDisconnected(this ILogger logger, string camera, IPAddress? address, int port, long sent, long skipped);
}
