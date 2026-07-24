using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using GZCTF.Models.Data;
using GZCTF.Modules.Content.Domain;
using GZCTF.Modules.TeamLab.Domain;
using Microsoft.AspNetCore.Mvc;

namespace GZCTF.Modules.Content.Contracts;

public enum BootstrapParameterType : byte
{
    String = 0,
    Integer = 1,
    Boolean = 2
}

public enum BootstrapHealthCheckKind : byte
{
    Tcp = 0,
    Http = 1,
    Entrypoint = 2
}

public enum BootstrapRebootBehavior : byte
{
    None = 0,
    IfRequested = 1,
    Required = 2
}

public sealed record BootstrapParameterDefinition(
    string Key,
    BootstrapParameterType Type,
    bool Required,
    bool Secret,
    string? DefaultValue = null);

public sealed record BootstrapFileDefinition(
    string SourcePath,
    string TargetPath,
    string Mode = "0644",
    bool Template = false);

public sealed record BootstrapStepDefinition(
    string Id,
    string Entrypoint,
    int TimeoutSeconds = 300,
    string RunAs = "system",
    BootstrapRebootBehavior Reboot = BootstrapRebootBehavior.None);

public sealed record BootstrapHealthCheckDefinition(
    string Id,
    BootstrapHealthCheckKind Kind,
    string Target,
    int TimeoutSeconds = 10,
    int Attempts = 12);

public sealed record BootstrapProfileManifest(
    int SchemaVersion,
    IReadOnlySet<OSType> OperatingSystems,
    IReadOnlySet<TeamLabAssetKind> AssetKinds,
    IReadOnlySet<string> RequiredTemplateCapabilities,
    IReadOnlyList<BootstrapParameterDefinition> Parameters,
    IReadOnlyList<BootstrapFileDefinition> Files,
    IReadOnlyList<BootstrapStepDefinition> Steps,
    IReadOnlyList<BootstrapHealthCheckDefinition> HealthChecks,
    int MaxReboots);

public sealed record BootstrapProfileCreateModel(
    [Required, MaxLength(128)] string Name,
    [MaxLength(1024)] string? Description);

public sealed class BootstrapProfileVersionUploadModel
{
    [Required]
    [FromForm(Name = "artifact")]
    [JsonPropertyName("artifact")]
    public IFormFile Artifact { get; set; } = null!;

    [Required]
    [FromForm(Name = "manifest")]
    [JsonPropertyName("manifest")]
    public string Manifest { get; set; } = string.Empty;

    [FromForm(Name = "version")]
    [JsonPropertyName("version")]
    public int? Version { get; set; }

    [MaxLength(128)]
    [FromForm(Name = "expectedDigest")]
    [JsonPropertyName("expectedDigest")]
    public string? ExpectedDigest { get; set; }
}

public sealed record BootstrapProfileModel(
    Guid Id,
    string Name,
    string? Description,
    BootstrapProfileStatus Status,
    int? LatestVersion,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

public sealed record BootstrapProfileVersionModel(
    Guid ProfileId,
    int Version,
    BootstrapProfileVersionStatus Status,
    string ManifestDigest,
    string ArtifactDigest,
    long ArtifactSize,
    BootstrapProfileManifest Manifest,
    DateTimeOffset CreatedAt);

public sealed record BootstrapProfileCursorPage(
    IReadOnlyList<BootstrapProfileModel> Items,
    string? NextCursor);

public sealed record ImageTemplateCertificationRequest(
    [Required] IReadOnlyList<string> Capabilities,
    [MaxLength(128)] string? EvidenceDigest = null,
    [MaxLength(64)] string ProbeKind = "controlled-probe");

public sealed record ImageTemplateCertificationModel(
    long Id,
    int ImageTemplateId,
    string ImageHash,
    ImageTemplateCertificationStatus Status,
    IReadOnlyList<string> Capabilities,
    string EvidenceDigest,
    string ProbeKind,
    Guid? WorkerNodeId,
    string? ErrorCode,
    string? ErrorDetail,
    long? DomainCreateDurationMs,
    long? GuestReadyDurationMs,
    long? FullProbeDurationMs,
    int? PreparationContractVersion,
    int? GuestProtocolVersion,
    DateTimeOffset CertifiedAt);
