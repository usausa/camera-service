namespace CameraService.Core;

using Mofucat.JobScheduler;

public sealed class LocalTimeProviderTests
{
    // 9:00 in UTC+9
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static readonly TimeZoneInfo Zone = TimeZoneInfo.CreateCustomTimeZone("Test", TimeSpan.FromHours(9), "Test", "Test");

    [Fact]
    public void NowHasOffsetOfZone()
    {
        // Arrange
        var provider = new LocalTimeProvider(new FixedTimeProvider(Now), Zone);

        // Act
        var now = provider.GetUtcNow();

        // Assert (the same instant)
        Assert.Equal(Now, now);
        Assert.Equal(TimeSpan.FromHours(9), now.Offset);
        Assert.Equal(new DateTime(2026, 1, 1, 9, 0, 0, DateTimeKind.Unspecified), now.DateTime);
    }

    // GetLocalNow of TimeProvider adds the offset of LocalTimeZone to the clock time of GetUtcNow
    [Fact]
    public void LocalNowIsNotShiftedTwice()
    {
        // Arrange
        var provider = new LocalTimeProvider(new FixedTimeProvider(Now), Zone);

        // Act
        var now = provider.GetLocalNow();

        // Assert
        Assert.Equal(Now, now);
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
    public void CronRunsAtLocalTime()
    {
        // Arrange
        var cron = CronExpression.Parse("0 3 * * *");
        var provider = new LocalTimeProvider(new FixedTimeProvider(Now), Zone);

        // Act
        var local = cron.GetNextOccurrence(provider.GetUtcNow());
        var utc = cron.GetNextOccurrence(Now);

        // Assert (3:00 of the next day in the zone, while UTC gives 12:00 in the zone)
        Assert.Equal(new DateTimeOffset(2026, 1, 2, 3, 0, 0, TimeSpan.FromHours(9)), local);
        Assert.Equal(12, TimeZoneInfo.ConvertTime(utc!.Value, Zone).Hour);
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
