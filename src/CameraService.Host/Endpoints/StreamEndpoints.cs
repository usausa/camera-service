namespace CameraService.Host.Endpoints;

using System.Buffers;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;

using CameraService.Core;
using CameraService.Host.Services;

using Microsoft.AspNetCore.Http.Features;

public static class StreamEndpoints
{
    private const string MjpegClient = "MJPEG";

    private const string H264Client = "H.264";

    private const string NoCache = "no-cache, no-store";

    private const string Boundary = "frame";

    private const string MjpegContentType = "multipart/x-mixed-replace; boundary=" + Boundary;

    // Parameter of the viewer, which reads the same stream with fetch: a browser may refuse a multipart response for anything other than an image
    private const string RawParameter = "raw";

    private const string RawContentType = "application/octet-stream";

    // A browser shows a part when the header of the next part arrives, so the boundary and the next part header follow each frame
    private static readonly byte[] PartDelimiter = Encoding.ASCII.GetBytes($"\r\n--{Boundary}\r\nContent-Type: image/jpeg\r\n\r\n");

    // Includes opening the camera and the warm-up
    private static readonly TimeSpan SnapshotTimeout = TimeSpan.FromSeconds(10);

    private static readonly TimeSpan CloseTimeout = TimeSpan.FromSeconds(1);

    public static void MapStreamEndpoints(this WebApplication app)
    {
        app.MapGet("/cameras/{id}/stream.mjpg", HandleMjpegAsync);
        app.MapGet("/cameras/{id}/snapshot.jpg", HandleSnapshotAsync);
        app.Map("/cameras/{id}/h264", HandleH264Async);
    }

    //--------------------------------------------------------------------------------
    // MJPEG
    //--------------------------------------------------------------------------------

    private static async Task HandleMjpegAsync(HttpContext context, string id, CameraManager manager, IHostApplicationLifetime lifetime, ILoggerFactory loggerFactory)
    {
        var camera = manager.Find(id);
        if (camera is null)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        var log = loggerFactory.CreateLogger(typeof(StreamEndpoints));
        var connection = context.Connection;

        var response = context.Response;
        response.ContentType = context.Request.Query.ContainsKey(RawParameter) ? RawContentType : MjpegContentType;
        response.Headers.CacheControl = NoCache;
        context.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();

        // The camera stays open while the client is connected
        using var lease = camera.Channel.Acquire();
        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, lifetime.ApplicationStopping);
        using var subscription = camera.Jpeg.Hub.Subscribe(stopping.Token);
        log.InfoClientConnected(camera.Id, MjpegClient, connection.RemoteIpAddress, connection.RemotePort);

        var writer = response.BodyWriter;
        writer.Write(PartDelimiter.AsSpan(2));
        try
        {
            while (true)
            {
                var frame = await subscription.WaitAsync();
                try
                {
                    // Copied to the response buffer
                    writer.Write(frame.Data.Span);
                    writer.Write(PartDelimiter);
                    subscription.Complete(frame);
                }
                finally
                {
                    frame.Release();
                }

                var result = await writer.FlushAsync(stopping.Token);
                if (result.IsCompleted || result.IsCanceled)
                {
                    break;
                }
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException)
        {
            // Closed by the client or the application is stopping
        }

        log.InfoClientDisconnected(camera.Id, MjpegClient, connection.RemoteIpAddress, connection.RemotePort, subscription.SentFrames, subscription.SkippedFrames);
    }

    //--------------------------------------------------------------------------------
    // H.264
    //--------------------------------------------------------------------------------

    // WebSocket of H.264: a binary message for each frame (header and access unit), a text message from the client asks for a key frame
    private static async Task HandleH264Async(HttpContext context, string id, CameraManager manager, IHostApplicationLifetime lifetime, ILoggerFactory loggerFactory)
    {
        var camera = manager.Find(id);
        if (camera?.H264 is not { } h264)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        var log = loggerFactory.CreateLogger(typeof(StreamEndpoints));
        var connection = context.Connection;

        var encoderFailure = h264.PrepareClient();
        using var client = new H264Connection(await context.WebSockets.AcceptWebSocketAsync(), h264, context.RequestAborted, lifetime.ApplicationStopping, encoderFailure);
        // The camera stays open while the client is connected
        using var lease = camera.Channel.Acquire();
        using var subscription = h264.Hub.Subscribe(client.Stopping);
        log.InfoClientConnected(camera.Id, H264Client, connection.RemoteIpAddress, connection.RemotePort);

        await client.RunAsync(subscription);

        log.InfoClientDisconnected(camera.Id, H264Client, connection.RemoteIpAddress, connection.RemotePort, subscription.SentFrames, subscription.SkippedFrames);
    }

    //--------------------------------------------------------------------------------
    // Snapshot
    //--------------------------------------------------------------------------------

    private static async Task HandleSnapshotAsync(HttpContext context, string id, CameraManager manager, IHostApplicationLifetime lifetime)
    {
        var camera = manager.Find(id);
        if (camera is null)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        // The camera stays open for the idle time after the snapshot
        using var lease = camera.Channel.Acquire();
        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, lifetime.ApplicationStopping);
        stopping.CancelAfter(SnapshotTimeout);
        using var subscription = camera.Jpeg.Hub.Subscribe(stopping.Token);

