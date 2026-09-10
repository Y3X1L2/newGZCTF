using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using GZCTF.Modules.Audit.Contracts;
using GZCTF.Modules.Audit.Domain;
using GZCTF.Modules.TeamLab.Application;
using GZCTF.Modules.TeamLab.Infrastructure;
using GZCTF.Services.Fleet;
using GZCTF.TeamLab.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace GZCTF.Test.UnitTests.TeamLab;

public sealed class TeamLabFileErrorMappingTests
{
    [Theory]
    [InlineData("files.not_found", 404, "files.not_found")]
    [InlineData("files.permission_denied", 403, "files.permission_denied")]
    [InlineData("node.offline", 503, "files.node_unavailable")]
    public async Task KnownFileFailuresKeepTheirApiSemantics(string agentCode, int expectedStatus, string expectedCode)
    {
        var agent = new Mock<AgentClient>(Mock.Of<IHttpClientFactory>(), Mock.Of<IServiceScopeFactory>(),
            new ConfigurationBuilder().Build(), NullLogger<AgentClient>.Instance);
        agent.Setup(item => item.ManageTeamLabContainerFilesAsync(It.IsAny<Guid>(), It.IsAny<TeamLabContainerFileRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AgentClientException(new OperationalError(OperationalErrorCategory.Validation, agentCode, "file request failed", false)));
        var error = await Assert.ThrowsAsync<TeamLabApiContractException>(() => new AgentTeamLabAssetFileGateway(agent.Object)
            .ExecuteAsync(Guid.NewGuid(), new(1, 3, "fixture", "download", "/missing"), default));
        Assert.Equal(expectedStatus, error.StatusCode);
        Assert.Equal(expectedCode, error.Code);
    }
}
