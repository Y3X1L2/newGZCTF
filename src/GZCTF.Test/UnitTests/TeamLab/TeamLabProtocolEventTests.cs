using System;
using System.Threading;
using System.Threading.Tasks;
using GZCTF.Infrastructure.Telemetry;
using GZCTF.Models;
using GZCTF.Modules.Audit.Application;
using GZCTF.Modules.TeamLab.Application;
using GZCTF.Modules.TeamLab.Contracts;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace GZCTF.Test.UnitTests.TeamLab;

public sealed class TeamLabProtocolEventTests
{
    [Theory]
    [InlineData(null, "plc-1")]
    [InlineData("", "plc-1")]
    [InlineData("read", null)]
    [InlineData("read", "")]
    public async Task Record_RejectsMissingTypeOrSource(string? type, string? source)
    {
        await using var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"protocol-events-{Guid.NewGuid():N}").Options);
        var service = new TeamLabProtocolEventService(context,
            new TeamLabEventRecorder(context, Mock.Of<IOperationalEventWriter>(), new OperationalCorrelation()));

        var error = await Assert.ThrowsAsync<TeamLabApiContractException>(() => service.RecordAsync(
            Guid.NewGuid(), new TeamLabProtocolEventReportModel(type!, source!), CancellationToken.None));

        Assert.Equal("protocol_event_invalid", error.Code);
        Assert.Equal(422, error.StatusCode);
    }

    [Fact]
    public async Task Record_RejectsTypeAndSourceAboveLengthLimit()
    {
        await using var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"protocol-events-{Guid.NewGuid():N}").Options);
        var service = new TeamLabProtocolEventService(context,
            new TeamLabEventRecorder(context, Mock.Of<IOperationalEventWriter>(), new OperationalCorrelation()));

        var typeError = await Assert.ThrowsAsync<TeamLabApiContractException>(() => service.RecordAsync(
            Guid.NewGuid(), new TeamLabProtocolEventReportModel(new string('t', 65), "plc-1"), CancellationToken.None));
        var sourceError = await Assert.ThrowsAsync<TeamLabApiContractException>(() => service.RecordAsync(
            Guid.NewGuid(), new TeamLabProtocolEventReportModel("read", new string('s', 129)), CancellationToken.None));

        Assert.Equal("protocol_event_invalid", typeError.Code);
        Assert.Equal("protocol_event_invalid", sourceError.Code);
    }
}
