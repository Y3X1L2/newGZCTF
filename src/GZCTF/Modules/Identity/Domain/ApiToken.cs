namespace GZCTF.Modules.Identity.Domain;

public class ApiToken
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public required string Name { get; set; }
    public Guid CreatorId { get; set; }
    public byte[] SecretHash { get; set; } = [];
    public int RequestsPerMinute { get; set; } = 60;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset? LastUsedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public List<ApiTokenScopeGrant> Scopes { get; set; } = [];
    public List<ApiTokenResourceGrant> Resources { get; set; } = [];
}
