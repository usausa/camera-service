namespace CameraService.Host.Components;

// Texts of the values shown on the pages
public static class Texts
{
    private const double GigaBytes = 1024d * 1024 * 1024;

    public static string Date(DateOnly date) => date.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture);

    public static string Time(TimeOnly time) => time.ToString("HH:mm:ss", CultureInfo.InvariantCulture);

    public static string DateTime(DateOnly date, TimeOnly time) => $"{Date(date)} {Time(time)}";

    public static string FreeSpace(long? bytes) => bytes is { } value ? String.Create(CultureInfo.InvariantCulture, $"{value / GigaBytes:F1} GB") : "-";

    public static string Rate(double value) => value.ToString("F1", CultureInfo.InvariantCulture);

    // Frame rate, bitrate (Mbps) and clients of a stream
    public static string Stream(double frameRate, double bitrate, int clients) =>
        String.Create(CultureInfo.InvariantCulture, $"{frameRate:F1} fps, {bitrate:F1} Mbps, {clients} {(clients == 1 ? "client" : "clients")}");
}
