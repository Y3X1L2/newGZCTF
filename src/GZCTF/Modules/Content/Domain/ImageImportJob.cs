using GZCTF.Models.Data;

namespace GZCTF.Modules.Content.Domain;

public enum ImageImportSourceKind
{
    DockerReference = 0,
    DockerArchive = 1
}

public sealed class ImageImportJob
{
    public Guid OperationId { get; set; }
    public ImageImportSourceKind SourceKind { get; set; }
    public string SourceReference { get; set; } = string.Empty;
    public string? StagedPath { get; set; }
    public string? OriginalFileName { get; set; }
    public long ContentLength { get; set; }
    public string? ExpectedDigest { get; set; }
    public ImageType RequestedTemplateKind { get; set; }
    public OSType RequestedOsType { get; set; }
    public string RequestedName { get; set; } = string.Empty;
    public Guid? CreatedById { get; set; }
    public int? ImageTemplateId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
