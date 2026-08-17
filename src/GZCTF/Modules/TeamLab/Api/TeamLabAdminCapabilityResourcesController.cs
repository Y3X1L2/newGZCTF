using GZCTF.Middlewares;
using GZCTF.Modules.TeamLab.Application;
using GZCTF.Modules.TeamLab.Contracts;
using GZCTF.Modules.TeamLab.Domain;
using Microsoft.AspNetCore.Mvc;

namespace GZCTF.Modules.TeamLab.Api;

/// <summary>
/// Administration surface of the capability resource catalog: registering
/// externally produced device packages and field connectors, health reporting
/// and lifecycle closure. Occupancy stays on the open API, scoped by runtime.
/// </summary>
[RequireAdmin]
[ApiController]
[Route("api/admin/teamlab")]
public sealed class TeamLabAdminCapabilityResourcesController(
    TeamLabDevicePackageService packages,
    TeamLabConnectorService connectors,
    TeamLabResourcePoolService pools) : ControllerBase
{
    [HttpGet("device-packages")]
    [ProducesResponseType(typeof(TeamLabDevicePackagePageModel), StatusCodes.Status200OK)]
    public Task<TeamLabDevicePackagePageModel> ListDevicePackages(
        [FromQuery] string? name = null,
        [FromQuery] int limit = 50,
        [FromQuery] string? after = null) =>
        packages.ListAsync(name, after, limit, HttpContext.RequestAborted);

    [HttpGet("device-packages/{packageId:guid}")]
    [ProducesResponseType(typeof(TeamLabDevicePackageModel), StatusCodes.Status200OK)]
    public Task<TeamLabDevicePackageModel> GetDevicePackage(Guid packageId) =>
        packages.GetAsync(packageId, HttpContext.RequestAborted);

    [HttpGet("connectors")]
    [ProducesResponseType(typeof(TeamLabConnectorPageModel), StatusCodes.Status200OK)]
    public Task<TeamLabConnectorPageModel> ListConnectors(
        [FromQuery] Guid? scopeId = null,
        [FromQuery] int limit = 50,
        [FromQuery] string? after = null) =>
        connectors.ListAsync(scopeId, after, limit, HttpContext.RequestAborted);

    [HttpGet("resource-pools")]
    [ProducesResponseType(typeof(TeamLabResourcePoolSnapshotModel), StatusCodes.Status200OK)]
    public Task<TeamLabResourcePoolSnapshotModel> ResourcePoolSnapshot() =>
        pools.GetSnapshotAsync(HttpContext.RequestAborted);

    [HttpGet("resource-pools/node-cache")]
    [ProducesResponseType(typeof(TeamLabNodeCachePageModel), StatusCodes.Status200OK)]
    public Task<TeamLabNodeCachePageModel> ListNodeCache(
        [FromQuery] int limit = 50,
        [FromQuery] string? after = null) =>
        pools.ListNodeCacheAsync(after, limit, HttpContext.RequestAborted);

    [HttpPost("device-packages")]
    [ProducesResponseType(typeof(TeamLabDevicePackageModel), StatusCodes.Status201Created)]
    public async Task<IActionResult> RegisterDevicePackage(
        RegisterTeamLabDevicePackageModel model,
        CancellationToken cancellationToken)
    {
        var package = await packages.RegisterAsync(model, cancellationToken);
        return Created($"/api/open/v1/teamlab/device-packages/{package.Id:D}", package);
    }

    [HttpPost("device-packages/{packageId:guid}/enable")]
    [ProducesResponseType(typeof(TeamLabDevicePackageModel), StatusCodes.Status200OK)]
    public Task<TeamLabDevicePackageModel> EnableDevicePackage(Guid packageId, CancellationToken cancellationToken) =>
        packages.SetEnabledAsync(packageId, true, cancellationToken);

    [HttpPost("device-packages/{packageId:guid}/disable")]
    [ProducesResponseType(typeof(TeamLabDevicePackageModel), StatusCodes.Status200OK)]
    public Task<TeamLabDevicePackageModel> DisableDevicePackage(Guid packageId, CancellationToken cancellationToken) =>
        packages.SetEnabledAsync(packageId, false, cancellationToken);

    [HttpPost("device-packages/{packageId:guid}/archive")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ArchiveDevicePackage(Guid packageId, CancellationToken cancellationToken)
    {
        await packages.ArchiveAsync(packageId, cancellationToken);
        return NoContent();
    }

    [HttpPost("connectors")]
    [ProducesResponseType(typeof(TeamLabConnectorModel), StatusCodes.Status201Created)]
    public async Task<IActionResult> RegisterConnector(
        RegisterTeamLabConnectorModel model,
        CancellationToken cancellationToken)
    {
        var connector = await connectors.RegisterAsync(model, cancellationToken);
        return Created($"/api/open/v1/teamlab/connectors/{connector.Id:D}", connector);
    }

    [HttpPost("connectors/{connectorId:guid}/health")]
    [ProducesResponseType(typeof(TeamLabConnectorModel), StatusCodes.Status200OK)]
    public async Task<TeamLabConnectorModel> SetConnectorHealth(
        Guid connectorId,
        SetTeamLabConnectorHealthModel model,
        CancellationToken cancellationToken)
    {
        if (!TeamLabCapabilityResourceContractMapper.TryParseConnectorHealth(model.Health, out var health))
            throw new TeamLabApiContractException("connector_health_invalid", "连接器健康状态无效", 422);
        return await connectors.SetHealthAsync(connectorId, health, cancellationToken);
    }

    [HttpPost("connectors/{connectorId:guid}/leases/revoke")]
    [ProducesResponseType(typeof(TeamLabConnectorLeaseModel), StatusCodes.Status200OK)]
    public async Task<TeamLabConnectorLeaseModel> RevokeConnectorLease(
        Guid connectorId,
        ReleaseTeamLabConnectorLeaseModel model,
        CancellationToken cancellationToken) =>
        await connectors.ReleaseAsync(
            connectorId, model.RuntimeId, TeamLabConnectorLeaseReleaseReason.AdminRevoked, cancellationToken);

    [HttpPost("connectors/{connectorId:guid}/archive")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ArchiveConnector(Guid connectorId, CancellationToken cancellationToken)
    {
        await connectors.ArchiveAsync(connectorId, cancellationToken);
        return NoContent();
    }
}
