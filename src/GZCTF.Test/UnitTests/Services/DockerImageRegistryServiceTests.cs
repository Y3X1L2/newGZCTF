using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
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

    [Fact]
    public async Task DeleteManagedImageAsync_DeletesManifestByResolvedDigest()
    {
        var handler = new RecordingHandler();
        var service = CreateService("10.24.0.28:5000", "ctf", handler);

        await service.DeleteManagedImageAsync(
            "gzctf-internal://ctf/imports/demo:latest",
            CancellationToken.None);

        Assert.Collection(handler.Requests,
            request =>
            {
                Assert.Equal(HttpMethod.Head, request.Method);
                Assert.Equal("http://10.24.0.28:5000/v2/ctf/imports/demo/manifests/latest", request.Uri);
            },
            request =>
            {
                Assert.Equal(HttpMethod.Delete, request.Method);
                Assert.Equal(
                    "http://10.24.0.28:5000/v2/ctf/imports/demo/manifests/sha256%3Aabc",
                    request.Uri);
            });
    }

    [Fact]
    public async Task DeleteManagedImageAsync_DoesNotDeleteExternalImage()
    {
        var handler = new RecordingHandler();
        var service = CreateService("10.24.0.28:5000", "ctf", handler);

        await service.DeleteManagedImageAsync("ghcr.io/example/demo:latest", CancellationToken.None);

        Assert.Empty(handler.Requests);
    }

    static DockerImageRegistryService CreateService(
        string address,
        string ns,
        HttpMessageHandler? handler = null)
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
            NullLogger<DockerImageRegistryService>.Instance,
            handler is null ? null : new StaticHttpClientFactory(handler));
    }

    sealed class StaticHttpClientFactory(HttpMessageHandler? handler = null) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
    }

    sealed class RecordingHandler : HttpMessageHandler
    {
        public List<(HttpMethod Method, string Uri)> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add((request.Method, request.RequestUri!.ToString()));
            var response = request.Method == HttpMethod.Head
                ? new HttpResponseMessage(HttpStatusCode.OK)
                : new HttpResponseMessage(HttpStatusCode.Accepted);
            if (request.Method == HttpMethod.Head)
                response.Headers.TryAddWithoutValidation("Docker-Content-Digest", "sha256:abc");
            return Task.FromResult(response);
        }
    }
}
