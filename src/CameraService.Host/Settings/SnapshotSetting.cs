namespace CameraService.Host.Settings;

// Saving of the images of a camera
public sealed class SnapshotSetting
{
    public bool Enable { get; set; }

    // Cron expression in the local time: 5 fields, or 6 fields with the seconds first
    [CronSchedule]
    public string Schedule { get; set; } = "* * * * *";

    // Folder of the images, relative to Storage:Path (the camera id when empty)
    public string Path { get; set; } = string.Empty;

    [Range(1, 100)]
    public int Quality { get; set; } = 90;

    public TimestampSetting Timestamp { get; set; } = new();
}
