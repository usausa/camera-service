namespace CameraService.Host.Endpoints;

using CameraService.Core;

public static class DeviceEndpoints
{
    public static void MapDeviceEndpoints(this WebApplication app)
    {
        app.MapGet("/api/devices", HandleDevicesAsync);
    }

    // The cameras of the PC with their formats, for the settings (a camera in use by another application may be missing)
    private static async Task<DeviceResponse[]> HandleDevicesAsync(ICameraProvider provider)
    {
        var devices = await provider.GetDevicesAsync();
        return [.. devices.Select(static x => new DeviceResponse(x.Id, x.Name, [.. x.Formats.Select(static f => f.ToString())]))];
    }
}
