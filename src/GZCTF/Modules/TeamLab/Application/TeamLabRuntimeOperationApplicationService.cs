using System.Security.Cryptography;
using System.Text.Json;
using GZCTF.Modules.Audit.Application;
using GZCTF.Modules.Audit.Domain;
using GZCTF.Modules.TeamLab.Contracts;
using GZCTF.Modules.TeamLab.Domain;
using Microsoft.AspNetCore.DataProtection;

namespace GZCTF.Modules.TeamLab.Application;

public sealed record TeamLabRuntimeOperationPayload(
    CreateTeamLabRuntimeModel? Create,
    Guid? RuntimeId,
    ResetTeamLabRuntimeModel? Reset)
{
    public CreateTeamLabTopologyModel? CreateTopology { get; init; }
    public Guid? TopologyId { get; init; }
    public UpdateTeamLabTopologyModel? UpdateTopology { get; init; }
    public PublishTeamLabTopologyModel? PublishTopology { get; init; }
    public TeamLabAccessGrantCreateModel? CreateAccessGrant { get; init; }
    public Guid? AccessGrantId { get; init; }
    public CreateTeamLabCaptureModel? CreateCapture { get; init; }
    public Guid? CaptureId { get; init; }
}

public sealed record TeamLabRuntimeOperationSubmission(
    Guid ApiTokenId,
    Guid ActorUserId,
    string RouteKey,
    string IdempotencyKey,
    string RequestHash,
    string ResourceType,
    string? ResourceId,
    TeamLabRuntimeOperationJob Job);

public interface ITeamLabRuntimeOperationSubmissionStore
{
    Task<IdempotencyBeginResult> SubmitAsync(TeamLabRuntimeOperationSubmission submission, CancellationToken cancellationToken);
}

public sealed class TeamLabRuntimeOperationPayloadProtector(IDataProtectionProvider provider)
{
    private readonly IDataProtector _protector = provider.CreateProtector("GZCTF.TeamLab.Operation.v1");

    public string Protect(TeamLabRuntimeOperationPayload payload) =>
        _protector.Protect(JsonSerializer.Serialize(payload));

    public TeamLabRuntimeOperationPayload Unprotect(string payload)
    {
        try
        {
            return JsonSerializer.Deserialize<TeamLabRuntimeOperationPayload>(_protector.Unprotect(payload))
                   ?? throw new JsonException();
        }
        catch (Exception exception) when (exception is CryptographicException or JsonException)
        {
            throw new ApiOperationTerminalException("teamlab_payload_invalid", "The protected TeamLab operation payload is invalid.");
        }
    }
}