        EncodedFrame frame;
        try
        {
            frame = await subscription.WaitAsync();
        }
        catch (OperationCanceledException)
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            return;
        }

        try
        {
            var response = context.Response;
            response.ContentType = "image/jpeg";
            response.ContentLength = frame.Data.Length;
            response.Headers.CacheControl = NoCache;
            await response.Body.WriteAsync(frame.Data, context.RequestAborted);
        }
        finally
        {
            frame.Release();
        }
    }

    // A client of the H.264 stream: the frames are sent until the client closes, the encoder fails or the application stops,
    // and a text message from the client asks for a key frame
    private sealed class H264Connection : IDisposable
    {
        private readonly WebSocket socket;

        private readonly H264Publisher h264;

        private readonly CancellationToken applicationStopping;

        private readonly CancellationToken encoderFailure;

        // Canceled when the client closes, the encoder fails or the application stops
        private readonly CancellationTokenSource stopping;

        // Ends the wait for the close from the client
        private readonly CancellationTokenSource closing = new();

        public CancellationToken Stopping => stopping.Token;

        public H264Connection(WebSocket socket, H264Publisher h264, CancellationToken requestAborted, CancellationToken applicationStopping, CancellationToken encoderFailure)
        {
            this.socket = socket;
            this.h264 = h264;
            this.applicationStopping = applicationStopping;
            this.encoderFailure = encoderFailure;
            stopping = CancellationTokenSource.CreateLinkedTokenSource(requestAborted, applicationStopping, encoderFailure);
        }

        public void Dispose()
        {
            closing.Dispose();
            stopping.Dispose();
            socket.Dispose();
        }

        public async Task RunAsync(FrameSubscription subscription)
        {
            var receiveTask = ReceiveAsync();
            try
            {
                await SendAsync(subscription);
                await CloseOutputAsync(GetCloseStatus());
            }
            finally
            {
                // Waits for the close from the client, aborts if it does not answer
                closing.CancelAfter(CloseTimeout);
                await receiveTask;
            }
        }

        // InternalServerError: there is no encoder, the viewer uses MJPEG instead of connecting again
        private WebSocketCloseStatus GetCloseStatus()
        {
            if (applicationStopping.IsCancellationRequested)
            {
                return WebSocketCloseStatus.EndpointUnavailable;
            }

            return encoderFailure.IsCancellationRequested ? WebSocketCloseStatus.InternalServerError : WebSocketCloseStatus.NormalClosure;
        }

        // Receives until the client closes, then stops the sending
        private async Task ReceiveAsync()
        {
            var buffer = new byte[256];
            try
            {
                while (true)
                {
                    var result = await socket.ReceiveAsync(buffer.AsMemory(), closing.Token);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        break;
                    }

                    // A text message from a client that cannot decode (a decoder error or a full queue) asks for a key frame
                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        h264.RequestKeyFrame();
                    }
                }
            }
            catch (Exception ex) when (ex is WebSocketException or OperationCanceledException)
            {
                // Connection lost or aborted
            }
            finally
            {
                await stopping.CancelAsync();
            }
        }

        // The frames wait for a key frame (requested from the encoder) at the start and after a skipped frame
        private async Task SendAsync(FrameSubscription subscription)
        {
            var keyFrameGate = new KeyFrameGate();
            using var sender = new FrameSender(socket, stopping.Token);
            h264.RequestKeyFrame();
            try
            {
                while (true)
                {
                    var frame = await subscription.WaitAsync();
                    try
                    {
                        if (!keyFrameGate.CanSend(frame.Index, frame.IsKeyFrame, subscription.LastIndex))
                        {
                            subscription.Skip(frame);
                            h264.RequestKeyFrame();
                            continue;
                        }

                        await sender.SendAsync(frame.Data);
                        subscription.Complete(frame);
                    }
                    finally
                    {
                        frame.Release();
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Closed by the client or the application is stopping
            }
            catch (WebSocketException)
            {
                // Connection lost
            }
        }

        private async Task CloseOutputAsync(WebSocketCloseStatus status)
        {
            if (socket.State is not (WebSocketState.Open or WebSocketState.CloseReceived))
            {
                return;
            }

            using var timeout = new CancellationTokenSource(CloseTimeout);
            try
            {
                await socket.CloseOutputAsync(status, null, timeout.Token);
            }
            catch (Exception ex) when (ex is WebSocketException or OperationCanceledException)
            {
                // Connection lost
            }
        }
    }

    // Sends a WebSocket message without the token: with a cancelable token, ManagedWebSocket takes its slower path for each send
    // (an asynchronous wait and a registration, and the send buffer goes back to the pool on another thread, which allocates).
    // A send in progress when the token is canceled is stopped by aborting the connection, as ManagedWebSocket does.
    private sealed class FrameSender : IDisposable
    {
        private readonly WebSocket socket;

        private readonly CancellationToken cancellationToken;

        private readonly CancellationTokenRegistration registration;

        private int sending;

        public FrameSender(WebSocket socket, CancellationToken cancellationToken)
        {
            this.socket = socket;
            this.cancellationToken = cancellationToken;
            registration = cancellationToken.UnsafeRegister(static state => ((FrameSender)state!).OnCanceled(), this);
        }

        public void Dispose() => registration.Dispose();

        // The state of a send that waits (a large frame) is pooled instead of allocated for each frame
        [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder))]
        public async ValueTask SendAsync(ReadOnlyMemory<byte> message)
        {
            // Full fence: either the cancel sees the send, or the send sees the cancel
            Interlocked.Exchange(ref sending, 1);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                await socket.SendAsync(message, WebSocketMessageType.Binary, true, CancellationToken.None);
            }
            finally
            {
                Volatile.Write(ref sending, 0);
            }
        }

        // Not while waiting for a frame, so that the connection is closed normally
        private void OnCanceled()
        {
            if (Volatile.Read(ref sending) == 1)
            {
                socket.Abort();
            }
        }
    }
}
