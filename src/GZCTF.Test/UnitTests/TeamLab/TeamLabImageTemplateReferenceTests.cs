using System;
using System.Threading.Tasks;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Modules.Content.Application;
using GZCTF.Modules.TeamLab.Domain;
using GZCTF.Modules.TeamLab.Domain.Runtime;
using GZCTF.Modules.TeamLab.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GZCTF.Test.UnitTests.TeamLab;

public sealed class TeamLabImageTemplateReferenceTests
{
    [Theory]
    [InlineData(TeamLabRuntimeStatus.Running)]
    [InlineData(TeamLabRuntimeStatus.Paused)]
    [InlineData(TeamLabRuntimeStatus.Stopped)]
    [InlineData(TeamLabRuntimeStatus.Failed)]
    public async Task CanonicalArtifact_IsProtectedForHotAddedAssetWithoutDraftOrRelease(TeamLabRuntimeStatus status)
    {
        await using var context = Context();
        context.TeamLabRuntimes.Add(new TeamLabRuntime
        {
            Status = status,
            Assets = [new() { Name = "hot-added", SourceTemplateId = 7, Status = status }]
        });
        await context.SaveChangesAsync();

        var result = await new ImageTemplateReferenceService([new TeamLabImageTemplateReferenceProvider(context)])
            .CanDeleteAsync(7, default);

        Assert.False(result.Allowed);
        Assert.Equal("runtime-asset", Assert.Single(result.References).ResourceType);
    }

    [Fact]
    public async Task CanonicalArtifact_IsProtectedDuringResetThenReleasedAfterFullDestroy()
    {
        await using var context = Context();
        var runtime = new TeamLabRuntime
        {
            Status = TeamLabRuntimeStatus.Destroyed,
            Assets = [new() { SourceTemplateId = 7, Status = TeamLabRuntimeStatus.Destroyed }]
        };
        context.Add(runtime);
        await context.SaveChangesAsync();
        var ticket = new DeploymentQueueTicket
        {
            Kind = DeploymentQueueKind.TeamLabRuntime, TeamLabRuntimeId = runtime.Id,
            Operation = RuntimeOperationKind.Reset, Status = DeploymentQueueTicketStatus.Running
        };
        context.Add(ticket);
        await context.SaveChangesAsync();
        var service = new ImageTemplateReferenceService([new TeamLabImageTemplateReferenceProvider(context)]);

        Assert.False((await service.CanDeleteAsync(7, default)).Allowed);
        ticket.Status = DeploymentQueueTicketStatus.Cancelled;
        await context.SaveChangesAsync();
        Assert.True((await service.CanDeleteAsync(7, default)).Allowed);
    }

    private static AppDbContext Context() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
}
