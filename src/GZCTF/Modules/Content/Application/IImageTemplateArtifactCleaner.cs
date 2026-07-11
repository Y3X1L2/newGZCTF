using GZCTF.Models.Data;

namespace GZCTF.Modules.Content.Application;

public interface IImageTemplateArtifactCleaner
{
    Task CleanupAsync(ImageTemplate template, CancellationToken cancellationToken);
}
