namespace CameraService.Host.Settings;

using Mofucat.JobScheduler;

// Cron expression: 5 fields, or 6 fields with the seconds first
[AttributeUsage(AttributeTargets.Property)]
public sealed class CronScheduleAttribute : ValidationAttribute
{
    public CronScheduleAttribute()
        : base("The field {0} must be a cron expression of 5 or 6 fields.")
    {
    }

    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (value is string expression)
        {
            try
            {
                _ = CronExpression.Parse(expression);
                return ValidationResult.Success;
            }
            catch (FormatException ex)
            {
                return new ValidationResult($"{FormatErrorMessage(validationContext.DisplayName)} {ex.Message}", GetMemberNames(validationContext));
            }
        }

        return new ValidationResult(FormatErrorMessage(validationContext.DisplayName), GetMemberNames(validationContext));
    }

    private static string[]? GetMemberNames(ValidationContext validationContext) =>
        validationContext.MemberName is { } name ? [name] : null;
}
