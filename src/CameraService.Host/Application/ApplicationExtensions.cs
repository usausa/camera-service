namespace CameraService.Host.Application;

using System.Runtime.InteropServices;

using CameraService.Core;
using CameraService.Host.Application.HealthChecks;
using CameraService.Host.Components;
using CameraService.Host.Endpoints;
using CameraService.Host.Services;
using CameraService.Host.Settings;
using CameraService.Host.Workers;
using CameraService.Platform.Windows;

using Serilog;

public static class ApplicationExtensions
{
    //--------------------------------------------------------------------------------
    // Host
    //--------------------------------------------------------------------------------

    public static IHostApplicationBuilder ConfigureHost(this IHostApplicationBuilder builder)
    {
        // Runs as a Windows service when started by the service control manager
        builder.Services.AddWindowsService();

        return builder;
    }

    //--------------------------------------------------------------------------------
    // Logging
    //--------------------------------------------------------------------------------

    public static IHostApplicationBuilder ConfigureLogging(this IHostApplicationBuilder builder)
    {
        builder.Logging.ClearProviders();
        builder.Services.AddSerilog(options => options.ReadFrom.Configuration(builder.Configuration));

        return builder;
    }

    //--------------------------------------------------------------------------------
    // Http
    //--------------------------------------------------------------------------------

    public static IHostApplicationBuilder ConfigureHttp(this IHostApplicationBuilder builder)
    {
        // JSON
        builder.Services.ConfigureHttpJsonOptions(static options =>
        {
            options.SerializerOptions.TypeInfoResolverChain.Insert(0, ApiJsonContext.Default);
        });

        return builder;
    }

    //--------------------------------------------------------------------------------
    // Blazor
    //--------------------------------------------------------------------------------

    public static IHostApplicationBuilder ConfigureBlazor(this IHostApplicationBuilder builder)
    {
        builder.Services.AddRazorComponents()
            .AddInteractiveServerComponents();

        return builder;
    }

    //--------------------------------------------------------------------------------
    // Health
    //--------------------------------------------------------------------------------

    public static IHostApplicationBuilder ConfigureHealth(this IHostApplicationBuilder builder)
    {
        builder.Services.AddHealthChecks()
            .AddCheck<CameraHealthCheck>("cameras")
            .AddCheck<StorageHealthCheck>("storage");

        return builder;
    }

    //--------------------------------------------------------------------------------
    // Components
    //--------------------------------------------------------------------------------

    public static IHostApplicationBuilder ConfigureComponents(this IHostApplicationBuilder builder)
    {
        // Setting
        builder.Services.AddOptions<CameraSetting>().BindConfiguration("Camera").ValidateDataAnnotations().ValidateOnStart();
        builder.Services.AddSingleton(static p => p.GetRequiredService<IOptions<CameraSetting>>().Value);
        builder.Services.AddOptions<StreamSetting>().BindConfiguration("Stream").ValidateDataAnnotations().ValidateOnStart();
        builder.Services.AddSingleton(static p => p.GetRequiredService<IOptions<StreamSetting>>().Value);
        builder.Services.AddOptions<H264Setting>().BindConfiguration("Stream:H264").ValidateDataAnnotations().ValidateOnStart();
        builder.Services.AddSingleton(static p => p.GetRequiredService<IOptions<H264Setting>>().Value);
        builder.Services.AddOptions<List<CameraDeviceSetting>>().BindConfiguration("Cameras").ValidateOnStart();
        builder.Services.AddSingleton<IValidateOptions<List<CameraDeviceSetting>>, CameraDeviceSettingValidator>();
        builder.Services.AddOptions<StorageSetting>().BindConfiguration("Storage").ValidateDataAnnotations().ValidateOnStart();
        builder.Services.AddSingleton(static p => p.GetRequiredService<IOptions<StorageSetting>>().Value);
        builder.Services.AddOptions<RetentionSetting>().BindConfiguration("Retention").ValidateDataAnnotations().ValidateOnStart();
        builder.Services.AddSingleton(static p => p.GetRequiredService<IOptions<RetentionSetting>>().Value);

        // Platform
        builder.Services.AddWindowsPlatform();

        // Service
        // Not registered as TimeProvider, other components expect UTC from it
        builder.Services.AddSingleton(static _ => new LocalTimeProvider());
        builder.Services.AddSingleton<CameraManager>();
        builder.Services.AddHostedService(static p => p.GetRequiredService<CameraManager>());
        builder.Services.AddSingleton<ImageRetention>();
        builder.Services.AddSingleton<ImageStore>();
        builder.Services.AddSingleton<ThumbnailCache>();

        // Worker
        builder.Services.AddHostedService<ScheduleWorker>();

        return builder;
    }

