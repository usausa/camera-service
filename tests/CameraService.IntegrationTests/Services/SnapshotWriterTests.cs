namespace CameraService.Host.Services;

using System.Globalization;

using CameraService.Host.Testing;

using Microsoft.Extensions.Logging;

using Mofucat.JobScheduler;

public sealed class SnapshotWriterTests
{
    // 6 fields with the seconds
    private const string EverySecond = "* * * * * *";

    // Does not run while a test runs (it runs at the start of a year)
    private const string OnceAYear = "0 0 1 1 *";

    private const string SecondCameraId = "second";

    [Fact]
    public Task ImagesAreNamedByScheduledTime() =>
        ServiceTestServer.RunAsync(Saving(), static async (server, cancellationToken) =>
        {
            // Arrange
            var start = DateTime.Now;

            // Act
            await ServiceTestServer.WaitUntilAsync(() => server.Snapshot.SavedCount >= 2, cancellationToken).ConfigureAwait(true);

            // Assert ({camera}\yyyyMMdd\HHmmss.jpg of the seconds of the schedule, JPEG of the size of the camera)
            var files = Directory.GetFiles(Path.Combine(server.StoragePath, ServiceTestServer.CameraId), "*.jpg", SearchOption.AllDirectories);
            Assert.True(files.Length >= 2);
            foreach (var file in files)
            {
                var name = Path.GetFileName(Path.GetDirectoryName(file)) + Path.GetFileNameWithoutExtension(file);
                var time = DateTime.ParseExact(name, "yyyyMMddHHmmss", CultureInfo.InvariantCulture);
                Assert.InRange(time, start.AddSeconds(-5), DateTime.Now);
                var jpeg = await File.ReadAllBytesAsync(file, cancellationToken).ConfigureAwait(true);
                Assert.Equal((TestCameraProvider.Width, TestCameraProvider.Height), MjpegReader.GetJpegSize(jpeg));
            }

            Assert.Null(server.Snapshot.ErrorMessage);
        });

    [Fact]
    public Task FrameIsCapturedAtScheduledTime() =>
        ServiceTestServer.RunAsync(Saving(), static async (server, cancellationToken) =>
        {
            // Act
            await ServiceTestServer.WaitUntilAsync(() => server.Snapshot.SavedCount >= 1, cancellationToken).ConfigureAwait(true);
            var saved = server.Snapshot.LastSaved!;

            // Assert (the burned-in time is the local time of a frame captured at the scheduled time or a little later)
            Assert.Equal(TimeZoneInfo.Local.GetUtcOffset(saved.ScheduledTime), saved.ScheduledTime.Offset);
            Assert.Equal(saved.ScheduledTime.Offset, saved.CaptureTime.Offset);
            Assert.InRange(saved.CaptureTime - saved.ScheduledTime, TimeSpan.Zero, TimeSpan.FromSeconds(2));
            Assert.Equal(saved.ScheduledTime.ToString("HHmmss", CultureInfo.InvariantCulture) + ".jpg", Path.GetFileName(saved.Path));
        });

    [Fact]
    public Task CamerasSaveIntoOwnFolders() =>
        ServiceTestServer.RunAsync(
            Saving(new Dictionary<string, string?>
            {
                ["Cameras:1:Id"] = SecondCameraId,
                ["Cameras:1:Device"] = TestCameraProvider.SecondDeviceName,
                ["Cameras:1:Width"] = TestCameraProvider.Width.ToString(CultureInfo.InvariantCulture),
                ["Cameras:1:Height"] = TestCameraProvider.Height.ToString(CultureInfo.InvariantCulture),
                ["Cameras:1:Snapshot:Enable"] = "true",
                ["Cameras:1:Snapshot:Schedule"] = EverySecond,
                ["Cameras:1:Snapshot:Path"] = @"Outdoor\Second"
            }),
            static async (server, cancellationToken) =>
            {
                // Arrange
                var first = server.Snapshot;
                var second = server.Manager.Find(SecondCameraId)!.Snapshot!;

                // Act
                await ServiceTestServer.WaitUntilAsync(() => (first.SavedCount >= 1) && (second.SavedCount >= 1), cancellationToken).ConfigureAwait(true);

                // Assert (the folder of the id, and the path relative to the storage)
                Assert.StartsWith(Path.Combine(server.StoragePath, ServiceTestServer.CameraId) + Path.DirectorySeparatorChar, first.LastSaved!.Path, StringComparison.Ordinal);
                Assert.StartsWith(Path.Combine(server.StoragePath, "Outdoor", "Second") + Path.DirectorySeparatorChar, second.LastSaved!.Path, StringComparison.Ordinal);
                Assert.Equal(2, server.Camera.CreatedCount);
            });

    [Fact]
    public Task MissingCameraIsReported() =>
        ServiceTestServer.RunAsync(
            Saving(new Dictionary<string, string?> { ["Cameras:0:Device"] = "Missing camera" }),
            static async (server, cancellationToken) =>
            {
                // Act
                await ServiceTestServer.WaitUntilAsync(() => server.Snapshot.ErrorMessage is not null, cancellationToken).ConfigureAwait(true);

                // Assert (logged once until a save succeeds)
                Assert.Equal("Camera not found.", server.Snapshot.ErrorMessage);
                Assert.Equal(0, server.Snapshot.SavedCount);
                Assert.Single(server.Logs.Entries, static x => (x.Level == LogLevel.Warning) && x.Message.StartsWith("Snapshot failed. id=[test]", StringComparison.Ordinal));
                Assert.Empty(Directory.GetFiles(server.StoragePath, "*.jpg", SearchOption.AllDirectories));
            });

