namespace CameraService.Host.Services;

using System.Globalization;

using CameraService.Host.Testing;

using Microsoft.Extensions.Logging;

// The deletion runs at the start of the service
public sealed class ImageRetentionTests
{
    [Fact]
    public Task ExpiredDaysAreDeleted()
    {
        var today = DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var recent = DateTime.Now.AddDays(-29).ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        return ServiceTestServer.RunAsync(
            new Dictionary<string, string?> { ["Retention:Days"] = "30" },
            storage =>
            {
                var folder = Path.Combine(storage, ServiceTestServer.CameraId);
                CreateImage(folder, "20200101");
                CreateImage(folder, recent);
                CreateImage(folder, today);
                CreateImage(folder, "notes");
                CreateImage(folder, "2020010");
                CreateImage(folder, "20201301");
            },
            async (server, cancellationToken) =>
            {
                // Act
                var folder = Path.Combine(server.StoragePath, ServiceTestServer.CameraId);
                await ServiceTestServer.WaitUntilAsync(() => !Directory.Exists(Path.Combine(folder, "20200101")), cancellationToken).ConfigureAwait(true);

                // Assert (only the folders of a date are deleted)
                Assert.Equal(
                    ["2020010", "20201301", recent, today, "notes"],
                    Directory.GetDirectories(folder).Select(static x => Path.GetFileName(x)).Order(StringComparer.Ordinal));
            });
    }

    [Fact]
    public Task OldestDaysAreDeletedForFreeSpace()
    {
        var today = DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var yesterday = DateTime.Now.AddDays(-1).ToString("yyyyMMdd", CultureInfo.InvariantCulture);

        // More free space than any drive has, and no limit of the days
        return ServiceTestServer.RunAsync(
            new Dictionary<string, string?> { ["Retention:Days"] = "0", ["Retention:MinFreeSpace"] = "1000000" },
            storage =>
            {
                var folder = Path.Combine(storage, ServiceTestServer.CameraId);
                CreateImage(folder, "20200101");
                CreateImage(folder, yesterday);
                CreateImage(folder, today);
            },
            async (server, cancellationToken) =>
            {
                // Act
                var folder = Path.Combine(server.StoragePath, ServiceTestServer.CameraId);
                await ServiceTestServer.WaitUntilAsync(() => server.Logs.Entries.Any(static x => x.Message.StartsWith("Free space is low.", StringComparison.Ordinal)), cancellationToken).ConfigureAwait(true);

                // Assert (today is kept)
                Assert.Equal([today], Directory.GetDirectories(folder).Select(static x => Path.GetFileName(x)));
                Assert.Equal(2, server.Logs.Entries.Count(static x => (x.Level == LogLevel.Warning) && x.Message.StartsWith("Images deleted for free space.", StringComparison.Ordinal)));
            });
    }

    private static void CreateImage(string cameraFolder, string folderName)
    {
        var folder = Path.Combine(cameraFolder, folderName);
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, "120000.jpg"), [0xFF, 0xD8, 0xFF, 0xD9]);
    }
}
