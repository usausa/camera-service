namespace CameraService.Host.Components.Pages;

using CameraService.Host.Services;

using Microsoft.AspNetCore.Components;

// Temporary page: the cameras with a live view each (replaced by the camera pages later)
public sealed partial class CamerasPage
{
    private readonly HashSet<string> liveCameras = new(StringComparer.OrdinalIgnoreCase);

    //--------------------------------------------------------------------------------
    // Property
    //--------------------------------------------------------------------------------

    [Inject]
    public required CameraManager Manager { get; set; }

    //--------------------------------------------------------------------------------
    // Event
    //--------------------------------------------------------------------------------

    private bool IsLive(string id) => liveCameras.Contains(id);

    private void Toggle(string id)
    {
        if (!liveCameras.Remove(id))
        {
            liveCameras.Add(id);
        }
    }

    private static string SnapshotUrl(string id) => $"cameras/{id}/snapshot.jpg";
}
