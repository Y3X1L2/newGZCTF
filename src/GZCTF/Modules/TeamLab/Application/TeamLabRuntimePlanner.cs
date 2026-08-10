using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Diagnostics;
using GZCTF.Infrastructure.Telemetry;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Modules.Audit.Application;
using GZCTF.Modules.Audit.Domain;
using GZCTF.Modules.TeamLab.Contracts;
using GZCTF.Modules.TeamLab.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GZCTF.Modules.TeamLab.Application;

public sealed class TeamLabRuntimePlanner(
    AppDbContext context,
    TeamLabRuntimeOverlayService overlayService,
    TeamLabEventRecorder eventRecorder)
{
    public async Task<TeamLabRuntimeCreateResult> CreateAsync(
        CreateTeamLabRuntimeModel command,
        Guid runtimeOwnerUserId,
        string requestHash,
        string? creationIdempotencyKey,
        Func<TeamLabRuntime, CancellationToken, Task>? admitPlannedRuntime,
        CancellationToken cancellationToken)
    {
        var release = await context.TeamLabTopologyReleases.AsNoTracking()
            .Include(item => item.Topology)
            .SingleOrDefaultAsync(item => item.Id == command.ReleaseId, cancellationToken)
            ?? throw new TeamLabApiContractException("release_not_found", "未找到拓扑版本", 404);
        if (release.Topology.OwnerUserId != runtimeOwnerUserId)
            throw new TeamLabApiContractException(
                "insufficient_permission",
                "拓扑版本不属于运行时所有者",
                403);

        var normalizedIdempotencyKey = string.IsNullOrWhiteSpace(creationIdempotencyKey)
            ? null
            : ExternalIdempotencyKey.Normalize(creationIdempotencyKey);
        if (normalizedIdempotencyKey is not null)
        {
            var existing = await context.TeamLabRuntimes.AsNoTracking()
                .SingleOrDefaultAsync(item => item.CreatedById == runtimeOwnerUserId &&
                                              item.CreationIdempotencyKey == normalizedIdempotencyKey,
                    cancellationToken);
            if (existing is not null)
            {
                if (!string.Equals(existing.CreateRequestHash, requestHash, StringComparison.Ordinal))
                    throw new IdempotencyConflictException();
                return new TeamLabRuntimeCreateResult(existing.Id, existing.PublicId, true);
            }
        }

        var externalReference = NormalizeExternalReference(command.ExternalReference);
        if (externalReference is not null)
        {
            var existing = await context.TeamLabRuntimes.AsNoTracking()
                .SingleOrDefaultAsync(item => item.CreatedById == runtimeOwnerUserId && item.ExternalReference == externalReference,
                    cancellationToken);
            if (existing is not null)
            {
                if (existing.Status == TeamLabRuntimeStatus.Destroyed)
                    return await ResetAndAdmitAsync(
                        existing.PublicId,
                        command.Overlays,
                        command.ReleaseId,
                        requestHash,
                        admitPlannedRuntime,
                        cancellationToken);
                if (!string.Equals(existing.CreateRequestHash, requestHash, StringComparison.Ordinal))
                    throw new TeamLabApiContractException(
                        "external_reference_conflict",
                        "外部引用已被其他运行时请求占用",
                        409);
                return new TeamLabRuntimeCreateResult(existing.Id, existing.PublicId, true);
            }
        }

        return await CreatePlannedRuntimeAsync(
            release,
            runtimeOwnerUserId,
            externalReference,
            normalizedIdempotencyKey,
            requestHash,
            command.Overlays,
            admitPlannedRuntime: admitPlannedRuntime,
            cancellationToken: cancellationToken);
    }

    private async Task<TeamLabRuntimeCreateResult> CreatePlannedRuntimeAsync(
        TeamLabTopologyRelease release,
        Guid runtimeOwnerUserId,
        string? externalReference,
        string? creationIdempotencyKey,
        string requestHash,
        IReadOnlyList<TeamLabRuntimeOverlayModel>? runtimeOverlays,
        Func<TeamLabRuntime, CancellationToken, Task>? admitPlannedRuntime,
        CancellationToken cancellationToken)
    {
        var definition = TeamLabReleaseCodec.DecodeExecution(release.SchemaVersion, release.CanonicalJson);
        await TeamLabTopologyApplicationService.ValidateImageTemplatesAsync(context, definition, cancellationToken);
        try
        {
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            var runtime = new TeamLabRuntime
            {
                TopologyReleaseId = release.Id,
                ControlScopeId = release.ControlScopeId,
                CreatedById = runtimeOwnerUserId,
                ExternalReference = externalReference,
                CreationIdempotencyKey = creationIdempotencyKey,
                CreateRequestHash = requestHash,
                Generation = 1,
                Status = TeamLabRuntimeStatus.Planning,
                IsOpenToPlayers = false,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            context.TeamLabRuntimes.Add(runtime);
            await context.SaveChangesAsync(cancellationToken);

            await PlanGenerationAsync(
                runtime,
                definition,
                runtimeOverlays,
                cancellationToken);
            if (admitPlannedRuntime is not null)
                await admitPlannedRuntime(runtime, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new TeamLabRuntimeCreateResult(runtime.Id, runtime.PublicId, false);
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.ExclusionViolation })
        {
            throw new TeamLabApiContractException(
                "address_pool_exhausted", "并发地址分配已耗尽地址池", 409);
        }
        catch (DbUpdateException exception) when (
            creationIdempotencyKey is not null &&
            exception.InnerException is PostgresException idempotencyPostgres &&
            idempotencyPostgres.SqlState == PostgresErrorCodes.UniqueViolation &&
            idempotencyPostgres.ConstraintName?.Contains(
                "CreationIdempotencyKey", StringComparison.OrdinalIgnoreCase) == true)
        {
            context.ChangeTracker.Clear();
            var existing = await context.TeamLabRuntimes.AsNoTracking()
                .SingleOrDefaultAsync(item => item.CreatedById == runtimeOwnerUserId &&
                                              item.CreationIdempotencyKey == creationIdempotencyKey,
                    cancellationToken);
            if (existing is null) throw;
            if (!string.Equals(existing.CreateRequestHash, requestHash, StringComparison.Ordinal))
                throw new IdempotencyConflictException();
            return new TeamLabRuntimeCreateResult(existing.Id, existing.PublicId, true);
        }
        catch (DbUpdateException exception) when (
            externalReference is not null &&
            exception.InnerException is PostgresException postgres &&
            postgres.SqlState == PostgresErrorCodes.UniqueViolation &&
            postgres.ConstraintName?.Contains(
                "CreatedById_ExternalReference", StringComparison.OrdinalIgnoreCase) == true)
        {
            context.ChangeTracker.Clear();
            var existing = await context.TeamLabRuntimes.AsNoTracking()
                .SingleOrDefaultAsync(item => item.CreatedById == runtimeOwnerUserId &&
                                              item.ExternalReference == externalReference,
                    cancellationToken);
            if (existing is null) throw;
            if (existing.Status == TeamLabRuntimeStatus.Destroyed)
                return await ResetAndAdmitAsync(
                    existing.PublicId,
                    runtimeOverlays,
                    release.Id,
                    requestHash,
                    admitPlannedRuntime,
                    cancellationToken);
            if (!string.Equals(existing.CreateRequestHash, requestHash, StringComparison.Ordinal))
                throw new TeamLabApiContractException(
                    "external_reference_conflict",
                    "The external reference is already used by a different runtime request.",
                    409);
            return new TeamLabRuntimeCreateResult(existing.Id, existing.PublicId, true);
        }
    }

    public Task<TeamLabRuntimeCreateResult> ResetAsync(
        Guid runtimePublicId,
        IReadOnlyList<TeamLabRuntimeOverlayModel>? runtimeOverlays,
        Guid? targetReleaseId,
        string? createRequestHash,
        CancellationToken cancellationToken) =>
        ResetCoreAsync(
            runtimePublicId,
            runtimeOverlays,
            targetReleaseId,
            createRequestHash,
            admitPlannedRuntime: null,
            cancellationToken: cancellationToken);

    private Task<TeamLabRuntimeCreateResult> ResetAndAdmitAsync(
        Guid runtimePublicId,
        IReadOnlyList<TeamLabRuntimeOverlayModel>? runtimeOverlays,
        Guid? targetReleaseId,
        string? createRequestHash,
        Func<TeamLabRuntime, CancellationToken, Task>? admitPlannedRuntime,
        CancellationToken cancellationToken) =>
        ResetCoreAsync(
            runtimePublicId,
            runtimeOverlays,
            targetReleaseId,
            createRequestHash,
            admitPlannedRuntime: admitPlannedRuntime,
            cancellationToken: cancellationToken);

    private async Task<TeamLabRuntimeCreateResult> ResetCoreAsync(
        Guid runtimePublicId,
        IReadOnlyList<TeamLabRuntimeOverlayModel>? runtimeOverlays,
        Guid? targetReleaseId,
        string? createRequestHash,
        Func<TeamLabRuntime, CancellationToken, Task>? admitPlannedRuntime,
        CancellationToken cancellationToken)
    {
        var runtime = await context.TeamLabRuntimes
            .Include(item => item.PublicUdpMapping)
            .Include(item => item.Shards)
            .Include(item => item.Networks)
            .Include(item => item.Assets)
            .Include(item => item.Infrastructure)
            .Include(item => item.DependencyStates)
            .Include(item => item.SecretEnvelopes)
            .Include(item => item.Events)
            .SingleOrDefaultAsync(item => item.PublicId == runtimePublicId, cancellationToken)
            ?? throw new TeamLabApiContractException("runtime_not_found", "未找到 TeamLab 运行时", 404);
        if (runtime.Status != TeamLabRuntimeStatus.Destroyed)
            throw new TeamLabApiContractException("runtime_cleanup_pending", "当前运行时 generation 尚未完全清理", 409);
        var releaseId = targetReleaseId ?? runtime.TopologyReleaseId;
        if (releaseId == Guid.Empty)
            throw new TeamLabApiContractException("runtime_invalid", "运行时未关联拓扑版本", 500);
        var release = await context.TeamLabTopologyReleases.AsNoTracking()
            .Include(item => item.Topology)
            .SingleOrDefaultAsync(item => item.Id == releaseId, cancellationToken)
            ?? throw new TeamLabApiContractException("release_not_found", "未找到拓扑版本", 404);
        if (release.Topology.OwnerUserId != runtime.CreatedById)
            throw new TeamLabApiContractException(
                "insufficient_permission",
                "目标拓扑版本不属于运行时所有者",
                403);
        var definition = TeamLabReleaseCodec.DecodeExecution(release.SchemaVersion, release.CanonicalJson);
        await TeamLabTopologyApplicationService.ValidateImageTemplatesAsync(context, definition, cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        runtime.Generation++;
        runtime.TopologyReleaseId = release.Id;
        runtime.ControlScopeId = release.ControlScopeId;
        if (createRequestHash is not null) runtime.CreateRequestHash = createRequestHash;
        runtime.EntryShardId = null;
        runtime.Status = TeamLabRuntimeStatus.Planning;
        runtime.LastError = null;
        runtime.UpdatedAt = DateTimeOffset.UtcNow;
        await PlanGenerationAsync(
            runtime,
            definition,
            runtimeOverlays,
            cancellationToken);
        if (admitPlannedRuntime is not null)
            await admitPlannedRuntime(runtime, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new TeamLabRuntimeCreateResult(runtime.Id, runtime.PublicId, false);
    }

    private async Task PlanGenerationAsync(
        TeamLabRuntime runtime,
        TeamLabExecutionTopology definition,
        IReadOnlyList<TeamLabRuntimeOverlayModel>? runtimeOverlays,
        CancellationToken cancellationToken)
    {
        using var activity = PlatformTelemetry.TeamLabActivitySource.StartActivity(
            "teamlab.plan", ActivityKind.Internal);
        activity?.SetTag("gzctf.teamlab_runtime_id", runtime.Id);
        activity?.SetTag("teamlab.generation", runtime.Generation);
        eventRecorder.Record(
            runtime,
            "planning",
            TeamLabEventLevel.Info,
            OperationalEventCodes.TeamLab.PlanStarted,
            OperationalEventOutcome.Started,
            "TeamLab runtime planning started.");
        var groups = TeamLabAssetPlanner.BuildGroups(definition);
        var groupByNetwork = groups.SelectMany(group => group.NetworkKeys.Select(key => (key, group.Key)))
            .ToDictionary(item => item.key, item => item.Key, StringComparer.Ordinal);
        var groupByAsset = groups.SelectMany(group => group.AssetKeys.Select(key => (key, group.Key)))
            .ToDictionary(item => item.key, item => item.Key, StringComparer.Ordinal);
        if (context.Database.IsRelational())
        {
            const string networkLeaseLock = "teamlab:network-lease-allocation";
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtextextended({networkLeaseLock}, 0))",
                cancellationToken);
        }
        var usedCidrs = await context.TeamLabNetworkLeases.AsNoTracking()
            .Where(item => item.ReleasedAt == null)
            .Select(item => item.AllocatedCidr)
            .ToArrayAsync(cancellationToken);
        var allocated = new List<IPNetwork>(definition.Networks.Count);
        var runtimeNetworkByKey = new Dictionary<string, TeamLabRuntimeNetwork>(StringComparer.Ordinal);
        var resolvedTemplateIds = definition.Assets.ToDictionary(
            item => item.Key,
            item => item.ImageTemplateId,
            StringComparer.Ordinal);
        var templateIds = resolvedTemplateIds.Values.Distinct().ToArray();
        var templateDigests = await context.ImageTemplates.AsNoTracking()
            .Where(item => templateIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, item => item.ImageHash, cancellationToken);
        foreach (var asset in definition.Assets)
        {
            var resolvedTemplateId = resolvedTemplateIds[asset.Key];
            var expectedDigest = asset.ImageDigest;
            if (!templateDigests.TryGetValue(resolvedTemplateId, out var currentDigest) ||
                string.IsNullOrWhiteSpace(currentDigest))
                throw new TeamLabApiContractException(
                    "image_template_unavailable",
                    $"资源 '{asset.Key}' 解析到的镜像模板 {resolvedTemplateId} 不可用",
                    409);
            if (!string.IsNullOrWhiteSpace(expectedDigest) &&
                !string.Equals(expectedDigest, currentDigest, StringComparison.Ordinal))
                throw new TeamLabApiContractException(
                    "image_template_digest_changed",
                    $"资源 '{asset.Key}' 解析到的镜像模板 {resolvedTemplateId} 与已发布 digest 不匹配",
                    409);
        }
        foreach (var network in definition.Networks.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            var cidr = Allocate(network.AddressPoolCidr, network.RuntimePrefixLength, usedCidrs.Concat(allocated));
            if (cidr is null)
                throw new TeamLabApiContractException("address_pool_exhausted", $"网络 '{network.Key}' 的地址池已耗尽", 409);
            allocated.Add(cidr.Value);
            var runtimeNetwork = new TeamLabRuntimeNetwork
            {
                RuntimeId = runtime.Id,
                Generation = runtime.Generation,
                PlacementGroupKey = groupByNetwork[network.Key],
                IsEntry = network.IsEntry,
                TopologyKey = network.Key,
                Name = network.Name,
                Cidr = cidr.Value.ToString(),
                GatewayIp = HostAt(cidr.Value, 1),
                BridgeName = TeamLabResourceNameFactory.Bridge(runtime.Id, network.Key),
                NetworkLease = new TeamLabNetworkLease
                {
                    RuntimeId = runtime.Id,
                    Generation = runtime.Generation,
                    TopologyReleaseId = runtime.TopologyReleaseId,
                    NetworkKey = network.Key,
                    AllocatedCidr = cidr.Value
                }
            };
            runtime.Networks.Add(runtimeNetwork);
            runtimeNetworkByKey[network.Key] = runtimeNetwork;
        }
        foreach (var asset in definition.Assets.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            var interfaces = asset.Interfaces.OrderBy(item => item.DisplayOrder).Select(iface =>
            {
                var network = runtimeNetworkByKey[iface.NetworkKey];
                var parsed = ParseNetwork(network.Cidr);
                return new RuntimeInterfaceIntent(iface.Key, iface.NetworkKey, HostAt(parsed, iface.HostOffset),
                    parsed.PrefixLength, MacAddress(runtime.Id, asset.Key, iface.Key), iface.Primary);
            }).ToArray();
            var primary = interfaces.Single(item => item.Primary);
            runtime.Assets.Add(new TeamLabRuntimeAsset
            {
                RuntimeId = runtime.Id,
                Generation = runtime.Generation,
                PlacementGroupKey = groupByAsset[asset.Key],
                Kind = asset.Kind == TeamLabAssetKind.Docker ? TeamLabResourceKind.Docker : TeamLabResourceKind.Vm,
                TopologyKey = asset.Key,
                Name = asset.Name,
                SourceTemplateId = resolvedTemplateIds[asset.Key],
                NetworkKey = primary.NetworkKey,
                IpAddress = primary.IpAddress,
                MacAddress = primary.MacAddress,
                InterfaceSummaryJson = JsonSerializer.Serialize(interfaces),
                Status = TeamLabRuntimeStatus.Pending,
                ExecutionStage = TeamLabAssetExecutionStage.Pending,
                EndpointObservation = asset.EndpointObservation,
                ImageDigest = asset.ImageDigest ?? templateDigests.GetValueOrDefault(resolvedTemplateIds[asset.Key])
            });
        }
        var connectionsByNode = definition.Connections
            .Where(item => item.ViaNodeKey is not null)
            .GroupBy(item => item.ViaNodeKey!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        foreach (var infrastructure in definition.Infrastructure.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            var interfaces = infrastructure.Interfaces.Select(item =>
                new TeamLabRuntimeInfrastructureInterfaceIntent(
                    item.Key, item.NetworkKey, item.HostOffset, item.Primary)).ToArray();
            var connections = connectionsByNode.GetValueOrDefault(infrastructure.Key) ?? [];
            runtime.Infrastructure.Add(new TeamLabRuntimeInfrastructure
            {
                RuntimeId = runtime.Id,
                Generation = runtime.Generation,
                TopologyKey = infrastructure.Key,
                Name = infrastructure.Name,
                Kind = infrastructure.Kind,
                NetworkKey = infrastructure.NetworkKey,
                InterfaceSummaryJson = JsonSerializer.Serialize(interfaces),
                ConnectionSummaryJson = JsonSerializer.Serialize(connections.Select(item =>
                    new TeamLabRuntimeInfrastructureConnectionIntent(
                        item.FromNetworkKey, item.ToNetworkKey, item.Direction)).ToArray()),
                Status = TeamLabRuntimeStatus.Pending
            });
        }
        foreach (var dependency in definition.Dependencies)
        {
            runtime.DependencyStates.Add(new TeamLabRuntimeDependencyState
            {
                RuntimeId = runtime.Id,
                Generation = runtime.Generation,
                AssetKey = dependency.AssetKey,
                DependsOnKey = dependency.DependsOnKey,
                Condition = dependency.Condition
            });
        }
        var envelope = overlayService.Protect(runtime.Id, runtime.Generation, runtimeOverlays,
            definition.Assets.Select(item => item.Key).ToHashSet(StringComparer.Ordinal),
            definition.Assets
                .Where(item => item.EndpointObservation != TeamLabEndpointObservationMode.Disabled)
                .Select(item => item.Key)
                .ToHashSet(StringComparer.Ordinal));
        if (envelope is not null) runtime.SecretEnvelopes.Add(envelope);
        runtime.Status = TeamLabRuntimeStatus.Scheduled;
        eventRecorder.Record(
            runtime,
            "planning",
            TeamLabEventLevel.Success,
            OperationalEventCodes.TeamLab.PlanSucceeded,
            OperationalEventOutcome.Succeeded,
            $"Runtime generation {runtime.Generation} logical network groups compiled; physical nodes will be assigned by the scheduler.");
        await context.SaveChangesAsync(cancellationToken);
        activity?.SetStatus(ActivityStatusCode.Ok);
    }

    private static IPNetwork? Allocate(string poolCidr, int runtimePrefix, IEnumerable<IPNetwork> unavailable)
    {
        var pool = ParseNetwork(poolCidr);
        var used = unavailable.Select(ToRange).ToArray();
        var poolRange = ToRange(pool);
        var size = 1u << (32 - runtimePrefix);
        for (var start = poolRange.Start; start <= poolRange.End - size + 1; start += size)
        {
            var end = start + size - 1;
            if (used.All(item => end < item.Start || item.End < start))
                return new IPNetwork(FromUInt32(start), runtimePrefix);
            if (uint.MaxValue - start < size) break;
        }
        return null;
    }

    private static IPNetwork ParseNetwork(string cidr)
    {
        var parts = cidr.Split('/');
        if (parts.Length != 2 || !IPAddress.TryParse(parts[0], out var address) ||
            address.AddressFamily != AddressFamily.InterNetwork || !int.TryParse(parts[1], out var prefix))
            throw new TeamLabApiContractException("release_invalid", $"CIDR '{cidr}' 无效", 500);
        return new IPNetwork(address, prefix);
    }

    private static (uint Start, uint End) ToRange(IPNetwork network)
    {
        var start = ToUInt32(network.BaseAddress);
        var size = 1u << (32 - network.PrefixLength);
        return (start, start + size - 1);
    }

    private static string HostAt(IPNetwork network, int offset)
    {
        var range = ToRange(network);
        if (offset <= 0 || (uint)offset >= range.End - range.Start)
            throw new TeamLabApiContractException("topology_invalid", "主机偏移超出运行时网络范围", 422);
        return FromUInt32(range.Start + (uint)offset).ToString();
    }

    private static uint ToUInt32(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];
    }

    private static IPAddress FromUInt32(uint value) => new([
        (byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value
    ]);

    private static string MacAddress(int runtimeId, string assetKey, string interfaceKey)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{runtimeId}:{assetKey}:{interfaceKey}"));
        return $"02:42:{hash[0]:x2}:{hash[1]:x2}:{hash[2]:x2}:{hash[3]:x2}";
    }


    private static string? NormalizeExternalReference(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.Trim();
        if (normalized.Length > 256)
            throw new TeamLabApiContractException("topology_invalid", "外部引用不能超过 256 个字符", 422);
        return normalized;
    }

    private sealed record RuntimeInterfaceIntent(
        string Key,
        string NetworkKey,
        string IpAddress,
        int PrefixLength,
        string MacAddress,
        bool Primary);
}
