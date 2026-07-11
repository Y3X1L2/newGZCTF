namespace GZCTF.Modules.Content.Contracts;

public interface IImageTemplateReferenceProvider
{
    string Module { get; }

    Task<IReadOnlyList<ImageTemplateReference>> GetReferencesAsync(
        int imageTemplateId,
        CancellationToken cancellationToken);
}