    // The image is named by the time of the schedule, not by the time of the run
    [Theory]
    [InlineData("* * * * *", "2026-01-02T03:04:00.0112+09:00", "2026-01-02T03:04:00+09:00")]
    [InlineData("* * * * *", "2026-01-02T03:04:52.5+09:00", "2026-01-02T03:04:00+09:00")]
    [InlineData("* * * * * *", "2026-01-02T03:04:52.0112+09:00", "2026-01-02T03:04:52+09:00")]
    [InlineData("*/10 * * * * *", "2026-01-02T03:04:13+09:00", "2026-01-02T03:04:10+09:00")]
    [InlineData("0 3 * * *", "2026-01-02T03:00:00+09:00", "2026-01-02T03:00:00+09:00")]
    [InlineData("0 3 * * *", "2026-01-02T03:05:30+09:00", "2026-01-02T03:00:00+09:00")]
    [InlineData("0 3 * * *", "2026-01-02T04:30:00+09:00", "2026-01-02T03:00:00+09:00")]
    [InlineData("0 3 * * *", "2026-01-02T12:00:00+09:00", "2026-01-02T12:00:00+09:00")]
    public void ScheduledTimeIsFoundFromRunTime(string schedule, string time, string expected)
    {
        // Act
        var scheduled = SnapshotWriter.GetScheduledTime(CronExpression.Parse(schedule), DateTimeOffset.Parse(time, CultureInfo.InvariantCulture));

        // Assert
        Assert.Equal(DateTimeOffset.Parse(expected, CultureInfo.InvariantCulture), scheduled);
        Assert.Equal(TimeSpan.FromHours(9), scheduled.Offset);
    }

    [Fact]
    public Task RunTakesPreparedLease() =>
        ServiceTestServer.RunAsync(
            Saving(new Dictionary<string, string?> { ["Cameras:0:Snapshot:Schedule"] = OnceAYear }),
            static async (server, cancellationToken) =>
            {
                // Arrange
                var time = DateTimeOffset.Now;
                server.Snapshot.Prepare(time);
                var leasesPrepared = server.Channel.Leases;

                // Act
                await server.Snapshot.ExecuteAsync(time, cancellationToken).ConfigureAwait(true);

                // Assert (the camera can close after the run)
                Assert.Equal(1, leasesPrepared);
                Assert.Equal(1, server.Snapshot.SavedCount);
                Assert.Equal(0, server.Channel.Leases);
            });

    // The worker read the next time just before the run started: the camera must not stay open until the next run
    [Fact]
    public Task PrepareForStartedRunTakesNoLease() =>
        ServiceTestServer.RunAsync(
            Saving(new Dictionary<string, string?> { ["Cameras:0:Snapshot:Schedule"] = OnceAYear }),
            static async (server, cancellationToken) =>
            {
                // Arrange
                var time = DateTimeOffset.Now;
                await server.Snapshot.ExecuteAsync(time, cancellationToken).ConfigureAwait(true);

                // Act
                server.Snapshot.Prepare(time);
                var leasesForStarted = server.Channel.Leases;
                server.Snapshot.Prepare(time.AddHours(1));
                var leasesForNext = server.Channel.Leases;

                // Assert
                Assert.Equal(0, leasesForStarted);
                Assert.Equal(1, leasesForNext);
                server.Snapshot.ReleasePrepared();
            });

    [Fact]
    public Task CameraOpensShortlyBeforeSave()
    {
        // Once a minute, about 8 seconds from now (the camera opens 2 seconds before it, the warm-up is 0)
        var start = DateTime.Now;
        var second = (start.Second + 8) % 60;
        return ServiceTestServer.RunAsync(
            Saving(new Dictionary<string, string?> { ["Cameras:0:Snapshot:Schedule"] = String.Create(CultureInfo.InvariantCulture, $"{second} * * * * *") }),
            async (server, cancellationToken) =>
            {
                // Act
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(true);
                var createdBefore = server.Camera.CreatedCount;

                // The server started too late for this test, the camera is open already
                if (DateTime.Now - start >= TimeSpan.FromSeconds(5))
                {
                    Assert.Skip("The server started too late.");
                }

                await ServiceTestServer.WaitUntilAsync(() => server.Snapshot.SavedCount == 1, cancellationToken).ConfigureAwait(true);
                var stateAfterSave = server.Channel.State;
                await ServiceTestServer.WaitUntilAsync(() => server.Channel.State == CameraState.Closed, cancellationToken).ConfigureAwait(true);

                // Assert (closed after the idle time)
                Assert.Equal(0, createdBefore);
                Assert.Equal(CameraState.Open, stateAfterSave);
                Assert.Equal(1, server.Camera.CreatedCount);
            });
    }

    private static Dictionary<string, string?> Saving(Dictionary<string, string?>? settings = null)
    {
        var values = new Dictionary<string, string?>
        {
            ["Cameras:0:Snapshot:Enable"] = "true",
            ["Cameras:0:Snapshot:Schedule"] = EverySecond
        };
        foreach (var (key, value) in settings ?? [])
        {
            values[key] = value;
        }

        return values;
    }
}
