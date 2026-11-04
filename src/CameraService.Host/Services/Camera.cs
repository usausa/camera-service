namespace CameraService.Host.Services;

using CameraService.Host.Settings;

// A configured camera, its streams and the saving of its images
public sealed class Camera : IAsyncDisposable
{
    public CameraDeviceSetting Setting { get; }

    public CameraChannel Channel { get; }

    public JpegPublisher Jpeg { get; }

    // Null when H.264 is disabled
    public H264Publisher? H264 { get; }

    // Folder of the saved images (also while the saving is disabled, the old images are deleted)
    public string Folder { get; }

    // Null when the saving is disabled
    public SnapshotWriter? Snapshot { get; }

    public string Id => Setting.Id;

    public string Name => Setting.DisplayName;

    // Why the camera or its saving does not work now (the last state while nobody uses the camera)
    public string? ErrorMessage =>
        Channel.IsFailing
            ? Channel.ErrorMessage ?? "Camera failed."
            : Snapshot?.FolderError ?? Snapshot?.ErrorMessage;

    public Camera(CameraDeviceSetting setting, CameraChannel channel, JpegPublisher jpeg, H264Publisher? h264, string folder, SnapshotWriter? snapshot)
    {
        Setting = setting;
        Channel = channel;
        Jpeg = jpeg;
        H264 = h264;
        Folder = folder;
        Snapshot = snapshot;
    }

    public async ValueTask DisposeAsync()
    {
        Snapshot?.Dispose();
        await Channel.DisposeAsync().ConfigureAwait(false);
        Jpeg.Dispose();
        if (H264 is not null)
        {
            await H264.DisposeAsync().ConfigureAwait(false);
        }
    }
}
