namespace CameraService.Host.Endpoints;

using System.Text.Json;
using System.Text.Json.Serialization;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(DeviceResponse[]))]
public sealed partial class ApiJsonContext : JsonSerializerContext;
