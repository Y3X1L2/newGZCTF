using GZCTF.Modules.TeamLab.Domain.Runtime;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GZCTF.Modules.TeamLab.Infrastructure.Persistence;

public sealed class TeamLabRuntimeRemoteCredentialEntityConfiguration : IEntityTypeConfiguration<TeamLabRuntimeRemoteCredential>
{
    public void Configure(EntityTypeBuilder<TeamLabRuntimeRemoteCredential> builder)
    {
        builder.ToTable("TeamLabRuntimeRemoteCredentials");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Protocol).HasConversion<byte>();
        builder.Property(item => item.Mode).HasConversion<byte>();
        builder.HasIndex(item => new { item.RuntimeId, item.Generation, item.RuntimeAssetId, item.Protocol }).IsUnique();
        builder.HasOne(item => item.Runtime).WithMany().HasForeignKey(item => item.RuntimeId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(item => item.RuntimeAsset).WithMany().HasForeignKey(item => item.RuntimeAssetId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class TeamLabRemoteSessionEntityConfiguration : IEntityTypeConfiguration<TeamLabRemoteSession>
{
    public void Configure(EntityTypeBuilder<TeamLabRemoteSession> builder)
    {
        builder.ToTable("TeamLabRemoteSessions");
        builder.HasKey(item => item.Id);
        builder.HasIndex(item => item.PublicId).IsUnique();
        builder.HasIndex(item => new { item.Status, item.ExpiresAt });
        builder.HasIndex(item => new { item.RuntimeId, item.Generation, item.RuntimeAssetId });
        builder.Property(item => item.Protocol).HasConversion<byte>();
        builder.Property(item => item.Status).HasConversion<byte>();
        builder.HasOne(item => item.Runtime).WithMany().HasForeignKey(item => item.RuntimeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(item => item.RuntimeAsset).WithMany().HasForeignKey(item => item.RuntimeAssetId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(item => item.WorkerNode).WithMany().HasForeignKey(item => item.WorkerNodeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(item => item.RequestedBy).WithMany().HasForeignKey(item => item.RequestedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class TeamLabRemoteAuditFileEntityConfiguration : IEntityTypeConfiguration<TeamLabRemoteAuditFile>
{
    public void Configure(EntityTypeBuilder<TeamLabRemoteAuditFile> builder)
    {
        builder.ToTable("TeamLabRemoteAuditFiles");
        builder.HasKey(item => item.Id);
        builder.HasIndex(item => new { item.SessionId, item.RelativePath }).IsUnique();
        builder.HasOne(item => item.Session).WithMany(item => item.AuditFiles).HasForeignKey(item => item.SessionId).OnDelete(DeleteBehavior.Restrict);
    }
}
