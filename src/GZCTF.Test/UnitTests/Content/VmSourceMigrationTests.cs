using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GZCTF.Models.Data;
using GZCTF.Models.Internal;
using GZCTF.Modules.Audit.Application;
using GZCTF.Modules.Content.Application;
using GZCTF.Modules.Content.Infrastructure;
using GZCTF.Modules.Identity.Application;
using GZCTF.Utils;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace GZCTF.Test.UnitTests.Content;

public sealed class VmSourceMigrationTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), $"gz-source-{Guid.NewGuid():N}");
    readonly byte[] _payload = new byte[100];
    string SourcePath => Path.Combine(_root, "windows-source.qcow2");
    string Digest => Convert.ToHexStringLower(SHA256.HashData(_payload));
    public VmSourceMigrationTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, true);

    [Fact]
    public async Task CorrectCapturedFile_DeleteLeavesNumericCacheAndXmlAlone()
    {
        await File.WriteAllBytesAsync(SourcePath, _payload);
        await File.WriteAllBytesAsync(Path.Combine(_root, "1.qcow2"), _payload);
        await File.WriteAllTextAsync(Path.ChangeExtension(SourcePath, ".xml"), "fixture");
        var files = Files();
        var source = Capture(files);
        await files.DeleteAsync(Guid.NewGuid(), source, Digest, _payload.Length, default);
        Assert.False(File.Exists(SourcePath));
        Assert.True(File.Exists(Path.Combine(_root, "1.qcow2")));
        Assert.True(File.Exists(Path.ChangeExtension(SourcePath, ".xml")));
        await files.DeleteAsync(Guid.NewGuid(), source, Digest, _payload.Length, default);
    }

    [Fact]
    public void NumericCacheAndPathOutsideConfiguredRoot_AreRejectedBeforeCapture()
    {
        var files = Files();
        Assert.Throws<ApiOperationTerminalException>(() => files.Capture(Path.Combine(_root, "1.qcow2")));
        Assert.Throws<ApiOperationTerminalException>(() => files.Capture(Path.Combine(_root, "..", "outside.qcow2")));
        Assert.Throws<ApiOperationTerminalException>(() => files.Capture("relative.qcow2"));
    }

    [Fact]
    public async Task ReplacementWithIdenticalBytes_IsNotDeleted()
    {
        await File.WriteAllBytesAsync(SourcePath, _payload);
        var files = Files();
        var source = Capture(files);
        File.Move(SourcePath, Path.Combine(_root, "kept.qcow2"));
        await File.WriteAllBytesAsync(SourcePath, _payload);
        await Assert.ThrowsAsync<ApiOperationTerminalException>(() => files.DeleteAsync(Guid.NewGuid(), source, Digest, 100, default));
        Assert.True(File.Exists(SourcePath));
        Assert.True(File.Exists(Path.Combine(_root, "kept.qcow2")));
    }

    [Fact]
    public async Task WrongHashAndActualBacking_KeepTheOriginalFile()
    {
        await File.WriteAllBytesAsync(SourcePath, _payload);
        var files = Files();
        var source = Capture(files);
        await Assert.ThrowsAsync<ApiOperationTerminalException>(() => files.DeleteAsync(Guid.NewGuid(), source, new string('a', 64), 100, default));
        var backing = new Mock<IVmSourceBackingInspector>();
        backing.Setup(item => item.EnsureUnusedAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("fixture backing is active"));
        await Assert.ThrowsAsync<IOException>(() => Files(backing.Object).DeleteAsync(Guid.NewGuid(), source, Digest, 100, default));
        Assert.True(File.Exists(SourcePath));
    }

    [Fact]
    public async Task CapturedQuarantine_IsRecoverableAfterRestartAndANewExplicitRequest()
    {
        await File.WriteAllBytesAsync(SourcePath, _payload);
        var files = Files();
        var cleanupId = Guid.NewGuid();
        var source = Capture(files) with { CleanupId = cleanupId };
        File.Move(SourcePath, Path.Combine(_root, $".source-migration-{cleanupId:N}.pending-delete"));
        // A new service instance / operation ID must keep the original durable cleanup identity.
        await Files().DeleteAsync(Guid.NewGuid(), source, Digest, 100, default);
        Assert.Empty(Directory.EnumerateFiles(_root));
    }

    [Fact]
    public async Task SourceDirectlyInConfiguredRoot_MissingPathCanResumeQuarantineThenRepeatCleanup()
    {
        await File.WriteAllBytesAsync(SourcePath, _payload);
        var files = Files();
        var cleanupId = Guid.NewGuid();
        var source = Capture(files) with { CleanupId = cleanupId };
        File.Move(SourcePath, Path.Combine(_root, $".source-migration-{cleanupId:N}.pending-delete"));
        Assert.False(File.Exists(SourcePath));
        await Files().DeleteAsync(Guid.NewGuid(), source, Digest, 100, default);
        await Files().DeleteAsync(Guid.NewGuid(), source, Digest, 100, default);
        Assert.Empty(Directory.EnumerateFileSystemEntries(_root));
    }

    [Fact]
    public async Task NonAdministrator_CannotSubmitOrReadMigrationState()
    {
        var store = new Mock<IImageSourceMigrationStore>(MockBehavior.Strict);
        var service = new ImageSourceMigrationService(store.Object);
        var teacher = new ActorContext(Guid.NewGuid(), Role.Teacher);
        var submit = await Assert.ThrowsAsync<ImageSourceMigrationContractException>(() => service.SubmitAsync(1, teacher, "fixture", default));
        Assert.Equal(403, submit.StatusCode);
        await Assert.ThrowsAsync<ImageSourceMigrationContractException>(() => service.FindAsync(1, Guid.NewGuid(), teacher, default));
        store.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RegistryVerification_RejectsCorrectManifestWhenTheActualBlobIsWrong(bool shortBody)
    {
        var bytes = shortBody ? new byte[99] : new byte[100];
        if (!shortBody) bytes[0] = 1;
        var client = RegistryClient(bytes);
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.VerifyBytesAsync(
            new("registry.example", "fixture/image", "fixture", "sha256:" + Digest, 100), default));
    }

    [Fact]
    public async Task RegistryVerification_ReadsCompleteBlobAndRechecksManifest()
    {
        var handler = new RegistryHandler(_payload, Digest);
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(item => item.CreateClient(It.IsAny<string>())).Returns(() => new HttpClient(handler, false));
        await new OciArtifactRegistryClient(factory.Object, NullLogger<OciArtifactRegistryClient>.Instance)
            .VerifyBytesAsync(new("registry.example", "fixture/image", "fixture", "sha256:" + Digest, 100), default);
        Assert.Equal(1, handler.BlobReads);
        Assert.Equal(2, handler.ManifestReads);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RegistryVerification_RequiresCorrectManifestByteDigest(bool missing)
    {
        var handler = new RegistryHandler(_payload, Digest) { WrongManifestDigest = !missing, MissingManifestDigest = missing };
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(item => item.CreateClient(It.IsAny<string>())).Returns(() => new HttpClient(handler, false));
        var client = new OciArtifactRegistryClient(factory.Object, NullLogger<OciArtifactRegistryClient>.Instance);
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.VerifyBytesAsync(
            new("registry.example", "fixture/image", "fixture", "sha256:" + Digest, 100), default));
        Assert.Equal(0, handler.BlobReads);
    }

    VmSourceMigrationFiles Files(IVmSourceBackingInspector? backing = null) => new(
        Options.Create(new KvmSettings { ImageStoragePath = _root }), backing ?? Mock.Of<IVmSourceBackingInspector>());
    VmSourceMigrationReference Capture(VmSourceMigrationFiles files)
    {
        var file = files.Capture(SourcePath);
        return new(file.Path, file.Identity, "registry.example", "fixture/image", Digest);
    }
    OciArtifactRegistryClient RegistryClient(byte[] bytes)
    {
        var handler = new RegistryHandler(bytes, Digest);
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(item => item.CreateClient(It.IsAny<string>())).Returns(() => new HttpClient(handler, false));
        return new(factory.Object, NullLogger<OciArtifactRegistryClient>.Instance);
    }
    sealed class RegistryHandler(byte[] bytes, string digest) : HttpMessageHandler
    {
        public int ManifestReads { get; private set; }
        public int BlobReads { get; private set; }
        public bool WrongManifestDigest { get; init; }
        public bool MissingManifestDigest { get; init; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            if (request.RequestUri!.AbsolutePath.Contains("/manifests/", StringComparison.Ordinal))
            {
                ManifestReads++;
                var content = new StringContent(JsonSerializer.Serialize(new { layers = new[] { new { digest = "sha256:" + digest, size = 100 } } }));
                var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
                if (!MissingManifestDigest)
                    response.Headers.Add("Docker-Content-Digest", WrongManifestDigest ? "sha256:" + new string('a', 64) :
                        "sha256:" + Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(
                            JsonSerializer.Serialize(new { layers = new[] { new { digest = "sha256:" + digest, size = 100 } } })))));
                return Task.FromResult(response);
            }
            BlobReads++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
        }
    }
}
