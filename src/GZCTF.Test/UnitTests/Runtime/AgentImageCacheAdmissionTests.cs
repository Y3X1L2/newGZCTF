using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using GZCTF.Agent.Controllers;
using GZCTF.Agent.Models;
using GZCTF.Agent.Services;
using GZCTF.Agent.Services.Vm;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace GZCTF.Test.UnitTests.Runtime;

public sealed class AgentImageCacheAdmissionTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), $"gzctf-image-cache-{Guid.NewGuid():N}");
    readonly byte[] _payload = new byte[40];
    public AgentImageCacheAdmissionTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, true);

    [Fact]
    public async Task VerifiedCacheHit_DoesNotRequireFreeSpaceOrStartAnotherDownload()
    {
        await File.WriteAllBytesAsync(Path.Combine(_root, "10.qcow2"), _payload);
        var handler = new PayloadHandler(_payload);
        var controller = Create(handler, _ => throw new InvalidOperationException("Cache hit must not admit bytes."));
        var result = Assert.IsType<OkObjectResult>(await controller.DownloadVmImage(Request(10), CancellationToken.None));
        var response = Assert.IsType<DownloadVmImageResponse>(result.Value);
        Assert.True(response.AlreadyExists);
        Assert.True(response.Verified);
        Assert.Equal(0, handler.Requests);
    }

    [Fact]
    public async Task CompleteVerifiedPartial_IsPromotedWithoutASecondTransferOrReservation()
    {
        await File.WriteAllBytesAsync(Path.Combine(_root, "10.qcow2.part"), _payload);
        var handler = new PayloadHandler(_payload);
        var controller = Create(handler, _ => throw new InvalidOperationException("Rename must not reserve another copy."));
        var result = Assert.IsType<OkObjectResult>(await controller.DownloadVmImage(Request(10), CancellationToken.None));
        Assert.True(Assert.IsType<DownloadVmImageResponse>(result.Value).Verified);
        Assert.True(File.Exists(Path.Combine(_root, "10.qcow2")));
        Assert.False(File.Exists(Path.Combine(_root, "10.qcow2.part")));
        Assert.Equal(0, handler.Requests);
    }

    [Fact]
    public async Task DifferentTemplateIdsWithOneDigest_MustEachCreateTheirActualDestination()
    {
        var handler = new PayloadHandler(_payload);
        var controller = Create(handler, _ => new AgentStorageSnapshot("device", 1000));
        await Task.WhenAll(controller.DownloadVmImage(Request(10), CancellationToken.None),
            controller.DownloadVmImage(Request(11), CancellationToken.None));
        Assert.Equal(_payload, await File.ReadAllBytesAsync(Path.Combine(_root, "10.qcow2")));
        Assert.Equal(_payload, await File.ReadAllBytesAsync(Path.Combine(_root, "11.qcow2")));
        Assert.Equal(2, handler.Requests);
    }

    [Fact]
    public async Task CapacityFailure_DoesNotCreateCacheOrPartialFile()
    {
        var handler = new PayloadHandler(_payload);
        var controller = Create(handler, _ => new AgentStorageSnapshot("device", 49));
        var error = await Assert.ThrowsAsync<AgentOperationException>(() =>
            controller.DownloadVmImage(Request(10), CancellationToken.None));
        Assert.Equal("image.storage_capacity_insufficient", error.Code);
        Assert.Empty(Directory.EnumerateFiles(_root));
    }

    [Fact]
    public async Task CacheHitWithWrongDeclaredSize_IsRejectedRatherThanReportedVerified()
    {
        await File.WriteAllBytesAsync(Path.Combine(_root, "10.qcow2"), _payload);
        var handler = new PayloadHandler(_payload);
        var controller = Create(handler, _ => throw new InvalidOperationException("Cache verification must not reserve bytes."));
        var request = Request(10);
        request.ExpectedSize = _payload.Length + 1;
        var error = await Assert.ThrowsAsync<AgentOperationException>(() =>
            controller.DownloadVmImage(request, CancellationToken.None));
        Assert.Equal("image.size_mismatch", error.Code);
        Assert.Equal(0, handler.Requests);
        Assert.True(File.Exists(Path.Combine(_root, "10.qcow2")));
    }

    [Fact]
    public async Task MissingDigest_DoesNotTurnAnArbitraryExistingFileIntoAVerifiedCacheHit()
    {
        await File.WriteAllBytesAsync(Path.Combine(_root, "10.qcow2"), _payload);
        var handler = new PayloadHandler(_payload);
        var controller = Create(handler, _ => throw new InvalidOperationException("Invalid request must not reserve bytes."));
        var request = Request(10);
        request.Hash = string.Empty;
        await Assert.ThrowsAsync<ArgumentException>(() => controller.DownloadVmImage(request, CancellationToken.None));
        Assert.Equal(0, handler.Requests);
    }

    DownloadVmImageRequest Request(int template) => new()
    {
        TemplateId = template,
        Hash = Convert.ToHexString(SHA256.HashData(_payload)).ToLowerInvariant(),
        ExpectedSize = _payload.Length,
        DownloadUrl = "http://artifact.example/image"
    };

    ImageController Create(PayloadHandler handler, Func<string, AgentStorageSnapshot> probe)
    {
        var config = Options.Create(new AgentConfig
        { ExecutionLimits = new AgentExecutionLimitOverrides { VmImageTransfers = 2 } });
        var kvm = Options.Create(new KvmConfig { ImageStoragePath = _root });
        var locks = new AgentResourceLock();
        var budget = AgentImageStorageBudgetTests.Create(probe);
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(item => item.CreateClient(It.IsAny<string>())).Returns(() => new HttpClient(handler, false));
        var services = new ServiceCollection().AddSingleton(factory.Object).BuildServiceProvider();
        var controller = new ImageController(
            null!, // VM paths do not call Docker; main and Agent use different Docker.DotNet packages.
            new AgentOperationGate(config), locks, new ImageTransferSingleFlight(),
            new AgentOciArtifactUploader(factory.Object),
            new VmImageBackingChainInspector(NullLogger<VmImageBackingChainInspector>.Instance),
            new AgentImageDownloadWriter(budget),
            Options.Create(new AgentTeamLabConfig()), kvm, NullLogger<ImageController>.Instance)
        { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { RequestServices = services } } };
        return controller;
    }

    sealed class PayloadHandler(byte[] payload) : HttpMessageHandler
    {
        int _requests;
        public int Requests => _requests;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Interlocked.Increment(ref _requests);
            await Task.Yield();
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) };
        }
    }
}
