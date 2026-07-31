using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Modules.Penetration.Application;
using GZCTF.Modules.Penetration.Domain;
using GZCTF.Modules.TeamLab.Application;
using GZCTF.Modules.TeamLab.Contracts;
using GZCTF.Modules.TeamLab.Domain;
using GZCTF.Utils;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace GZCTF.Test.UnitTests.TeamLab;

public sealed class TeamLabCompetitionSubmissionTests
{
    [Fact]
    public async Task DeployGameAsync_SubmitsAcceptedTeamsInOrderAndCountsResults()
    {
        await using var context = CreateContext();
        const int gameId = 7;
        var releaseId = Guid.NewGuid();
        var topology = new TeamLabTopology { Id = 11, Name = "competition", OwnerUserId = Guid.NewGuid() };
        context.TeamLabTopologies.Add(topology);
        context.TeamLabTopologyReleases.Add(new TeamLabTopologyRelease
        {
            Id = releaseId,
            TopologyId = topology.Id,
            Topology = topology,
            Version = 1,
            SourceRevision = 1,
            CanonicalJson = "{}",
            ContentHash = "release"
        });
        context.PenetrationGameLabBindings.Add(new PenetrationGameLabBinding
        {
            GameId = gameId,
            TopologyId = topology.Id,
            ActiveReleaseId = releaseId
        });
        context.Participations.AddRange(
            Participation(gameId, 30, ParticipationStatus.Accepted),
            Participation(gameId, 10, ParticipationStatus.Accepted),
            Participation(gameId, 20, ParticipationStatus.Accepted),
            Participation(gameId, 40, ParticipationStatus.Rejected));
        await context.SaveChangesAsync();

        var commands = new List<CreateTeamLabRuntimeModel>();
        var activeSubmissions = 0;
        var maximumConcurrentSubmissions = 0;
        var results = new Queue<bool>([false, true, false]);
        var runtimes = new Mock<ITeamLabRuntimeApplicationService>();
        runtimes.Setup(item => item.PlanAndEnqueueAsync(
                It.IsAny<CreateTeamLabRuntimeModel>(),
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(async (CreateTeamLabRuntimeModel command, Guid _, Guid _, string _, string? _, Guid? _,
                string? _, CancellationToken cancellationToken) =>
            {
                lock (commands)
                {
                    commands.Add(command);
                    activeSubmissions++;
                    maximumConcurrentSubmissions = Math.Max(maximumConcurrentSubmissions, activeSubmissions);
                }

                try
                {
                    await Task.Delay(10, cancellationToken);
                    var reused = results.Dequeue();
                    return new TeamLabRuntimeCreateResult(commands.Count, Guid.NewGuid(), reused);
                }
                finally
                {
                    lock (commands) activeSubmissions--;
                }
            });

        var objectives = new PenetrationObjectiveService(context, null!, null!, null!, null!, null!);
        var adapter = new PenetrationTeamLabAdapter(context, runtimes.Object, objectives);

        var result = await adapter.DeployGameAsync(gameId, Guid.NewGuid(), CancellationToken.None);

        Assert.Equal((2, 1), result);
        Assert.Equal(new[] { 10, 20, 30 }, commands.Select(GetTeamId));
        Assert.Equal(1, maximumConcurrentSubmissions);
        Assert.Equal(new[] { 10, 20, 30 }, await context.PenetrationTeamRuntimeBindings.AsNoTracking()
            .OrderBy(item => item.TeamId)
            .Select(item => item.TeamId)
            .ToArrayAsync());
    }

    private static int GetTeamId(CreateTeamLabRuntimeModel command) =>
        int.Parse(command.ExternalReference!.Split(':')[^1], CultureInfo.InvariantCulture);

    private static Participation Participation(int gameId, int teamId, ParticipationStatus status) => new()
    {
        GameId = gameId,
        TeamId = teamId,
        Status = status,
        Token = $"team-{teamId}"
    };

    private static AppDbContext CreateContext() => new(
        new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
}
