namespace GZCTF.Modules.Content.Contracts;

public sealed record AssetDescriptor(string Hash, string Name, long Size, string RemoteUrl,
    string? CreatorUserName);

public enum AssetDeleteStatus
{
    Success,
    NotFound,
    InUse,
    Failed
}
