using System;
using System.Linq;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Modules.Audit.Infrastructure.Persistence;
using GZCTF.Modules.Awdp.Infrastructure.Persistence;
using GZCTF.Modules.Ctf.Infrastructure.Persistence;
using GZCTF.Modules.Runtime.Infrastructure.Persistence;
using GZCTF.Modules.Theory.Infrastructure.Persistence;
using GZCTF.Modules.Training.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GZCTF.Test.UnitTests.Architecture;

public sealed class DatabaseGovernanceBoundaryTests
{
    [Fact]
    public void GovernedEntities_UseModulePersistenceConfigurations()
    {
        Type[] configurations =
        [
            typeof(ParticipationEntityConfiguration),
            typeof(SubmissionEntityConfiguration),
            typeof(TrainingCourseProgressEntityConfiguration),
            typeof(TrainingChapterProgressEntityConfiguration),
            typeof(TheoryQuestionBankItemEntityConfiguration),
            typeof(DeploymentQueueTicketEntityConfiguration),
            typeof(ImageDistributionRecordEntityConfiguration),
            typeof(AwdpRoundEntityConfiguration),
            typeof(AwdpCheckerTaskEntityConfiguration),
            typeof(SystemLogEntityConfiguration)
        ];

        Assert.All(configurations, type =>
            Assert.Contains(type.GetInterfaces(), contract =>
                contract.IsGenericType &&
                contract.GetGenericTypeDefinition() == typeof(IEntityTypeConfiguration<>)));
    }

    [Fact]
    public void GovernedEntities_DoNotUseIndexAttributes()
    {
        Type[] entities =
        [
            typeof(Participation), typeof(Submission), typeof(TrainingCourseProgress),
            typeof(TrainingChapterProgress), typeof(TheoryQuestionBankItem),
            typeof(TheoryPaper), typeof(TheoryPaperQuestion), typeof(TheoryAnswerSheet),
            typeof(TheorySubmissionAnswer), typeof(DeploymentQueueTicket),
            typeof(ImageDistributionRecord)
        ];

        Assert.All(entities, entity => Assert.DoesNotContain(
            entity.GetCustomAttributesData(),
            attribute => attribute.AttributeType.Namespace == "Microsoft.EntityFrameworkCore"));
    }

    [Fact]
    public void CoreUniqueness_IsExpressedInTheEfModel()
    {
        using var context = CreateContext();
        var participation = context.Model.FindEntityType(typeof(Participation))!;
        var distribution = context.Model.FindEntityType(typeof(ImageDistributionRecord))!;

        Assert.Contains(participation.GetIndexes(), index =>
            index.IsUnique && index.Properties.Select(property => property.Name)
                .SequenceEqual([nameof(Participation.GameId), nameof(Participation.TeamId)]));
        Assert.Contains(distribution.GetIndexes(), index =>
            index.IsUnique && index.Properties.Select(property => property.Name)
                .SequenceEqual([nameof(ImageDistributionRecord.ImageTemplateId),
                    nameof(ImageDistributionRecord.WorkerNodeId)]));
    }

    private static AppDbContext CreateContext() => new(
        new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
}
