using GZCTF.Modules.Identity.Domain;
using Microsoft.AspNetCore.WebUtilities;
using ApiTokenEntity = GZCTF.Modules.Identity.Domain.ApiToken;

namespace GZCTF.Modules.Identity.Application;

public sealed record ApiTokenValidationResult(bool Succeeded, ApiTokenEntity? Token, string? ErrorCode)
{
    public static ApiTokenValidationResult Failure(string code) => new(false, null, code);
    public static ApiTokenValidationResult Success(ApiTokenEntity token) => new(true, token, null);
}

public sealed class ApiTokenValidator(IApiTokenStore store, IApiTokenSecretHasher secretHasher)
{
    private const string Prefix = "gzctf_pat_";

    public async Task<ApiTokenValidationResult> ValidateAsync(
        string plainTextToken,
        CancellationToken cancellationToken)
    {
        if (!TryParse(plainTextToken, out var id, out var secret))
            return ApiTokenValidationResult.Failure("invalid_token");

        try
        {
            var validation = await store.FindForValidationAsync(id, cancellationToken);
            if (validation is null || validation.CreatorRole < Role.Teacher)
                return ApiTokenValidationResult.Failure("invalid_token");

            var token = validation.Token;
            if (token.RevokedAt.HasValue ||
                token.ExpiresAt is { } expiresAt && expiresAt <= DateTimeOffset.UtcNow)
                return ApiTokenValidationResult.Failure("invalid_token");

            if (token.Scopes.Any(scope => !ApiTokenScopes.IsAllowed(validation.CreatorRole, scope.Scope)))
                return ApiTokenValidationResult.Failure("invalid_token");

            if (!secretHasher.Verify(secret, token.SecretHash))
                return ApiTokenValidationResult.Failure("invalid_token");

            return ApiTokenValidationResult.Success(token);
        }
        finally
        {
            Array.Clear(secret);
        }
    }

    private static bool TryParse(string value, out Guid id, out byte[] secret)
    {
        id = default;
        secret = [];
        if (!value.StartsWith(Prefix, StringComparison.Ordinal) || value.Split('.') is not [var publicPart, var secretPart])
            return false;

        var idText = publicPart[Prefix.Length..];
        if (idText.Length != 32 || !Guid.TryParseExact(idText, "N", out id))
            return false;

        try
        {
            secret = WebEncoders.Base64UrlDecode(secretPart);
            return secret.Length == 32;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
