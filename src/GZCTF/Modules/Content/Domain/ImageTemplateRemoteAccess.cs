using System.ComponentModel.DataAnnotations;
using GZCTF.Models.Data;
using GZCTF.Modules.TeamLab.Domain.Runtime;

namespace GZCTF.Modules.Content.Domain;

public sealed class ImageTemplateRemoteAccess
{
    public int ImageTemplateId { get; set; }
    public bool Enabled { get; set; }
    public TeamLabRemoteProtocol Protocol { get; set; }
    public int Port { get; set; }
    [MaxLength(128)] public string? Username { get; set; }
    public RemoteCredentialMode CredentialMode { get; set; } = RemoteCredentialMode.PlatformGenerated;
    [MaxLength(8192)] public string? ProtectedSecret { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public ImageTemplate ImageTemplate { get; set; } = null!;
}
