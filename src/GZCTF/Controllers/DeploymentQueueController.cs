using System.Net.Mime;
using GZCTF.Extensions;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Middlewares;
using GZCTF.Services.Fleet;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Controllers;

[ApiController]
[Route("api/v1/deployment-queue")]
[Produces(MediaTypeNames.Application.Json)]
public sealed class DeploymentQueueController(
    AppDbContext context,
    DeploymentQueueService queue,
    DeploymentQueueViewService queueView,
    ILogger<DeploymentQueueController> logger) : ControllerBase
{
    [HttpGet]
    [RequireAdmin]
    public async Task<IActionResult> List(
        [FromQuery] string? status = null,
        [FromQuery] string? cursor = null,
        [FromQuery] int pageSize = 20)
    {
        try
        {
            return Ok(await queueView.ListAsync(status, cursor, pageSize, HttpContext.RequestAborted));
        }
        catch (Infrastructure.Persistence.Queries.InvalidTimeCursorException)
        {
            return BadRequest(new { code = "invalid_cursor", message = "The pagination cursor is invalid." });
        }
    }

    [HttpGet("{id:guid}")]
    [RequireAdmin]
    public async Task<IActionResult> GetById(Guid id)
    {
        var ticket = await context.DeploymentQueueTickets
            .Include(item => item.TargetNode)
            .FirstOrDefaultAsync(item => item.Id == id);
        if (ticket is null)
            return NotFound();
        return Ok(new
        {
            ticket.Id, ticket.TargetNodeId, ticket.Kind, ticket.Operation, ticket.Status, ticket.Stage,
            CorrelationId = ticket.Id,
            TargetNodeName = ticket.TargetNode?.Name,
            TargetNodeHost = ticket.TargetNode?.HostAddress,
            ticket.SubjectDisplayName, ticket.ResourceDisplayName,
            ticket.CreatedAt, ticket.StartedAt, ticket.CompletedAt, ticket.ErrorMessage,
            ticket.ErrorCategory, ticket.ErrorCode, ticket.Retryable
        });
    }

    [HttpDelete("{id:guid}")]
    [RequireAdmin]
    public async Task<IActionResult> Cancel(Guid id)
    {
        var token = HttpContext.RequestAborted;
        var ticket = await context.DeploymentQueueTickets
            .Include(item => item.TargetNode)
            .SingleOrDefaultAsync(item => item.Id == id, token);
        if (ticket is null)
            return NotFound();

        await queue.CancelAsync(ticket.Id, "Deployment queue ticket was cancelled by administrator.", token);
        await context.Entry(ticket).ReloadAsync(token);
        if (ticket.Status != DeploymentQueueTicketStatus.Cancelled)
            return CancellationConflict(ticket.Status);
        var node = ticket.TargetNode;
        logger.SystemLog(
            $"Deployment queue ticket {ticket.Id} cancelled by administrator: kind={ticket.Kind}, game={ticket.GameId}, team={ticket.OwnerTeamId}, user={ticket.OwnerUserId}, challenge={ticket.ChallengeId}, node={node?.Name ?? node?.HostAddress ?? "unassigned"}.",
            TaskStatus.Exit, LogLevel.Information);
        return NoContent();
    }

    internal static ConflictObjectResult CancellationConflict(DeploymentQueueTicketStatus status) => new(new
    {
        code = status == DeploymentQueueTicketStatus.Running ? "ticket_running" : "ticket_not_cancellable",
        message = status == DeploymentQueueTicketStatus.Running
            ? "任务正在执行，不能直接取消。请通过运行资源的停止或销毁操作进行清理。"
            : "任务状态已变化，无法取消。请刷新后查看实际结果。"
    });
}
