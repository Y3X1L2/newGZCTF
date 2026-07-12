using GZCTF.Models.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GZCTF.Modules.Awdp.Infrastructure.Persistence;

public sealed class AwdpRoundEntityConfiguration : IEntityTypeConfiguration<AwdpRound>
{
    public void Configure(EntityTypeBuilder<AwdpRound> builder)
    {
        builder.Property(item => item.Status).HasConversion<string>().HasMaxLength(32);
        builder.HasIndex(item => new { item.GameId, item.RoundNumber }).IsUnique()
            .HasDatabaseName("UX_AwdpRounds_Game_Round");
        builder.HasIndex(item => new { item.GameId, item.Status, item.RoundNumber })
            .IsDescending(false, false, true)
            .HasDatabaseName("IX_AwdpRounds_Game_Status_Round");
        builder.HasOne(item => item.Game).WithMany(item => item.AwdpRounds)
            .HasForeignKey(item => item.GameId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class AwdpCheckerTaskEntityConfiguration : IEntityTypeConfiguration<AwdpCheckerTask>
{
    public void Configure(EntityTypeBuilder<AwdpCheckerTask> builder)
    {
        builder.Property(item => item.Status).HasConversion<string>().HasMaxLength(32);
        builder.HasIndex(item => new { item.RoundId, item.ServiceId, item.TeamId }).IsUnique()
            .HasDatabaseName("UX_AwdpCheckerTasks_Round_Service_Team");
        builder.HasIndex(item => new { item.Status, item.ExecutedAt, item.Id })
            .HasDatabaseName("IX_AwdpCheckerTasks_Status_Executed_Id");
        builder.HasOne(item => item.Round).WithMany(item => item.CheckerTasks)
            .HasForeignKey(item => item.RoundId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(item => item.Service).WithMany().HasForeignKey(item => item.ServiceId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(item => item.Team).WithMany().HasForeignKey(item => item.TeamId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
