namespace CameraService.Core;

// Small JPEG of a saved image for the lists of the pages
public interface IThumbnailGenerator
{
    // Scaled down to about the width while decoding (the decoder chooses the nearest scale it supports),
    // null when the data is not an image that can be scaled this way
    byte[]? Generate(ReadOnlySpan<byte> image, int width);
}
