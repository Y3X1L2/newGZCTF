using GZCTF.Modules.League.Domain;
using GZCTF.Modules.TeamLab.Domain;
using GZCTF.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GZCTF.Modules.League.Infrastructure;

public sealed class LeagueMatchConfiguration : IEntityTypeConfiguration<LeagueMatch>
{
    public void Configure(EntityTypeBuilder<LeagueMatch> b)
    {
        b.ToTable("LeagueMatches", t =>
        {
            t.HasCheckConstraint("CK_LeagueMatches_Coins", "\"InitialCoins\" >= 0");
            t.HasCheckConstraint("CK_LeagueMatches_Result", "(\"State\" = 5 AND \"EndedAt\" IS NOT NULL AND \"EndReason\" IS NOT NULL AND ((\"EndReason\" = 0 AND \"WinnerTeamId\" IS NOT NULL AND \"WinningSubmissionId\" IS NOT NULL) OR (\"EndReason\" = 1 AND \"WinnerTeamId\" IS NULL AND \"WinningSubmissionId\" IS NULL))) OR (\"State\" <> 5 AND \"EndedAt\" IS NULL AND \"EndReason\" IS NULL AND \"WinnerTeamId\" IS NULL AND \"WinningSubmissionId\" IS NULL)");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(160);
        b.Property(x => x.AbortReason).HasMaxLength(500);
        b.HasIndex(x => new { x.State, x.NextAttemptAt });
        b.HasIndex(x => x.WinningSubmissionId).IsUnique();
        b.HasOne<UserInfo>().WithMany().HasForeignKey(x => x.CreatedById).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<TeamLabTopologyRelease>().WithMany().HasForeignKey(x => x.ReleaseId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(x => x.Registrations).WithOne().HasForeignKey(x => x.MatchId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class LeagueRegistrationConfiguration : IEntityTypeConfiguration<LeagueRegistration>
{
    public void Configure(EntityTypeBuilder<LeagueRegistration> b)
    {
        b.ToTable("LeagueRegistrations", t => t.HasCheckConstraint("CK_LeagueRegistrations_Seat",
            "(\"Selected\" AND \"Seat\" IS NOT NULL AND \"Seat\" IN (1, 2)) OR (NOT \"Selected\" AND \"Seat\" IS NULL)"));
        b.HasKey(x => new { x.MatchId, x.TeamId });
        b.HasIndex(x => new { x.MatchId, x.Seat }).IsUnique();
        b.Property(x => x.TeamName).HasMaxLength(Limits.MaxTeamNameLength);
        b.HasOne<Team>().WithMany().HasForeignKey(x => x.TeamId).OnDelete(DeleteBehavior.Restrict);
    }
}
