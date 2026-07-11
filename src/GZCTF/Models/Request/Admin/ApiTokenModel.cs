using System.ComponentModel.DataAnnotations;
using GZCTF.Modules.Identity.Domain;

namespace GZCTF.Models.Request.Admin;

/// <summary>
/// API token creation model.
/// </summary>
public class ApiTokenCreateModel
{
    /// <summary>
    /// The user-friendly name for the token to identify its purpose.
    /// </summary>
    [Required]
    [MaxLength(128)]
    public required string Name { get; set; }

    [MinLength(1)]
    public List<string> Scopes { get; set; } = [];

    public List<ApiTokenResourceGrantModel> Resources { get; set; } = [];

    [Range(1, 10_000)]
    public int RequestsPerMinute { get; set; } = 60;

    public DateTimeOffset? ExpiresAt { get; set; }
}

public sealed record ApiTokenResourceGrantModel(string ResourceType, string ResourceId);

public sealed record ApiTokenModel(
    Guid Id,
    string Name,
    Guid CreatorId,
    IReadOnlyCollection<string> Scopes,
    IReadOnlyCollection<ApiTokenResourceGrantModel> Resources,
    int RequestsPerMinute,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? LastUsedAt,
    DateTimeOffset? RevokedAt)
{
    public static ApiTokenModel FromEntity(ApiToken token) => new(
        token.Id,
        token.Name,
        token.CreatorId,
        token.Scopes.Select(scope => scope.Scope).ToArray(),
        token.Resources.Select(resource =>
            new ApiTokenResourceGrantModel(resource.ResourceType, resource.ResourceId)).ToArray(),
        token.RequestsPerMinute,
        token.CreatedAt,
        token.ExpiresAt,
        token.LastUsedAt,
        token.RevokedAt);
}

public sealed record ApiTokenResponse(string PlainTextToken, ApiTokenModel Info);
