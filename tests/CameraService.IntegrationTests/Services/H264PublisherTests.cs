namespace CameraService.Host.Services;

using CameraService.Host.Settings;
using CameraService.Host.Testing;

using Microsoft.Extensions.Logging.Abstractions;

// The publisher on a camera of its own with short times. Tick is called by the test instead of the timer of the manager.
public sealed class H264PublisherTests : IAsyncDisposable
{
    private const int TestTimeout = 30_000;

    private static readonly TimeSpan ShortTime = TimeSpan.FromMilliseconds(100);

    private static readonly TimeSpan LongTime = TimeSpan.FromMinutes(1);

    private readonly TestH264EncoderFactory factory = new();

    private readonly CameraChannel channel = TestChannel.Create(new TestCameraProvider());

    public ValueTask DisposeAsync() => channel.DisposeAsync();

    [Fact(Timeout = TestTimeout)]
    public async Task EncoderIsReleasedAfterIdleTime()
    {
        // Arrange (the first frame with a client creates the encoder)
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var publisher = CreatePublisher(ShortTime, LongTime);
        using var lease = channel.Acquire();
        var client = publisher.Hub.Subscribe(cancellationToken);
        await WaitForEncoderAsync(publisher, cancellationToken).ConfigureAwait(true);
        var encoder = factory.Encoder!;

        // Act (the client leaves)
        client.Dispose();
        await Task.Delay(ShortTime * 2, cancellationToken).ConfigureAwait(true);
        publisher.Tick();
        await ServiceTestServer.WaitUntilAsync(() => encoder.IsDisposed, cancellationToken).ConfigureAwait(true);

        // Assert
        Assert.Null(publisher.EncoderName);
    }

    [Fact(Timeout = TestTimeout)]
    public async Task EncoderIsKeptWithinIdleTime()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var publisher = CreatePublisher(LongTime, LongTime);
        using var lease = channel.Acquire();
        var client = publisher.Hub.Subscribe(cancellationToken);
        await WaitForEncoderAsync(publisher, cancellationToken).ConfigureAwait(true);

        // Act
        client.Dispose();
        publisher.Tick();

