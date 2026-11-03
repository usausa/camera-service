namespace CameraService.Host.Testing;

using System.Globalization;

using CameraService.Core;
using CameraService.Host.Application;
using CameraService.Host.Services;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// The service on a free port of the loopback address, with the test camera instead of the cameras of the PC.
// The settings are only the values given here (the appsettings of the application are not read), and the pages are not mapped.
internal sealed class ServiceTestServer : IAsyncDisposable
{
    public const string CameraId = "test";

    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(50);

    private readonly WebApplication app;

    private readonly string directory;

    public Uri BaseAddress { get; }

    public HttpClient Client { get; }

    public TestCameraProvider Camera { get; }

    public CameraManager Manager { get; }

    public CameraChannel Channel => Manager.Find(CameraId)!.Channel;

    private ServiceTestServer(WebApplication app, string directory, TestCameraProvider camera)
    {
        this.app = app;
        this.directory = directory;
        Camera = camera;
        Manager = app.Services.GetRequiredService<CameraManager>();
        BaseAddress = new Uri(app.Urls.First());
        Client = new HttpClient { BaseAddress = BaseAddress };
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await app.StopAsync().ConfigureAwait(true);
        await app.DisposeAsync().ConfigureAwait(true);
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, true);
        }
    }

    // settings: values that replace the defaults (for example "Camera:IdleTime")
    public static async Task RunAsync(IReadOnlyDictionary<string, string?>? settings, Func<ServiceTestServer, CancellationToken, Task> test)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TestTimeout);
        var server = await StartAsync(settings, timeout.Token).ConfigureAwait(true);
        try
        {
            await test(server, timeout.Token).ConfigureAwait(true);
        }
        finally
        {
            await server.DisposeAsync().ConfigureAwait(true);
        }
    }

    // Polls the condition (the steps of the camera run on a timer)
    public static async Task WaitUntilAsync(Func<bool> condition, CancellationToken cancellationToken)
    {
        while (!condition())
        {
            await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(true);
        }
    }

    public Uri GetUri(string path) => new(BaseAddress, path);

    private static async Task<ServiceTestServer> StartAsync(IReadOnlyDictionary<string, string?>? settings, CancellationToken cancellationToken)
    {
        // The content root is an empty folder, so the appsettings of the application are not read
        var directory = Path.Combine(Path.GetTempPath(), "CameraService.IntegrationTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var values = new Dictionary<string, string?>
        {
            ["Cameras:0:Id"] = CameraId,
            ["Cameras:0:Device"] = TestCameraProvider.DeviceName,
            ["Cameras:0:Format"] = "Yuy2",
            ["Cameras:0:Width"] = TestCameraProvider.Width.ToString(CultureInfo.InvariantCulture),
            ["Cameras:0:Height"] = TestCameraProvider.Height.ToString(CultureInfo.InvariantCulture),
            ["Cameras:0:Fps"] = TestCameraProvider.FrameRate.ToString(CultureInfo.InvariantCulture),
            // Short times, so that the tests see the camera close and open again
            ["Camera:IdleTime"] = "1",
            ["Camera:WarmupTime"] = "0",
            ["Camera:FrameTimeout"] = "1",
            ["Stream:Fps"] = "30"
        };
        foreach (var (key, value) in settings ?? new Dictionary<string, string?>())
        {
            values[key] = value;
        }

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production, ContentRootPath = directory });
        builder.Configuration.AddInMemoryCollection(values);
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.ConfigureHttp();
        builder.ConfigureHealth();
        builder.ConfigureComponents();
        // Replaces the cameras of the PC (the last registration is used)
        var camera = new TestCameraProvider();
        builder.Services.AddSingleton<ICameraProvider>(camera);

        var app = builder.Build();
        app.MapEndpoints();
        try
        {
            await app.StartAsync(cancellationToken).ConfigureAwait(true);
            return new ServiceTestServer(app, directory, camera);
        }
        catch
        {
            await app.DisposeAsync().ConfigureAwait(true);
            Directory.Delete(directory, true);
            throw;
        }
    }
}
