namespace CameraService.Core;

// Called on the encoder thread with an access unit in Annex B, the data is valid only during the call
// (timestamp: the one passed with the input of the frame)
public delegate void H264OutputCallback(ReadOnlySpan<byte> data, bool keyFrame, long timestamp);

// H.264 encoder of the platform: the frames are passed on the caller thread, and the output comes on the encoder thread
public interface IH264Encoder : IDisposable
{
    string Name { get; }

    bool IsHardware { get; }

    // Frames not encoded because the encoder was busy
    long DroppedFrames { get; }

    // The encoder stopped working (for example the device was lost) and encodes no more frames
    bool IsFaulted { get; }

    // HRESULT (or the error code of the platform) of the failure
    int FaultCode { get; }

    // Applied to the next frame
    void RequestKeyFrame();

    // NV12 of the size of the options, false when the frame is dropped or the encoder is stopped or faulted
    bool Encode(ReadOnlySpan<byte> y, int yStride, ReadOnlySpan<byte> uv, int uvStride, long timestamp);

    // YUY2 of the size of the options, converted to NV12 in the same range
    bool EncodeYuy2(ReadOnlySpan<byte> yuy2, int stride, long timestamp);
}

public interface IH264EncoderFactory
{
    // Throws InvalidOperationException when no encoder accepts the options, ArgumentException for invalid options
    IH264Encoder Create(H264EncoderOptions options, H264OutputCallback callback);
}
