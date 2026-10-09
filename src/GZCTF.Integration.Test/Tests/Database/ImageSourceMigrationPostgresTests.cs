using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Models.Internal;
using GZCTF.Modules.Audit.Application;
using GZCTF.Modules.Audit.Domain;
using GZCTF.Modules.Audit.Infrastructure;
using GZCTF.Modules.Content.Domain;
using GZCTF.Modules.Content.Infrastructure;
using GZCTF.Modules.TeamLab.Domain.Runtime;
using GZCTF.Services.Fleet;
using GZCTF.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Testcontainers.PostgreSql;
using Xunit;

namespace GZCTF.Integration.Test.Tests.Database;

public sealed class ImageSourceMigrationPostgresTests : IAsyncLifetime
{
    readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("source_migration").WithUsername("postgres").WithPassword("postgres").WithCleanUp(true).Build();
    readonly string _root = Path.Combine(Path.GetTempPath(), $"gz-source-pg-{Guid.NewGuid():N}");
    readonly byte[] _bytes = new byte[100];
    string SourcePath => Path.Combine(_root, "windows-source.qcow2");
    string Digest => Convert.ToHexStringLower(SHA256.HashData(_bytes));
    public async Task InitializeAsync() { Directory.CreateDirectory(_root); await _postgres.StartAsync(); }
    public async Task DisposeAsync() { await _postgres.DisposeAsync(); Directory.Delete(_root, true); }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RegistryRegistrationSurvivesRestartAndCleanupRetryWithoutChangingWindowsOrBindings(bool sharedSource)
    {
        await File.WriteAllBytesAsync(SourcePath, _bytes);
        var inspector = new Inspector { Reject = true };
        var actor = Guid.NewGuid();
        Guid operationId;
        int templateId;
        await using (var context = Context())
        {
            await context.Database.EnsureCreatedAsync();
            var owner = new UserInfo { Id = actor, UserName = "fixture-admin", Role = Role.Admin };
            var template = new ImageTemplate
            {
                Name = "windows-fixture", OSType = OSType.Windows, ImageType = ImageType.Qcow2,
                SupportsInstanceCredentials = true, VmRuntimeMode = VmRuntimeMode.Opaque,
                VmNetworkMode = VmNetworkMode.Dhcp, ImageHash = Digest, FileSize = _bytes.Length,
                LocalFilePath = SourcePath, Status = ImageStatus.Ready, Description = "retained",
                RemoteAccess = new ImageTemplateRemoteAccess { Enabled = true, Protocol = TeamLabRemoteProtocol.Rdp, Port = 3389 }
            };
            context.AddRange(owner, template);
            await context.SaveChangesAsync();
            templateId = template.Id;
            var game = new Game { Title = "fixture" };
            context.Games.Add(game);
            context.GameChallenges.AddRange(Enumerable.Range(1, 5).Select(index =>
                new GameChallenge { Title = "fixture-" + index, Game = game, ImageTemplateId = template.Id }));
            context.ExerciseChallenges.AddRange(Enumerable.Range(1, 4).Select(index =>
                new ExerciseChallenge { Title = "fixture-" + index, ImageTemplateId = template.Id }));
            await context.SaveChangesAsync();
            var registry = Registry();
            var files = Files(inspector);
            var store = new EfImageSourceMigrationStore(context, files, registry.Vm);
            var submitted = await store.SubmitAsync(templateId, actor, "migration-fixture", default);
            operationId = submitted.Operation.Id;
            var reused = await store.SubmitAsync(templateId, actor, "migration-fixture", default);
            Assert.True(reused.Reused);
            Assert.Equal(operationId, reused.Operation.Id);
            await ClaimAsync(context, operationId);
            var cleanup = await Assert.ThrowsAsync<ApiOperationRetryableException>(() =>
                Handler(context, registry, files).ExecuteAsync(operationId, "fixture", default));
            Assert.Equal("source_migration_cleanup_unproven", cleanup.Code);
        }

        await using (var restored = Context())
        {
            var template = await restored.ImageTemplates.Include(item => item.PreparedArtifact).Include(item => item.RemoteAccess)
                .SingleAsync(item => item.Id == templateId);
            Assert.Null(template.LocalFilePath);
            Assert.Equal(Digest, template.ImageHash);
            Assert.True(template.SupportsInstanceCredentials);
            Assert.Equal(VmRuntimeMode.Opaque, template.VmRuntimeMode);
            Assert.Equal(VmNetworkMode.Dhcp, template.VmNetworkMode);
            Assert.Equal("retained", template.Description);
            Assert.True(template.RemoteAccess!.Enabled);
            Assert.Equal(TeamLabRemoteProtocol.Rdp, template.RemoteAccess.Protocol);
            Assert.Equal(VmArtifactStatus.Ready, template.VmArtifactStatus);
            Assert.Equal("registry.example:5000", template.PreparedArtifact!.RegistryAddress);
            Assert.Null(template.PreparedArtifact.EvidenceDigest); // Source registration is not guest certification.
            Assert.Empty(await restored.ImageTemplateCapabilityCertifications.ToArrayAsync());
            Assert.Equal(5, await restored.GameChallenges.CountAsync());
            Assert.All(await restored.GameChallenges.ToArrayAsync(), item => Assert.Equal(templateId, item.ImageTemplateId));
            Assert.Equal(4, await restored.ExerciseChallenges.CountAsync());
            Assert.All(await restored.ExerciseChallenges.ToArrayAsync(), item => Assert.Equal(templateId, item.ImageTemplateId));
            Assert.Null(FleetVmService.ValidateImage(template));
            Assert.True(File.Exists(SourcePath));
            var job = await restored.ImageImportJobs.SingleAsync(item => item.OperationId == operationId);
            Assert.Null(job.StagedPath);
            Assert.Equal(SourcePath, VmSourceMigrationReference.Decode(job.SourceReference).Path);
            inspector.Reject = false;
            if (sharedSource)
            {
                var other = new ImageTemplate { Name = "shared-source-fixture", LocalFilePath = SourcePath };
                restored.ImageTemplates.Add(other);
                await restored.SaveChangesAsync();
                var error = await Assert.ThrowsAsync<ApiOperationTerminalException>(() =>
                    Handler(restored, Registry(), Files(inspector)).ExecuteAsync(operationId, "fixture", default));
                Assert.Equal("source_migration_source_still_referenced", error.Code);
                Assert.True(File.Exists(SourcePath));
                restored.ImageTemplates.Remove(await restored.ImageTemplates.SingleAsync(item => item.Id == other.Id));
                await restored.SaveChangesAsync();
            }
            // A new explicit idempotency key reuses the old captured path/cleanup identity after a restart.
            var retry = await new EfImageSourceMigrationStore(restored, Files(inspector), Registry().Vm)
                .SubmitAsync(templateId, actor, "cleanup-retry", default);
            var retryJob = await restored.ImageImportJobs.SingleAsync(item => item.OperationId == retry.Operation.Id);
            Assert.Equal(job.SourceReference, retryJob.SourceReference);
            operationId = retry.Operation.Id;
            await ClaimAsync(restored, operationId);
            await Handler(restored, Registry(), Files(inspector)).ExecuteAsync(operationId, "fixture", default);
            Assert.False(File.Exists(SourcePath));
            Assert.Single(await restored.VmPreparedArtifacts.ToArrayAsync());
        }
        await using (var afterCleanup = Context())
        {
            // Re-running after the source was removed checks Registry/DB and does not require the old file.
            await Handler(afterCleanup, Registry(), Files(inspector)).ExecuteAsync(operationId, "fixture", default);
            Assert.Equal(1, await afterCleanup.ImageTemplates.CountAsync());
            Assert.Equal(1, await afterCleanup.VmPreparedArtifacts.CountAsync());
            Assert.Equal("source-migrated", (await afterCleanup.ApiOperations.SingleAsync(item => item.Id == operationId)).Stage);
        }
    }

