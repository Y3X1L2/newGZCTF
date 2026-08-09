using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GZCTF.GuestControl.Contracts;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Models.Internal;
using GZCTF.Modules.Audit.Application;
using GZCTF.Modules.Content.Application;
using GZCTF.Modules.Content.Contracts;
using GZCTF.Modules.Content.Domain;
using GZCTF.Modules.Content.Infrastructure;
using GZCTF.Modules.Identity.Application;
using GZCTF.Modules.TeamLab.Application;
using GZCTF.Modules.TeamLab.Domain;
using GZCTF.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace GZCTF.Test.UnitTests.Content;

public sealed class BootstrapProfileTests
{
    [Fact]
    public void OfficialBootstrapProfileManifests_AreBoundedAndValid()
    {
        var root = FindRepositoryRoot();
        var files = Directory.GetFiles(Path.Combine(root, "scenarios", "bootstrap-profiles"),
            "manifest.json", SearchOption.AllDirectories);

        Assert.Equal(4, files.Length);
        foreach (var file in files)
        {
            var manifest = BootstrapProfileApplicationService.ParseAndValidateManifest(File.ReadAllText(file));
            Assert.Equal(1, manifest.SchemaVersion);
            Assert.NotEmpty(manifest.AssetKinds);
            Assert.NotEmpty(manifest.OperatingSystems);
            Assert.All(manifest.Steps, step =>
                Assert.Equal("system", step.RunAs, ignoreCase: true));
            foreach (var source in manifest.Files.Select(item => item.SourcePath)
                         .Concat(manifest.Steps.Select(item => item.Entrypoint))
                         .Concat(manifest.HealthChecks
                             .Where(item => item.Kind == BootstrapHealthCheckKind.Entrypoint)
                             .Select(item => item.Target)))
                Assert.True(File.Exists(Path.Combine(Path.GetDirectoryName(file)!, source.Replace('/', Path.DirectorySeparatorChar))),
                    $"Official bootstrap artifact file is missing: {source}");
        }
    }

    [Fact]
    public async Task CreateSubmission_ReusesOperationAndRejectsConflictingPayload()
    {
        await using var context = CreateContext();
        var audit = new ExternalApiAuditContext();
        var service = CreateApplicationService(context, audit);
        var tokenId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var actor = new ActorContext(actorId, Role.Teacher, tokenId);

        var first = await service.SubmitCreateAsync(tokenId, actor, "bootstrap-create-1",
            new BootstrapProfileCreateModel("linux-service", "test"), CancellationToken.None);
        var repeated = await service.SubmitCreateAsync(tokenId, actor, "bootstrap-create-1",
            new BootstrapProfileCreateModel("linux-service", "test"), CancellationToken.None);

        Assert.Equal(first.Operation.Id, repeated.Operation.Id);
        Assert.True(repeated.Reused);
        await Assert.ThrowsAsync<IdempotencyConflictException>(() => service.SubmitCreateAsync(
            tokenId, actor, "bootstrap-create-1",
            new BootstrapProfileCreateModel("different", "test"), CancellationToken.None));
    }

