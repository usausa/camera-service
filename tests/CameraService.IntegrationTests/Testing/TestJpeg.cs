namespace CameraService.Host.Testing;

using CameraService.Platform.Windows;

internal static class TestJpeg
{
    // Gray JPEG of the size (as the saved images, 4:2:0)
    public static byte[] Create(int width, int height)
    {
        var uvStride = ((width + 1) / 2) * 2;
        var y = Enumerable.Repeat((byte)128, width * height).ToArray();
        var uv = Enumerable.Repeat((byte)128, uvStride * ((height + 1) / 2)).ToArray();
        using var encoder = new JpegEncoder(90);
        var destination = new byte[encoder.GetMaxLength(width, height)];
        Assert.True(encoder.TryEncodeNv12(y, width, uv, uvStride, width, height, destination, out var written));
        return destination[..written];
    }

    // {storage}\{camera}\{date}\{time}.jpg
    public static void Save(string storage, string date, string time, byte[] jpeg)
    {
        var folder = Path.Combine(storage, ServiceTestServer.CameraId, date);
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, time + ".jpg"), jpeg);
    }
}
