namespace CameraService.Host.Services;

public sealed class KeyFrameGateTests
{
    private readonly KeyFrameGate gate = new();

    [Fact]
    public void NewClientWaitsForKeyFrame()
    {
        // Act
        var delta = gate.CanSend(1, false, 0);
        var key = gate.CanSend(2, true, 1);

        // Assert
        Assert.False(delta);
        Assert.True(key);
    }

    [Fact]
    public void FramesAfterKeyFrameAreSent()
    {
        // Arrange
        gate.CanSend(1, true, 0);

        // Act
        var next = gate.CanSend(2, false, 1);
        var last = gate.CanSend(3, false, 2);

        // Assert
        Assert.True(next);
        Assert.True(last);
    }

    [Fact]
    public void SkippedFrameWaitsForNextKeyFrame()
    {
        // Arrange
        gate.CanSend(1, true, 0);

        // Act (frame 2 was skipped, the frames depend on it until the next key frame)
        var afterSkip = gate.CanSend(3, false, 1);
        var nextDelta = gate.CanSend(4, false, 3);
        var key = gate.CanSend(5, true, 4);
        var afterKey = gate.CanSend(6, false, 5);

        // Assert
        Assert.False(afterSkip);
        Assert.False(nextDelta);
        Assert.True(key);
        Assert.True(afterKey);
    }

    [Fact]
    public void KeyFrameAfterSkipIsSent()
    {
        // Arrange
        gate.CanSend(1, true, 0);

        // Act
        var key = gate.CanSend(3, true, 1);

        // Assert
        Assert.True(key);
    }
}
