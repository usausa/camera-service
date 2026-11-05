namespace CameraService.Host.Services;

using CameraService.Host.Settings;
using CameraService.Host.Testing;

using Microsoft.Extensions.Logging;

// The publisher on a camera of its own. The waiting client keeps the camera open, so a close does not clear the failure.
public sealed class JpegPublisherTests : IAsyncDisposable
{
    private const int TestTimeout = 30_000;

    private readonly TestJpegEncoderFactory factory = new();

    private readonly TestLoggerProvider logs = new();

    private readonly CameraChannel channel = TestChannel.Create(new TestCameraProvider());

    public async ValueTask DisposeAsync()
    {
        await channel.DisposeAsync().ConfigureAwait(true);
        logs.Dispose();
    }

    [Fact(Timeout = TestTimeout)]
    public async Task EncoderIsTriedAgainAfterFailure()
    {
        // Arrange
        factory.Failures = 1;
        using var publisher = CreatePublisher(TimeSpan.FromMilliseconds(100));
        using var lease = channel.Acquire();
        using var client = publisher.Hub.Subscribe(TestContext.Current.CancellationToken);

        // Act
        var frame = await client.WaitAsync().ConfigureAwait(true);
        frame.Release();

        // Assert (with a new encoder)
        Assert.Equal(2, factory.Encoders.Count);
        Assert.True(factory.Encoders[0].IsDisposed);
        Assert.Single(logs.Entries, static x => (x.Level == LogLevel.Error) && x.Message.StartsWith("Encode failed.", StringComparison.Ordinal));
    }

    [Fact(Timeout = TestTimeout)]
    public async Task EncoderIsNotTriedAgainWithinInterval()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        factory.Failures = 1;
        using var publisher = CreatePublisher(TimeSpan.FromMinutes(1));
        using var lease = channel.Acquire();
        using var client = publisher.Hub.Subscribe(cancellationToken);

        // Act (the camera sends about 30 frames per second)
        await ServiceTestServer.WaitUntilAsync(() => logs.Entries.Count > 0, cancellationToken).ConfigureAwait(true);
        await Task.Delay(TimeSpan.FromMilliseconds(300), cancellationToken).ConfigureAwait(true);

        // Assert
        Assert.Single(factory.Encoders);
        Assert.Single(logs.Entries);
        Assert.Equal(0, publisher.EncodedFrames);
    }

    private JpegPublisher CreatePublisher(TimeSpan retryInterval) =>
        new(logs.CreateLogger(nameof(JpegPublisher)), channel, factory, new StreamSetting { Fps = TestCameraProvider.FrameRate }, retryInterval);
}
