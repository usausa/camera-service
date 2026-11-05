namespace CameraService.Host.Components.Common;

// Calls an action on a timer until the page that owns it disposes it (the action goes through InvokeAsync of the page)
public sealed class PageTimer : IDisposable
{
    private readonly CancellationTokenSource disposed = new();

    public PageTimer(TimeSpan interval, Func<Task> action)
    {
        _ = RunAsync(interval, action, disposed.Token);
    }

    public void Dispose()
    {
        disposed.Cancel();
        disposed.Dispose();
    }

    private static async Task RunAsync(TimeSpan interval, Func<Task> action, CancellationToken cancellationToken)
    {
        try
        {
            using var timer = new PeriodicTimer(interval);
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                await action();
            }
        }
        catch (OperationCanceledException)
        {
            // The page is closed
        }
    }
}
