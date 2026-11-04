namespace CameraService.Host;

using System.Diagnostics;

// The start of the real application (the test server does not go through Program.cs).
// The settings stop it before a camera is used.
public sealed class ProgramTests
{
    private static readonly TimeSpan ExitTimeout = TimeSpan.FromSeconds(20);

    // A service has no console, so the reason must be in the log file
    [Theory]
    [InlineData("--Camera:IdleTime=99999", "IdleTime")]
    [InlineData("--Camera:IdleTime=abc", "IdleTime")]
    [InlineData("--Cameras:0:Snapshot:Schedule=x", "Schedule")]
    public async Task InvalidSettingIsWrittenToLogFile(string argument, string reason)
    {
        // Arrange
        var directory = Path.Combine(Path.GetTempPath(), "CameraService.IntegrationTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var start = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "CameraService.Host.exe"))
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                ArgumentList =
                {
                    argument,
                    $"--Serilog:WriteTo:1:Args:path={Path.Combine(directory, "app_.log")}"
                },
                Environment =
                {
                    ["ASPNETCORE_ENVIRONMENT"] = "Production",
                    ["DOTNET_ENVIRONMENT"] = "Production"
                }
            };

            // Act
            using var process = Process.Start(start)!;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            timeout.CancelAfter(ExitTimeout);
            var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var error = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(true);
            await Task.WhenAll(output, error).ConfigureAwait(true);

            // Assert
            Assert.Equal(1, process.ExitCode);
            var log = await File.ReadAllTextAsync(Directory.GetFiles(directory, "app_*.log").Single(), timeout.Token).ConfigureAwait(true);
            Assert.Contains("FATL", log, StringComparison.Ordinal);
            Assert.Contains("Service failed.", log, StringComparison.Ordinal);
            Assert.Contains(reason, log, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
