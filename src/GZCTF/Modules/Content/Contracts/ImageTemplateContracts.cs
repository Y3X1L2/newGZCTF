using GZCTF.Modules.Content.Domain;

namespace GZCTF.Modules.Content.Contracts;

public sealed record ImageTemplateReference(
    string Module,
    string ResourceType,
    string ResourceId,
    string DisplayName);

public sealed record ImageTemplateDeleteDecision(
    bool Allowed,
    IReadOnlyList<ImageTemplateReference> References);

public sealed record ImageTemplateDescriptor(int Id, Guid? CreatedById, string Name);

public sealed record ImageTemplateDetails(
    int Id,
    Guid? CreatedById,
    string? CreatorUserName,
    string Name,
    OSType OSType,
    ImageType ImageType,
    ImageStatus Status,
    string? RegistryUrl,
    long FileSize,
    string? Description,
    string? ErrorMessage,
    string? ImageHash,
    VmArtifactStatus VmArtifactStatus,
    VmRuntimeMode VmRuntimeMode,
    VmNetworkMode VmNetworkMode,
    DateTimeOffset UploadedAt);

public sealed record OpenImageTemplateModel(
    int Id,
    string? CreatorUserName,
    string Name,
    OSType OSType,
    ImageType ImageType,
    ImageStatus Status,
    string? RegistryUrl,
    long FileSize,
    string? Description,
    string? ErrorMessage,
    string? ImageHash,
    VmArtifactStatus VmArtifactStatus,
    VmRuntimeMode VmRuntimeMode,
    VmNetworkMode VmNetworkMode,
    DateTimeOffset UploadedAt)
{
    public static OpenImageTemplateModel FromDetails(ImageTemplateDetails details) => new(
        details.Id,
        details.CreatorUserName,
        details.Name,
        details.OSType,
        details.ImageType,
        details.Status,
        details.RegistryUrl,
        details.FileSize,
        details.Description,
        details.ErrorMessage,
        details.ImageHash,
        details.VmArtifactStatus,
        details.VmRuntimeMode,
        details.VmNetworkMode,
        details.UploadedAt);
}

public enum ImageTemplateDeleteStatus
{
    Deleted,
    NotFound,
    Forbidden,
    InUse
}

public sealed record ImageTemplateDeleteResult(
    ImageTemplateDeleteStatus Status,
    IReadOnlyList<ImageTemplateReference> References);
