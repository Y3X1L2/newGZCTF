using GZCTF.Models;
using GZCTF.Modules.TeamLab.Contracts;
using GZCTF.Modules.TeamLab.Domain;
using GZCTF.Modules.TeamLab.Domain.Runtime;
using Microsoft.EntityFrameworkCore;
using static GZCTF.Modules.TeamLab.Application.TeamLabCapabilityResourceValidation;

namespace GZCTF.Modules.TeamLab.Application;

/// <summary>
/// Field connector registry and lease lifecycle. An exclusive connector is
/// held by at most one runtime at a time; occupancy survives node loss and is
/// only released by an explicit, audited command or runtime destruction.
/// </summary>
public sealed class TeamLabConnectorService(AppDbContext context)
{
    private static readonly TeamLabRuntimeStatus[] NonAcquirableStatuses =
    [
        TeamLabRuntimeStatus.CleanupPending, TeamLabRuntimeStatus.Destroying, TeamLabRuntimeStatus.Destroyed
    ];

    public async Task<TeamLabConnectorModel> RegisterAsync(
        RegisterTeamLabConnectorModel command,
        CancellationToken cancellationToken)
    {
        var name = Slug(command.Name, 96, "connector_name_invalid", "连接器名称无效");
        var displayName = Text(command.DisplayName, 1, 128, "connector_display_name_invalid", "连接器显示名称无效");
        if (!TeamLabCapabilityResourceContractMapper.TryParseConnectorKind(command.Kind, out var kind))
            throw new TeamLabApiContractException("connector_kind_invalid", "连接器类型无效", 422);
        if (command.SupportsSharedUse && command.Capacity is < 1 or > 64)
            throw new TeamLabApiContractException("connector_capacity_invalid", "共享连接器容量必须是 1-64", 422);
        var capacity = command.SupportsSharedUse ? command.Capacity : 1;

        if (await context.TeamLabConnectors.AnyAsync(item => item.Name == name, cancellationToken))
            throw new TeamLabApiContractException("connector_name_conflict", "连接器名称已存在", 409);
        if (command.ControlScopeId is { } scopeId && !await context.TeamLabControlScopes
                .AnyAsync(scope => scope.Id == scopeId, cancellationToken))
            throw new TeamLabApiContractException("scope_not_found", "未找到 TeamLab 控制范围", 404);

        var connector = new TeamLabConnector
        {
            Name = name,
            DisplayName = displayName,
            Kind = kind,
            ControlScopeId = command.ControlScopeId,
            SupportsSharedUse = command.SupportsSharedUse,
            Capacity = capacity,
            AttachmentReference = OptionalText(
                command.AttachmentReference, 512, "connector_attachment_reference_invalid", "连接器接入引用超出长度限制"),
            Description = OptionalText(
                command.Description, 2048, "connector_description_invalid", "连接器描述超出长度限制")
        };
        context.TeamLabConnectors.Add(connector);
        await context.SaveChangesAsync(cancellationToken);
        return ToModel(connector, []);
    }

    public async Task<TeamLabConnectorPageModel> ListAsync(
        Guid? scopeId,
        string? after,
        int limit,
        CancellationToken cancellationToken)
    {
        var cursor = DecodeIntCursor(after, "connector_cursor_invalid", "连接器 cursor 无效");
        var take = Math.Clamp(limit, 1, 100);
        var query = context.TeamLabConnectors.AsNoTracking()
            .Where(item => !item.IsArchived && (item.ControlScopeId == null || item.ControlScopeId == scopeId));
        if (cursor is not null)
            query = query.Where(item => item.Id > cursor);
        var rows = await query.OrderBy(item => item.Id).Take(take + 1).ToArrayAsync(cancellationToken);
        var leases = await LoadActiveLeasesAsync(rows.Take(take).Select(item => item.Id).ToArray(), cancellationToken);
        return new TeamLabConnectorPageModel(
            rows.Take(take).Select(connector => ToModel(connector, leases.GetValueOrDefault(connector.Id, []))).ToArray(),
            rows.Length > take ? EncodeIntCursor(rows[take - 1].Id) : null);
    }

    public async Task<TeamLabConnectorModel> GetAsync(
        Guid connectorId,
        Guid? scopeId,
        CancellationToken cancellationToken)
    {
        var connector = await RequireVisibleAsync(connectorId, scopeId)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new TeamLabApiContractException("connector_not_found", "未找到连接器", 404);
        var leases = await LoadActiveLeasesAsync([connector.Id], cancellationToken);
        return ToModel(connector, leases.GetValueOrDefault(connector.Id, []));
    }

