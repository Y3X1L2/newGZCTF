using GZCTF.Infrastructure.Concurrency;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Modules.Audit.Domain;
using GZCTF.Modules.Audit.Contracts;
using GZCTF.Modules.Audit.Application;
using GZCTF.Modules.Runtime.Contracts;
using GZCTF.Modules.TeamLab.Domain;
using GZCTF.Modules.TeamLab.Domain.Runtime;
using GZCTF.Storage.Interface;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GZCTF.Modules.TeamLab.Application;

/// <summary>
/// Deletes definitions and terminal records; never substitutes for resource destruction.
/// Blob deletion is idempotent: a failed request keeps its metadata for a safe retry.
/// </summary>
public sealed class TeamLabRecordDeletionService(
    AppDbContext context,
    ITeamLabUsageProjectionProvider usage,
    ITeamLabNodeExecutor executor,
    TeamLabReleaseImagePreparationService preparation,
    IBlobStorage storage,
    IDistributedLeaseProvider leases,
    TeamLabRuntimeOperationPayloadProtector payloads,
    ITeamLabRecordReferenceQuery references,
    IOperationalEventWriter events,
    ITeamLabCreationHistory creationHistory)
{
    public Task DeleteTopologyAsync(Guid publicId, Guid actorId, bool administrator, CancellationToken token) =>
        WithConflictMappingAsync(() => DeleteTopologyCoreAsync(publicId, actorId, administrator, token), "topology_in_use");

    public Task DeleteRuntimeAsync(Guid publicId, Guid actorId, bool administrator, CancellationToken token) =>
        WithConflictMappingAsync(() => DeleteRuntimeCoreAsync(publicId, actorId, administrator, token), "runtime_cleanup_pending");

    private static async Task WithConflictMappingAsync(Func<Task> action, string conflictCode)
    {
        try { await action(); }
        catch (DbUpdateException error) when (error.InnerException is PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation })
        {
            throw Conflict(conflictCode, "记录新增了引用，请刷新关联状态后重试。");
        }
        catch (PostgresException error) when (error.SqlState is PostgresErrorCodes.DeadlockDetected or PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.LockNotAvailable)
        {
            throw Conflict("record_delete_conflict", "记录正在被其他操作修改，请稍后重试。");
        }
        catch (TimeoutException)
        {
            throw Conflict("record_delete_conflict", "记录的审计或清理操作仍在执行，请稍后重试。");
        }
    }

    private async Task DeleteTopologyCoreAsync(Guid publicId, Guid actorId, bool administrator, CancellationToken token)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(token);
        await ConfigureTimeoutsAsync(token);
        var topology = await context.TeamLabTopologies.SingleOrDefaultAsync(item => item.PublicId == publicId, token);
        if (topology is null)
        {
            if (!administrator) throw Missing("topology");
            return;
        }
        RequireOwner(topology.OwnerUserId, actorId, administrator);
        if (context.Database.IsNpgsql())
        {
            var publishLock = $"teamlab:topology-release:{topology.Id}";
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtextextended({publishLock}, 0))", token);
            // The row lock also fences draft replacement and new business FK references.
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT 1 FROM \"TeamLabTopologies\" WHERE \"Id\" = {topology.Id} FOR UPDATE", token);
            await context.Entry(topology).ReloadAsync(token);
            if (context.Entry(topology).State == EntityState.Detached)
            {
                if (!administrator) throw Missing("topology");
                return;
            }
            RequireOwner(topology.OwnerUserId, actorId, administrator);
        }
        var releases = await context.TeamLabTopologyReleases.Where(item => item.TopologyId == topology.Id)
            .OrderBy(item => item.Id).Select(item => item.Id).ToArrayAsync(token);
        foreach (var release in releases) await TeamLabReleaseLifecycle.LockAsync(context, release, token);
        if ((await usage.GetGameBindingCountsAsync([topology.Id], token)).GetValueOrDefault(topology.Id) > 0 ||
            await context.TeamLabRuntimes.AnyAsync(item => releases.Contains(item.TopologyReleaseId), token) ||
            await context.TeamLabRollouts.AnyAsync(item => releases.Contains(item.ReleaseId), token) ||
            await context.TeamLabNetworkLeases.AnyAsync(item => releases.Contains(item.TopologyReleaseId), token))
            throw Conflict("topology_in_use", "场景仍有关联比赛、运行记录或批量发放记录，请先解除关联并删除运行记录。");
        var activeOperations = await references.GetActiveOperationIdsAsync(token);
        var pending = await context.TeamLabRuntimeOperationJobs.AsNoTracking()
            .Where(job => activeOperations.Contains(job.OperationId))
            .Select(job => job.ProtectedPayload).ToArrayAsync(token);
        foreach (var value in pending)
        {
            if (string.IsNullOrWhiteSpace(value)) throw Conflict("topology_in_use", "存在无法确认引用的组网操作，请完成操作后重试。");
            TeamLabRuntimeOperationPayload payload;
            try { payload = payloads.Unprotect(value); }
            catch (ApiOperationTerminalException) { throw Conflict("topology_in_use", "存在无法确认引用的组网操作，请完成操作后重试。"); }
            if (payload.TopologyId == publicId || releases.Contains(payload.ReleaseId ?? Guid.Empty) ||
                releases.Contains(payload.Create?.ReleaseId ?? Guid.Empty) || releases.Contains(payload.Reset?.ReleaseId ?? Guid.Empty) ||
                releases.Contains(payload.CreateRollout?.ReleaseId ?? Guid.Empty))
                throw Conflict("topology_in_use", "存在使用此场景或版本的组网操作，请完成或取消后重试。");
        }
        // Withdrawing explicit prewarm uses the same port as normal release retirement.
        // It neither deletes source images nor withdraws another consumer's demand.
        foreach (var release in releases) await preparation.ReleaseAsync(release, token);
        await preparation.ReleaseTopologyAsync(topology.Id, token);
        context.TeamLabTopologies.Remove(topology);
        RecordDeletion(OperationalEventCodes.TeamLab.SceneDeleted, "teamlab-topology", publicId, actorId, topology.OwnerUserId);
        await context.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
    }

    private async Task DeleteRuntimeCoreAsync(Guid publicId, Guid actorId, bool administrator, CancellationToken token)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(token);
        await ConfigureTimeoutsAsync(token);
        var runtime = await context.TeamLabRuntimes.SingleOrDefaultAsync(item => item.PublicId == publicId, token);
        if (runtime is null)
        {
            if (!administrator) throw Missing("runtime");
            return;
        }
        RequireOwner(runtime.CreatedById, actorId, administrator);
        await TeamLabReleaseLifecycle.LockAsync(context, runtime.TopologyReleaseId, token);
        await TeamLabRecordMutationLock.LockAsync(context, runtime.Id, runtime.PublicId, token);
        if (context.Database.IsNpgsql())
        {
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT 1 FROM \"TeamLabRuntimes\" WHERE \"Id\" = {runtime.Id} FOR UPDATE", token);
            await context.Entry(runtime).ReloadAsync(token);
            if (context.Entry(runtime).State == EntityState.Detached)
            {
                if (!administrator) throw Missing("runtime");
                return;
            }
            RequireOwner(runtime.CreatedById, actorId, administrator);
        }
        if (runtime.Status != TeamLabRuntimeStatus.Destroyed)
            throw Conflict("runtime_not_destroyed", "请先销毁运行环境，等待资源清理完成后再删除记录。");
        if ((await usage.GetGameBoundRuntimeIdsAsync(token)).Contains(runtime.Id) ||
            await context.TeamLabRolloutTargets.AnyAsync(item => item.RuntimeId == runtime.Id, token))
            throw Conflict("runtime_in_use", "运行记录仍被比赛或批量发放引用，请先解除关联。");
        await RequireCleanAsync(runtime, token);
        await RequireEmptyInventoryAsync(runtime, token);

        // Share the maintenance lock and every per-session generation lock. No evidence
        // writer can recreate a blob between unlink and the final database commit.
        await using var auditMaintenance = await leases.AcquireAsync("teamlab:remote-audit",
            TimeSpan.FromSeconds(2), TimeSpan.FromMinutes(5), token);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, auditMaintenance.LeaseLost);
        token = linked.Token;
        var sessions = await context.TeamLabRemoteSessions.Where(item => item.RuntimeId == runtime.Id)
            .Include(item => item.AuditFiles).OrderBy(item => item.Id).ToArrayAsync(token);
        List<IDistributedLease> sessionLeases = [];
        List<CancellationTokenRegistration> leaseRegistrations = [];
        try
        {
            foreach (var session in sessions)
            {
                var lease = await leases.AcquireAsync($"teamlab:remote-audit:{session.PublicId:N}",
                    TimeSpan.FromSeconds(2), TimeSpan.FromMinutes(5), token);
                sessionLeases.Add(lease);
                leaseRegistrations.Add(lease.LeaseLost.Register(linked.Cancel));
                linked.Token.ThrowIfCancellationRequested();
                var expectedPath = $"teamlab/remote-audit/{session.PublicId:N}.json";
                foreach (var file in session.AuditFiles)
                {
                    if (file.RelativePath != expectedPath || await context.Set<TeamLabRemoteAuditFile>()
                            .AnyAsync(item => item.Id != file.Id && item.RelativePath == file.RelativePath, token))
                        throw Conflict("runtime_artifact_cleanup_pending", "审计文件路径或引用不符合回收条件，请检查存储记录。");
                    try
                    {
                        await storage.DeleteAsync(expectedPath, token);
                        if (await storage.ExistsAsync(expectedPath, token))
                            throw new IOException("The storage object still exists after deletion.");
                    }
                    catch (Exception exception) when (exception is not OperationCanceledException)
                    {
                        throw new TeamLabApiContractException("runtime_artifact_cleanup_pending",
                            "审计文件回收失败，记录已保留，请修复存储后重试删除。", 409);
                    }
                }
            }
            context.RemoveRange(sessions.SelectMany(item => item.AuditFiles));
            await context.SaveChangesAsync(token);
            context.RemoveRange(sessions);
            await context.SaveChangesAsync(token);
            // These two owned historical tables deliberately use Restrict FKs.
            await context.TeamLabConnectorLeases.Where(item => item.RuntimeId == runtime.Id).ExecuteDeleteAsync(token);
            await context.TeamLabLinkPolicies.Where(item => item.RuntimeId == runtime.Id).ExecuteDeleteAsync(token);
            // Break the entry-shard cycle before EF schedules tracked cascade deletes.
            runtime.EntryShardId = null;
            await context.SaveChangesAsync(token);
            if (runtime.CreatedById is { } owner && !string.IsNullOrWhiteSpace(runtime.CreationIdempotencyKey))
                await creationHistory.RetireAsync(owner, runtime.CreationIdempotencyKey, runtime.CreateRequestHash, runtime.PublicId, token);
            context.TeamLabRuntimes.Remove(runtime);
            RecordDeletion(OperationalEventCodes.TeamLab.RuntimeRecordDeleted, "teamlab-runtime", publicId, actorId, runtime.CreatedById);
            await context.SaveChangesAsync(token);
            await transaction.CommitAsync(token);
        }
        finally
        {
            foreach (var registration in leaseRegistrations) await registration.DisposeAsync();
            foreach (var lease in sessionLeases) await lease.DisposeAsync();
        }
    }

    private async Task RequireCleanAsync(TeamLabRuntime runtime, CancellationToken token)
    {
        var id = runtime.Id;
        var activeOperations = await references.GetActiveOperationIdsAsync(token);
        if (await context.TeamLabRuntimeAssets.AnyAsync(item => item.RuntimeId == id && item.Status != TeamLabRuntimeStatus.Destroyed, token) ||
            await context.TeamLabRuntimeShards.AnyAsync(item => item.RuntimeId == id && item.Status != TeamLabRuntimeStatus.Destroyed, token) ||
            await context.TeamLabRuntimeInfrastructures.AnyAsync(item => item.RuntimeId == id &&
                (item.Status != TeamLabRuntimeStatus.Destroyed || item.Fragments.Any(fragment => fragment.Status != TeamLabRuntimeStatus.Destroyed)), token) ||
            await context.TeamLabNetworkLeases.AnyAsync(item => item.RuntimeId == id && item.ReleasedAt == null, token) ||
            await context.TeamLabFabricLinkLeases.AnyAsync(item => item.RuntimeId == id && item.ReleasedAt == null, token) ||
            await context.TeamLabConnectorLeases.AnyAsync(item => item.RuntimeId == id && item.ReleasedAt == null, token) ||
            await context.TeamLabLinkPolicies.AnyAsync(item => item.RuntimeId == id && item.Status == TeamLabLinkPolicyStatus.Active, token) ||
            await context.TeamLabAccessGrants.AnyAsync(item => item.RuntimeId == id && !item.Revoked, token) ||
            await context.TeamLabVpnPeerRuntimes.AnyAsync(item => item.RuntimeId == id && !item.Revoked, token) ||
            await context.TeamLabRuntimeSecretEnvelopes.AnyAsync(item => item.RuntimeId == id && item.ConsumedAt == null, token) ||
            await context.TeamLabPublicUdpMappings.AnyAsync(item => item.RuntimeId == id && item.IsSynced, token) ||
            await context.TeamLabServiceAccesses.AnyAsync(item => item.RuntimeId == id && item.Status != "revoked", token) ||
            await context.TeamLabObservationPoints.AnyAsync(item => item.RuntimeId == id && item.Enabled, token) ||
            await context.TeamLabRemoteSessions.AnyAsync(item => item.RuntimeId == id &&
                (item.Status != TeamLabRemoteSessionStatus.Ended && item.Status != TeamLabRemoteSessionStatus.Failed ||
                 item.Status == TeamLabRemoteSessionStatus.Failed &&
                    (item.RelayId != null || item.GuacamoleConnectionId != null || item.GuacamoleUserId != null || item.GuacamoleCreationStarted)), token) ||
            await context.TeamLabTrafficCaptureJobs.AnyAsync(item => item.RuntimeId == id &&
                (item.Status != TeamLabTrafficCaptureStatus.Expired || item.Segments.Any(segment => segment.ObjectPath != null)), token) ||
            await context.TeamLabRuntimeOperationJobs.AnyAsync(item => item.RuntimePublicId == runtime.PublicId && activeOperations.Contains(item.OperationId), token) ||
            await references.HasActiveRuntimeReferencesAsync(id, runtime.PublicId, token))
            throw Conflict("runtime_cleanup_pending", "仍有资源、网络、访问、镜像或执行任务引用，请完成清理后重试。");
    }

    private async Task RequireEmptyInventoryAsync(TeamLabRuntime runtime, CancellationToken token)
    {
        var shards = await context.TeamLabRuntimeShards.AsNoTracking().Where(item => item.RuntimeId == runtime.Id)
            .Select(item => new { item.Id, item.WorkerNodeId }).ToArrayAsync(token);
        // Normal destruction removes the recovery snapshots. Retained generation,
        // shard and asset identities still let inventory prove their absence.
        var assets = await context.TeamLabRuntimeAssets.Where(item => item.RuntimeId == runtime.Id)
            .Select(item => new { item.WorkerNodeId, item.RuntimeResourceId, item.NativeIdentity, item.Kind }).ToArrayAsync(token);
        if (assets.Any(item => item.WorkerNodeId == null) || assets.Length > 0 && shards.Length == 0 ||
            shards.Length == 0 && await context.TeamLabRuntimeNetworks.AnyAsync(item => item.RuntimeId == runtime.Id, token))
            throw Conflict("runtime_cleanup_pending", "历史资产缺少执行节点，无法确认资源已清理，请先完成资源对账。");
        var nativeIds = assets.SelectMany(item => new[] { item.RuntimeResourceId, item.NativeIdentity })
            .OfType<string>().Where(item => !string.IsNullOrWhiteSpace(item)).ToHashSet(StringComparer.Ordinal);
        var vmPrefix = $"gzctf-tl-{runtime.PublicId:N}-";
        var hostNames = new HashSet<string>(StringComparer.Ordinal)
        {
            TeamLabResourceNameFactory.WireGuardInterface(runtime.Id),
            TeamLabResourceNameFactory.FabricHostInterface(runtime.Id),
            TeamLabResourceNameFactory.FabricNamespaceInterface(runtime.Id)
        };
        foreach (var shard in shards) hostNames.Add(TeamLabResourceNameFactory.RouterNamespace(runtime.Id, shard.Id));
        var networkKeys = await context.TeamLabRuntimeNetworks.Where(item => item.RuntimeId == runtime.Id)
            .Select(item => item.TopologyKey).Distinct().ToArrayAsync(token);
        foreach (var key in networkKeys)
        {
            hostNames.Add(TeamLabResourceNameFactory.Bridge(runtime.Id, key));
            hostNames.Add(TeamLabResourceNameFactory.ServiceGatewayInterface(runtime.Id, key));
            hostNames.Add(TeamLabResourceNameFactory.DhcpDnsService(runtime.Id, key));
        }
        foreach (var node in shards.Select(item => item.WorkerNodeId).Concat(assets.Select(item => item.WorkerNodeId).OfType<Guid>()).Distinct())
        {
            TeamLabNodeRuntimeInventory inventory;
            using var probeTimeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            probeTimeout.CancelAfter(TimeSpan.FromSeconds(20));
            try { inventory = await executor.GetRuntimeInventoryAsync(node, probeTimeout.Token); }
            catch (Exception exception) when (!token.IsCancellationRequested &&
                exception is HttpRequestException or TeamLabRuntimeExecutionException or OperationCanceledException or IOperationalFailureException)
            {
                throw new TeamLabApiContractException("runtime_inventory_unavailable", "关联节点无法确认资源状态，请恢复节点后重试。", 503);
            }
            if (inventory.ObservedAt < DateTimeOffset.UtcNow.AddMinutes(-2) || inventory.ObservedAt > DateTimeOffset.UtcNow.AddMinutes(1))
                throw new TeamLabApiContractException("runtime_inventory_unavailable", "节点返回的资源清单已过期，请刷新节点后重试。", 503);
            if (assets.Any(item => item.WorkerNodeId == node && item.Kind == TeamLabResourceKind.Vm) && inventory.KvmAvailable != true ||
                assets.Any(item => item.WorkerNodeId == node && item.Kind == TeamLabResourceKind.Docker) && inventory.DockerAvailable != true)
                throw new TeamLabApiContractException("runtime_inventory_unavailable", "节点无法读取此运行环境所用执行引擎的资源清单，请恢复后重试。", 503);
            if (inventory.Containers.Concat(inventory.Vms).Concat(inventory.Infrastructure).Any(item =>
                    item.RuntimeId == runtime.Id || nativeIds.Contains(item.NativeId) || nativeIds.Contains(item.StableName) ||
                    item.StableName.StartsWith(vmPrefix, StringComparison.Ordinal) || hostNames.Contains(item.StableName)))
                throw Conflict("runtime_cleanup_pending", "节点仍存在此运行环境的资源，请完成资源清理后重试。");
        }
    }

    private static void RequireOwner(Guid? owner, Guid actor, bool administrator)
    {
        if (!administrator && owner != actor)
            throw new TeamLabApiContractException("insufficient_permission", "仅创建者或管理员可以删除此记录。", 403);
    }

    private static TeamLabApiContractException Conflict(string code, string message) => new(code, message, 409);
    private static TeamLabApiContractException Missing(string kind) => new($"{kind}_not_found", "未找到记录。", 404);

    private Task ConfigureTimeoutsAsync(CancellationToken token) => context.Database.IsNpgsql()
        ? context.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '5s'; SET LOCAL statement_timeout = '60s'", token)
        : Task.CompletedTask;

    private void RecordDeletion(string code, string type, Guid publicId, Guid actorId, Guid? ownerId) =>
        events.Append(new OperationalEventDraft(code, OperationalEventOutcome.Succeeded,
            "TeamLab record and owned history deleted.", ActorUserId: actorId, OwnerUserId: ownerId,
            ResourceType: type, ResourceId: publicId.ToString("D")));
}

internal static class TeamLabRecordMutationLock
{
    internal static async Task LockAsync(AppDbContext context, int runtimeId, Guid publicId, CancellationToken token)
    {
        if (!context.Database.IsNpgsql()) return;
        var resourceKey = $"teamlab-runtime:{publicId:D}";
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({resourceKey}, 0))", token);
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({TeamLabRuntimeCleanupService.RuntimeLockKey(runtimeId)})", token);
    }
}
