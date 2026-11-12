namespace CameraService.Core;

public sealed class MjpegFrameTests
{
    // Start of image, APP0 of a camera, DQT and SOF0 (contents not used)
    private static readonly byte[] Header = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x06, 0x41, 0x56, 0x49, 0x31, 0xFF, 0xDB, 0x00, 0x04, 0x00, 0x80, 0xFF, 0xC0, 0x00, 0x03, 0x08];

    // Start of scan, data with a stuffed 0xFF, end of image
    private static readonly byte[] Scan = [0xFF, 0xDA, 0x00, 0x03, 0x01, 0x92, 0xFF, 0x00, 0xA4, 0xFF, 0xD9];

    [Fact]
    public void StandardTablesComeBeforeScan()
    {
        // Arrange
        byte[] frame = [.. Header, .. Scan];
        var destination = new byte[frame.Length + MjpegFrame.MaxAddedLength];

        // Act
        var written = MjpegFrame.CopyAsJpeg(frame, destination);

        // Assert
        Assert.Equal(frame.Length + MjpegFrame.MaxAddedLength, written);
        Assert.Equal(Header, destination[..Header.Length]);
        Assert.Equal(MjpegFrame.StandardTables.ToArray(), destination[Header.Length..(Header.Length + MjpegFrame.MaxAddedLength)]);
        Assert.Equal(Scan, destination[(Header.Length + MjpegFrame.MaxAddedLength)..written]);
    }

    [Fact]
    public void FrameWithTablesIsCopied()
    {
        // Arrange (a DHT of its own)
        byte[] frame = [.. Header, 0xFF, 0xC4, 0x00, 0x03, 0x00, .. Scan];
        var destination = new byte[frame.Length + MjpegFrame.MaxAddedLength];

        // Act
        var written = MjpegFrame.CopyAsJpeg(frame, destination);

        // Assert
        Assert.Equal(frame, destination[..written]);
    }

    [Fact]
    public void FillBytesBeforeMarkerAreSkipped()
    {
        // Arrange
        byte[] frame = [.. Header, 0xFF, .. Scan];
        var destination = new byte[frame.Length + MjpegFrame.MaxAddedLength];

        // Act
        var written = MjpegFrame.CopyAsJpeg(frame, destination);

        // Assert (the tables follow the fill byte)
        Assert.Equal(frame.Length + MjpegFrame.MaxAddedLength, written);
        Assert.Equal(Scan, destination[(written - Scan.Length)..written]);
    }

    // No start of image, no start of scan, or a segment cut short
    [Theory]
    [InlineData(new byte[] { 0x00, 0xD8, 0xFF, 0xDA })]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x04, 0x80, 0x80 })]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00 })]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x01, 0xFF, 0xDA })]
    [InlineData(new byte[] { 0xFF, 0xD8, 0x80, 0xDA })]
    public void DataThatIsNotJpegIsNotCopied(byte[] frame)
    {
        // Act
        var written = MjpegFrame.CopyAsJpeg(frame, new byte[frame.Length + MjpegFrame.MaxAddedLength]);

        // Assert
        Assert.Equal(0, written);
    }

    // DC and AC tables of the luminance (id 0) and the chrominance (id 1): the counts of the code lengths add up to the values
    [Fact]
    public void StandardTablesAreOneSegment()
    {
        // Arrange
        var tables = MjpegFrame.StandardTables;

        // Act
        var ids = new List<byte>();
        var position = 4;
        while (position < tables.Length)
        {
            ids.Add(tables[position]);
            var values = 0;
            foreach (var count in tables.Slice(position + 1, 16))
            {
                values += count;
            }

            position += 1 + 16 + values;
        }

        // Assert
        Assert.Equal([0xFF, 0xC4], tables[..2].ToArray());
        Assert.Equal(tables.Length - 2, (tables[2] << 8) | tables[3]);
        Assert.Equal(tables.Length, position);
        Assert.Equal([0x00, 0x10, 0x01, 0x11], ids);
    }
}
