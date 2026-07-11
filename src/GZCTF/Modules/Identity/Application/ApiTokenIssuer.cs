using System.Security.Cryptography;
using GZCTF.Modules.Identity.Domain;
using Microsoft.AspNetCore.WebUtilities;
using ApiTokenEntity = GZCTF.Modules.Identity.Domain.ApiToken;

namespace GZCTF.Modules.Identity.Application;

public sealed record ApiTokenResourceGrantSpec(string ResourceType, string ResourceId);

public sealed record IssueApiTokenCommand(
    string Name,
    IReadOnlyCollection<string> Scopes,
    IReadOnlyCollection<ApiTokenResourceGrantSpec> Resources,
    int RequestsPerMinute,
    DateTimeOffset? ExpiresAt);

public sealed record ApiTokenIssueResult(ApiTokenEntity Token, string PlainTextToken);

public sealed class ApiTokenScopeException(string message) : InvalidOperationException(message);

public sealed class ApiTokenIssuer(IApiTokenStore store, IApiTokenSecretHasher secretHasher)
{
    public async Task<ApiTokenIssueResult> IssueAsync(
        ActorContext actor,
        IssueApiTokenCommand command,
        CancellationToken cancellationToken)
    {
        if (actor.UserId is not { } creatorId || actor.Role < Role.Teacher)
            throw new UnauthorizedAccessException("Only teachers and administrators can create API tokens.");

        var name = command.Name.Trim();
        if (name.Length is < 1 or > 128)
            throw new ArgumentException("Token name must contain between 1 and 128 characters.", nameof(command));

        var scopes = command.Scopes
            .Where(scope => !string.IsNullOrWhiteSpace(scope))
            .Select(scope => scope.Trim())
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        var deniedScope = scopes.FirstOrDefault(scope => !ApiTokenScopes.IsAllowed(actor.Role, scope));
        if (deniedScope is not null)
            throw new ApiTokenScopeException($"Scope '{deniedScope}' is not available to this actor.");
        if (scopes.Length == 0)
            throw new ApiTokenScopeException("At least one API scope is required.");

        if (command.RequestsPerMinute is < 1 or > 10_000)
            throw new ArgumentOutOfRangeException(nameof(command), "Requests per minute must be between 1 and 10000.");
        if (command.ExpiresAt is { } expiresAt && expiresAt <= DateTimeOffset.UtcNow)
            throw new ArgumentException("Token expiration must be in the future.", nameof(command));

        var resources = command.Resources
            .Where(resource => !string.IsNullOrWhiteSpace(resource.ResourceType) &&
                               !string.IsNullOrWhiteSpace(resource.ResourceId))
            .Select(resource => new ApiTokenResourceGrantSpec(
                resource.ResourceType.Trim().ToLowerInvariant(),
                resource.ResourceId.Trim()))
            .DistinctBy(resource => (resource.ResourceType, resource.ResourceId))
            .ToArray();
        if (resources.Length > 128)
            throw new ArgumentException("A token cannot contain more than 128 resource grants.", nameof(command));
        if (resources.Any(resource => resource.ResourceType.Length is < 1 or > 64 ||
                                      resource.ResourceId.Length is < 1 or > 128 ||
                                      resource.ResourceType.Any(character =>
                                          !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_' and not '.')))
            throw new ArgumentException("A resource grant contains an invalid type or identifier.", nameof(command));

        var token = new ApiTokenEntity
        {
            Name = name,
            CreatorId = creatorId,
            RequestsPerMinute = command.RequestsPerMinute,
            ExpiresAt = command.ExpiresAt
        };

        var secret = RandomNumberGenerator.GetBytes(32);
        token.Scopes = scopes.Select(scope => new ApiTokenScopeGrant
        {
            TokenId = token.Id,
            Scope = scope
        }).ToList();
        token.Resources = resources
            .Select(resource => new ApiTokenResourceGrant
            {
                TokenId = token.Id,
                ResourceType = resource.ResourceType,
                ResourceId = resource.ResourceId
            }).ToList();

        try
        {
            token.SecretHash = secretHasher.Hash(secret);
            await store.AddAsync(token, cancellationToken);
            var plainText = $"gzctf_pat_{token.Id:N}.{WebEncoders.Base64UrlEncode(secret)}";
            return new ApiTokenIssueResult(token, plainText);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secret);
        }
    }
}
