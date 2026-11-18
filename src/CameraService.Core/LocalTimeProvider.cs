namespace CameraService.Core;

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

    public override TimeZoneInfo LocalTimeZone => Zone;

    public override long TimestampFrequency => source.TimestampFrequency;

    public override DateTimeOffset GetUtcNow() => source.GetUtcNow();

    public override long GetTimestamp() => source.GetTimestamp();

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) => source.CreateTimer(callback, state, dueTime, period);

    // The same instant in the offset of the zone (its clock time is the local time)
    public DateTimeOffset ToLocal(DateTimeOffset time) => TimeZoneInfo.ConvertTime(time, Zone);
}
