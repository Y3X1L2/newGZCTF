using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace GZCTF.Models.Request;

/// <summary>
/// Validates optional contact phone numbers used by account and admin user APIs.
/// </summary>
public sealed partial class PhoneNumberAttribute : ValidationAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (value is null)
            return ValidationResult.Success;

        if (value is not string raw)
            return new ValidationResult(FormatErrorMessage(validationContext.DisplayName));

        var phone = raw.Trim();
        if (phone.Length == 0)
            return ValidationResult.Success;

        return PhoneRegex().IsMatch(phone)
            ? ValidationResult.Success
            : new ValidationResult(FormatErrorMessage(validationContext.DisplayName));
    }

    [GeneratedRegex(@"^(?:1[3-9]\d{9}|\+[1-9]\d{7,14})$", RegexOptions.CultureInvariant)]
    private static partial Regex PhoneRegex();
}
