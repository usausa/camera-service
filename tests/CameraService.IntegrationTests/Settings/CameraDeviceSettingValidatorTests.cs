namespace CameraService.Host.Settings;

using CameraService.Core;

using Microsoft.Extensions.Options;

public sealed class CameraDeviceSettingValidatorTests
{
    private readonly CameraDeviceSettingValidator validator = new(Options.Create(new StorageSetting()));

    [Fact]
    public void ValidCamerasPass()
    {
        // Act
        var result = validator.Validate(null, [Camera("front"), Camera("back_door-2")]);

        // Assert
        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData("")]
    [InlineData("front door")]
    [InlineData("front/door")]
    [InlineData("玄関")]
    public void InvalidIdFails(string id)
    {
        // Act
        var result = validator.Validate(null, [Camera(id)]);

        // Assert
        Assert.True(result.Failed);
        Assert.Contains("Cameras[0]", result.FailureMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void DuplicateIdFails()
    {
        // Act (the folder names do not distinguish upper and lower case)
        var result = validator.Validate(null, [Camera("front"), Camera("FRONT")]);

        // Assert
        Assert.True(result.Failed);
        Assert.Contains("[front]", result.FailureMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void UnsupportedFormatFails()
    {
        // Arrange
        var camera = Camera("front");
        camera.Format = VideoPixelFormat.Bgra8;

        // Act
        var result = validator.Validate(null, [camera]);

        // Assert
        Assert.True(result.Failed);
    }

    [Theory]
    [InlineData("* * * * *")]
    [InlineData("*/10 * 22-23,0-5 * * *")]
    [InlineData("0 0 3 * * 1-5")]
    public void ValidScheduleSucceeds(string schedule)
    {
        // Arrange
        var camera = Camera("front");
        camera.Snapshot.Schedule = schedule;

        // Act
        var result = validator.Validate(null, [camera]);

        // Assert
        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData("")]
    [InlineData("* * *")]
    [InlineData("* * * * * * *")]
    [InlineData("60 * * * *")]
    public void InvalidScheduleFails(string schedule)
    {
        // Arrange
        var camera = Camera("front");
        camera.Snapshot.Schedule = schedule;

        // Act
        var result = validator.Validate(null, [camera]);

        // Assert
        Assert.True(result.Failed);
        Assert.Contains("Cameras[0].Snapshot", result.FailureMessage, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("yyyy/MM/dd HH:mm:ss", true)]
    [InlineData("yyyy-MM-dd HH:mm:ss.fff zzz", true)]
    [InlineData("yyyy/MM/dd ddd", false)]
    [InlineData("hh:mm tt", false)]
    [InlineData("", false)]
    public void TimestampFormatNeedsCharactersOfFont(string format, bool expected)
    {
        // Arrange
        var camera = Camera("front");
        camera.Snapshot.Timestamp.Format = format;

        // Act
        var result = validator.Validate(null, [camera]);

        // Assert
        Assert.Equal(expected, result.Succeeded);
    }

    [Fact]
    public void QualityOutOfRangeFails()
    {
        // Arrange
        var camera = Camera("front");
        camera.Snapshot.Quality = 0;

        // Act
        var result = validator.Validate(null, [camera]);

        // Assert
        Assert.True(result.Failed);
    }

    [Fact]
    public void SharedSnapshotPathFails()
    {
        // Arrange (the default path of the second camera is its id)
        var first = Camera("front");
        first.Snapshot.Path = "door";
        var second = Camera("door");

        // Act
        var result = validator.Validate(null, [first, second]);

        // Assert
        Assert.True(result.Failed);
        Assert.Contains("door]", result.FailureMessage, StringComparison.Ordinal);
    }

    // Other forms of a path are the same folder
    [Theory]
    [InlineData("shared", "shared")]
    [InlineData("shared", "SHARED")]
    [InlineData("shared", @".\shared")]
    [InlineData("shared", @"shared\")]
    [InlineData("shared", @"other\..\shared")]
    [InlineData(@"x\y", "x/y")]
    public void SnapshotPathsOfOneFolderFail(string firstPath, string secondPath)
    {
        // Arrange
        var first = Camera("front");
        first.Snapshot.Path = firstPath;
        var second = Camera("back");
        second.Snapshot.Path = secondPath;

        // Act
        var result = validator.Validate(null, [first, second]);

        // Assert
        Assert.True(result.Failed);
        Assert.Contains("used by more than one camera", result.FailureMessage, StringComparison.Ordinal);
    }

    // The default folder of a camera is named by its id
    [Theory]
    [InlineData(@".\front")]
    [InlineData("front/")]
    public void SnapshotPathOfDefaultFolderFails(string path)
    {
        // Arrange
        var second = Camera("back");
        second.Snapshot.Path = path;

        // Act
        var result = validator.Validate(null, [Camera("front"), second]);

        // Assert
        Assert.True(result.Failed);
    }

    [Fact]
    public void AbsolutePathOfOneFolderFails()
    {
        // Arrange
        var second = Camera("back");
        second.Snapshot.Path = Path.Combine(AppContext.BaseDirectory, new StorageSetting().Path, "front");

        // Act
        var result = validator.Validate(null, [Camera("front"), second]);

        // Assert
        Assert.True(result.Failed);
    }

    [Fact]
    public void DifferentFoldersPass()
    {
        // Arrange (a folder inside the folder of another camera is another folder)
        var first = Camera("front");
        first.Snapshot.Path = "x";
        var second = Camera("back");
        second.Snapshot.Path = @"x\y";

        // Act
        var result = validator.Validate(null, [first, second, Camera("door")]);

        // Assert
        Assert.True(result.Succeeded);
    }

    [Fact]
    public void InvalidSnapshotPathFails()
    {
        // Arrange
        var camera = Camera("front");
        camera.Snapshot.Path = "bad\0name";

        // Act
        var result = validator.Validate(null, [camera]);

        // Assert
        Assert.True(result.Failed);
        Assert.Contains("[front]", result.FailureMessage, StringComparison.Ordinal);
    }

    private static CameraDeviceSetting Camera(string id) => new() { Id = id };
}
