using GZCTF.Agent.Models;
using GZCTF.Agent.Services;
using GZCTF.Agent.Services.RemoteAccess;
using Microsoft.AspNetCore.Mvc;
using System.Net.WebSockets;

namespace GZCTF.Agent.Controllers;

[ApiController]
[Route("api/remote-access")]
public sealed class RemoteAccessController(RemoteAccessRelayService relays) : ControllerBase
{
    [HttpPost("relays")]
    public Task<RemoteRelayResponse> CreateRelay(CreateRemoteRelayRequest request, CancellationToken cancellationToken) =>
        relays.CreateAsync(request, cancellationToken);

    [HttpDelete("relays/{sessionId:guid}")]
    public async Task<IActionResult> DeleteRelay(Guid sessionId)
    {
        await relays.DeleteAsync(sessionId);
        return NoContent();
    }

    [HttpGet("terminals/{sessionId:guid}")]
    public async Task Terminal(
        Guid sessionId,
        [FromQuery] int runtimeId,
        [FromQuery] int generation,
        [FromQuery] string containerId,
        [FromQuery] DateTimeOffset expiresAt,
        [FromServices] DockerService docker,
        CancellationToken cancellationToken)
    {
        if (!HttpContext.WebSockets.IsWebSocketRequest)
        {
            HttpContext.Response.StatusCode = StatusCodes.Status426UpgradeRequired;
            return;
        }
        if (sessionId == Guid.Empty || runtimeId <= 0 || generation <= 0 ||
            string.IsNullOrWhiteSpace(containerId) || expiresAt <= DateTimeOffset.UtcNow ||
            expiresAt > DateTimeOffset.UtcNow.AddHours(2))
            throw new AgentOperationException("RemoteAccess", "remote_access.invalid_terminal_request",
                "The terminal request is invalid.", false);

        using var expiry = new CancellationTokenSource(expiresAt - DateTimeOffset.UtcNow);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, expiry.Token);
        using var socket = await HttpContext.WebSockets.AcceptWebSocketAsync();
        await docker.RunTeamLabTerminalAsync(runtimeId, generation, containerId, socket, linked.Token);
        if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "terminal_closed", CancellationToken.None);
    }
}
