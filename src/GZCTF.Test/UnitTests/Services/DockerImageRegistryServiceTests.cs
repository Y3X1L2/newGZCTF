using System;
using System.Net.Http;
using GZCTF.Models.Internal;
using GZCTF.Services;
using GZCTF.Services.Fleet;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace GZCTF.Test.UnitTests.Services;

public class DockerImageRegistryServiceTests
{
    [Theory]
    [InlineData("10.0.7.130:5000", "ctf", "web/demo", "v1", "gzctf-internal://ctf/web/demo:v1")]
    [InlineData("http://registry.internal:5000", "ctf", "Web/Demo", "latest", "gzctf-internal://ctf/web/demo:latest")]
    [InlineData("https://registry.internal", "", "team/pwn", "20260614", "gzctf-internal://team/pwn:20260614")]
    public void BuildInternalImageReference_UsesStableInternalReference(
        string address,
        string ns,
        string repository,
        string tag,
        string expected)
    {
        var service = CreateService(address, ns);

        var actual = service.BuildInternalImageReference(repository, tag);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void BuildImageReferenceForRegistry_RejectsMissingRegistryAddress()
    {
        var service = CreateService("", "ctf");

        Assert.Throws<InvalidOperationException>(() =>
            service.BuildImageReferenceForRegistry("", "web/demo", "v1"));
    }

    [Fact]
    public void BuildImageReferenceForRegistry_UsesExternalRegistryAddress()
    {
        var service = CreateService("", "ctf");

        var actual = service.BuildImageReferenceForRegistry("http://registry.internal:5000", "Web/Demo", "latest");

        Assert.Equal("registry.internal:5000/ctf/web/demo:latest", actual);
    }

    [Theory]
    [InlineData("../web")]
    [InlineData("web:demo")]
    [InlineData("web demo")]
    public void BuildInternalImageReference_RejectsInvalidRepository(string repository)
    {
        var service = CreateService("registry.internal:5000", "ctf");

        Assert.Throws<InvalidOperationException>(() =>
            service.BuildInternalImageReference(repository, "v1"));
    }

    static DockerImageRegistryService CreateService(string address, string ns)
    {
        var services = new ServiceCollection().BuildServiceProvider();
        var scopeFactory = services.GetRequiredService<IServiceScopeFactory>();
        var agentClient = new AgentClient(
            new StaticHttpClientFactory(),
            scopeFactory,
            new ConfigurationBuilder().Build(),
            NullLogger<AgentClient>.Instance);

        return new DockerImageRegistryService(
            Options.Create(new DockerRegistrySettings { Address = address, Namespace = ns }),
            scopeFactory,
            agentClient,
            NullLogger<DockerImageRegistryService>.Instance);
    }

    sealed class StaticHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }
}
