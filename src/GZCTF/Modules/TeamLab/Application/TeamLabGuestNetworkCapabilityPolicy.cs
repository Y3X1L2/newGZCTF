using System.Text.Json;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Modules.Runtime.Contracts;
using GZCTF.Modules.TeamLab.Domain;
using GZCTF.Modules.TeamLab.Domain.Runtime;
using GZCTF.Services.Fleet;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Modules.TeamLab.Application;

/// <summary>Host execution capabilities; guest tools are still checked during execution.</summary>
public static class TeamLabGuestNetworkCapabilityPolicy
{
    public static IReadOnlyList<string> AdvertisedFeatures(WorkerNode node) =>
        AgentCapabilityEvaluator.Parse(node.CapabilityManifestJson) is
            { ManifestSchemaVersion: AgentCapabilityEvaluator.SupportedManifestSchema } manifest ? manifest.Features : [];

    public static string[] RequiredFeatures(VmNetworkMode mode, OSType operatingSystem) =>
        mode != VmNetworkMode.ManagedStatic ? [] : operatingSystem == OSType.Windows
            ? [AgentFeatureIds.TeamLabManagedGuestNetwork]
            : [AgentFeatureIds.TeamLabManagedGuestNetwork, AgentFeatureIds.CloudInit];

    public static string[] ForAsset(TeamLabExecutionAsset? declared, TeamLabRuntimeAsset asset, ImageTemplate template)
    {
        if (asset.Kind != TeamLabResourceKind.Vm) return [];
        var frozen = string.IsNullOrWhiteSpace(asset.ExecutionPlanJson) ? null :
            JsonSerializer.Deserialize<TeamLabExecutionAsset>(asset.ExecutionPlanJson);
        return RequiredFeatures(declared?.VmNetworkMode ?? frozen?.VmNetworkMode ?? template.VmNetworkMode,
            template.OSType);
    }

    public static async Task<IReadOnlyDictionary<string, string[]>> LoadDeclaredAsync(
        AppDbContext context, TeamLabExecutionTopology definition, CancellationToken token)
    {
        var assets = definition.Assets.Where(asset => asset.Kind == TeamLabAssetKind.Vm).ToArray();
        var ids = assets.Select(asset => asset.ImageTemplateId).Distinct().ToArray();
        var templates = await context.ImageTemplates.AsNoTracking().Where(template => ids.Contains(template.Id))
            .ToDictionaryAsync(template => template.Id, token);
        return assets.ToDictionary(asset => asset.Key, asset =>
        {
            var template = templates.GetValueOrDefault(asset.ImageTemplateId);
            return RequiredFeatures(asset.VmNetworkMode ?? template?.VmNetworkMode ?? VmNetworkMode.Dhcp,
                template?.OSType ?? OSType.Linux);
        }, StringComparer.Ordinal);
    }

    public static async Task<IReadOnlyDictionary<string, string[]>> LoadRuntimeAsync(
        AppDbContext context, TeamLabRuntime runtime, IReadOnlyCollection<TeamLabRuntimeAsset> assets,
        CancellationToken token)
    {
        var release = await context.TeamLabTopologyReleases.AsNoTracking()
            .SingleAsync(item => item.Id == runtime.TopologyReleaseId, token);
        var source = TeamLabReleaseCodec.DecodeExecution(release.SchemaVersion, release.CanonicalJson);
        var byKey = source.Assets.ToDictionary(asset => asset.Key, StringComparer.Ordinal);
        var effective = assets.Where(asset => asset.Status != TeamLabRuntimeStatus.Destroyed).Select(asset =>
        {
            var definition = string.IsNullOrWhiteSpace(asset.ExecutionPlanJson)
                ? byKey.GetValueOrDefault(asset.TopologyKey)
                : JsonSerializer.Deserialize<TeamLabExecutionAsset>(asset.ExecutionPlanJson);
            return definition is null ? null : definition with
            { ImageTemplateId = asset.SourceTemplateId ?? definition.ImageTemplateId };
        }).Where(asset => asset is not null).Select(asset => asset!).ToArray();
        return await LoadDeclaredAsync(context, source with { Assets = effective }, token);
    }

    public static string? MissingReason(WorkerNode node, IReadOnlyCollection<string> required)
    {
        var missing = AgentCapabilityEvaluator.MissingFeatures(node, required.ToArray());
        return missing.Length == 0 ? null :
            $"teamlab_guest_network_capability_unavailable: 节点 {node.Name} 缺少 {string.Join("、", missing)}；请升级 Agent 或补齐宿主配置盘工具。";
    }
}