    [Fact]
    public async Task WrongRegistryBytesNeverClearTheLocalSourceOrCreatePreparedProvenance()
    {
        await File.WriteAllBytesAsync(SourcePath, _bytes);
        await using var context = Context();
        await context.Database.EnsureCreatedAsync();
        var actor = Guid.NewGuid();
        context.Users.Add(new UserInfo { Id = actor, UserName = "fixture-admin", Role = Role.Admin });
        var template = new ImageTemplate
        { Name = "fixture", ImageType = ImageType.Qcow2, LocalFilePath = SourcePath, ImageHash = Digest, FileSize = 100 };
        context.ImageTemplates.Add(template);
        await context.SaveChangesAsync();
        var registry = Registry(corrupt: true);
        var files = Files(new Inspector());
        var submitted = await new EfImageSourceMigrationStore(context, files, registry.Vm)
            .SubmitAsync(template.Id, actor, "corrupt-fixture", default);
        await ClaimAsync(context, submitted.Operation.Id);
        var error = await Assert.ThrowsAsync<ApiOperationTerminalException>(() =>
            Handler(context, registry, files).ExecuteAsync(submitted.Operation.Id, "fixture", default));
        Assert.Equal("source_migration_registry_verification_failed", error.Code);
        context.ChangeTracker.Clear();
        Assert.Equal(SourcePath, (await context.ImageTemplates.SingleAsync()).LocalFilePath);
        Assert.Empty(await context.VmPreparedArtifacts.ToArrayAsync());
        Assert.True(File.Exists(SourcePath));
    }