public sealed class TeamLabRuntimeOperationApplicationService(
    ITeamLabRuntimeOperationSubmissionStore submissions,
    TeamLabRuntimeOperationPayloadProtector protector)
{
    public const string OperationKind = "teamlab.runtime.v1";

    public Task<IdempotencyBeginResult> SubmitCreateAsync(
        Guid apiTokenId,
        Guid actorUserId,
        string idempotencyKey,
        string routeKey,
        CreateTeamLabRuntimeModel command,
        CancellationToken cancellationToken) =>
        SubmitAsync(apiTokenId, actorUserId, idempotencyKey, routeKey, TeamLabRuntimeOperationKind.Create,
            new TeamLabRuntimeOperationPayload(command, null, null), cancellationToken);

    public Task<IdempotencyBeginResult> SubmitResetAsync(
        Guid apiTokenId,
        Guid actorUserId,
        string idempotencyKey,
        string routeKey,
        Guid runtimeId,
        ResetTeamLabRuntimeModel command,
        CancellationToken cancellationToken) =>
        SubmitAsync(apiTokenId, actorUserId, idempotencyKey, routeKey, TeamLabRuntimeOperationKind.Reset,
            new TeamLabRuntimeOperationPayload(null, runtimeId, command), cancellationToken);

    public Task<IdempotencyBeginResult> SubmitDestroyAsync(
        Guid apiTokenId,
        Guid actorUserId,
        string idempotencyKey,
        string routeKey,
        Guid runtimeId,
        CancellationToken cancellationToken) =>
        SubmitAsync(apiTokenId, actorUserId, idempotencyKey, routeKey, TeamLabRuntimeOperationKind.Destroy,
            new TeamLabRuntimeOperationPayload(null, runtimeId, null), cancellationToken);

    public Task<IdempotencyBeginResult> SubmitTopologyCreateAsync(
        Guid apiTokenId,
        Guid actorUserId,
        string idempotencyKey,
        CreateTeamLabTopologyModel command,
        CancellationToken cancellationToken) =>
        SubmitAsync(apiTokenId, actorUserId, idempotencyKey,
            "POST:/api/open/v1/teamlab/topologies", TeamLabRuntimeOperationKind.TopologyCreate,
            new TeamLabRuntimeOperationPayload(null, null, null) { CreateTopology = command }, cancellationToken);

    public Task<IdempotencyBeginResult> SubmitTopologyUpdateAsync(
        Guid apiTokenId,
        Guid actorUserId,
        string idempotencyKey,
        Guid topologyId,
        UpdateTeamLabTopologyModel command,
        CancellationToken cancellationToken) =>
        SubmitAsync(apiTokenId, actorUserId, idempotencyKey,
            $"PUT:/api/open/v1/teamlab/topologies/{topologyId:D}", TeamLabRuntimeOperationKind.TopologyUpdate,
            new TeamLabRuntimeOperationPayload(null, null, null)
            {
                TopologyId = topologyId,
                UpdateTopology = command
            }, cancellationToken);

    public Task<IdempotencyBeginResult> SubmitTopologyDeleteAsync(
        Guid apiTokenId,
        Guid actorUserId,
        string idempotencyKey,
        Guid topologyId,
        CancellationToken cancellationToken) =>
        SubmitAsync(apiTokenId, actorUserId, idempotencyKey,
            $"DELETE:/api/open/v1/teamlab/topologies/{topologyId:D}", TeamLabRuntimeOperationKind.TopologyDelete,
            new TeamLabRuntimeOperationPayload(null, null, null) { TopologyId = topologyId }, cancellationToken);

    public Task<IdempotencyBeginResult> SubmitTopologyPublishAsync(
        Guid apiTokenId,
        Guid actorUserId,
        string idempotencyKey,
        Guid topologyId,
        PublishTeamLabTopologyModel command,
        CancellationToken cancellationToken) =>
        SubmitAsync(apiTokenId, actorUserId, idempotencyKey,
            $"POST:/api/open/v1/teamlab/topologies/{topologyId:D}/releases",
            TeamLabRuntimeOperationKind.TopologyPublish,
            new TeamLabRuntimeOperationPayload(null, null, null)
            {
                TopologyId = topologyId,
                PublishTopology = command
            }, cancellationToken);

    public Task<IdempotencyBeginResult> SubmitAccessGrantCreateAsync(
        Guid apiTokenId,
        Guid actorUserId,
        string idempotencyKey,
        Guid runtimeId,
        TeamLabAccessGrantCreateModel command,
        CancellationToken cancellationToken) =>
        SubmitAsync(apiTokenId, actorUserId, idempotencyKey,
            $"POST:/api/open/v1/teamlab/runtimes/{runtimeId:D}/access-grants",
            TeamLabRuntimeOperationKind.AccessGrantCreate,
            new TeamLabRuntimeOperationPayload(null, runtimeId, null) { CreateAccessGrant = command },
            cancellationToken);

    public Task<IdempotencyBeginResult> SubmitAccessGrantRevokeAsync(
        Guid apiTokenId,
        Guid actorUserId,
        string idempotencyKey,
        Guid runtimeId,
        Guid grantId,
        CancellationToken cancellationToken) =>
        SubmitAsync(apiTokenId, actorUserId, idempotencyKey,
            $"DELETE:/api/open/v1/teamlab/runtimes/{runtimeId:D}/access-grants/{grantId:D}",
            TeamLabRuntimeOperationKind.AccessGrantRevoke,
            new TeamLabRuntimeOperationPayload(null, runtimeId, null) { AccessGrantId = grantId },
            cancellationToken);

    public Task<IdempotencyBeginResult> SubmitCaptureStartAsync(
        Guid apiTokenId,
        Guid actorUserId,
        string idempotencyKey,
        Guid runtimeId,
        CreateTeamLabCaptureModel command,
        CancellationToken cancellationToken) =>
        SubmitAsync(apiTokenId, actorUserId, idempotencyKey,
            $"POST:/api/open/v1/teamlab/runtimes/{runtimeId:D}/captures",
            TeamLabRuntimeOperationKind.CaptureStart,
            new TeamLabRuntimeOperationPayload(null, runtimeId, null) { CreateCapture = command },
            cancellationToken);

    public Task<IdempotencyBeginResult> SubmitCaptureStopAsync(
        Guid apiTokenId,
        Guid actorUserId,
        string idempotencyKey,
        Guid runtimeId,
        Guid captureId,
        CancellationToken cancellationToken) =>
        SubmitAsync(apiTokenId, actorUserId, idempotencyKey,
            $"POST:/api/open/v1/teamlab/runtimes/{runtimeId:D}/captures/{captureId:D}/stop",
            TeamLabRuntimeOperationKind.CaptureStop,
            new TeamLabRuntimeOperationPayload(null, runtimeId, null) { CaptureId = captureId },
            cancellationToken);

    private Task<IdempotencyBeginResult> SubmitAsync(
        Guid apiTokenId,
        Guid actorUserId,
        string idempotencyKey,
        string routeKey,
        TeamLabRuntimeOperationKind kind,
        TeamLabRuntimeOperationPayload payload,
        CancellationToken cancellationToken)
    {
        var normalizedKey = ExternalIdempotencyKey.Normalize(idempotencyKey);
        var payloadBytes = JsonSerializer.SerializeToUtf8Bytes(new { kind, payload });
        var requestHash = Convert.ToHexStringLower(SHA256.HashData(payloadBytes));
        var job = new TeamLabRuntimeOperationJob
        {
            Kind = kind,
            RuntimePublicId = payload.RuntimeId,
            ProtectedPayload = protector.Protect(payload),
            PayloadHash = $"sha256:{requestHash}"
        };
        var (resourceType, resourceId) = ResolveResource(kind, payload);
        return submissions.SubmitAsync(new TeamLabRuntimeOperationSubmission(
            apiTokenId, actorUserId, routeKey.Trim(), normalizedKey, requestHash,
            resourceType, resourceId, job), cancellationToken);
    }

    private static (string Type, string? Id) ResolveResource(
        TeamLabRuntimeOperationKind kind,
        TeamLabRuntimeOperationPayload payload) => kind switch
        {
            TeamLabRuntimeOperationKind.TopologyCreate => ("teamlab-topology", null),
            TeamLabRuntimeOperationKind.TopologyUpdate or TeamLabRuntimeOperationKind.TopologyDelete or
                TeamLabRuntimeOperationKind.TopologyPublish =>
                ("teamlab-topology", payload.TopologyId?.ToString("D")),
            TeamLabRuntimeOperationKind.AccessGrantRevoke =>
                ("teamlab-access-grant", payload.AccessGrantId?.ToString("D")),
            TeamLabRuntimeOperationKind.CaptureStop =>
                ("teamlab-capture", payload.CaptureId?.ToString("D")),
            _ => ("teamlab-runtime", payload.RuntimeId?.ToString("D"))
        };

}
