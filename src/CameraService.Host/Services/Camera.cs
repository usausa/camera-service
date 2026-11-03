namespace CameraService.Host.Services;

using CameraService.Host.Settings;

// A configured camera, its stream and the saving of its images
public sealed class Camera : IAsyncDisposable
{
    public CameraDeviceSetting Setting { get; }

    public CameraChannel Channel { get; }

    public JpegPublisher Jpeg { get; }

    // Folder of the saved images (also while the saving is disabled, the old images are deleted)
    public string Folder { get; }

    // Null when the saving is disabled
    public SnapshotWriter? Snapshot { get; }

    public string Id => Setting.Id;

    public string Name => Setting.DisplayName;

    // Why the camera or its saving does not work now (the last state while nobody uses the camera)
    public string? ErrorMessage =>
        Channel.State == CameraState.Failed
            ? Channel.ErrorMessage ?? "Camera failed."
            : Snapshot?.FolderError ?? Snapshot?.ErrorMessage;

    public Camera(CameraDeviceSetting setting, CameraChannel channel, JpegPublisher jpeg, string folder, SnapshotWriter? snapshot)
    {
        Setting = setting;
        Channel = channel;
        Jpeg = jpeg;
        Folder = folder;
        Snapshot = snapshot;
    }

    public async ValueTask DisposeAsync()
    {
        Snapshot?.Dispose();
        await Channel.DisposeAsync().ConfigureAwait(false);
        Jpeg.Dispose();
    }
}
