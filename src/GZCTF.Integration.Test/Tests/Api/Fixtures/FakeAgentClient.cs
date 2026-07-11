using GZCTF.Services.Fleet;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GZCTF.Integration.Test.Tests.Api.Fixtures;

public sealed class FakeAgentClient(
    IHttpClientFactory httpClientFactory,
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<AgentClient> logger)
    : AgentClient(httpClientFactory, scopeFactory, configuration, logger)
{
    public override Task PullDockerImageAsync(
        Guid nodeId,
        string image,
        string? registryAuth,
        CancellationToken token) => Task.CompletedTask;

    public override Task DeleteDockerImageAsync(
        Guid nodeId,
        string image,
        CancellationToken token) => Task.CompletedTask;
}
