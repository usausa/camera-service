namespace CameraService.Host.Services;

using System.Globalization;

using CameraService.Host.Testing;

using Microsoft.Extensions.Logging;

public sealed class SnapshotWriterTests
{
    // 6 fields with the seconds
    private const string EverySecond = "* * * * * *";

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
            Assert.InRange(saved.CaptureTime - saved.ScheduledTime, TimeSpan.Zero, TimeSpan.FromSeconds(1));
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

    [Fact]
    public Task CameraOpensShortlyBeforeSave()
    {
        // Once a minute, about 8 seconds from now (the camera opens 2 seconds before it, the warm-up is 0)
        var second = (DateTime.Now.Second + 8) % 60;
        return ServiceTestServer.RunAsync(
            Saving(new Dictionary<string, string?> { ["Cameras:0:Snapshot:Schedule"] = String.Create(CultureInfo.InvariantCulture, $"{second} * * * * *") }),
            static async (server, cancellationToken) =>
            {
                // Act
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(true);
                var createdBefore = server.Camera.CreatedCount;
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
