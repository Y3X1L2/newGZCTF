using GZCTF.Infrastructure.Persistence;
using GZCTF.Models.Data;
using GZCTF.Modules.Theory.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GZCTF.Modules.Theory.Infrastructure.Persistence;

public sealed class TheoryQuestionBankItemEntityConfiguration : IEntityTypeConfiguration<TheoryQuestionBankItem>
{
    public void Configure(EntityTypeBuilder<TheoryQuestionBankItem> builder)
    {
        builder.Property(item => item.Type).HasConversion<string>().HasMaxLength(32);
        builder.Property(item => item.BankName).HasMaxLength(128);
        builder.Property(item => item.Options).HasJsonListConversion();
        builder.Property(item => item.AnswerIndexes).HasJsonListConversion();
        builder.HasIndex(item => new { item.Type, item.UpdatedAt, item.Id })
            .IsDescending(false, true, true)
            .HasDatabaseName("IX_TheoryQuestions_Type_Updated_Id");
        builder.HasIndex(item => item.Title)
            .HasMethod("gin")
            .HasOperators("gin_trgm_ops")
            .HasDatabaseName("IX_TheoryQuestions_Title_Trgm");
        builder.HasIndex(item => item.BankName)
            .HasMethod("gin")
            .HasOperators("gin_trgm_ops")
            .HasDatabaseName("IX_TheoryQuestions_Bank_Trgm");
        builder.HasMany(item => item.TagBindings)
            .WithOne()
            .HasForeignKey(item => item.QuestionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class TheoryQuestionTagEntityConfiguration : IEntityTypeConfiguration<TheoryQuestionTag>
{
    public void Configure(EntityTypeBuilder<TheoryQuestionTag> builder)
    {
        builder.ToTable("TheoryQuestionTags");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.DisplayName).HasMaxLength(64).IsRequired();
        builder.Property(item => item.NormalizedName).HasMaxLength(64).IsRequired();
        builder.HasIndex(item => item.NormalizedName).IsUnique()
            .HasDatabaseName("UX_TheoryQuestionTags_NormalizedName");
    }
}

public sealed class TheoryQuestionTagBindingEntityConfiguration : IEntityTypeConfiguration<TheoryQuestionTagBinding>
{
    public void Configure(EntityTypeBuilder<TheoryQuestionTagBinding> builder)
    {
        builder.ToTable("TheoryQuestionTagBindings");
        builder.HasKey(item => new { item.QuestionId, item.TagId });
        builder.HasIndex(item => new { item.TagId, item.QuestionId })
            .HasDatabaseName("IX_TheoryQuestionTagBindings_Tag_Question");
        builder.HasOne(item => item.Tag).WithMany(item => item.Questions)
            .HasForeignKey(item => item.TagId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class TheoryPaperEntityConfiguration : IEntityTypeConfiguration<TheoryPaper>
{
    public void Configure(EntityTypeBuilder<TheoryPaper> builder)
    {
        builder.HasIndex(item => item.GameId).IsUnique();
        builder.HasOne(item => item.Game).WithMany().HasForeignKey(item => item.GameId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(item => item.Questions).WithOne(item => item.Paper)
            .HasForeignKey(item => item.PaperId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class TheoryPaperQuestionEntityConfiguration : IEntityTypeConfiguration<TheoryPaperQuestion>
{
    public void Configure(EntityTypeBuilder<TheoryPaperQuestion> builder)
    {
        builder.Property(item => item.Type).HasConversion<string>().HasMaxLength(32);
        builder.Property(item => item.Options).HasJsonListConversion();
        builder.Property(item => item.AnswerIndexes).HasJsonListConversion();
        builder.HasIndex(item => item.PaperId);
        builder.HasIndex(item => item.SourceQuestionId);
        builder.HasOne(item => item.SourceQuestion).WithMany().HasForeignKey(item => item.SourceQuestionId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

public sealed class TheoryAnswerSheetEntityConfiguration : IEntityTypeConfiguration<TheoryAnswerSheet>
{
    public void Configure(EntityTypeBuilder<TheoryAnswerSheet> builder)
    {
        builder.Property(item => item.Status).HasConversion<string>().HasMaxLength(32);
        builder.HasIndex(item => new { item.UserId, item.GameId }).IsUnique();
        builder.HasIndex(item => new { item.GameId, item.Status, item.SubmittedAt, item.Id })
            .IsDescending(false, false, true, true)
            .HasDatabaseName("IX_TheoryAnswerSheets_Game_Status_Submitted_Id");
        builder.HasOne(item => item.Game).WithMany().HasForeignKey(item => item.GameId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(item => item.Paper).WithMany().HasForeignKey(item => item.PaperId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(item => item.Participation).WithMany().HasForeignKey(item => item.ParticipationId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(item => item.User).WithMany().HasForeignKey(item => item.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(item => item.Answers).WithOne(item => item.AnswerSheet)
            .HasForeignKey(item => item.AnswerSheetId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(item => item.Participation).AutoInclude();
        builder.Navigation(item => item.User).AutoInclude();
    }
}

public sealed class TheorySubmissionAnswerEntityConfiguration : IEntityTypeConfiguration<TheorySubmissionAnswer>
{
    public void Configure(EntityTypeBuilder<TheorySubmissionAnswer> builder)
    {
        builder.Property(item => item.SelectedIndexes).HasJsonListConversion();
        builder.HasIndex(item => item.AnswerSheetId);
        builder.HasIndex(item => item.PaperQuestionId);
        builder.HasOne(item => item.PaperQuestion).WithMany().HasForeignKey(item => item.PaperQuestionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
