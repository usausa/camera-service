namespace CameraService.Host.Endpoints;

using System.Buffers;
using System.Text;

using CameraService.Core;
using CameraService.Host.Services;

using Microsoft.AspNetCore.Http.Features;

public static class StreamEndpoints
{
    private const string NoCache = "no-cache, no-store";

    private const string Boundary = "frame";

    private const string MjpegContentType = "multipart/x-mixed-replace; boundary=" + Boundary;

    // A browser shows a part when the header of the next part arrives, so the boundary and the next part header follow each frame
    private static readonly byte[] PartDelimiter = Encoding.ASCII.GetBytes($"\r\n--{Boundary}\r\nContent-Type: image/jpeg\r\n\r\n");

    // Includes opening the camera and the warm-up
    private static readonly TimeSpan SnapshotTimeout = TimeSpan.FromSeconds(10);

    public static void MapStreamEndpoints(this WebApplication app)
    {
        app.MapGet("/cameras/{id}/stream.mjpg", HandleMjpegAsync);
        app.MapGet("/cameras/{id}/snapshot.jpg", HandleSnapshotAsync);
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
        response.ContentType = MjpegContentType;
        response.Headers.CacheControl = NoCache;
        context.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();

        // The camera stays open while the client is connected
        using var lease = camera.Channel.Acquire();
        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, lifetime.ApplicationStopping);
        using var subscription = camera.Jpeg.Hub.Subscribe(stopping.Token);
        log.InfoClientConnected(camera.Id, connection.RemoteIpAddress, connection.RemotePort);

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

        log.InfoClientDisconnected(camera.Id, connection.RemoteIpAddress, connection.RemotePort, subscription.SentFrames, subscription.SkippedFrames);
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
}
