namespace CameraService.Host.Components.Pages;

using CameraService.Host.Services;

using Microsoft.AspNetCore.Components;

// The cameras with their newest saved image and their state.
// No live view here: a browser opens at most 6 connections to a server over HTTP/1.1, the live view is on the page of a camera.
public sealed partial class CamerasPage : IDisposable
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(10);

    private readonly CancellationTokenSource disposed = new();

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
        _ = RefreshAsync(disposed.Token);
    }

    public void Dispose()
    {
        disposed.Cancel();
        disposed.Dispose();
    }

    //--------------------------------------------------------------------------------
    // Refresh
    //--------------------------------------------------------------------------------

    // The newest images and the states, while the page is shown
    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var timer = new PeriodicTimer(RefreshInterval);
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                await InvokeAsync(() =>
                {
                    Update();
                    StateHasChanged();
                });
            }
        }
        catch (OperationCanceledException)
        {
            // The page is closed
        }
    }

    private void Update() => items = [.. Manager.Cameras.Select(x => new CameraItem(x, Store.FindLatest(x), x.ErrorMessage))];

    private sealed record CameraItem(Camera Camera, (DateOnly Date, TimeOnly Time)? Latest, string? Error);
}
