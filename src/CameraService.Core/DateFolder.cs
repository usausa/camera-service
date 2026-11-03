namespace CameraService.Core;

// Folder of the saved images of one day (yyyyMMdd)
public readonly record struct DateFolder(string Path, DateOnly Date);
