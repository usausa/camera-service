namespace CameraService.Host.Components.Common;

using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

// Saved images of a date one by one: ←/→ move, Space plays a time-lapse, Escape closes (the keys come from the script)
public sealed partial class ImageViewer : IAsyncDisposable
{
    private const int PreloadCount = 3;

    private static readonly int[] Speeds = [5, 10, 20, 30];

    private DotNetObjectReference<ImageViewer>? reference;

    private IJSObjectReference? module;

    private CancellationTokenSource? playback;

    private int index;

    private int fps = 10;

    //--------------------------------------------------------------------------------
    // Property
    //--------------------------------------------------------------------------------

    [Inject]
    public required IJSRuntime JS { get; set; }

    [Parameter]
    [EditorRequired]
    public string CameraId { get; set; } = string.Empty;

    [Parameter]
    [EditorRequired]
    public DateOnly Date { get; set; }

    // Oldest first, not empty
    [Parameter]
    [EditorRequired]
    public IReadOnlyList<TimeOnly> Times { get; set; } = [];

    [Parameter]
    public int Index { get; set; }

    // Starts the time-lapse
    [Parameter]
    public bool Play { get; set; }

    [Parameter]
    public EventCallback OnClose { get; set; }

    private TimeOnly Current => Times[index];

    private bool Playing => playback is not null;

    //--------------------------------------------------------------------------------
    // Lifecycle
    //--------------------------------------------------------------------------------

    protected override void OnInitialized()
    {
        index = Math.Clamp(Index, 0, Times.Count - 1);
        if (Play)
        {
            StartPlayback();
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            reference = DotNetObjectReference.Create(this);
            module = await JS.InvokeAsync<IJSObjectReference>("import", "./js/viewer.js");
            await module.InvokeVoidAsync("listen", reference);
        }
    }

    public async ValueTask DisposeAsync()
    {
        StopPlayback();
        if (module is not null)
        {
            try
            {
                await module.InvokeVoidAsync("stop");
                await module.DisposeAsync();
            }
            catch (JSDisconnectedException)
            {
                // The page is closed
            }
        }

        reference?.Dispose();
    }

    //--------------------------------------------------------------------------------
    // Event
    //--------------------------------------------------------------------------------

    [JSInvokable]
    public async Task OnKeyAsync(string key)
    {
        switch (key)
        {
            case "ArrowLeft":
                Move(index - 1);
                break;
            case "ArrowRight":
                Move(index + 1);
                break;
            case "Home":
                Move(0);
                break;
            case "End":
                Move(Times.Count - 1);
                break;
            case " ":
                TogglePlay();
                break;
            case "Escape":
                await CloseAsync();
                return;
        }

        StateHasChanged();
    }

    // Stops the time-lapse
    private void Move(int value)
    {
        StopPlayback();
        index = Math.Clamp(value, 0, Times.Count - 1);
    }

    private void TogglePlay()
    {
        if (Playing)
        {
            StopPlayback();
        }
        else
        {
            StartPlayback();
        }
    }

    private void OnSpeedChanged(ChangeEventArgs e)
    {
        fps = Int32.TryParse(e.Value?.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out var value) && (value > 0) ? value : fps;
        if (Playing)
        {
            StartPlayback();
        }
    }

    private Task CloseAsync()
    {
        StopPlayback();
        return OnClose.InvokeAsync();
    }

    private static string SpeedText(int speed) => String.Create(CultureInfo.InvariantCulture, $"{speed} fps");

    //--------------------------------------------------------------------------------
    // Time-lapse
    //--------------------------------------------------------------------------------

    // From the start when the last image is shown
    private void StartPlayback()
    {
        StopPlayback();
        if (index >= Times.Count - 1)
        {
            index = 0;
        }

        playback = new CancellationTokenSource();
        _ = PlayAsync(fps, playback.Token);
    }

    private void StopPlayback()
    {
        if (playback is not null)
        {
            playback.Cancel();
            playback.Dispose();
            playback = null;
        }
    }

    private async Task PlayAsync(int framesPerSecond, CancellationToken cancellationToken)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1d / framesPerSecond));
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                await InvokeAsync(() =>
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        return;
                    }

                    if (index < Times.Count - 1)
                    {
                        index++;
                    }
                    else
                    {
                        StopPlayback();
                    }

                    StateHasChanged();
                });
            }
        }
        catch (OperationCanceledException)
        {
            // Stopped
        }
    }
}
