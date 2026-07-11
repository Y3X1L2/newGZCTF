namespace GZCTF.Modules.Identity.Domain;

public class ApiTokenScopeGrant
{
    public Guid TokenId { get; set; }
    public string Scope { get; set; } = string.Empty;
}
