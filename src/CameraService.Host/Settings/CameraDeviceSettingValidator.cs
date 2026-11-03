namespace CameraService.Host.Settings;

// Each camera is validated by its attributes, and the Ids must be unique (they are used in the URLs and the folder names)
public sealed class CameraDeviceSettingValidator : IValidateOptions<List<CameraDeviceSetting>>
{
    public ValidateOptionsResult Validate(string? name, List<CameraDeviceSetting> options)
    {
        var errors = new List<string>();
        for (var i = 0; i < options.Count; i++)
        {
            var camera = options[i];
            var results = new List<ValidationResult>();
            if (!Validator.TryValidateObject(camera, new ValidationContext(camera), results, true))
            {
                var index = i;
                errors.AddRange(results.Select(x => String.Create(CultureInfo.InvariantCulture, $"Cameras[{index}]: {x.ErrorMessage}")));
            }
        }

        foreach (var id in options.GroupBy(static x => x.Id, StringComparer.OrdinalIgnoreCase).Where(static x => x.Count() > 1).Select(static x => x.Key))
        {
            errors.Add($"Cameras: the id [{id}] is used more than once.");
        }

        return errors.Count > 0 ? ValidateOptionsResult.Fail(errors) : ValidateOptionsResult.Success;
    }
}
