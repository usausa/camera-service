namespace CameraService.Host.Application;

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
    // Health
    //--------------------------------------------------------------------------------

    public static IHostApplicationBuilder ConfigureHealth(this IHostApplicationBuilder builder)
    {
        builder.Services.AddHealthChecks();

        return builder;
    }

    //--------------------------------------------------------------------------------
    // End point
    //--------------------------------------------------------------------------------

    public static WebApplication MapEndpoints(this WebApplication app)
    {
        // Health
        app.MapHealthChecks("/health");

        return app;
    }
}
