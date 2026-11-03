namespace CameraService.Host.Endpoints;

public sealed record DeviceResponse(string Id, string Name, IReadOnlyList<string> Formats);
