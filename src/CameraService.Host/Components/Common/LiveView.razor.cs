namespace CameraService.Host.Components.Common;

using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

// MJPEG of a camera. The image is created by the script and its request is stopped when the view is removed
// (a browser may keep loading an image that was only removed from the page).
public sealed partial class LiveView : IAsyncDisposable
{
    private readonly string key = Guid.NewGuid().ToString("N");

    private ElementReference container;

    private IJSObjectReference? module;

    //--------------------------------------------------------------------------------
    // Property
    //--------------------------------------------------------------------------------

    [Inject]
    public required IJSRuntime JS { get; set; }

    [Parameter]
    [EditorRequired]
    public string CameraId { get; set; } = string.Empty;

    //--------------------------------------------------------------------------------
    // Lifecycle
    //--------------------------------------------------------------------------------

    // Not while prerendering, so that the stream starts once
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            module = await JS.InvokeAsync<IJSObjectReference>("import", "./js/camera.js");
            await module.InvokeVoidAsync("start", container, key, $"cameras/{Uri.EscapeDataString(CameraId)}/stream.mjpg");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (module is null)
        {
            return;
        }

        try
        {
            await module.InvokeVoidAsync("stop", key);
            await module.DisposeAsync();
        }
        catch (JSDisconnectedException)
        {
            // The page is closed, the request ends with the connection
        }
    }
}
