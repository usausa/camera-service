namespace CameraService.Host.Endpoints;

using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.WebSockets;

using CameraService.Host.Services;
using CameraService.Host.Testing;

// The test server uses the software encoder
public sealed class H264EndpointsTests
{
    private const string H264Path = "/cameras/test/h264";

    // Longer than the interval of the key frame requests on the server (500 ms)
    private static readonly TimeSpan KeyFrameRequestInterval = TimeSpan.FromMilliseconds(600);

    // The key frame of the stream comes only when it is requested
    private static readonly Dictionary<string, string?> RareKeyFrames = new() { ["Stream:H264:KeyFrameInterval"] = "60" };

    [Fact]
    public Task StreamStartsAtKeyFrame() =>
        ServiceTestServer.RunAsync(RareKeyFrames, static async (server, cancellationToken) =>
        {
            // Arrange
            using var socket = new ClientWebSocket();
            await socket.ConnectAsync(server.GetWebSocketUri(H264Path), cancellationToken).ConfigureAwait(true);

            // Act (about a second of frames)
            var first = await ReceiveAsync(socket, cancellationToken).ConfigureAwait(true);
            var following = new List<byte[]>();
            for (var i = 0; i < 30; i++)
            {
                following.Add(await ReceiveAsync(socket, cancellationToken).ConfigureAwait(true));
            }

            // Assert (a frame that follows without a gap is not a key frame,
            // after a frame skipped by the server, for example under load, the stream starts again at a key frame)
            Assert.True(IsKeyFrame(first));
            AssertAnnexB(first);
            var previous = ReadUInt32(first, 8);
            foreach (var message in following)
            {
                var sequence = ReadUInt32(message, 8);
                Assert.True(sequence > previous);
                if (sequence == previous + 1)
                {
                    Assert.False(IsKeyFrame(message));
                }

                previous = sequence;
                AssertAnnexB(message);
            }
        });

    [Fact]
    public Task HeaderHasFrameValues() =>
        ServiceTestServer.RunAsync(null, static async (server, cancellationToken) =>
        {
            // Arrange
            using var socket = new ClientWebSocket();
            await socket.ConnectAsync(server.GetWebSocketUri(H264Path), cancellationToken).ConfigureAwait(true);

            // Act
            var message = await ReceiveAsync(socket, cancellationToken).ConfigureAwait(true);
            var now = (DateTime.UtcNow - DateTime.UnixEpoch).TotalMilliseconds;

            // Assert (version, header length, size, video range of YUY2, capture time before the encoded time)
            Assert.Equal(H264Header.Version, ReadUInt16(message, 0));
            Assert.Equal(H264Header.Length, ReadUInt16(message, 2));
            Assert.Equal(TestCameraProvider.Width, ReadUInt16(message, 4));
            Assert.Equal(TestCameraProvider.Height, ReadUInt16(message, 6));
            Assert.Equal(0u, ReadUInt32(message, 12) & H264Header.FullRangeFlag);
            var captureTime = ReadDouble(message, 16);
            var encodedTime = ReadDouble(message, 24);
            Assert.InRange(captureTime, now - 5000, encodedTime);
            Assert.InRange(encodedTime, now - 5000, now);
        });

    [Fact]
    public Task TextMessageRequestsKeyFrame() =>
        ServiceTestServer.RunAsync(RareKeyFrames, static async (server, cancellationToken) =>
        {
            // Arrange (the key frame requests are 500 ms apart at least, so the request of the connection has passed)
            using var socket = new ClientWebSocket();
            await socket.ConnectAsync(server.GetWebSocketUri(H264Path), cancellationToken).ConfigureAwait(true);
            for (var i = 0; i < 30; i++)
            {
                await ReceiveAsync(socket, cancellationToken).ConfigureAwait(true);
            }

            // Act (requested again until a key frame arrives, as the viewer does:
            // the server ignores a request within its interval from a request for a frame it skipped)
            var received = 0;
            var keyFrame = false;
            var requested = 0L;
            while (!keyFrame && (received < 90))
            {
                if ((requested == 0) || (Stopwatch.GetElapsedTime(requested) > KeyFrameRequestInterval))
                {
                    await socket.SendAsync("key"u8.ToArray().AsMemory(), WebSocketMessageType.Text, true, cancellationToken).ConfigureAwait(true);
                    requested = Stopwatch.GetTimestamp();
                }

                keyFrame = IsKeyFrame(await ReceiveAsync(socket, cancellationToken).ConfigureAwait(true));
                received++;
            }

            // Assert
            Assert.True(keyFrame);
        });

