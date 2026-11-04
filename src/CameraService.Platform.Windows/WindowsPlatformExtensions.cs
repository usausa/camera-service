namespace CameraService.Platform.Windows;

using CameraService.Core;

using Microsoft.Extensions.DependencyInjection;

public static class WindowsPlatformExtensions
{
    // Cameras (MediaFrameReader), images (WIC) and H.264 (Media Foundation) of Windows
    public static IServiceCollection AddWindowsPlatform(this IServiceCollection services)
    {
        services.AddSingleton<ICameraProvider, WindowsCameraProvider>();
        services.AddSingleton<IJpegEncoderFactory, JpegEncoderFactory>();
        services.AddSingleton<IThumbnailGenerator, ThumbnailGenerator>();
        services.AddSingleton<IH264EncoderFactory, H264EncoderFactory>();
        return services;
    }
}
