namespace CameraService.Host.Settings;

using CameraService.Core;

public sealed class CameraDeviceSettingValidatorTests
{
    private readonly CameraDeviceSettingValidator validator = new();

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

    private static CameraDeviceSetting Camera(string id) => new() { Id = id };
}
