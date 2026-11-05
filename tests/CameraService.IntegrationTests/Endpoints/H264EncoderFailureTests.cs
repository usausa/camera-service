namespace CameraService.Host.Endpoints;

using System.Net;
using System.Net.WebSockets;

using CameraService.Core;
using CameraService.Host.Testing;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// A client must not wait for frames that do not come: it is closed with a status that makes the viewer use MJPEG
public sealed class H264EncoderFailureTests
{
    private const string H264Path = "/cameras/test/h264";

    [Fact]
    public Task ClientIsClosedWhenEncoderCannotBeCreated() =>
        ServiceTestServer.RunWithServicesAsync(
            null,
            static services => services.AddSingleton<IH264EncoderFactory>(new TestH264EncoderFactory { Fails = true }),
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
                Assert.Equal(TestH264EncoderFactory.CreateError, server.Manager.Find(ServiceTestServer.CameraId)!.H264!.ErrorMessage);
                Assert.Equal(HttpStatusCode.OK, health.StatusCode);
                Assert.Equal("Degraded", await health.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(true));
            });

    // Sooner than the retry interval (10 seconds): the first frame of the new client asks for the encoder
    [Fact]
    public Task NewClientTriesEncoderAgain()
    {
        var factory = new TestH264EncoderFactory { Fails = true };
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
            static services => services.AddSingleton<IH264EncoderFactory>(new TestH264EncoderFactory { Faults = true }),
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

    // Each frame of the camera (30 per second) has the same layout, so the error is the same
    [Fact]
    public Task UnexpectedLayoutIsLoggedOnce()
    {
        var factory = new TestH264EncoderFactory { RejectsFrames = true };
        return ServiceTestServer.RunWithServicesAsync(
            null,
            services => services.AddSingleton<IH264EncoderFactory>(factory),
            async (server, cancellationToken) =>
            {
                // Arrange
                using var socket = new ClientWebSocket();
                await socket.ConnectAsync(server.GetWebSocketUri(H264Path), cancellationToken).ConfigureAwait(true);
                await ServiceTestServer.WaitUntilAsync(() => factory.Encoder is not null, cancellationToken).ConfigureAwait(true);

                // Act
                var frames = server.Channel.CapturedFrames;
                await ServiceTestServer.WaitUntilAsync(() => server.Channel.CapturedFrames >= frames + 5, cancellationToken).ConfigureAwait(true);

                // Assert
                Assert.Single(server.Logs.Entries, static x => (x.Level == LogLevel.Error) && x.Message.StartsWith("Encode failed.", StringComparison.Ordinal));
            });
    }

    private static async Task<WebSocketMessageType> ReceiveTypeAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[1024 * 1024];
        var result = await socket.ReceiveAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(true);
        return result.MessageType;
    }
}