    public async Task<TeamLabConnectorModel> SetHealthAsync(
        Guid connectorId,
        TeamLabConnectorHealth health,
        CancellationToken cancellationToken)
    {
        var connector = await context.TeamLabConnectors
            .SingleOrDefaultAsync(item => item.PublicId == connectorId, cancellationToken)
            ?? throw new TeamLabApiContractException("connector_not_found", "未找到连接器", 404);
        connector.Health = health;
        connector.HealthObservedAt = DateTimeOffset.UtcNow;
        connector.UpdatedAt = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(cancellationToken);
        var leases = await LoadActiveLeasesAsync([connector.Id], cancellationToken);
        return ToModel(connector, leases.GetValueOrDefault(connector.Id, []));
    }

    public async Task ArchiveAsync(Guid connectorId, CancellationToken cancellationToken)
    {
        var connector = await context.TeamLabConnectors
            .SingleOrDefaultAsync(item => item.PublicId == connectorId, cancellationToken)
            ?? throw new TeamLabApiContractException("connector_not_found", "未找到连接器", 404);
        if (await context.TeamLabConnectorLeases.AnyAsync(
                lease => lease.ConnectorId == connector.Id && lease.ReleasedAt == null, cancellationToken))
            throw new TeamLabApiContractException("connector_leased", "连接器仍被运行时占用，无法归档", 409);
        connector.IsArchived = true;
        connector.UpdatedAt = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Acquires a lease for a runtime. Idempotent for the same pair; slot
    /// allocation is bounded by the filtered unique indexes, so a lost race
    /// surfaces as a stable conflict instead of over-occupancy.
    /// </summary>
    public async Task<TeamLabConnectorLeaseModel> AcquireAsync(
        Guid connectorId,
        Guid runtimeId,
        Guid? scopeId,
        CancellationToken cancellationToken)
    {
        var connector = await RequireVisibleAsync(connectorId, scopeId)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new TeamLabApiContractException("connector_not_found", "未找到连接器", 404);
        if (connector.Health == TeamLabConnectorHealth.Unreachable)
            throw new TeamLabApiContractException("connector_unreachable", "连接器当前不可达，无法分配", 409);

        var runtime = await context.TeamLabRuntimes
            .SingleOrDefaultAsync(item => item.PublicId == runtimeId, cancellationToken)
            ?? throw new TeamLabApiContractException("runtime_not_found", "未找到 TeamLab 运行时", 404);
        if (NonAcquirableStatuses.Contains(runtime.Status))
            throw new TeamLabApiContractException("runtime_not_active", "运行时已在清理或销毁流程中，无法占用连接器", 409);

        var existing = await context.TeamLabConnectorLeases.SingleOrDefaultAsync(
            lease => lease.ConnectorId == connector.Id && lease.RuntimeId == runtime.Id && lease.ReleasedAt == null,
            cancellationToken);
        if (existing is not null) return ToModel(existing, connector.PublicId, runtime.PublicId);

        var takenSlots = await context.TeamLabConnectorLeases
            .Where(lease => lease.ConnectorId == connector.Id && lease.ReleasedAt == null)
            .Select(lease => lease.Slot)
            .ToArrayAsync(cancellationToken);
        var slot = Enumerable.Range(1, connector.Capacity).Except(takenSlots).FirstOrDefault();
        if (slot == 0)
            throw new TeamLabApiContractException("connector_occupied", "连接器容量已满，无法分配", 409);

        var lease = new TeamLabConnectorLease { ConnectorId = connector.Id, RuntimeId = runtime.Id, Slot = slot };
        context.TeamLabConnectorLeases.Add(lease);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            throw new TeamLabApiContractException("connector_occupied", "连接器容量已满，无法分配", 409);
        }
        return ToModel(lease, connector.PublicId, runtime.PublicId);
    }

