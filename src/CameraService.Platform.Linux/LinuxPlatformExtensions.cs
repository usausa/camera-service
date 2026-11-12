namespace CameraService.Platform.Linux;

using CameraService.Core;

using Microsoft.Extensions.DependencyInjection;

public static class LinuxPlatformExtensions
{
    // Cameras (Video for Linux) and images (TurboJPEG) of Linux, without an H.264 encoder
    public static IServiceCollection AddLinuxPlatform(this IServiceCollection services)
    {
        services.AddSingleton<ICameraProvider, LinuxCameraProvider>();
        services.AddSingleton<IJpegEncoderFactory, JpegEncoderFactory>();
        services.AddSingleton<IJpegDecoderFactory, JpegDecoderFactory>();
        services.AddSingleton<IThumbnailGenerator, ThumbnailGenerator>();
        return services;
    }
}
