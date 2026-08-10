using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using GZCTF.Middlewares;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Modules.Content.Domain;
using GZCTF.Modules.TeamLab.Api;
using GZCTF.Modules.TeamLab.Application;
using GZCTF.Modules.TeamLab.Contracts;
using GZCTF.Modules.TeamLab.Domain;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GZCTF.Test.UnitTests.TeamLab;

public sealed class TeamLabPreparationAndCatalogTests
{
    [Fact]
    public async Task AdminCatalog_UsesCookieRouteAndSharedSafeProjection()
    {
        using var context = CreateContext();
        var profile = SeedServiceProfile(context);
        var controller = new TeamLabAdminServiceProfilesController(
            new TeamLabServiceProfileCatalogService(context));

        var page = await controller.List(cancellationToken: default);
        var summary = Assert.Single(page.Items);
        var detail = await controller.Get(profile.PublicId, cancellationToken: default);

        Assert.Equal(profile.PublicId, summary.Id);
        Assert.Equal(profile.PublicId, detail.Id);
        Assert.Equal(summary.UpdatedAt, detail.UpdatedAt);
        Assert.Null(Assert.Single(detail.Parameters, parameter => parameter.Key == "flag").DefaultValue);
        Assert.Equal(
            "api/admin/teamlab/service-profiles",
            typeof(TeamLabAdminServiceProfilesController).GetCustomAttribute<Microsoft.AspNetCore.Mvc.RouteAttribute>()?.Template);
        Assert.NotNull(typeof(TeamLabAdminServiceProfilesController).GetCustomAttribute<RequireTeacherAttribute>());
    }

    [Fact]
    public void Catalog_ListAndDetailExposeSchemaWithoutSecrets()
    {
        using var context = CreateContext();
        var profile = SeedServiceProfile(context);

        var service = new TeamLabServiceProfileCatalogService(context);
        var page = service.ListAsync(null, 10, default).Result;
        var item = Assert.Single(page.Items);
        Assert.Equal(profile.PublicId, item.Id);
        Assert.Equal(1, item.Version);
        Assert.Contains(TeamLabAssetKind.Vm, item.AssetKinds);

        var detail = service.GetAsync(profile.PublicId, null, default).Result;
        var flag = Assert.Single(detail.Parameters, parameter => parameter.Key == "flag");
        Assert.True(flag.Secret);
        Assert.Null(flag.DefaultValue);
        var port = Assert.Single(detail.Parameters, parameter => parameter.Key == "service_port");
        Assert.False(port.Secret);
        Assert.Equal("Integer", port.Type);
        Assert.NotNull(port.DefaultValue);
        Assert.Equal("install", detail.Execution.Phase);
        Assert.Equal("published", detail.Status);
    }

    [Fact]
    public void Catalog_MissingOrRetiredProfileReturnsStableCode()
    {
        using var context = CreateContext();
        var service = new TeamLabServiceProfileCatalogService(context);
        var exception = Assert.ThrowsAsync<TeamLabApiContractException>(async () =>
            await service.GetAsync(Guid.NewGuid(), null, default)).Result;
        Assert.Equal("service_profile_not_found", exception.Code);
    }

    [Fact]
    public void Preparation_ReadyWhenAllImagesReadyOnEligibleNodes()
    {
        using var context = CreateContext();
        var releaseId = SeedRelease(context, out var templateId, out var digest);
        SeedNodeAndRecord(context, templateId, digest, ImageDistributionStatus.Ready);

        var preparation = new TeamLabReleaseImagePreparationService(context, null!)
            .GetPreparationAsync(releaseId, default).Result;

        Assert.Equal("readyToStart", preparation.State);
        Assert.True(preparation.PlanAvailable);
        Assert.True(preparation.ReadyToStart);
        Assert.Empty(preparation.Blockers);
        var image = Assert.Single(preparation.Images);
        Assert.Equal(templateId, image.TemplateId);
        Assert.Equal(1, image.EligibleNodeCount);
        Assert.Equal(1, image.ReadyNodeCount);
        Assert.Null(image.Failure);
    }

    [Fact]
    public void Preparation_BlockedWhenImageDistributionFailed()
    {
        using var context = CreateContext();
        var releaseId = SeedRelease(context, out var templateId, out var digest);
        SeedNodeAndRecord(context, templateId, digest, ImageDistributionStatus.Failed);

        var preparation = new TeamLabReleaseImagePreparationService(context, null!)
            .GetPreparationAsync(releaseId, default).Result;

        Assert.Equal("blocked", preparation.State);
        Assert.False(preparation.ReadyToStart);
        Assert.Single(preparation.Blockers);
        var image = Assert.Single(preparation.Images);
        Assert.NotNull(image.Failure);
        Assert.Equal("image_distribution_failed", image.Failure!.Code);
        Assert.False(image.Failure.Retryable);
    }

    [Fact]
    public void Preparation_PreparingWhileImagesPulling()
    {
        using var context = CreateContext();
        var releaseId = SeedRelease(context, out var templateId, out var digest);
        SeedNodeAndRecord(context, templateId, digest, ImageDistributionStatus.Pulling);

        var preparation = new TeamLabReleaseImagePreparationService(context, null!)
            .GetPreparationAsync(releaseId, default).Result;

        Assert.Equal("preparing", preparation.State);
        Assert.True(preparation.PlanAvailable);
        Assert.False(preparation.ReadyToStart);
        Assert.Equal(1, preparation.Images[0].PreparingNodeCount);
    }

