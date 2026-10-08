using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using GZCTF.Models.Data;
using GZCTF.Modules.Audit.Contracts;
using GZCTF.Modules.Audit.Domain;
using GZCTF.Repositories.Interface;
using GZCTF.Services.Fleet;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace GZCTF.Test.UnitTests.Fleet;

public sealed class AgentVmImageErrorMappingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Capacity507_PreservesAgentCategoryCodeRetryabilityAndNode(bool prepared)
    {
        using var fixture = new Fixture((HttpStatusCode)507, new AgentErrorResponse(
            "Storage", "image.storage_capacity_insufficient",
            "requiredAdditionalBytes=40, availableBytes=30, safetyMarginBytes=10", true,
            "image.vm.download", Guid.NewGuid().ToString()));
        var error = await Assert.ThrowsAsync<AgentClientException>(() => Download(fixture, prepared));
        Assert.Equal(OperationalErrorCategory.Storage, error.Error.Category);
        Assert.Equal("image.storage_capacity_insufficient", error.Error.Code);
        Assert.True(error.Error.Retryable);
        Assert.Equal(507, error.Error.HttpStatus);
        Assert.Equal(fixture.Node.Id, error.Error.WorkerNodeId);
        Assert.Equal(prepared ? "image.vm.download-prepared" : "image.vm.download", error.Error.Operation);
        Assert.Contains("requiredAdditionalBytes=40", error.Error.Message);
        Assert.Equal("/api/images/download-vm", fixture.Handler.RequestPath);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PermanentSizeMismatch_DoesNotBecomeAnAutomaticallyRetryableHttp500(bool prepared)
    {
        using var fixture = new Fixture(HttpStatusCode.InternalServerError, new AgentErrorResponse(
            "ImageTransfer", "image.size_mismatch", "Artifact size is invalid.", false,
            "image.vm.download", Guid.NewGuid().ToString()));
        var error = await Assert.ThrowsAsync<AgentClientException>(() => Download(fixture, prepared));
        Assert.Equal(OperationalErrorCategory.ImageTransfer, error.Error.Category);
        Assert.Equal("image.size_mismatch", error.Error.Code);
        Assert.False(error.Error.Retryable);
        Assert.Equal(500, error.Error.HttpStatus);
        Assert.Contains("Artifact size is invalid.", error.Error.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingNode_KeepsTheExistingFailedResultWithoutHttpRequest(bool prepared)
    {
        using var fixture = new Fixture(HttpStatusCode.OK, AgentVmImageDownloadResult.Ok(false, true, 40,
            "sha256:" + new string('a', 64)), missingNode: true);
        var result = await Download(fixture, prepared);
        Assert.False(result.Success);
        Assert.Contains("not found", result.Message);
        Assert.Null(fixture.Handler.RequestPath);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SuccessfulHttpResult_KeepsVerificationAndSizeFacts(bool prepared)
    {
        using var fixture = new Fixture(HttpStatusCode.OK, AgentVmImageDownloadResult.Ok(false, true, 40,
            "sha256:" + new string('a', 64)));
        var result = await Download(fixture, prepared);
        Assert.True(result.Success);
        Assert.True(result.Verified);
        Assert.Equal(40, result.Size);
        Assert.Equal("sha256:" + new string('a', 64), result.Digest);
    }

    static Task<AgentVmImageDownloadResult> Download(Fixture fixture, bool prepared) => prepared
        ? fixture.Client.DownloadPreparedVmImageAsync(fixture.Node.Id, 1, new string('a', 64), 40,
            "registry.example:5000", "test/image", "release", CancellationToken.None)
        : fixture.Client.DownloadVmImageAsync(fixture.Node.Id, 1, new string('a', 64),
            "http://artifact.example/image", 40, CancellationToken.None);

    sealed class Fixture : IDisposable
    {
        readonly ServiceProvider _services;
        public WorkerNode Node { get; } = new() { Id = Guid.NewGuid(), Name = "capacity-worker", HostAddress = "192.0.2.20" };
        public Handler Handler { get; }
        public AgentClient Client { get; }

        public Fixture(HttpStatusCode status, object body, bool missingNode = false)
        {
            Handler = new Handler(status, body);
            var repository = new Mock<INodeRepository>();
            repository.Setup(item => item.GetNodeByIdAsync(Node.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(missingNode ? null : Node);
            _services = new ServiceCollection().AddSingleton(repository.Object).BuildServiceProvider();
            var factory = new Mock<IHttpClientFactory>();
            factory.Setup(item => item.CreateClient("Agent")).Returns(() => new HttpClient(Handler, false));
            Client = new AgentClient(factory.Object, _services.GetRequiredService<IServiceScopeFactory>(),
                new ConfigurationBuilder().Build(), NullLogger<AgentClient>.Instance);
        }
        public void Dispose() { Handler.Dispose(); _services.Dispose(); }
    }

    sealed class Handler(HttpStatusCode status, object body) : HttpMessageHandler
    {
        public string? RequestPath { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            RequestPath = request.RequestUri!.AbsolutePath;
            return Task.FromResult(new HttpResponseMessage(status) { Content = JsonContent.Create(body) });
        }
    }
}
