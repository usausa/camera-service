namespace CameraService.Host.Services;

using CameraService.Host.Settings;

// A configured camera and its stream
public sealed class Camera : IAsyncDisposable
{
    public CameraDeviceSetting Setting { get; }

    public CameraChannel Channel { get; }

    public JpegPublisher Jpeg { get; }

    public string Id => Setting.Id;

    public string Name => Setting.DisplayName;

    public Camera(CameraDeviceSetting setting, CameraChannel channel, JpegPublisher jpeg)
    {
        Setting = setting;
        Channel = channel;
        Jpeg = jpeg;
    }

    public async ValueTask DisposeAsync()
    {
        await Channel.DisposeAsync().ConfigureAwait(false);
        Jpeg.Dispose();
    }
}
