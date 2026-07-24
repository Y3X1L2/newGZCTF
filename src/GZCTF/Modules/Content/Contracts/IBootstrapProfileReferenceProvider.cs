namespace GZCTF.Modules.Content.Contracts;

public sealed record BootstrapProfileReference(
    string Module,
    string ResourceType,
    string ResourceId,
    string DisplayName);

public interface IBootstrapProfileReferenceProvider
{
    string Module { get; }
    Task<IReadOnlyList<BootstrapProfileReference>> GetReferencesAsync(
        Guid profileId,
        CancellationToken cancellationToken);
}
