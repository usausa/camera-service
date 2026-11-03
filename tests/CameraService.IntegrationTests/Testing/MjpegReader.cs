namespace CameraService.Host.Testing;

using System.Buffers.Binary;
using System.Runtime.InteropServices;

// Reads the parts of an MJPEG response (multipart/x-mixed-replace with the boundary "frame"), the data after a part is kept for the next read
internal sealed class MjpegReader
{
    private static readonly byte[] Delimiter = "\r\n--frame\r\nContent-Type: image/jpeg\r\n\r\n"u8.ToArray();

    // The first part header has no line break before it
    private static readonly byte[] FirstHeader = Delimiter[2..];

    private readonly Stream stream;

    private readonly List<byte> data = [];

    private readonly byte[] buffer = new byte[64 * 1024];

    // Start of the next part (-1 until the first header is read)
    private int start = -1;

    public MjpegReader(Stream stream)
    {
        this.stream = stream;
    }

    // A part is complete when the delimiter after it arrives
    public async Task<List<byte[]>> ReadPartsAsync(int count, CancellationToken cancellationToken)
    {
        var parts = new List<byte[]>();
        while (true)
        {
            TakeParts(parts, count);
            if (parts.Count == count)
            {
                return parts;
            }

            var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(true);
            if (read == 0)
            {
                throw new EndOfStreamException();
            }

            data.AddRange(buffer.AsSpan(0, read));
        }
    }

    // Width and height of the frame header (SOF0-SOF2), the JPEG must be complete
    public static (int Width, int Height) GetJpegSize(ReadOnlySpan<byte> jpeg)
    {
        Assert.True(jpeg[..2].SequenceEqual((byte[])[0xFF, 0xD8]));
        Assert.True(jpeg[^2..].SequenceEqual((byte[])[0xFF, 0xD9]));
        var offset = 2;
        while (offset + 9 < jpeg.Length)
        {
            Assert.Equal(0xFF, jpeg[offset]);
            var marker = jpeg[offset + 1];
            if (marker is >= 0xC0 and <= 0xC2)
            {
                return (BinaryPrimitives.ReadUInt16BigEndian(jpeg[(offset + 7)..]), BinaryPrimitives.ReadUInt16BigEndian(jpeg[(offset + 5)..]));
            }

            offset += 2 + BinaryPrimitives.ReadUInt16BigEndian(jpeg[(offset + 2)..]);
        }

        throw new InvalidDataException("No frame header.");
    }

    private void TakeParts(List<byte[]> parts, int count)
    {
        var span = CollectionsMarshal.AsSpan(data);
        if (start < 0)
        {
            if (span.Length < FirstHeader.Length)
            {
                return;
            }

            Assert.True(span[..FirstHeader.Length].SequenceEqual(FirstHeader));
            start = FirstHeader.Length;
        }

        while (parts.Count < count)
        {
            var index = span[start..].IndexOf(Delimiter);
            if (index < 0)
            {
                return;
            }

            parts.Add(span.Slice(start, index).ToArray());
            start += index + Delimiter.Length;
        }
    }
}
