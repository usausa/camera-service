namespace CameraService.Platform.Windows;

using CameraService.Core;

using Microsoft.Extensions.DependencyInjection;

public static class WindowsPlatformExtensions
{
    // Cameras (MediaFrameReader) and images (WIC) of Windows
    public static IServiceCollection AddWindowsPlatform(this IServiceCollection services)
    {
        services.AddSingleton<ICameraProvider, WindowsCameraProvider>();
        services.AddSingleton<IJpegEncoderFactory, JpegEncoderFactory>();
        services.AddSingleton<IThumbnailGenerator, ThumbnailGenerator>();
        return services;
    }
}
