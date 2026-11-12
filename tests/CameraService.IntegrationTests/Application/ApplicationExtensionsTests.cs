namespace CameraService.Host.Application;

using Microsoft.Extensions.Configuration;

public sealed class ApplicationExtensionsTests
{
    private const string LogFile = "CameraService_.log";

    private const string ConfiguredPath = "../log/" + LogFile;

    // The logs directory of systemd replaces the folder of the file sink, the console sink does not change
    [Fact]
    public void LogFilesGoToLogsDirectory()
    {
        // Arrange
        using var configuration = CreateConfiguration();

        // Act (systemd separates more than one logs directory with ':')
        ApplicationExtensions.SetLogFolder(configuration, "/var/log/camera-service:/var/log/other");

        // Assert
        Assert.Equal(Path.Combine("/var/log/camera-service", LogFile), configuration["Serilog:WriteTo:1:Args:path"]);
        Assert.Null(configuration["Serilog:WriteTo:0:Args:path"]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void LogFilesStayWithoutLogsDirectory(string? folder)
    {
        // Arrange
        using var configuration = CreateConfiguration();

        // Act
        ApplicationExtensions.SetLogFolder(configuration, folder);

        // Assert
        Assert.Equal(ConfiguredPath, configuration["Serilog:WriteTo:1:Args:path"]);
    }

    private static ConfigurationManager CreateConfiguration()
    {
        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Serilog:WriteTo:0:Name"] = "Console",
            ["Serilog:WriteTo:0:Args:outputTemplate"] = "{Message}",
            ["Serilog:WriteTo:1:Name"] = "File",
            ["Serilog:WriteTo:1:Args:path"] = ConfiguredPath
        });
        return configuration;
    }
}
