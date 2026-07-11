namespace GZCTF.Modules.Identity.Application;

public sealed record ActorContext(
    Guid? UserId,
    Role Role,
    Guid? ApiTokenId = null,
    IReadOnlySet<string>? Scopes = null,
    IReadOnlySet<string>? Resources = null)
{
    public bool IsApiToken => ApiTokenId.HasValue;
}