    [Fact]
    public void Preparation_BlockedWithoutEligibleNodes()
    {
        using var context = CreateContext();
        var releaseId = SeedRelease(context, out _, out _);

        var preparation = new TeamLabReleaseImagePreparationService(context, null!)
            .GetPreparationAsync(releaseId, default).Result;

        Assert.Equal("blocked", preparation.State);
        Assert.False(preparation.PlanAvailable);
        Assert.Contains("没有具备对应能力的可调度节点", preparation.Blockers[0]);
    }

    private static Guid SeedRelease(
        AppDbContext context,
        out int templateId,
        out string digest)
    {
        var topology = new TeamLabTopology
        {
            Id = 1,
            PublicId = Guid.NewGuid(),
            Name = "单服务拓扑",
            OwnerUserId = Guid.NewGuid(),
            Revision = 1,
            SchemaVersion = 2
        };
        context.TeamLabTopologies.Add(topology);
        var definition = new TeamLabTopologyDefinitionModel(
            "单服务拓扑",
            [new TeamLabTopologyNetworkModel("net-a", "net-a", new TeamLabAddressPoolModel("10.20.0.0/24", 28), true)],
            [new TeamLabTopologyAssetModel(
                "svc-1", "服务", TeamLabAssetKind.Docker, 100,
                new TeamLabAssetResourceModel(1, 256, 0), [], RoutingEnabled: true)],
            []);
        var release = new TeamLabTopologyRelease
        {
            Id = Guid.NewGuid(),
            TopologyId = 1,
            ControlScopeId = null,
            Version = 1,
            SourceRevision = 1,
            SchemaVersion = 2,
            CanonicalJson = TeamLabReleaseCodec.Encode(2, definition),
            ContentHash = "sha256:test"
        };
        context.TeamLabTopologyReleases.Add(release);
        templateId = 100;
        digest = "sha256:abc123";
        context.ImageTemplates.Add(new ImageTemplate
        {
            Id = 100,
            Name = "web 镜像",
            ImageType = ImageType.Docker,
            Status = ImageStatus.Ready,
            ImageHash = digest
        });
        context.SaveChanges();
        return release.Id;
    }

    private static BootstrapProfile SeedServiceProfile(AppDbContext context)
    {
        var profile = new BootstrapProfile
        {
            Id = 1,
            PublicId = Guid.NewGuid(),
            Name = "Linux 服务",
            Description = "部署带健康检查的 Linux 服务",
            Status = BootstrapProfileStatus.Active,
            CreatedById = Guid.NewGuid()
        };
        context.BootstrapProfiles.Add(profile);
        context.BootstrapProfileVersions.Add(new BootstrapProfileVersion
        {
            Id = 1,
            ProfileId = 1,
            Version = 1,
            Status = BootstrapProfileVersionStatus.Ready,
            ManifestJson = LinuxServiceManifest,
            ManifestDigest = "sha256:test",
            CreatedById = Guid.NewGuid()
        });
        context.SaveChanges();
        return profile;
    }

    private static void SeedNodeAndRecord(
        AppDbContext context,
        int templateId,
        string digest,
        ImageDistributionStatus status)
    {
        context.WorkerNodes.Add(new WorkerNode
        {
            Id = Guid.NewGuid(),
            Name = "node-a",
            IsSchedulable = true,
            TeamLabNetworkEnabled = true,
            TeamLabTunnelStatus = TeamLabTunnelStatus.Healthy,
            Capabilities = NodeCapability.Docker,
            Status = NodeStatus.Online,
            LastHeartbeat = DateTimeOffset.UtcNow
        });
        context.SaveChanges();
        var node = context.WorkerNodes.Single();
        context.ImageDistributionRecords.Add(new ImageDistributionRecord
        {
            Id = Guid.NewGuid(),
            ImageTemplateId = templateId,
            WorkerNodeId = node.Id,
            ImageHash = digest,
            ImageType = ImageType.Docker,
            Status = status,
            CreatedAt = DateTimeOffset.UtcNow
        });
        context.SaveChanges();
    }

    private const string LinuxServiceManifest = """
        {
          "schemaVersion": 1,
          "operatingSystems": ["Linux"],
          "assetKinds": ["Vm"],
          "requiredTemplateCapabilities": ["bootstrap.firstboot.v1", "guest.qga.v1"],
          "parameters": [
            { "key": "service_name", "type": "String", "required": true, "secret": false },
            { "key": "service_port", "type": "Integer", "required": true, "secret": false, "defaultValue": "8080" },
            { "key": "flag", "type": "String", "required": true, "secret": true }
          ],
          "files": [
            { "sourcePath": "files/gzctf-runtime.service", "targetPath": "/etc/systemd/system/gzctf-runtime.service", "mode": "0644", "template": true }
          ],
          "steps": [
            { "id": "install", "entrypoint": "bin/install.sh", "timeoutSeconds": 300, "runAs": "system", "reboot": "None" }
          ],
          "healthChecks": [
            { "id": "service-port", "kind": "Tcp", "target": "${service_port}", "timeoutSeconds": 5, "attempts": 24 }
          ],
          "maxReboots": 0
        }
        """;

    private static AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
}
