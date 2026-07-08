using System;
using System.IO;
using Xunit;

namespace GZCTF.Test.UnitTests.TeamLab;

public class TeamLabPenetrationUxContractTests
{
    [Theory]
    [InlineData("src/GZCTF/ClientApp/src/pages/admin/games/[id]/Penetration.tsx",
        "公网入口区", "外网入口服务", "外网接入点", "Public / Edge", "edge-gateway", "入口节点",
        "发布宿主端口", "一键生成企业多级内网", "入口可达", "VPN 初始网段", "{{node:nm-node",
        "DMZ / 初始业务区", "dmz-service", "安全域", "访问策略")]
    [InlineData("src/GZCTF/ClientApp/src/pages/games/[id]/Penetration.tsx",
        "目标入口", "所属模块")]
    [InlineData("src/GZCTF/Services/PenetrationService.cs",
        "公网入口区", "队伍首先接触的外部入口安全域", "至少需要一个入口节点或公开端口节点", "入口可达", "生成入口端口")]
    public void TeamLab_VpnFirstUx_DoesNotExposeLegacyExternalEntryLanguage(string relativePath,
        params string[] forbidden)
    {
        var content = File.ReadAllText(ResolveRepoPath(relativePath));

        foreach (var text in forbidden)
            Assert.DoesNotContain(text, content, StringComparison.Ordinal);
    }

    [Fact]
    public void TeamLab_AdminEditor_DoesNotExposeDeprecatedPublicOrEntryOptions()
    {
        var content = File.ReadAllText(ResolveRepoPath("src/GZCTF/ClientApp/src/pages/admin/games/[id]/Penetration.tsx"));

        Assert.DoesNotContain("[PenetrationZoneType.Public]", content, StringComparison.Ordinal);
        Assert.DoesNotContain("[PenetrationNodeType.Entry]", content, StringComparison.Ordinal);
        Assert.DoesNotContain("PenetrationZoneType.Public", content, StringComparison.Ordinal);
        Assert.DoesNotContain("PenetrationNodeType.Entry", content, StringComparison.Ordinal);
    }

    [Fact]
    public void TeamLab_RuntimePlan_TreatsEveryPublishedNetworkAsVpnInternal()
    {
        var content = File.ReadAllText(ResolveRepoPath("src/GZCTF/Services/PenetrationService.cs"));

        Assert.Contains("static bool IsInternalNetwork(PenetrationNetwork network) => true;", content,
            StringComparison.Ordinal);
        Assert.DoesNotContain("PenetrationZoneType.Public && !network.IsEntry", content, StringComparison.Ordinal);
        Assert.DoesNotContain("!routedNetworkIds.Contains", content, StringComparison.Ordinal);
    }

    [Fact]
    public void TeamLab_PenetrationConfigPersistence_NormalizesDeprecatedPublicAndEntryValues()
    {
        var content = File.ReadAllText(ResolveRepoPath("src/GZCTF/Services/PenetrationService.cs"));

        Assert.Contains("NormalizeTeamLabZoneType(networkModel.ZoneType)", content, StringComparison.Ordinal);
        Assert.Contains("NormalizeTeamLabNodeType(nodeModel.NodeType)", content, StringComparison.Ordinal);
        Assert.Contains("ZoneType = NormalizeTeamLabZoneType(n.ZoneType)", content, StringComparison.Ordinal);
        Assert.Contains("NodeType = NormalizeTeamLabNodeType(n.NodeType)", content, StringComparison.Ordinal);
    }

    [Fact]
    public void TeamLab_Validation_AllowsExplicitMixedRfc1918Cidrs()
    {
        var content = File.ReadAllText(ResolveRepoPath("src/GZCTF/Services/PenetrationService.cs"));

        Assert.Contains("IsRfc1918Cidr", content, StringComparison.Ordinal);
        Assert.Contains("string.IsNullOrWhiteSpace(network.Cidr) &&", content, StringComparison.Ordinal);
        Assert.Contains("!ContainsCidr(sampleTeamNetwork, sampleTeamPrefix, networkAddress, networkPrefix)", content, StringComparison.Ordinal);
    }

    [Fact]
    public void TeamLab_AdminOneClickPreset_UsesDeployableRuntimeRoutesByDefault()
    {
        var content = File.ReadAllText(ResolveRepoPath("src/GZCTF/ClientApp/src/pages/admin/games/[id]/Penetration.tsx"));

        Assert.Contains("const edge = (id: number, sourceKey: string, targetKey: string, label: string, description: string, priority: number) => ({",
            content, StringComparison.Ordinal);
        Assert.Contains("enforcementMode: PenetrationEnforcementMode.Both", content, StringComparison.Ordinal);
        Assert.DoesNotContain("enforcementMode: PenetrationEnforcementMode.HintOnly,", content,
            StringComparison.Ordinal);
    }

