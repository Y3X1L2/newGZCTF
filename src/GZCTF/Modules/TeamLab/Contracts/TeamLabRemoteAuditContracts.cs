namespace GZCTF.Modules.TeamLab.Contracts;

public sealed class TeamLabRemoteAuditOptions
{
    public int RetentionDays { get; set; } = 90;
    public long MaxStorageBytes { get; set; } = 256 * 1024 * 1024;
}
public sealed record TeamLabRemoteAuditFileModel(long Id, long Size, string Sha256,
    DateTimeOffset CreatedAt, DateTimeOffset? ExpiresAt);
public sealed record TeamLabRemoteAuditPage(string State, int RetentionDays, IReadOnlyList<TeamLabRemoteAuditFileModel> Items);
public sealed record TeamLabRemoteAuditDownload(byte[] Content, string FileName);
