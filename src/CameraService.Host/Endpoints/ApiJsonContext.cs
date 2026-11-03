namespace CameraService.Host.Endpoints;

using System.Text.Json;
using System.Text.Json.Serialization;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(DeviceResponse[]))]
[JsonSerializable(typeof(string[]))]
public sealed partial class ApiJsonContext : JsonSerializerContext;