    [Fact]
    public void TeamLab_RuntimeRouteCompilation_DoesNotRequireLegacyDualHomedRouterAsset()
    {
        var content = File.ReadAllText(ResolveRepoPath("src/GZCTF/Services/PenetrationService.cs"));

        Assert.Contains("TeamLab Fabric runtime route", content, StringComparison.Ordinal);
        Assert.DoesNotContain("FindRouteNode(config, interfacesByNode, pair.Source.Network.Id, pair.Target.Network.Id)", content,
            StringComparison.Ordinal);
        Assert.DoesNotContain("缺少同时连接两个网段且允许路由", content, StringComparison.Ordinal);
    }

    [Fact]
    public void TeamLab_AdminEditor_DoesNotOfferHintOnlyRouteMode()
    {
        var content = File.ReadAllText(ResolveRepoPath("src/GZCTF/ClientApp/src/pages/admin/games/[id]/Penetration.tsx"));

        Assert.DoesNotContain("PenetrationEnforcementMode.HintOnly", content, StringComparison.Ordinal);
        Assert.DoesNotContain("Object.values(PenetrationEnforcementMode)", content, StringComparison.Ordinal);
        Assert.DoesNotContain("同步作为题目路径线索", content, StringComparison.Ordinal);
    }

    [Fact]
    public void TeamLab_EdgeDefaults_AreDeployableRuntimeRoutes()
    {
        var requestModels = File.ReadAllText(ResolveRepoPath("src/GZCTF/Models/Request/Game/PenetrationModels.cs"));
        var dataModels = File.ReadAllText(ResolveRepoPath("src/GZCTF/Models/Data/PenetrationEntities.cs"));

        Assert.Contains("public PenetrationEnforcementMode EnforcementMode { get; set; } = PenetrationEnforcementMode.Both;", requestModels, StringComparison.Ordinal);
        Assert.Contains("public PenetrationEnforcementMode EnforcementMode { get; set; } = PenetrationEnforcementMode.Both;", dataModels, StringComparison.Ordinal);
    }

    [Fact]
    public void TeamLab_AdminBatchDeploy_DoesNotUseLegacyFabricDeploymentPath()
    {
        var content = File.ReadAllText(ResolveRepoPath("src/GZCTF/Services/PenetrationService.cs"));

        Assert.Contains("TeamLabDeploymentService", content, StringComparison.Ordinal);
        Assert.DoesNotContain("async Task<(bool Success, string Message)> DeployTeam(", content, StringComparison.Ordinal);
        Assert.DoesNotContain("AttachRuntimeFabricInterfaces", content, StringComparison.Ordinal);
        Assert.DoesNotContain("BuildContainerConfig(RuntimeNodePlan", content, StringComparison.Ordinal);
        Assert.DoesNotContain("ResolveImage(PenetrationNode", content, StringComparison.Ordinal);
        Assert.DoesNotContain("CheckFleetCapacity(", content, StringComparison.Ordinal);
    }

    [Fact]
    public void TeamLab_AdminBatchDeploy_DoesNotDestroyAlreadyDestroyedRuntime()
    {
        var content = File.ReadAllText(ResolveRepoPath("src/GZCTF/Services/PenetrationService.cs"));

        Assert.Contains("runtime.Status != TeamLabRuntimeStatus.Destroyed", content, StringComparison.Ordinal);
    }

    [Fact]
    public void TeamLab_RuntimeProjection_DoesNotReturnLegacyPublicAccessFields()
    {
        var content = File.ReadAllText(ResolveRepoPath("src/GZCTF/Services/PenetrationService.cs"));

        Assert.Contains("AdminAccessUrl = null", content, StringComparison.Ordinal);
        Assert.Contains("PublicPort = null", content, StringComparison.Ordinal);
        Assert.Contains("PublicHost = null", content, StringComparison.Ordinal);
        Assert.DoesNotContain("Url = url", content, StringComparison.Ordinal);
    }

    [Fact]
    public void TeamLab_PublishedTopology_ResetsLegacyEntryAndPublishFlags()
    {
        var content = File.ReadAllText(ResolveRepoPath("src/GZCTF/Services/TeamLab/TeamLabPublishedTopologyService.cs"));

        Assert.Contains("IsEntry = false", content, StringComparison.Ordinal);
        Assert.Contains("PublishPort = false", content, StringComparison.Ordinal);
    }

    static string ResolveRepoPath(string relativePath)
    {
        var current = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, relativePath);
            if (File.Exists(candidate))
                return candidate;

            current = current.Parent;
        }

        current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, relativePath);
            if (File.Exists(candidate))
                return candidate;

            current = current.Parent;
        }

        throw new FileNotFoundException($"Could not locate repository file {relativePath}.");
    }
}
