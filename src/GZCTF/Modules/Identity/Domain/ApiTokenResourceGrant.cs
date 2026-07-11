namespace GZCTF.Modules.Identity.Domain;

public class ApiTokenResourceGrant
{
    public Guid TokenId { get; set; }
    public string ResourceType { get; set; } = string.Empty;
    public string ResourceId { get; set; } = string.Empty;
}
