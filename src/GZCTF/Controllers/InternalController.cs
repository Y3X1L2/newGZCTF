using GZCTF.Middlewares;
using GZCTF.Repositories.Interface;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Net.Mime;

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
    private readonly ILogger<InternalController> _logger;

    public InternalController(IContainerRepository containerRepository, ILogger<InternalController> logger)
    {
        _containerRepository = containerRepository;
        _logger = logger;
    }

    /// <summary>
    /// 获取所有活跃容器的端口映射（用于 Nginx stream 配置同步）
    /// </summary>
    [HttpGet("port-map")]
    [RequireAdmin]
    public async Task<IActionResult> GetPortMap(CancellationToken token)
    {
        var mappings = await _containerRepository.GetProxyPortMappingsAsync(token);
        return Ok(mappings);
    }
}