    [Fact]
    public async Task Compatibility_RequiresCertificationBoundToCurrentImageDigest()
    {
        await using var context = CreateContext();
        var profileId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var profile = new BootstrapProfile
        {
            PublicId = profileId,
            Name = "linux-service",
            CreatedById = userId
        };
        var manifest = new BootstrapProfileManifest(
            1,
            new HashSet<OSType> { OSType.Linux },
            new HashSet<TeamLabAssetKind> { TeamLabAssetKind.Vm },
            new HashSet<string> { ImageTemplateCapabilityIds.GuestQga },
            [new BootstrapParameterDefinition("service", BootstrapParameterType.String, true, false)],
            [], [], [], 0);
        var version = new BootstrapProfileVersion
        {
            Profile = profile,
            ProfileId = profile.Id,
            Version = 1,
            Status = BootstrapProfileVersionStatus.Ready,
            ManifestJson = BootstrapProfileApplicationService.SerializeManifest(manifest),
            ManifestDigest = new string('b', 64),
            ArtifactDigest = new string('c', 64),
            ArtifactSize = 1,
            RegistryAddress = "10.24.0.28:5000",
            RegistryRepository = $"gzctf/bootstrap-profile/{profileId:N}",
            RegistryTag = "1",
            CreatedById = userId
        };
        profile.Versions.Add(version);
        var artifact = new VmPreparedArtifact
        {
            OSType = OSType.Linux,
            Status = VmPreparedArtifactStatus.Ready,
            ArtifactDigest = new string('a', 64),
            ArtifactSize = 1,
            RegistryAddress = "10.24.0.28:5000",
            RegistryRepository = "gzctf/vm-template/ubuntu",
            RegistryTag = "managed"
        };
        var template = new ImageTemplate
        {
            Id = 77,
            Name = "ubuntu",
            OSType = OSType.Linux,
            ImageType = ImageType.Qcow2,
            Status = ImageStatus.Ready,
            ImageHash = new string('a', 64),
            VmRuntimeMode = VmRuntimeMode.Managed,
            VmArtifactStatus = VmArtifactStatus.Ready,
            PreparedArtifact = artifact
        };
        context.AddRange(profile, template);
        await context.SaveChangesAsync();
        var topology = Topology(profileId, template.Id);
        var service = new BootstrapProfileCompatibilityService(context);

        await Assert.ThrowsAsync<TeamLabApiContractException>(() =>
            service.ValidateReleaseAsync(topology, CancellationToken.None));

        context.ImageTemplateCapabilityCertifications.Add(new ImageTemplateCapabilityCertification
        {
            ImageTemplateId = template.Id,
            ImageHash = template.ImageHash,
            Status = ImageTemplateCertificationStatus.Certified,
            CapabilitiesJson = JsonSerializer.Serialize(new[]
            {
                ImageTemplateCapabilityIds.GuestQga,
                ImageTemplateCapabilityIds.LinuxCloudInitNoCloud,
                ImageTemplateCapabilityIds.NetworkVirtio,
                ImageTemplateCapabilityIds.GuestSupervisor,
                ImageTemplateCapabilityIds.VmPreparedImage
            }),
            EvidenceDigest = new string('d', 64),
            ProbeKind = "controlled-probe",
            PreparationContractVersion = GuestControlProtocol.PreparationContractVersion,
            GuestProtocolVersion = GuestControlProtocol.SchemaVersion,
            CertifiedById = userId
        });
        await context.SaveChangesAsync();

        var compatible = await service.ValidateReleaseAsync(topology, CancellationToken.None);
        Assert.Single(compatible);
        Assert.Equal(version.Id, compatible[0].Id);
    }

    [Fact]
    public async Task OciArtifactPush_DigestMismatchFailsBeforeNetworkAccess()
    {
        var path = Path.GetTempFileName();
        await File.WriteAllTextAsync(path, "artifact");
        try
        {
            var client = new OciArtifactRegistryClient(
                new Mock<IHttpClientFactory>(MockBehavior.Strict).Object,
                NullLogger<OciArtifactRegistryClient>.Instance);
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => client.PushFileAsync(
                "10.24.0.28:5000", "gzctf/test", "1", path, new string('a', 64),
                "application/vnd.test", "application/octet-stream", null, CancellationToken.None));
            Assert.Contains("digest mismatch", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static TeamLabExecutionTopology Topology(Guid profileId, int templateId) => new(
        2,
        "bootstrap",
        [],
        [],
        [new TeamLabExecutionAsset(
            "web", "web", TeamLabAssetKind.Vm, templateId, 1, 1024, 10240, [], false, null,
            new Dictionary<string, string>(), null, null, null, 0, false,
            new TeamLabExecutionBootstrapReference(profileId, 1,
                new Dictionary<string, string> { ["service"] = "nginx" }),
            TeamLabEndpointObservationMode.Disabled)],
        [], [], new TeamLabExecutionObservationPolicy(true, true, TeamLabEndpointObservationMode.Optional));

    private static BootstrapProfileApplicationService CreateApplicationService(
        AppDbContext context,
        ExternalApiAuditContext audit)
    {
        var root = Path.Combine(Path.GetTempPath(), $"gzctf-bootstrap-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var environment = new TestHostEnvironment { ContentRootPath = root };
        var registry = new OciArtifactRegistryClient(
            new Mock<IHttpClientFactory>().Object,
            NullLogger<OciArtifactRegistryClient>.Instance);
        var artifacts = new BootstrapProfileArtifactService(
            environment,
            Options.Create(new DockerRegistrySettings
            {
                Address = "10.24.0.28:5000",
                MaxUploadSizeGb = 1
            }),
            registry);
        return new BootstrapProfileApplicationService(context, artifacts, audit);
    }

    private static AppDbContext CreateContext() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !Directory.Exists(Path.Combine(current.FullName, "scenarios")))
            current = current.Parent;
        return current?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "GZCTF.Test";
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
