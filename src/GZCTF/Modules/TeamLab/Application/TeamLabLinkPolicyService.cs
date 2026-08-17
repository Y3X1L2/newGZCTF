using System.Text.Json;
using GZCTF.Models;
using GZCTF.Modules.TeamLab.Contracts;
using GZCTF.Modules.TeamLab.Domain;
using GZCTF.Modules.TeamLab.Domain.Runtime;
using Microsoft.EntityFrameworkCore;
using static GZCTF.Modules.TeamLab.Application.TeamLabCapabilityResourceValidation;

namespace GZCTF.Modules.TeamLab.Application;

/// <summary>
/// Link and network policy control plane. Policies are declarative desired
/// state on a runtime link: apply is an idempotent upsert per
/// (network, asset, kind), recovery is manual, scheduled or implied by
/// runtime destruction, and the entity itself carries the audit trail.
/// </summary>
public sealed class TeamLabLinkPolicyService(AppDbContext context)
{
    private static readonly TeamLabRuntimeStatus[] NonApplicableStatuses =
    [
        TeamLabRuntimeStatus.CleanupPending, TeamLabRuntimeStatus.Destroying, TeamLabRuntimeStatus.Destroyed
    ];

    public async Task<TeamLabLinkPolicyModel> ApplyAsync(
        ApplyTeamLabLinkPolicyModel command,
        CancellationToken cancellationToken)
    {
        var runtime = await context.TeamLabRuntimes
            .Include(item => item.Networks)
            .Include(item => item.Assets)
            .SingleOrDefaultAsync(item => item.PublicId == command.RuntimeId, cancellationToken)
            ?? throw new TeamLabApiContractException("runtime_not_found", "未找到 TeamLab 运行时", 404);
        if (NonApplicableStatuses.Contains(runtime.Status))
            throw new TeamLabApiContractException("runtime_not_active", "运行时已在清理或销毁流程中，无法应用链路策略", 409);
        if (!TeamLabCapabilityResourceContractMapper.TryParseLinkPolicyKind(command.Kind, out var kind))
            throw new TeamLabApiContractException("link_policy_kind_invalid", "链路策略类型无效", 422);
        var networkKey = Slug(command.NetworkKey, 64, "link_policy_network_invalid", "链路策略网段标识无效");
        if (!runtime.Networks.Any(network => network.Generation == runtime.Generation && network.TopologyKey == networkKey))
            throw new TeamLabApiContractException("link_policy_network_unknown", "链路策略网段不属于该运行时", 422);
        string? assetKey = null;
        if (!string.IsNullOrWhiteSpace(command.AssetKey))
        {
            assetKey = Slug(command.AssetKey, 64, "link_policy_asset_invalid", "链路策略资产标识无效");
            if (!runtime.Assets.Any(asset => asset.Generation == runtime.Generation && asset.TopologyKey == assetKey))
                throw new TeamLabApiContractException("link_policy_asset_unknown", "链路策略资产不属于该运行时", 422);
        }
        var parameters = TeamLabLinkPolicyParameters.Validate(kind, command.Parameters);
        if (command.RecoverAt is { } recoverAt && recoverAt <= DateTimeOffset.UtcNow)
            throw new TeamLabApiContractException("link_policy_recover_at_invalid", "定时恢复时间必须晚于当前时间", 422);

        var existing = await context.TeamLabLinkPolicies.SingleOrDefaultAsync(
            policy => policy.RuntimeId == runtime.Id && policy.NetworkKey == networkKey &&
                      policy.AssetKey == assetKey && policy.Kind == kind &&
                      policy.Status == TeamLabLinkPolicyStatus.Active,
            cancellationToken);
        if (existing is not null)
        {
            if (existing.ParametersJson != parameters)
                throw new TeamLabApiContractException(
                    "link_policy_conflict", "同一条链路已有不同参数的活动策略，请先恢复后再应用", 409);
            existing.RecoverAt = command.RecoverAt;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
            await context.SaveChangesAsync(cancellationToken);
            return ToModel(existing, runtime.PublicId);
        }

        var policy = new TeamLabLinkPolicy
        {
            RuntimeId = runtime.Id,
            ControlScopeId = runtime.ControlScopeId,
            NetworkKey = networkKey,
            AssetKey = assetKey,
            Kind = kind,
            ParametersJson = parameters,
            RecoverAt = command.RecoverAt
        };
        context.TeamLabLinkPolicies.Add(policy);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // A concurrent apply won the unique active-policy slot with
            // identical semantics; converge to the winner instead of failing.
            var winner = await context.TeamLabLinkPolicies.AsNoTracking().SingleOrDefaultAsync(
                candidate => candidate.RuntimeId == runtime.Id && candidate.NetworkKey == networkKey &&
                             candidate.AssetKey == assetKey && candidate.Kind == kind &&
                             candidate.Status == TeamLabLinkPolicyStatus.Active,
                cancellationToken);
            if (winner is null || winner.ParametersJson != parameters)
                throw new TeamLabApiContractException(
                    "link_policy_conflict", "同一条链路已有不同参数的活动策略，请先恢复后再应用", 409);
            return ToModel(winner, runtime.PublicId);
        }
        return ToModel(policy, runtime.PublicId);
    }

    public async Task<TeamLabLinkPolicyModel> RecoverAsync(Guid policyId, CancellationToken cancellationToken)
    {
        var policy = await context.TeamLabLinkPolicies
            .SingleOrDefaultAsync(item => item.PublicId == policyId, cancellationToken)
            ?? throw new TeamLabApiContractException("link_policy_not_found", "未找到链路策略", 404);
        var runtimePublicId = await context.TeamLabRuntimes.AsNoTracking()
            .Where(runtime => runtime.Id == policy.RuntimeId)
            .Select(runtime => runtime.PublicId)
            .SingleAsync(cancellationToken);
        if (policy.Status == TeamLabLinkPolicyStatus.Recovered) return ToModel(policy, runtimePublicId);
        policy.Status = TeamLabLinkPolicyStatus.Recovered;
        policy.RecoveredAt = DateTimeOffset.UtcNow;
        policy.RecoverOrigin = TeamLabLinkPolicyRecoverOrigin.Manual;
        policy.RecoverAt = null;
        policy.UpdatedAt = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(cancellationToken);
        return ToModel(policy, runtimePublicId);
    }

    public async Task<TeamLabLinkPolicyPageModel> ListByRuntimeAsync(
        Guid runtimeId,
        string? status,
        string? after,
        int limit,
        CancellationToken cancellationToken)
    {
        var runtime = await context.TeamLabRuntimes.AsNoTracking()
            .SingleOrDefaultAsync(item => item.PublicId == runtimeId, cancellationToken)
            ?? throw new TeamLabApiContractException("runtime_not_found", "未找到 TeamLab 运行时", 404);
        var statusFilter = string.IsNullOrWhiteSpace(status) ? null : status.Trim().ToLowerInvariant();
        if (statusFilter is not null and not ("active" or "recovered" or "failed"))
            throw new TeamLabApiContractException("link_policy_status_invalid", "状态筛选必须是 active、recovered 或 failed", 422);
        var cursor = DecodeIntCursor(after, "link_policy_cursor_invalid", "链路策略 cursor 无效");
        var take = Math.Clamp(limit, 1, 100);
        var query = context.TeamLabLinkPolicies.AsNoTracking()
            .Where(policy => policy.RuntimeId == runtime.Id);
        query = statusFilter switch
        {
            "recovered" => query.Where(policy => policy.Status == TeamLabLinkPolicyStatus.Recovered),
            "failed" => query.Where(policy => policy.Status == TeamLabLinkPolicyStatus.Failed),
            "active" => query.Where(policy => policy.Status == TeamLabLinkPolicyStatus.Active),
            _ => query.Where(policy => policy.Status != TeamLabLinkPolicyStatus.Recovered)
        };
        if (cursor is not null) query = query.Where(policy => policy.Id > cursor);
        var rows = await query.OrderBy(policy => policy.Id).Take(take + 1).ToArrayAsync(cancellationToken);
        return new TeamLabLinkPolicyPageModel(
            rows.Take(take).Select(policy => ToModel(policy, runtime.PublicId)).ToArray(),
            rows.Length > take ? EncodeIntCursor(rows[take - 1].Id) : null);
    }

    /// <summary>Recovers policies whose scheduled recovery time has passed; returns the recovered count.</summary>
    public Task<int> RecoverDueAsync(int batchLimit, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        return context.TeamLabLinkPolicies
            .Where(policy => policy.Status == TeamLabLinkPolicyStatus.Active &&
                             policy.RecoverAt != null && policy.RecoverAt <= now)
            .OrderBy(policy => policy.RecoverAt)
            .Take(batchLimit)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(policy => policy.Status, TeamLabLinkPolicyStatus.Recovered)
                .SetProperty(policy => policy.RecoveredAt, now)
                .SetProperty(policy => policy.RecoverOrigin, TeamLabLinkPolicyRecoverOrigin.Scheduled)
                .SetProperty(policy => policy.RecoverAt, (DateTimeOffset?)null)
                .SetProperty(policy => policy.UpdatedAt, now), cancellationToken);
    }

    /// <summary>Closes the active policies of destroyed runtimes; returns the closed count.</summary>
    public async Task<int> CloseDestroyedRuntimePoliciesAsync(int batchLimit, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var policyIds = await context.TeamLabLinkPolicies
            .Where(policy => policy.Status == TeamLabLinkPolicyStatus.Active &&
                             context.TeamLabRuntimes.Any(runtime =>
                                 runtime.Id == policy.RuntimeId && runtime.Status == TeamLabRuntimeStatus.Destroyed))
            .OrderBy(policy => policy.Id)
            .Take(batchLimit)
            .Select(policy => policy.Id)
            .ToArrayAsync(cancellationToken);
        if (policyIds.Length == 0) return 0;
        return await context.TeamLabLinkPolicies
            .Where(policy => policyIds.Contains(policy.Id))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(policy => policy.Status, TeamLabLinkPolicyStatus.Recovered)
                .SetProperty(policy => policy.RecoveredAt, now)
                .SetProperty(policy => policy.RecoverOrigin, TeamLabLinkPolicyRecoverOrigin.RuntimeDestroyed)
                .SetProperty(policy => policy.RecoverAt, (DateTimeOffset?)null)
                .SetProperty(policy => policy.UpdatedAt, now), cancellationToken);
    }

    internal static TeamLabLinkPolicyModel ToModel(TeamLabLinkPolicy policy, Guid runtimePublicId) => new(
        policy.PublicId,
        runtimePublicId,
        policy.NetworkKey,
        policy.AssetKey,
        TeamLabCapabilityResourceContractMapper.LinkPolicyKindName(policy.Kind),
        ParseJson(policy.ParametersJson),
        TeamLabCapabilityResourceContractMapper.LinkPolicyStatusName(policy.Status),
        policy.RecoverAt,
        policy.AppliedAt,
        policy.RecoveredAt,
        TeamLabCapabilityResourceContractMapper.LinkPolicyRecoverOriginName(policy.RecoverOrigin),
        policy.LastError);
}

