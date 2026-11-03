namespace CameraService.Host.Settings;

using CameraService.Core;

// Format of the date and time that gives only the characters of the font of the timestamp
[AttributeUsage(AttributeTargets.Property)]
public sealed class TimestampFormatAttribute : ValidationAttribute
{
    // All the digits of the fields are used
    private static readonly DateTime Sample = new(2026, 12, 31, 23, 59, 59, 999, DateTimeKind.Local);

    public TimestampFormatAttribute()
        : base($"The field {{0}} must be a date and time format that gives only the characters [{TextOverlay.Characters}].")
    {
    }

    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (value is string { Length: > 0 } format)
        {
            try
            {
                if (TextOverlay.IsSupported(Sample.ToString(format, CultureInfo.InvariantCulture)))
                {
                    return ValidationResult.Success;
                }
            }
            catch (FormatException)
            {
                // Invalid format
            }
        }

        return new ValidationResult(FormatErrorMessage(validationContext.DisplayName), validationContext.MemberName is { } name ? [name] : null);
    }
}