        // Assert
        Assert.NotNull(publisher.EncoderName);
        Assert.False(factory.Encoder!.IsDisposed);
    }

    [Fact(Timeout = TestTimeout)]
    public async Task EncoderIsKeptWhileClientIsConnected()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var publisher = CreatePublisher(ShortTime, LongTime);
        using var lease = channel.Acquire();
        using var client = publisher.Hub.Subscribe(cancellationToken);
        await WaitForEncoderAsync(publisher, cancellationToken).ConfigureAwait(true);

        // Act
        await Task.Delay(ShortTime * 2, cancellationToken).ConfigureAwait(true);
        publisher.Tick();

        // Assert
        Assert.NotNull(publisher.EncoderName);
        Assert.False(factory.Encoder!.IsDisposed);
    }

    [Fact(Timeout = TestTimeout)]
    public async Task NextClientCreatesEncoderAfterRelease()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var publisher = CreatePublisher(ShortTime, LongTime);
        using var lease = channel.Acquire();
        var first = publisher.Hub.Subscribe(cancellationToken);
        await WaitForEncoderAsync(publisher, cancellationToken).ConfigureAwait(true);
        var released = factory.Encoder!;
        first.Dispose();
        await Task.Delay(ShortTime * 2, cancellationToken).ConfigureAwait(true);
        publisher.Tick();
        await ServiceTestServer.WaitUntilAsync(() => released.IsDisposed, cancellationToken).ConfigureAwait(true);

        // Act
        using var second = publisher.Hub.Subscribe(cancellationToken);
        await WaitForEncoderAsync(publisher, cancellationToken).ConfigureAwait(true);

        // Assert
        Assert.Equal(2, factory.Attempts);
        Assert.NotSame(released, factory.Encoder);
    }

    [Fact(Timeout = TestTimeout)]
    public async Task StatisticsKeepCountOfReleasedEncoder()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var publisher = CreatePublisher(ShortTime, LongTime);
        using var lease = channel.Acquire();
        var client = publisher.Hub.Subscribe(cancellationToken);
        await WaitForEncoderAsync(publisher, cancellationToken).ConfigureAwait(true);
        var encoder = factory.Encoder!;
        encoder.DroppedFrames = 2;
        await WaitForKeyFramesAsync(publisher, cancellationToken).ConfigureAwait(true);
        var dropped = publisher.DroppedFrames;

        // Act (the client leaves, the encoder is released)
        client.Dispose();
        await Task.Delay(ShortTime * 2, cancellationToken).ConfigureAwait(true);
        publisher.Tick();
        await ServiceTestServer.WaitUntilAsync(() => encoder.IsDisposed, cancellationToken).ConfigureAwait(true);

        // Assert
        Assert.Equal(2, dropped);
        Assert.Equal(2, publisher.DroppedFrames);
        Assert.True(publisher.KeyFrames > 0);
    }

    [Fact(Timeout = TestTimeout)]
    public async Task EncoderCreatedWhileDisposingIsReleased()
    {
        // Arrange (the encoder is being created)
        var cancellationToken = TestContext.Current.CancellationToken;
        using var hold = new ManualResetEventSlim();
        factory.CreateHold = hold;
        var publisher = CreatePublisher(LongTime, LongTime);
        using var lease = channel.Acquire();
        using var client = publisher.Hub.Subscribe(cancellationToken);
        await ServiceTestServer.WaitUntilAsync(() => factory.Attempts == 1, cancellationToken).ConfigureAwait(true);

        // Act
        var disposing = publisher.DisposeAsync().AsTask();
        var waited = !disposing.IsCompleted;
        hold.Set();
        await disposing.ConfigureAwait(true);

        // Assert
        Assert.True(waited);
        Assert.True(factory.Encoder!.IsDisposed);
        Assert.Null(publisher.EncoderName);
    }

    [Fact(Timeout = TestTimeout)]
    public async Task FailedEncoderIsNotTriedAgainWithinInterval()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        factory.Fails = true;
        await using var publisher = CreatePublisher(LongTime, LongTime);
        using var lease = channel.Acquire();
        using var client = publisher.Hub.Subscribe(cancellationToken);

        // Act (the camera sends about 30 frames per second)
        await WaitForErrorAsync(publisher, cancellationToken).ConfigureAwait(true);
        await Task.Delay(TimeSpan.FromMilliseconds(300), cancellationToken).ConfigureAwait(true);

        // Assert
        Assert.Equal(TestH264EncoderFactory.CreateError, publisher.ErrorMessage);
        Assert.Equal(1, factory.Attempts);
    }

    [Fact(Timeout = TestTimeout)]
    public async Task FailedEncoderIsTriedAgainAfterInterval()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        factory.Fails = true;
        await using var publisher = CreatePublisher(LongTime, ShortTime);
        using var lease = channel.Acquire();
        using var client = publisher.Hub.Subscribe(cancellationToken);
        await WaitForErrorAsync(publisher, cancellationToken).ConfigureAwait(true);

        // Act
        factory.Fails = false;
        await WaitForEncoderAsync(publisher, cancellationToken).ConfigureAwait(true);

        // Assert
        Assert.Null(publisher.ErrorMessage);
    }

    private static Task WaitForEncoderAsync(H264Publisher publisher, CancellationToken cancellationToken) =>
        ServiceTestServer.WaitUntilAsync(() => publisher.EncoderName is not null, cancellationToken);

    private static Task WaitForKeyFramesAsync(H264Publisher publisher, CancellationToken cancellationToken) =>
        ServiceTestServer.WaitUntilAsync(() => publisher.KeyFrames > 0, cancellationToken);

    private static Task WaitForErrorAsync(H264Publisher publisher, CancellationToken cancellationToken) =>
        ServiceTestServer.WaitUntilAsync(() => publisher.ErrorMessage is not null, cancellationToken);

    private H264Publisher CreatePublisher(TimeSpan idleTime, TimeSpan retryInterval) =>
        new(NullLogger.Instance, channel, factory, new H264Setting(), idleTime, retryInterval);
}
