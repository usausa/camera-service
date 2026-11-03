namespace CameraService.Core;

using System.Globalization;

public sealed class SnapshotStorageTests
{
    private static readonly DateOnly Today = new(2026, 11, 3);

    [Theory]
    [InlineData("Images", "", @"C:\Service\Images\front")]
    [InlineData(@"..\Images", "", @"C:\Images\front")]
    [InlineData(@"D:\CameraImages", "", @"D:\CameraImages\front")]
    [InlineData("Images", @"outdoor\front", @"C:\Service\Images\outdoor\front")]
    [InlineData("Images", @"E:\Front", @"E:\Front")]
    public void CameraFolderIsResolved(string storagePath, string cameraPath, string expected)
    {
        // Act (relative to the folder of the service, then to the storage path)
        var folder = SnapshotStorage.GetCameraFolder(@"C:\Service", storagePath, cameraPath, "front");

        // Assert
        Assert.Equal(expected, folder);
    }

    [Fact]
    public void NamesAreDateAndTime()
    {
        // Arrange
        var time = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Local);

        // Act
        var folder = SnapshotStorage.GetDateFolderName(time);
        var file = SnapshotStorage.GetFileName(time);

        // Assert
        Assert.Equal("20260102", folder);
        Assert.Equal("030405.jpg", file);
    }

    [Theory]
    [InlineData("20260102", true)]
    [InlineData("2026010", false)]
    [InlineData("202601020", false)]
    [InlineData("2026-102", false)]
    [InlineData("20261301", false)]
    [InlineData("２０２６０１０２", false)]
    [InlineData("notes123", false)]
    public void OnlyDateFoldersAreParsed(string name, bool expected)
    {
        // Act
        var result = SnapshotStorage.TryParseDateFolderName(name, out var date);

        // Assert
        Assert.Equal(expected, result);
        if (expected)
        {
            Assert.Equal(new DateOnly(2026, 1, 2), date);
        }
    }

    [Theory]
    [InlineData("2026-10-04", 30, true)]
    [InlineData("2026-10-05", 30, false)]
    [InlineData("2026-11-02", 1, true)]
    [InlineData("2026-11-03", 1, false)]
    [InlineData("2000-01-01", 0, false)]
    public void ExpiredByDays(string date, int days, bool expected)
    {
        // Act (today is the first of the days)
        var result = SnapshotStorage.IsExpired(DateOnly.Parse(date, CultureInfo.InvariantCulture), Today, days);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void DeletableAreBeforeTodayOldestFirst()
    {
        // Arrange
        DateFolder[] folders =
        [
            new(@"C:\Images\a\20261103", Today),
            new(@"C:\Images\a\20261101", new DateOnly(2026, 11, 1)),
            new(@"C:\Images\b\20261030", new DateOnly(2026, 10, 30)),
            new(@"C:\Images\a\20261105", new DateOnly(2026, 11, 5))
        ];

        // Act
        var deletable = SnapshotStorage.GetDeletable(folders, Today);

        // Assert
        Assert.Equal([@"C:\Images\b\20261030", @"C:\Images\a\20261101"], deletable.Select(static x => x.Path));
    }

    [Fact]
    public void DateFoldersAreFound()
    {
        // Arrange
        var root = Path.Combine(Path.GetTempPath(), "CameraService.UnitTests", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "20260102"));
            Directory.CreateDirectory(Path.Combine(root, "20260101"));
            Directory.CreateDirectory(Path.Combine(root, "notes"));
            Directory.CreateDirectory(Path.Combine(root, "20261301"));
            File.WriteAllBytes(Path.Combine(root, "20260103"), []);

            // Act
            var folders = SnapshotStorage.FindDateFolders(root);

            // Assert
            Assert.Equal([new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 2)], folders.Select(static x => x.Date));
            Assert.Equal(Path.Combine(root, "20260101"), folders[0].Path);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void MissingFolderHasNoDateFolders()
    {
        // Act
        var folders = SnapshotStorage.FindDateFolders(Path.Combine(Path.GetTempPath(), "CameraService.UnitTests", Guid.NewGuid().ToString("N")));

        // Assert
        Assert.Empty(folders);
    }
}
