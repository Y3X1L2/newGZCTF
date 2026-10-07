using System.Collections.Generic;
using GZCTF.Models.Data;
using GZCTF.Modules.Runtime.Application;
using GZCTF.Modules.Runtime.Domain;
using GZCTF.Modules.TeamLab.Application;
using GZCTF.Modules.TeamLab.Domain;
using Xunit;

namespace GZCTF.Test.UnitTests.TeamLab;

public sealed class TeamLabReleaseReadinessDiagnosticsTests
{
    [Fact]
    public void MultiNicVmGroupReportsPerNodeVmBudgetInsteadOfSummedStorage()
    {
        var execution = Execution(
            [Network("entry"), Network("internal")],
            [
                Vm("web", 0, [Interface("entry"), Interface("internal")]),
                Vm("dc", 1, [Interface("internal")]),
                Vm("oa", 2, [Interface("internal")]),
                Vm("windows", 3, [Interface("internal")])
            ]);
        var nodes = new[]
        {
            Snapshot("docker-only", NodeCapability.Docker, 0, 0, 161052),
            Snapshot("vm-small", NodeCapability.Kvm, 7168, 4)
        };

        var reason = TeamLabAdminQueryService.DescribePlanningBlocker(execution, nodes);

        Assert.Contains("当前合格节点无法容纳网络组 entry/internal", reason);
        Assert.Contains("该组需在同一节点放置", reason);
        Assert.Contains("存储 80 GiB (81920 MiB)", reason);
        Assert.Contains("docker-only：缺少 KVM 能力", reason);
        Assert.Contains("vm-small：CPU 16/8、内存 32768/14336 MiB、VM 空间 7 GiB (7168 MiB)/80 GiB (81920 MiB)", reason);
        Assert.Contains("空间不足", reason);
        Assert.DoesNotContain("161052", reason);
    }

    [Fact]
    public void MissingGuestNetworkFeatureIsNotReportedAsStorageShortage()
    {
        var execution = Execution([Network("entry")], [Vm("web", 0, [Interface("entry")])]);
        var reason = TeamLabAdminQueryService.DescribePlanningBlocker(execution,
            [Snapshot("vm-ready", NodeCapability.Kvm, 100 * 1024, 4)],
            new Dictionary<string, string[]> { ["web"] = ["teamlab-managed-guest-network"] });

        Assert.Contains("缺少网络能力 teamlab-managed-guest-network", reason);
        Assert.DoesNotContain("空间不足", reason);
    }

    [Fact]
    public void IndividuallyFitGroupsReportCombinedPlacementAsUnknown()
    {
        var execution = Execution([Network("first"), Network("second")],
            [Vm("first", 0, [Interface("first")]), Vm("second", 1, [Interface("second")])]);
        var reason = TeamLabAdminQueryService.DescribePlanningBlocker(execution,
            [Snapshot("one-slot", NodeCapability.Kvm, 100 * 1024, 1)]);

        Assert.Contains("无法完成该版本的整体放置", reason);
        Assert.DoesNotContain("资源不足", reason);
    }

    private static NodeCapacitySnapshot Snapshot(string name, NodeCapability capability, long storageMiB,
        int vmSlots, long? dockerStorageMiB = null) =>
        new(new WorkerNode
            {
                Name = name,
                Capabilities = capability,
                AutomaticCapacity = false,
                MaxContainers = 4,
                MaxVms = vmSlots
            }, 0, 0, 0, 0, 0, 0,
            new WorkloadResourceVector(16, 32768, storageMiB, 0, 0),
            DockerStorageTotalMiB: dockerStorageMiB);

    private static TeamLabExecutionTopology Execution(
        IReadOnlyList<TeamLabExecutionNetwork> networks,
        IReadOnlyList<TeamLabExecutionAsset> assets) =>
        new(2, "readiness", networks, [], assets, [], new TeamLabExecutionObservationPolicy(false, false));

    private static TeamLabExecutionNetwork Network(string key) =>
        new(key, key, "10.10.0.0/16", 24, key == "entry", 0);

    private static TeamLabExecutionInterface Interface(string network) =>
        new($"if-{network}", network, 2, true, 0);

    private static TeamLabExecutionAsset Vm(string key, int order, IReadOnlyList<TeamLabExecutionInterface> interfaces) =>
        new(key, key, TeamLabAssetKind.Vm, order + 1, 2, 3584, 20480, interfaces,
            null, null, null, order);
}
