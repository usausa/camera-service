using Microsoft.Extensions.Hosting.WindowsServices;

//--------------------------------------------------------------------------------
// Configure builder
//--------------------------------------------------------------------------------
Directory.SetCurrentDirectory(AppContext.BaseDirectory);
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    // A service starts in the system folder, the settings are next to the executable
    ContentRootPath = WindowsServiceHelpers.IsWindowsService() ? AppContext.BaseDirectory : default
});

// Host
builder.ConfigureHost();

// Logging
builder.ConfigureLogging();

// Http
builder.ConfigureHttp();

// Blazor
builder.ConfigureBlazor();

// Health
builder.ConfigureHealth();

// Components
builder.ConfigureComponents();

//--------------------------------------------------------------------------------
// Configure the HTTP request pipeline
//--------------------------------------------------------------------------------
var app = builder.Build();

// Startup information
app.LogStartupInformation();

// Stop
app.UseStoppingGate();

// Antiforgery (Blazor)
app.UseAntiforgery();

// End point
app.MapEndpoints();

// Blazor
app.MapBlazor();

// Run
await app.RunAsync();
