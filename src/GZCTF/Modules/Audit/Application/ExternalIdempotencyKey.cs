namespace GZCTF.Modules.Audit.Application;

public static class ExternalIdempotencyKey
{
    public static string Normalize(string value)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length is < 1 or > 128 || normalized.Any(character =>
                !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_' and not '.'))
            throw new IdempotencyValidationException(
                string.IsNullOrEmpty(normalized) ? "idempotency_key_required" : "idempotency_key_invalid",
                "Idempotency-Key must contain 1-128 ASCII letters, digits, '-', '_' or '.'.");
        return normalized;
    }
}
