using GZCTF.Models.Data;
using GZCTF.Modules.Content.Application;
using GZCTF.Services;
using GZCTF.Services.Fleet;
using GZCTF.Storage;

namespace GZCTF.Modules.Content.Infrastructure;

public sealed class ImageTemplateArtifactCleaner(
    ImageDistributionService distribution,
    DockerImageRegistryService dockerRegistry,
    VmImageRegistryService vmRegistry,
    ImageStorage storage) : IImageTemplateArtifactCleaner
{
    public async Task CleanupAsync(ImageTemplate template, CancellationToken cancellationToken)
    {
        await distribution.CleanupTemplateForDeletionAsync(template.Id, cancellationToken);

        if (template.ImageType == ImageType.Docker)
        {
            await dockerRegistry.DeleteManagedImageAsync(template.RegistryUrl, cancellationToken);
            return;
        }

        await vmRegistry.DeleteArtifactAsync(template, cancellationToken);
        await storage.DeleteImageAsync(template);
    }
}