/// <summary>
/// Per-kind parameter validation producing the canonical stored form.
/// Everything outside the declared fields is rejected so two semantically
/// equal requests always canonicalize to the same string.
/// </summary>
internal static class TeamLabLinkPolicyParameters
{
    private static readonly string[] Directions = ["inbound", "outbound", "both"];
    private static readonly string[] Actions = ["allow", "deny"];
    private static readonly string[] Protocols = ["tcp", "udp", "icmp", "any"];
    private static readonly string[] NatModes = ["snat", "dnat"];

    public static string Validate(TeamLabLinkPolicyKind kind, JsonElement? input)
    {
        if (input is not { } element || element.ValueKind is not JsonValueKind.Object)
            throw ParametersInvalid("参数必须是 JSON 对象");
        var values = ToValueDictionary(element);
        var canonical = kind switch
        {
            TeamLabLinkPolicyKind.Latency => BoundedNumber(values, "delayMillis", 1, 10_000),
            TeamLabLinkPolicyKind.Jitter => BoundedNumber(values, "jitterMillis", 0, 5_000),
            TeamLabLinkPolicyKind.PacketLoss => BoundedNumber(values, "lossPercent", 0, 100),
            TeamLabLinkPolicyKind.Duplication => BoundedNumber(values, "duplicatePercent", 0, 100),
            TeamLabLinkPolicyKind.BandwidthLimit => BandwidthLimit(values),
            TeamLabLinkPolicyKind.LinkBreak => "{}",
            TeamLabLinkPolicyKind.AccessRule => AccessRule(values),
            TeamLabLinkPolicyKind.Nat => Nat(values),
            _ => throw ParametersInvalid("未知的链路策略类型")
        };
        return canonical.Length > 1024
            ? throw ParametersInvalid("参数超出长度限制")
            : canonical;
    }

