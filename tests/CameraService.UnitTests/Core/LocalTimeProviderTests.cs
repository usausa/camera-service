namespace CameraService.Core;

using Mofucat.JobScheduler;

public sealed class LocalTimeProviderTests
{
    // 9:00 in UTC+9
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static readonly TimeZoneInfo Zone = TimeZoneInfo.CreateCustomTimeZone("Test", TimeSpan.FromHours(9), "Test", "Test");

    [Fact]
    public void NowIsUtc()
    {
        // Arrange
        var provider = new LocalTimeProvider(new FixedTimeProvider(Now), Zone);

        // Act
        var now = provider.GetUtcNow();

        // Assert
        Assert.Equal(Now, now);
        Assert.Equal(TimeSpan.Zero, now.Offset);
        Assert.Same(Zone, provider.LocalTimeZone);
    }

    [Fact]
    public void LocalNowHasOffsetOfZone()
    {
        // Arrange
        var provider = new LocalTimeProvider(new FixedTimeProvider(Now), Zone);

        // Act
        var now = provider.GetLocalNow();

        // Assert
        Assert.Equal(Now, now);
        Assert.Equal(TimeSpan.FromHours(9), now.Offset);
        Assert.Equal(new DateTime(2026, 1, 1, 9, 0, 0, DateTimeKind.Unspecified), now.DateTime);
    }

    [Fact]
    public void TimeIsConvertedToZone()
    {
        // Arrange
        var provider = new LocalTimeProvider(new FixedTimeProvider(Now), Zone);

        // Act
        var local = provider.ToLocal(new DateTimeOffset(2026, 1, 1, 20, 0, 0, TimeSpan.Zero));

        // Assert (the next day in the zone)
        Assert.Equal(new DateTime(2026, 1, 2, 5, 0, 0, DateTimeKind.Unspecified), local.DateTime);
        Assert.Equal(TimeSpan.FromHours(9), local.Offset);
    }

    [Fact]
    public async Task CronRunsAtLocalTime()
    {
        // Arrange
        var provider = new LocalTimeProvider(new FixedTimeProvider(Now), Zone);
        var scheduler = new JobScheduler(provider);
        try
        {
            var handle = scheduler.AddJob("0 3 * * *", new NopJob(), "test");

            // Act
            await scheduler.StartAsync().ConfigureAwait(true);

            // Assert
            Assert.Equal(new DateTimeOffset(2026, 1, 2, 3, 0, 0, TimeSpan.FromHours(9)), handle.NextExecutionTime);
            Assert.Equal(TimeSpan.FromHours(9), handle.NextExecutionTime?.Offset);
        }
        finally
        {
            await scheduler.DisposeAsync().ConfigureAwait(true);
        }
    }

    private sealed class NopJob : ISchedulerJob
    {
        public ValueTask ExecuteAsync(DateTimeOffset time, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset now;

        public FixedTimeProvider(DateTimeOffset now)
        {
            this.now = now;
        }

        public override DateTimeOffset GetUtcNow() => now;
    }
}
