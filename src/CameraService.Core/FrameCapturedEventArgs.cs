namespace CameraService.Core;

// Reused for every frame to avoid allocation
public sealed class FrameCapturedEventArgs : EventArgs
{
    public FrameBuffer Frame { get; set; }
}
