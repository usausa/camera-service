namespace CameraService.Host.Components.Common;

using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

// Live view of a camera: H.264 where the browser can decode it, otherwise MJPEG (decided by the script).
// The canvas and the connection are created by the script and stopped when the view is removed.
public sealed partial class LiveView : IAsyncDisposable
{
    private readonly string key = Guid.NewGuid().ToString("N");

    private ElementReference container;

    private IJSObjectReference? module;

    private bool disposed;

    //--------------------------------------------------------------------------------
    // Property
    //--------------------------------------------------------------------------------

    [Inject]
    public required IJSRuntime JS { get; set; }

    [Parameter]
    [EditorRequired]
    public string CameraId { get; set; } = string.Empty;

    // The stream of the camera has H.264
    [Parameter]
    public bool H264 { get; set; }

    //--------------------------------------------------------------------------------
    // Lifecycle
    //--------------------------------------------------------------------------------

    // Not while prerendering, so that the stream starts once
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
        {
            return;
        }

        try
        {
            var imported = await JS.InvokeAsync<IJSObjectReference>("import", "./js/camera.js");
            if (disposed)
            {
                // Removed while the script was loaded, the element is gone
                await imported.DisposeAsync();
                return;
            }

            module = imported;
            await module.InvokeVoidAsync("start", container, key, Links.Stream(CameraId), H264 ? Links.H264(CameraId) : null);
        }
        catch (JSDisconnectedException)
        {
            // The page is closed
        }
    }

    public async ValueTask DisposeAsync()
    {
        disposed = true;
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
