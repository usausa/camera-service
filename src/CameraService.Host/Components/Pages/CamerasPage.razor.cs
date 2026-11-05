namespace CameraService.Host.Components.Pages;

using CameraService.Host.Components.Common;
using CameraService.Host.Services;

using Microsoft.AspNetCore.Components;

// The cameras with their newest saved image and their state.
// The live view is on the page of a camera.
public sealed partial class CamerasPage : IDisposable
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(10);

    private PageTimer? timer;

    private IReadOnlyList<CameraItem> items = [];

    //--------------------------------------------------------------------------------
    // Property
    //--------------------------------------------------------------------------------

    [Inject]
    public required CameraManager Manager { get; set; }

    [Inject]
    public required ImageStore Store { get; set; }

    //--------------------------------------------------------------------------------
    // Lifecycle
    //--------------------------------------------------------------------------------

    protected override void OnInitialized()
    {
        Update();
        timer = new PageTimer(RefreshInterval, () => InvokeAsync(OnTick));
    }

    public void Dispose() => timer?.Dispose();

    //--------------------------------------------------------------------------------
    // Refresh
    //--------------------------------------------------------------------------------

    // The newest images and the states, while the page is shown
    private void OnTick()
    {
        Update();
        StateHasChanged();
    }

    private void Update() => items = [.. Manager.Cameras.Select(x => new CameraItem(x, Store.FindLatest(x), x.ErrorMessage))];

    private sealed record CameraItem(Camera Camera, (DateOnly Date, TimeOnly Time)? Latest, string? Error);
}
