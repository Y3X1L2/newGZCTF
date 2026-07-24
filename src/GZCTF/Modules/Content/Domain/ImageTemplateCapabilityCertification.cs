using System.ComponentModel.DataAnnotations;
using GZCTF.Models.Data;
using GZCTF.Modules.Audit.Domain;

namespace GZCTF.Modules.Content.Domain;

public enum ImageTemplateCertificationStatus : byte
{
    Pending = 0,
    Certified = 1,
    Failed = 2,
    Invalidated = 3
}

public sealed class ImageTemplateCapabilityCertification
{
    [Key] public long Id { get; set; }
    public int ImageTemplateId { get; set; }
    [MaxLength(64)] public string ImageHash { get; set; } = string.Empty;
    public ImageTemplateCertificationStatus Status { get; set; }
    public string CapabilitiesJson { get; set; } = "[]";
    [MaxLength(128)] public string EvidenceDigest { get; set; } = string.Empty;
    [MaxLength(64)] public string ProbeKind { get; set; } = "external-evidence";
    [MaxLength(128)] public string? ProbeStep { get; set; }
    public Guid? WorkerNodeId { get; set; }
    [MaxLength(128)] public string? ErrorCode { get; set; }
    [MaxLength(1024)] public string? ErrorDetail { get; set; }
    public long? DomainCreateDurationMs { get; set; }
    public long? GuestReadyDurationMs { get; set; }
    public long? FullProbeDurationMs { get; set; }
    public int? PreparationContractVersion { get; set; }
    public int? GuestProtocolVersion { get; set; }
    public Guid CertifiedById { get; set; }
    public DateTimeOffset CertifiedAt { get; set; } = DateTimeOffset.UtcNow;
    public ImageTemplate ImageTemplate { get; set; } = null!;
    public WorkerNode? WorkerNode { get; set; }
    public UserInfo CertifiedBy { get; set; } = null!;
}

public sealed class ImageTemplateCertificationJob
{
    [Key] public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid OperationId { get; set; }
    public int ImageTemplateId { get; set; }
    public string CapabilitiesJson { get; set; } = "[]";
    [MaxLength(128)] public string? EvidenceDigest { get; set; }
    [MaxLength(64)] public string ProbeKind { get; set; } = "external-evidence";
    public Guid ActorUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public ApiOperation Operation { get; set; } = null!;
}

public static class ImageTemplateCapabilityIds
{
    public const string LinuxCloudInitNoCloud = "linux.cloud-init.nocloud.v1";
    public const string GuestQga = "guest.qga.v1";
    public const string GuestVirtioSerial = "guest.virtio-serial.v1";
    public const string WindowsPowerShell = "windows.powershell.v1";
    public const string WindowsCloudbaseInit = "windows.cloudbase-init.v1";
    public const string NetworkVirtio = "network.virtio.v1";
    public const string NetworkE1000E = "network.e1000e.v1";
    public const string BootstrapFirstBoot = "bootstrap.firstboot.v1";
    public const string GuestSupervisor = "guest.supervisor.v1";
    public const string VmPreparedImage = "image.vm.prepared.v1";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        LinuxCloudInitNoCloud,
        GuestQga,
        GuestVirtioSerial,
        WindowsPowerShell,
        WindowsCloudbaseInit,
        NetworkVirtio,
        NetworkE1000E,
        BootstrapFirstBoot,
        GuestSupervisor,
        VmPreparedImage
    };
}
