namespace CameraService.Host.Application.HealthChecks;

using CameraService.Host.Services;

using Microsoft.Extensions.Diagnostics.HealthChecks;

// Unhealthy while a camera failed (it does not open, or its frames stopped) or its saving failed.
// A camera that nobody uses keeps its last state (it is opened only when it is used).
public sealed class CameraHealthCheck : IHealthCheck
{
    private readonly CameraManager manager;

    public CameraHealthCheck(CameraManager manager)
    {
        this.manager = manager;
    }

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var errors = manager.Cameras
            .Select(static x => (x.Id, Message: x.ErrorMessage))
            .Where(static x => x.Message is not null)
            .Select(static x => $"{x.Id}: {x.Message}")
            .ToList();
        return Task.FromResult(errors.Count == 0 ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy(String.Join(" ", errors)));
    }
}
