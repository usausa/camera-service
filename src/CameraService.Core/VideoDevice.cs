namespace CameraService.Core;

public sealed record VideoDevice(string Id, string Name, IReadOnlyList<VideoFormat> Formats)
{
    public override string ToString() => Name;
}