    private static string BoundedNumber(Dictionary<string, JsonElement> values, string key, double minimum, double maximum)
    {
        _ = RequiredNumber(values, key, "link_policy_parameters_invalid", $"参数 {key} 缺失或不是数字");
        if (values[key].TryGetDouble(out var value) && (value < minimum || value > maximum))
            throw ParametersInvalid($"参数 {key} 必须在 {minimum}-{maximum} 范围内");
        return JsonSerializer.Serialize(new Dictionary<string, JsonElement> { [key] = values[key] });
    }

    private static string BandwidthLimit(Dictionary<string, JsonElement> values)
    {
        _ = RequiredNumber(values, "rateMbps", "link_policy_parameters_invalid", "参数 rateMbps 缺失或不是数字");
        if (values["rateMbps"].TryGetDouble(out var rate) && (rate < 0.1 || rate > 100_000))
            throw ParametersInvalid("参数 rateMbps 必须在 0.1-100000 范围内");
        var canonical = new Dictionary<string, JsonElement>
        {
            ["rateMbps"] = values["rateMbps"]
        };
        if (values.TryGetValue("burstKilobytes", out var burst) && burst.ValueKind == JsonValueKind.Number)
        {
            if (!burst.TryGetDouble(out var burstValue) || burstValue < 0 || burstValue > 2_097_152)
                throw ParametersInvalid("参数 burstKilobytes 必须在 0-2097152 范围内");
            canonical["burstKilobytes"] = burst;
        }
        return JsonSerializer.Serialize(canonical);
    }

