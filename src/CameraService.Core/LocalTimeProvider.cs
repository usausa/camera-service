namespace CameraService.Core;

// Returns the current time in the offset of the local time zone from GetUtcNow (the same instant as UTC),
// so that "0 3 * * *" runs at 3:00 of the local time.
// LocalTimeZone is UTC, because GetLocalNow adds its offset to the clock time of GetUtcNow, which is the local time already.
// Not registered as TimeProvider, other components expect UTC from it.
public sealed class LocalTimeProvider : TimeProvider
{
    private readonly TimeProvider source;

    // Time zone of the offset
    public TimeZoneInfo Zone { get; }

    public LocalTimeProvider()
        : this(TimeProvider.System, TimeZoneInfo.Local)
    {
    }

    public LocalTimeProvider(TimeProvider source, TimeZoneInfo zone)
    {
        this.source = source;
        Zone = zone;
    }

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

    public override long TimestampFrequency => source.TimestampFrequency;

    public override DateTimeOffset GetUtcNow() => TimeZoneInfo.ConvertTime(source.GetUtcNow(), Zone);

    public override long GetTimestamp() => source.GetTimestamp();

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) => source.CreateTimer(callback, state, dueTime, period);

    // The same instant in the offset of the zone (its clock time is the local time)
    public DateTimeOffset ToLocal(DateTimeOffset time) => TimeZoneInfo.ConvertTime(time, Zone);
}
