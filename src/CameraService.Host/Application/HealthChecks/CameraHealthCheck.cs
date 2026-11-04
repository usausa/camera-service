namespace CameraService.Host.Application.HealthChecks;

using CameraService.Host.Services;

using Microsoft.Extensions.Diagnostics.HealthChecks;

// Unhealthy from the failure of a camera (it does not open, or its frames stopped) until it sends frames again, or while its saving failed.
// Degraded while H.264 has no encoder: the live view uses MJPEG.
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
        if (errors.Count > 0)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy(String.Join(" ", errors)));
        }

        var warnings = manager.Cameras
            .Select(static x => (x.Id, Message: x.H264?.ErrorMessage))
            .Where(static x => x.Message is not null)
            .Select(static x => $"{x.Id}: {x.Message}")
            .ToList();
        return Task.FromResult(warnings.Count == 0 ? HealthCheckResult.Healthy() : HealthCheckResult.Degraded(String.Join(" ", warnings)));
    }
}
