namespace CameraService.Platform.Windows;

using global::Windows.Media.Capture.Frames;

public sealed class VideoDeviceCatalogTests
{
    [Theory]
    [InlineData(true, MediaFrameSourceKind.Color)]
    [InlineData(true, MediaFrameSourceKind.Color, MediaFrameSourceKind.Color)]
    [InlineData(true, MediaFrameSourceKind.Color, MediaFrameSourceKind.Custom)]
    [InlineData(false, MediaFrameSourceKind.Custom)]
    [InlineData(false, MediaFrameSourceKind.Infrared, MediaFrameSourceKind.Color)]
    [InlineData(false, MediaFrameSourceKind.Infrared)]
    [InlineData(false, MediaFrameSourceKind.Depth, MediaFrameSourceKind.Infrared)]
    [InlineData(false)]
    public void ColorCameraIsFoundBySourceKinds(bool expected, params MediaFrameSourceKind[] kinds)
    {
        // Act
        var result = VideoDeviceCatalog.IsColorCamera(kinds);

        // Assert
        Assert.Equal(expected, result);
    }
}
