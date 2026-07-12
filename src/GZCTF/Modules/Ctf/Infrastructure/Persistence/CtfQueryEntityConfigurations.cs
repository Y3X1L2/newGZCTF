using GZCTF.Models.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GZCTF.Modules.Ctf.Infrastructure.Persistence;

public sealed class ParticipationEntityConfiguration : IEntityTypeConfiguration<Participation>
{
    public void Configure(EntityTypeBuilder<Participation> builder)
    {
        builder.Property(item => item.Status).HasConversion<int>();
        builder.HasIndex(item => new { item.GameId, item.TeamId })
            .IsUnique()
            .HasDatabaseName("UX_Participations_Game_Team");
        builder.HasIndex(item => new { item.GameId, item.Status, item.DivisionId, item.TeamId })
            .HasDatabaseName("IX_Participations_Game_Status_Division_Team");

        builder.HasMany(item => item.Instances).WithOne();
        builder.HasMany(item => item.Submissions)
            .WithOne(item => item.Participation)
            .HasForeignKey(item => item.ParticipationId);
        builder.HasMany(item => item.FirstSolves)
            .WithOne(item => item.Participation)
            .HasForeignKey(item => item.ParticipationId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(item => item.Members)
            .WithOne(item => item.Participation)
            .HasForeignKey(item => item.ParticipationId);
        builder.HasOne(item => item.Writeup).WithMany();
        builder.HasOne(item => item.Division)
            .WithMany()
            .HasForeignKey(item => item.DivisionId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.Navigation(item => item.Game).AutoInclude();
        builder.Navigation(item => item.Team).AutoInclude();
        builder.Navigation(item => item.Members).AutoInclude();
        builder.Navigation(item => item.Writeup).AutoInclude();
        builder.HasMany(item => item.Challenges)
            .WithMany(item => item.Teams)
            .UsingEntity<GameInstance>(
                right => right.HasOne(item => item.Challenge)
                    .WithMany(item => item.Instances)
                    .HasForeignKey(item => item.ChallengeId),
                left => left.HasOne(item => item.Participation)
                    .WithMany(item => item.Instances)
                    .HasForeignKey(item => item.ParticipationId)
                    .OnDelete(DeleteBehavior.Cascade),
                join => join.HasKey(item => new { item.ChallengeId, item.ParticipationId }));
    }
}

public sealed class SubmissionEntityConfiguration : IEntityTypeConfiguration<Submission>
{
    public void Configure(EntityTypeBuilder<Submission> builder)
    {
        builder.Property(item => item.Status).HasConversion<string>();
        builder.Property(item => item.SubmissionType).HasConversion<string>().HasMaxLength(32);
        builder.HasIndex(item => new { item.GameId, item.SubmitTimeUtc, item.Id })
            .IsDescending(false, true, true)
            .HasDatabaseName("IX_Submissions_Game_Time_Id");
        builder.HasIndex(item => new { item.ChallengeId, item.SubmitTimeUtc, item.Id })
            .IsDescending(false, true, true)
            .HasDatabaseName("IX_Submissions_Challenge_Time_Id");
        builder.HasIndex(item => new { item.TeamId, item.SubmitTimeUtc, item.Id })
            .IsDescending(false, true, true)
            .HasDatabaseName("IX_Submissions_Team_Time_Id");
        builder.HasIndex(item => new { item.ParticipationId, item.ChallengeId })
            .HasDatabaseName("IX_Submissions_Participation_Challenge");
        builder.HasIndex(item => new { item.Status, item.SubmitTimeUtc, item.Id })
            .IsDescending(false, true, true)
            .HasFilter("\"Status\" = 'FlagSubmitted'")
            .HasDatabaseName("IX_Submissions_Unchecked_Time_Id");

        builder.HasOne(item => item.ReviewedBy)
            .WithMany()
            .HasForeignKey(item => item.ReviewedById)
            .OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(item => item.FlagContext)
            .WithMany()
            .HasForeignKey(item => item.FlagId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.Navigation(item => item.Team).AutoInclude();
        builder.Navigation(item => item.User).AutoInclude();
        builder.Navigation(item => item.GameChallenge).AutoInclude();
    }
}