    //--------------------------------------------------------------------------------
    // Startup
    //--------------------------------------------------------------------------------

    public static void LogStartupInformation(this WebApplication app)
    {
        var camera = app.Services.GetRequiredService<CameraSetting>();
        var stream = app.Services.GetRequiredService<StreamSetting>();
        var h264 = app.Services.GetRequiredService<H264Setting>();
        var storage = app.Services.GetRequiredService<StorageSetting>();
        var retention = app.Services.GetRequiredService<RetentionSetting>();
        var devices = app.Services.GetRequiredService<IOptions<List<CameraDeviceSetting>>>().Value;

        app.Logger.InfoServiceStart();
        app.Logger.InfoServiceSettingsRuntime(RuntimeInformation.OSDescription, RuntimeInformation.FrameworkDescription, RuntimeInformation.RuntimeIdentifier);
        app.Logger.InfoServiceSettingsEnvironment(typeof(Program).Assembly.GetName().Version, Environment.CurrentDirectory);
        app.Logger.InfoServiceSettingsCamera(camera.IdleTime, camera.WarmupTime, camera.FrameTimeout);
        app.Logger.InfoServiceSettingsStream(stream.Fps, stream.Quality);
        app.Logger.InfoServiceSettingsH264(h264.Enable, h264.Bitrate, h264.KeyFrameInterval, h264.PreferHardware);
        app.Logger.InfoServiceSettingsStorage(Path.GetFullPath(storage.Path, AppContext.BaseDirectory));
        app.Logger.InfoServiceSettingsRetention(retention.Schedule, retention.Days, retention.MinFreeSpace);
        if (devices.Count == 0)
        {
            app.Logger.WarnNoCamera();
        }

        foreach (var device in devices)
        {
            app.Logger.InfoServiceSettingsCameraDevice(device.Id, device.Device, device.DeviceId, device.ToVideoFormat().ToString());
            var snapshot = device.Snapshot;
            app.Logger.InfoServiceSettingsSnapshot(device.Id, snapshot.Enable, snapshot.Schedule, snapshot.Quality, snapshot.Timestamp.Enable ? snapshot.Timestamp.Format : string.Empty);
        }
    }

    //--------------------------------------------------------------------------------
    // Stop
    //--------------------------------------------------------------------------------

    // Requests after the stop started are refused: a page connects again at once when its connection is closed for the stop,
    // and the server would wait for the new connection until the shutdown timeout (30 seconds)
    public static WebApplication UseStoppingGate(this WebApplication app)
    {
        var stopping = app.Lifetime.ApplicationStopping;
        app.Use((context, next) =>
        {
            if (!stopping.IsCancellationRequested)
            {
                return next(context);
            }

            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            context.Response.Headers.Connection = "close";
            return Task.CompletedTask;
        });

        return app;
    }

    //--------------------------------------------------------------------------------
    // End point
    //--------------------------------------------------------------------------------

    public static WebApplication MapEndpoints(this WebApplication app)
    {
        // H.264 streams
        app.UseWebSockets();

        // Health
        app.MapHealthChecks("/health");

        // Camera
        app.MapStreamEndpoints();
        app.MapDeviceEndpoints();
        app.MapImageEndpoints();

        return app;
    }

    // Separate from the end points, so that the tests run the end points without the pages and their static files
    public static WebApplication MapBlazor(this WebApplication app)
    {
        app.MapStaticAssets();
        app.MapRazorComponents<App>()
            .AddInteractiveServerRenderMode();

        return app;
    }
}