    [Fact]
    public Task ClientsShareOneEncoder() =>
        ServiceTestServer.RunAsync(null, static async (server, cancellationToken) =>
        {
            // Arrange
            using var first = new ClientWebSocket();
            await first.ConnectAsync(server.GetWebSocketUri(H264Path), cancellationToken).ConfigureAwait(true);
            await ReceiveAsync(first, cancellationToken).ConfigureAwait(true);
            using var second = new ClientWebSocket();
            await second.ConnectAsync(server.GetWebSocketUri(H264Path), cancellationToken).ConfigureAwait(true);

            // Act (the second client starts at a key frame requested for it)
            var message = await ReceiveAsync(second, cancellationToken).ConfigureAwait(true);

            // Assert
            var h264 = server.Manager.Find(ServiceTestServer.CameraId)!.H264!;
            Assert.True(IsKeyFrame(message));
            Assert.Equal(2, h264.Hub.ClientCount);
            Assert.NotNull(h264.EncoderName);
            Assert.False(h264.IsHardware);
            Assert.Equal(1, server.Camera.CreatedCount);
            Assert.Equal(2, server.Channel.Leases);
        });

    [Fact]
    public Task DisabledH264IsNotFound() =>
        ServiceTestServer.RunAsync(
            new Dictionary<string, string?> { ["Stream:H264:Enable"] = "false" },
            static async (server, cancellationToken) =>
            {
                // Act
                using var response = await server.Client.GetAsync(server.GetUri(H264Path), cancellationToken).ConfigureAwait(true);

                // Assert
                Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
                Assert.Null(server.Manager.Find(ServiceTestServer.CameraId)!.H264);
            });

    [Theory]
    [InlineData("/cameras/missing/h264", HttpStatusCode.NotFound)]
    [InlineData(H264Path, HttpStatusCode.BadRequest)]
    public Task OtherRequestIsRejected(string path, HttpStatusCode expected) =>
        ServiceTestServer.RunAsync(null, async (server, cancellationToken) =>
        {
            // Act (not a WebSocket request)
            using var response = await server.Client.GetAsync(server.GetUri(path), cancellationToken).ConfigureAwait(true);

            // Assert
            Assert.Equal(expected, response.StatusCode);
        });

    //--------------------------------------------------------------------------------
    // Helper
    //--------------------------------------------------------------------------------

    private static async Task<byte[]> ReceiveAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[1024 * 1024];
        var length = 0;
        while (true)
        {
            var result = await socket.ReceiveAsync(buffer.AsMemory(length), cancellationToken).ConfigureAwait(true);
            Assert.Equal(WebSocketMessageType.Binary, result.MessageType);
            length += result.Count;
            if (result.EndOfMessage)
            {
                return buffer[..length];
            }
        }
    }

    private static bool IsKeyFrame(byte[] message) => (ReadUInt32(message, 12) & H264Header.KeyFrameFlag) != 0;

    // Access unit after the header
    private static void AssertAnnexB(byte[] message)
    {
        var data = message.AsSpan(ReadUInt16(message, 2));
        Assert.True(data.StartsWith((ReadOnlySpan<byte>)[0, 0, 0, 1]) || data.StartsWith((ReadOnlySpan<byte>)[0, 0, 1]));
    }

    private static int ReadUInt16(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset));

    private static uint ReadUInt32(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset));

    private static double ReadDouble(byte[] bytes, int offset) => BinaryPrimitives.ReadDoubleLittleEndian(bytes.AsSpan(offset));
}
