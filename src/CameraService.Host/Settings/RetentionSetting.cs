namespace CameraService.Host.Settings;

public sealed class RetentionSetting
{
    // Cron expression of the deletion in the local time (it also runs at the start)
    [CronSchedule]
    public string Schedule { get; set; } = "0 3 * * *";

    // Days of the images kept including today, 0 = not deleted by the date
    [Range(0, 36500)]
    public int Days { get; set; } = 30;

    // GB of free space kept on the drive by deleting the oldest days, 0 = not checked
    [Range(0, 1_000_000)]
    public int MinFreeSpace { get; set; } = 10;
}