    AppDbContext Context() => new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_postgres.GetConnectionString()).Options);
    VmSourceMigrationFiles Files(IVmSourceBackingInspector inspector) => new(Options.Create(new KvmSettings { ImageStoragePath = _root }), inspector);
    static async Task ClaimAsync(AppDbContext context, Guid id)
    {
        var operation = await context.ApiOperations.SingleAsync(item => item.Id == id);
        operation.Status = ApiOperationStatus.Running;
        operation.LeaseOwner = "fixture";
        operation.LeaseExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5);
        await context.SaveChangesAsync();
    }
    (VmImageRegistryService Vm, OciArtifactRegistryClient Oci) Registry(bool corrupt = false)
    {
        var bytes = _bytes.ToArray();
        if (corrupt) bytes[0] = 1;
        var oci = new OciArtifactRegistryClient(new Factory(bytes, Digest), NullLogger<OciArtifactRegistryClient>.Instance);
        return (new VmImageRegistryService(Options.Create(new DockerRegistrySettings { Address = "registry.example:5000", Namespace = "ctf" }), oci), oci);
    }
    static ImageSourceMigrationOperationHandler Handler(AppDbContext context,
        (VmImageRegistryService Vm, OciArtifactRegistryClient Oci) registry, VmSourceMigrationFiles files) => new(
        context, new ApiOperationService(new EfApiOperationStore(context)), files, registry.Vm, registry.Oci);
    sealed class Inspector : IVmSourceBackingInspector
    {
        public bool Reject { get; set; }
        public Task EnsureUnusedAsync(string sourcePath, string quarantinePath, CancellationToken token) =>
            Reject ? Task.FromException(new IOException("fixture actual backing still uses the source")) : Task.CompletedTask;
    }
    sealed class Factory(byte[] bytes, string digest) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new RegistryHandler(bytes, digest));
    }
    sealed class RegistryHandler(byte[] bytes, string digest) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = request.RequestUri!.AbsolutePath.Contains("/manifests/", StringComparison.Ordinal)
                    ? new StringContent(JsonSerializer.Serialize(new { layers = new[] { new { digest = "sha256:" + digest, size = 100 } } }))
                    : new ByteArrayContent(bytes)
            };
            response.Headers.Add("Docker-Content-Digest", "sha256:" + Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(
                JsonSerializer.Serialize(new { layers = new[] { new { digest = "sha256:" + digest, size = 100 } } })))));
            return Task.FromResult(response);
        }
    }
}
