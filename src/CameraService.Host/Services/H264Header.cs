namespace CameraService.Host.Services;

using System.Buffers.Binary;

// Header of a binary message of the H.264 WebSocket (little endian), followed by the access unit in Annex B.
//  0 uint16  version
//  2 uint16  header length (offset of the data)
//  4 uint16  width
//  6 uint16  height
//  8 uint32  sequence of the camera frame
// 12 uint32  flags (bit 3: key frame, bit 4: full range with the BT.601 matrix, otherwise video range)
// 16 float64 capture time (Unix ms)
// 24 float64 encoded time (Unix ms)
public static class H264Header
{
    public const int Version = 1;

    public const int Length = 32;

    public const uint KeyFrameFlag = 0x8;

    public const uint FullRangeFlag = 0x10;

    public static void Write(Span<byte> destination, int width, int height, long sequence, bool keyFrame, bool fullRange, double captureTime, double encodedTime)
    {
        var flags = (keyFrame ? KeyFrameFlag : 0) | (fullRange ? FullRangeFlag : 0);
        BinaryPrimitives.WriteUInt16LittleEndian(destination, Version);
        BinaryPrimitives.WriteUInt16LittleEndian(destination[2..], Length);
        BinaryPrimitives.WriteUInt16LittleEndian(destination[4..], (ushort)width);
        BinaryPrimitives.WriteUInt16LittleEndian(destination[6..], (ushort)height);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[8..], (uint)sequence);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[12..], flags);
        BinaryPrimitives.WriteDoubleLittleEndian(destination[16..], captureTime);
        BinaryPrimitives.WriteDoubleLittleEndian(destination[24..], encodedTime);
    }
}
