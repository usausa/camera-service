namespace CameraService.Host.Application;

using System.Runtime.InteropServices;

using CameraService.Core;
using CameraService.Host.Application.HealthChecks;
using CameraService.Host.Components;
using CameraService.Host.Endpoints;
using CameraService.Host.Services;
using CameraService.Host.Settings;
using CameraService.Host.Workers;
#if WINDOWS
using CameraService.Platform.Windows;
#else
using CameraService.Platform.Linux;
#endif

using Serilog;

public static class ApplicationExtensions
{
    //--------------------------------------------------------------------------------
    // Host
    //--------------------------------------------------------------------------------

    public static IHostApplicationBuilder ConfigureHost(this IHostApplicationBuilder builder)
    {
        // Runs as a Windows service or a systemd service when started as one
        builder.Services
            .AddWindowsService()
            .AddSystemd();

        return builder;
    }

    //--------------------------------------------------------------------------------
    // Logging
    //--------------------------------------------------------------------------------

    public static IHostApplicationBuilder ConfigureLogging(this IHostApplicationBuilder builder)
    {
        // The log files go to the logs directory when systemd runs the service with LogsDirectory
        SetLogFolder(builder.Configuration, Environment.GetEnvironmentVariable("LOGS_DIRECTORY"));

        builder.Logging.ClearProviders();
        builder.Services.AddSerilog(options => options.ReadFrom.Configuration(builder.Configuration));

        return builder;
    }

    // The file sinks of Serilog write into the folder (the first one, systemd separates more than one with ':'), nothing changes without a folder
    internal static void SetLogFolder(IConfiguration configuration, string? folder)
    {
        if (String.IsNullOrEmpty(folder))
        {
            return;
        }

        foreach (var sink in configuration.GetSection("Serilog:WriteTo").GetChildren())
        {
            if (String.Equals(sink["Name"], "File", StringComparison.Ordinal) && (sink["Args:path"] is { } path))
            {
                sink["Args:path"] = Path.Combine(folder.Split(':')[0], Path.GetFileName(path));
            }
        }
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
#if WINDOWS
        builder.Services.AddWindowsPlatform();
#else
        builder.Services.AddLinuxPlatform();
#endif

        // Service
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
        app.Logger.InfoServiceSettingsStorage(Path.GetFullPath(storage.Path, StorageSetting.BaseFolder));
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

    // Requests after the stop started are refused, so that a page that connects again does not keep the stop waiting
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

    // Separate from the end points, so that the tests run without the static files
    public static WebApplication MapBlazor(this WebApplication app)
    {
        app.MapStaticAssets();
        app.MapRazorComponents<App>()
            .AddInteractiveServerRenderMode();

        return app;
    }
}