    /// <summary>Releases the active lease of a connector/runtime pair; idempotent when already released.</summary>
    public async Task<TeamLabConnectorLeaseModel> ReleaseAsync(
        Guid connectorId,
        Guid runtimeId,
        TeamLabConnectorLeaseReleaseReason reason,
        CancellationToken cancellationToken)
    {
        var connector = await context.TeamLabConnectors.AsNoTracking()
            .SingleOrDefaultAsync(item => item.PublicId == connectorId, cancellationToken)
            ?? throw new TeamLabApiContractException("connector_not_found", "未找到连接器", 404);
        var runtime = await context.TeamLabRuntimes.AsNoTracking()
            .SingleOrDefaultAsync(item => item.PublicId == runtimeId, cancellationToken)
            ?? throw new TeamLabApiContractException("runtime_not_found", "未找到 TeamLab 运行时", 404);
        var lease = await context.TeamLabConnectorLeases
            .Where(item => item.ConnectorId == connector.Id && item.RuntimeId == runtime.Id)
            .OrderByDescending(item => item.Id)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new TeamLabApiContractException("connector_lease_not_found", "未找到连接器租约", 404);
        if (lease.ReleasedAt is null)
        {
            lease.ReleasedAt = DateTimeOffset.UtcNow;
            lease.ReleaseReason = reason;
            await context.SaveChangesAsync(cancellationToken);
        }
        return ToModel(lease, connector.PublicId, runtime.PublicId);
    }

    /// <summary>Releases every active lease of a runtime (destruction path); returns the released count.</summary>
    public Task<int> ReleaseRuntimeLeasesAsync(
        int runtimeId,
        TeamLabConnectorLeaseReleaseReason reason,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        return context.TeamLabConnectorLeases
            .Where(lease => lease.RuntimeId == runtimeId && lease.ReleasedAt == null)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(lease => lease.ReleasedAt, now)
                .SetProperty(lease => lease.ReleaseReason, reason), cancellationToken);
    }

    /// <summary>Scoped visibility: archived connectors and other scopes' connectors do not exist.</summary>
    private IQueryable<TeamLabConnector> RequireVisibleAsync(Guid connectorId, Guid? scopeId) =>
        context.TeamLabConnectors.AsNoTracking()
            .Where(item => item.PublicId == connectorId && !item.IsArchived &&
                           (item.ControlScopeId == null || item.ControlScopeId == scopeId));

    private async Task<Dictionary<int, TeamLabConnectorLeaseModel[]>> LoadActiveLeasesAsync(
        IReadOnlyCollection<int> connectorIds,
        CancellationToken cancellationToken)
    {
        if (connectorIds.Count == 0) return [];
        var rows = await context.TeamLabConnectorLeases.AsNoTracking()
            .Where(lease => lease.ReleasedAt == null && connectorIds.Contains(lease.ConnectorId))
            .Select(lease => new
            {
                lease.ConnectorId,
                ConnectorPublicId = lease.Connector.PublicId,
                lease.PublicId,
                RuntimePublicId = lease.Runtime.PublicId,
                lease.Slot,
                lease.AcquiredAt
            })
            .ToArrayAsync(cancellationToken);
        return rows.GroupBy(row => row.ConnectorId).ToDictionary(
            group => group.Key,
            group => group.Select(row => new TeamLabConnectorLeaseModel(
                row.PublicId,
                row.ConnectorPublicId,
                row.RuntimePublicId,
                row.Slot,
                row.AcquiredAt,
                null,
                TeamLabCapabilityResourceContractMapper.LeaseReleaseReasonName(TeamLabConnectorLeaseReleaseReason.None)))
            .ToArray());
    }

    internal static TeamLabConnectorLeaseModel ToModel(
        TeamLabConnectorLease lease,
        Guid connectorPublicId,
        Guid runtimePublicId) => new(
        lease.PublicId,
        connectorPublicId,
        runtimePublicId,
        lease.Slot,
        lease.AcquiredAt,
        lease.ReleasedAt,
        TeamLabCapabilityResourceContractMapper.LeaseReleaseReasonName(lease.ReleaseReason));

    internal static TeamLabConnectorModel ToModel(
        TeamLabConnector connector,
        IReadOnlyList<TeamLabConnectorLeaseModel> activeLeases) => new(
        connector.PublicId,
        connector.Name,
        connector.DisplayName,
        TeamLabCapabilityResourceContractMapper.ConnectorKindName(connector.Kind),
        connector.ControlScopeId,
        connector.SupportsSharedUse,
        connector.Capacity,
        activeLeases.Count,
        activeLeases,
        TeamLabCapabilityResourceContractMapper.ConnectorHealthName(connector.Health),
        connector.HealthObservedAt,
        connector.Description,
        connector.IsArchived,
        connector.CreatedAt,
        connector.UpdatedAt);
}
