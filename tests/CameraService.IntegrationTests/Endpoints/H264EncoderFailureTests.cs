namespace CameraService.Host.Endpoints;

using System.Net;
using System.Net.WebSockets;

using CameraService.Core;
using CameraService.Host.Testing;
using CameraService.Platform.Windows;

using Microsoft.Extensions.DependencyInjection;

// A client must not wait for frames that do not come: it is closed with a status that makes the viewer use MJPEG
public sealed class H264EncoderFailureTests
{
    private const string H264Path = "/cameras/test/h264";

    private const string CreateError = "No encoder.";

    [Fact]
    public Task ClientIsClosedWhenEncoderCannotBeCreated() =>
        ServiceTestServer.RunWithServicesAsync(
            null,
            static services => services.AddSingleton<IH264EncoderFactory>(new TestEncoderFactory { Fails = true }),
            static async (server, cancellationToken) =>
            {
                // Arrange
                using var socket = new ClientWebSocket();
                await socket.ConnectAsync(server.GetWebSocketUri(H264Path), cancellationToken).ConfigureAwait(true);

                // Act
                var type = await ReceiveTypeAsync(socket, cancellationToken).ConfigureAwait(true);
                using var health = await server.Client.GetAsync(server.GetUri("/health"), cancellationToken).ConfigureAwait(true);

                // Assert (still answered with 200, the live view works with MJPEG)
                Assert.Equal(WebSocketMessageType.Close, type);
                Assert.Equal(WebSocketCloseStatus.InternalServerError, socket.CloseStatus);
                Assert.Equal(CreateError, server.Manager.Find(ServiceTestServer.CameraId)!.H264!.ErrorMessage);
                Assert.Equal(HttpStatusCode.OK, health.StatusCode);
                Assert.Equal("Degraded", await health.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(true));
            });

    // Sooner than the retry interval (10 seconds): the first frame of the new client asks for the encoder
    [Fact]
    public Task NewClientTriesEncoderAgain()
    {
        var factory = new TestEncoderFactory { Fails = true };
        return ServiceTestServer.RunWithServicesAsync(
            null,
            services => services.AddSingleton<IH264EncoderFactory>(factory),
            async (server, cancellationToken) =>
            {
                // Arrange
                using var failed = new ClientWebSocket();
                await failed.ConnectAsync(server.GetWebSocketUri(H264Path), cancellationToken).ConfigureAwait(true);
                await ReceiveTypeAsync(failed, cancellationToken).ConfigureAwait(true);
                factory.Fails = false;

                // Act
                using var socket = new ClientWebSocket();
                await socket.ConnectAsync(server.GetWebSocketUri(H264Path), cancellationToken).ConfigureAwait(true);
                var type = await ReceiveTypeAsync(socket, cancellationToken).ConfigureAwait(true);
                using var health = await server.Client.GetAsync(server.GetUri("/health"), cancellationToken).ConfigureAwait(true);

                // Assert
                Assert.Equal(WebSocketCloseStatus.InternalServerError, failed.CloseStatus);
                Assert.Equal(WebSocketMessageType.Binary, type);
                Assert.Null(server.Manager.Find(ServiceTestServer.CameraId)!.H264!.ErrorMessage);
                Assert.Equal("Healthy", await health.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(true));
            });
    }

    [Fact]
    public Task ClientIsClosedWhenEncoderFails() =>
        ServiceTestServer.RunWithServicesAsync(
            null,
            static services => services.AddSingleton<IH264EncoderFactory>(new TestEncoderFactory { Faults = true }),
            static async (server, cancellationToken) =>
            {
                // Arrange
                using var socket = new ClientWebSocket();
                await socket.ConnectAsync(server.GetWebSocketUri(H264Path), cancellationToken).ConfigureAwait(true);

                // Act
                var type = await ReceiveTypeAsync(socket, cancellationToken).ConfigureAwait(true);

                // Assert
                Assert.Equal(WebSocketMessageType.Close, type);
                Assert.Equal(WebSocketCloseStatus.InternalServerError, socket.CloseStatus);
                Assert.Contains("H.264 encoder failed.", server.Manager.Find(ServiceTestServer.CameraId)!.H264!.ErrorMessage, StringComparison.Ordinal);
            });

    private static async Task<WebSocketMessageType> ReceiveTypeAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[1024 * 1024];
        var result = await socket.ReceiveAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(true);
        return result.MessageType;
    }

    private sealed class TestEncoderFactory : IH264EncoderFactory
    {
        private readonly H264EncoderFactory inner = new();

        public bool Fails { get; set; }

        public bool Faults { get; set; }

        public IH264Encoder Create(H264EncoderOptions options, H264OutputCallback callback)
        {
            if (Fails)
            {
                throw new InvalidOperationException(CreateError);
            }

            return Faults ? new FaultedEncoder() : inner.Create(options, callback);
        }
    }

    // Stops working with the first frame
    private sealed class FaultedEncoder : IH264Encoder
    {
        public string Name => "Faulted";

        public bool IsHardware => false;

        public long DroppedFrames => 0;

        public bool IsFaulted { get; private set; }

        public int FaultCode => unchecked((int)0x80004005);

        public void Dispose()
        {
        }

        public void RequestKeyFrame()
        {
        }

        public bool Encode(ReadOnlySpan<byte> y, int yStride, ReadOnlySpan<byte> uv, int uvStride, long timestamp) => Fault();

        public bool EncodeYuy2(ReadOnlySpan<byte> yuy2, int stride, long timestamp) => Fault();

        private bool Fault()
        {
            IsFaulted = true;
            return false;
        }
    }
}
