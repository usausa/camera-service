namespace CameraService.Host.Components.Pages;

using CameraService.Host.Services;

using Microsoft.AspNetCore.Components;

// Live view (MJPEG) or still images of a camera. A still image opens the camera when it is closed (about 2 seconds).
public sealed partial class CameraPage : IDisposable
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(1);

    private static readonly int[] Intervals = [0, 5, 10, 30, 60];

    private readonly CancellationTokenSource disposed = new();

    private bool live = true;

    // Seconds between the still images, 0 = by the button only
    private int interval;

    private long version;

    private DateTime lastRefresh;

    //--------------------------------------------------------------------------------
    // Property
    //--------------------------------------------------------------------------------

    [Inject]
    public required CameraManager Manager { get; set; }

    [Parameter]
    public string Id { get; set; } = string.Empty;

    //--------------------------------------------------------------------------------
    // Lifecycle
    //--------------------------------------------------------------------------------

    protected override void OnInitialized() => _ = TickAsync(disposed.Token);

    public void Dispose()
    {
        disposed.Cancel();
        disposed.Dispose();
    }

    //--------------------------------------------------------------------------------
    // Event
    //--------------------------------------------------------------------------------

    private void ShowLive() => live = true;

    private void ShowStill()
    {
        live = false;
        Refresh();
    }

    private void Refresh()
    {
        version++;
        lastRefresh = DateTime.UtcNow;
    }

    private void OnIntervalChanged(ChangeEventArgs e) =>
        interval = Int32.TryParse(e.Value?.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out var seconds) ? seconds : 0;

    private static string IntervalText(int seconds) => seconds == 0 ? "Manual" : String.Create(CultureInfo.InvariantCulture, $"Every {seconds} s");

    //--------------------------------------------------------------------------------
    // Timer
    //--------------------------------------------------------------------------------

    // The state of the camera, and the still image at the interval
    private async Task TickAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var timer = new PeriodicTimer(TickInterval);
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                await InvokeAsync(() =>
                {
                    if (!live && (interval > 0) && (DateTime.UtcNow - lastRefresh >= TimeSpan.FromSeconds(interval)))
                    {
                        Refresh();
                    }

                    StateHasChanged();
                });
            }
        }
        catch (OperationCanceledException)
        {
            // The page is closed
        }
    }
}
