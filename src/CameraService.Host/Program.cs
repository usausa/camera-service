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

// Health
builder.ConfigureHealth();

//--------------------------------------------------------------------------------
// Configure the HTTP request pipeline
//--------------------------------------------------------------------------------
var app = builder.Build();

// End point
app.MapEndpoints();

// Run
await app.RunAsync();
