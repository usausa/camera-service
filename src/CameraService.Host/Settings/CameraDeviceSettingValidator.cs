namespace CameraService.Host.Settings;

// Each camera is validated by its attributes (with the saving and the timestamp), and the Ids must be unique (they are used in the URLs and the folder names)
public sealed class CameraDeviceSettingValidator : IValidateOptions<List<CameraDeviceSetting>>
{
    public ValidateOptionsResult Validate(string? name, List<CameraDeviceSetting> options)
    {
        var errors = new List<string>();
        for (var i = 0; i < options.Count; i++)
        {
            var camera = options[i];
            var prefix = String.Create(CultureInfo.InvariantCulture, $"Cameras[{i}]");
            ValidateObject(errors, prefix, camera);
            ValidateObject(errors, prefix + ".Snapshot", camera.Snapshot);
            ValidateObject(errors, prefix + ".Snapshot.Timestamp", camera.Snapshot.Timestamp);
        }

        foreach (var id in options.GroupBy(static x => x.Id, StringComparer.OrdinalIgnoreCase).Where(static x => x.Count() > 1).Select(static x => x.Key))
        {
            errors.Add($"Cameras: the id [{id}] is used more than once.");
        }

        // The images of two cameras in one folder would replace each other
        foreach (var path in options.GroupBy(static x => x.Snapshot.Path.Length > 0 ? x.Snapshot.Path : x.Id, StringComparer.OrdinalIgnoreCase).Where(static x => x.Count() > 1).Select(static x => x.Key))
        {
            errors.Add($"Cameras: the snapshot path [{path}] is used by more than one camera.");
        }

        return errors.Count > 0 ? ValidateOptionsResult.Fail(errors) : ValidateOptionsResult.Success;
    }

    private static void ValidateObject(List<string> errors, string prefix, object value)
    {
        var results = new List<ValidationResult>();
        if (!Validator.TryValidateObject(value, new ValidationContext(value), results, true))
        {
            errors.AddRange(results.Select(x => $"{prefix}: {x.ErrorMessage}"));
        }
    }
}
