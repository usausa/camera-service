namespace CameraService.Host.Settings;

using CameraService.Core;

// Time of the frame burned into the saved images
public sealed class TimestampSetting
{
    public bool Enable { get; set; } = true;

    // Format of the date and time with the characters of the font only (digits, space and + - . / :)
    [TimestampFormat]
    public string Format { get; set; } = "yyyy/MM/dd HH:mm:ss";

    public TextPosition Position { get; set; } = TextPosition.BottomRight;

    // Size of the characters (7 pixels high per step), 0 = by the height of the image (480 lines: 2)
    [Range(0, 16)]
    public int Scale { get; set; }
}
