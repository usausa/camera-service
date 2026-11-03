namespace CameraService.Host.Components.Common;

using CameraService.Host.Services;

using Microsoft.AspNetCore.Components;

// State of a camera (open while it is used)
public sealed partial class StateBadge
{
    [Parameter]
    [EditorRequired]
    public CameraState State { get; set; }

    private string CssClass => State switch
    {
        CameraState.Open => "badge-open",
        CameraState.Opening => "badge-opening",
        CameraState.Failed => "badge-failed",
        _ => "badge-closed"
    };
}