    private static string AccessRule(Dictionary<string, JsonElement> values)
    {
        var canonical = new Dictionary<string, JsonElement>
        {
            ["direction"] = JsonSerializer.SerializeToElement(
                RequiredEnum(values, "direction", "link_policy_parameters_invalid", "direction 必须是 inbound、outbound 或 both", Directions)),
            ["action"] = JsonSerializer.SerializeToElement(
                RequiredEnum(values, "action", "link_policy_parameters_invalid", "action 必须是 allow 或 deny", Actions))
        };
        var protocol = values.TryGetValue("protocol", out var protocolElement) &&
                       protocolElement.ValueKind == JsonValueKind.String &&
                       !string.IsNullOrWhiteSpace(protocolElement.GetString())
            ? RequiredEnum(values, "protocol", "link_policy_parameters_invalid", "protocol 必须是 tcp、udp、icmp 或 any", Protocols)
            : "any";
        canonical["protocol"] = JsonSerializer.SerializeToElement(protocol);
        if (OptionalCidr(values, "sourceCidr", "link_policy_parameters_invalid") is { } sourceCidr)
            canonical["sourceCidr"] = JsonSerializer.SerializeToElement(sourceCidr);
        if (OptionalCidr(values, "destinationCidr", "link_policy_parameters_invalid") is { } destinationCidr)
            canonical["destinationCidr"] = JsonSerializer.SerializeToElement(destinationCidr);
        if (OptionalNumber(values, "priority") is { } priority)
        {
            if (priority < 0 || priority > 1000 || priority != Math.Floor(priority))
                throw ParametersInvalid("priority 必须是 0-1000 的整数");
            canonical["priority"] = values["priority"];
        }
        return JsonSerializer.Serialize(canonical);
    }

    private static string Nat(Dictionary<string, JsonElement> values)
    {
        var mode = RequiredEnum(values, "mode", "link_policy_parameters_invalid", "mode 必须是 snat 或 dnat", NatModes);
        var canonical = new Dictionary<string, JsonElement>
        {
            ["mode"] = JsonSerializer.SerializeToElement(mode)
        };
        if (mode == "snat")
        {
            if (OptionalAddress(values, "translatedAddress", "link_policy_parameters_invalid") is not { } address)
                throw ParametersInvalid("snat 必须声明 translatedAddress");
            canonical["translatedAddress"] = JsonSerializer.SerializeToElement(address);
            return JsonSerializer.Serialize(canonical);
        }
        _ = RequiredPort(values, "externalPort", "link_policy_parameters_invalid");
        canonical["externalPort"] = values["externalPort"];
        if (OptionalAddress(values, "internalAddress", "link_policy_parameters_invalid") is not { } internalAddress)
            throw ParametersInvalid("dnat 必须声明 internalAddress");
        canonical["internalAddress"] = JsonSerializer.SerializeToElement(internalAddress);
        if (values.TryGetValue("internalPort", out var internalPort) && internalPort.ValueKind == JsonValueKind.Number)
        {
            _ = RequiredPort(values, "internalPort", "link_policy_parameters_invalid");
            canonical["internalPort"] = internalPort;
        }
        return JsonSerializer.Serialize(canonical);
    }

    private static TeamLabApiContractException ParametersInvalid(string message) =>
        new("link_policy_parameters_invalid", message, 422);
}
