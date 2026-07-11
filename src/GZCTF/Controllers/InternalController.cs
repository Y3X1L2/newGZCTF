using GZCTF.Models.Internal;
using GZCTF.Models.Data;
using GZCTF.Repositories.Interface;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Net.Http.Headers;
using System.Net.Mime;
using System.Security.Cryptography;
using System.Text;

namespace GZCTF.Controllers;

/// <summary>
/// 内部接口控制器，供本机 Nginx 同步脚本或运维工具查询端口映射
/// </summary>
[ApiController]
[Route("api/internal")]
[Produces(MediaTypeNames.Application.Json)]
public class InternalController : ControllerBase
{
    private readonly IContainerRepository _containerRepository;
    private readonly AppDbContext _context;
    private readonly ContainerProvider _containerProvider;
    private readonly ILogger<InternalController> _logger;

    public InternalController(
        IContainerRepository containerRepository,
        AppDbContext context,
        IOptions<ContainerProvider> containerProvider,
        ILogger<InternalController> logger)
    {
        _containerRepository = containerRepository;
        _context = context;
        _containerProvider = containerProvider.Value;
        _logger = logger;
    }

    /// <summary>
    /// 获取所有活跃容器的端口映射（用于 Nginx stream 配置同步）
    /// </summary>
    [HttpGet("port-map")]
    [AllowAnonymous]
    public async Task<IActionResult> GetPortMap(CancellationToken token)
    {
        if (!await IsAuthorizedSyncRequest())
        {
            _logger.LogWarning("Rejected unauthorized internal port-map request from {RemoteIp}",
                HttpContext.Connection.RemoteIpAddress);
            return Unauthorized(new RequestResponse("Invalid internal sync token", StatusCodes.Status401Unauthorized));
        }

        var mappings = await _containerRepository.GetProxyPortMappingsAsync(token);
        return Ok(mappings);
    }

    /// <summary>
    /// Get active TeamLab WireGuard UDP mappings for a public UDP gateway.
    /// </summary>
    [HttpGet("teamlab-udp-map")]
    [AllowAnonymous]
    public async Task<IActionResult> GetTeamLabUdpMap(CancellationToken token)
    {
        if (!await IsAuthorizedSyncRequest())
        {
            _logger.LogWarning("Rejected unauthorized internal TeamLab UDP map request from {RemoteIp}",
                HttpContext.Connection.RemoteIpAddress);
            return Unauthorized(new RequestResponse("Invalid internal sync token", StatusCodes.Status401Unauthorized));
        }

        var mappings = await BuildTeamLabUdpMappings(_context.TeamLabPublicUdpMappings
                .AsNoTracking()
                .Include(m => m.Runtime))
            .ToArrayAsync(token);

        return Ok(mappings);
    }

    internal static IQueryable<TeamLabUdpMappingEntry> BuildTeamLabUdpMappings(
        IQueryable<TeamLabPublicUdpMapping> mappings) =>
        mappings
            .Where(m => m.Runtime.Status == TeamLabRuntimeStatus.Running
                        && m.Runtime.IsOpenToPlayers
                        && m.Runtime.WorkerNodeId != null)
            .OrderBy(m => m.PublicUdpPort)
            .Select(m => new TeamLabUdpMappingEntry(
                m.PublicUdpPort,
                m.WorkerTunnelIp,
                m.WorkerWireGuardPort,
                m.RuntimeId,
                m.Runtime.GameId,
                m.Runtime.TeamId,
                m.Runtime.WorkerNodeId!.Value,
                m.RuleVersion,
                m.IsSynced,
                m.LastSyncError));

    async Task<bool> IsAuthorizedSyncRequest() =>
        await ContextHelper.HasAdmin(HttpContext) || HasConfiguredSyncToken(Request);

    bool HasConfiguredSyncToken(HttpRequest request)
    {
        var expected = _containerProvider.NginxProxyConfig?.SyncToken;
        if (string.IsNullOrWhiteSpace(expected))
            return false;

        if (!AuthenticationHeaderValue.TryParse(request.Headers.Authorization, out var value) ||
            !string.Equals(value.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(value.Parameter))
            return false;

        var receivedBytes = Encoding.UTF8.GetBytes(value.Parameter);
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        if (receivedBytes.Length != expectedBytes.Length)
            return false;

        return CryptographicOperations.FixedTimeEquals(
            receivedBytes,
            expectedBytes);
    }
}
