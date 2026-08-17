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
    public Guid? ControlScopeId { get; init; }
    public CreateTeamLabRolloutModel? CreateRollout { get; init; }
    public ReplaceTeamLabRolloutTargetsModel? ReplaceRolloutTargets { get; init; }
    public Guid? RolloutId { get; init; }
    public Guid? RolloutTargetId { get; init; }
    public bool? RolloutAccessOpen { get; init; }
    public CreateTeamLabTopologyModel? CreateTopology { get; init; }
    public Guid? TopologyId { get; init; }
    public UpdateTeamLabTopologyModel? UpdateTopology { get; init; }
    public PublishTeamLabTopologyModel? PublishTopology { get; init; }
    public TeamLabAccessGrantCreateModel? CreateAccessGrant { get; init; }
    public Guid? AccessGrantId { get; init; }
    public CreateTeamLabCaptureModel? CreateCapture { get; init; }
    public Guid? CaptureId { get; init; }
    public Guid? ReleaseId { get; init; }
    public CreateTeamLabWebhookModel? CreateWebhook { get; init; }
    public Guid? WebhookId { get; init; }
    public long? ReplayFromEventId { get; init; }
}

public sealed record TeamLabRuntimeOperationSubmission(
    Guid? ApiTokenId,
    Guid ActorUserId,
    Guid ControlScopeId,
    string RouteKey,
    string IdempotencyKey,
    string RequestHash,
    string ResourceType,
    string? ResourceId,
    TeamLabRuntimeOperationJob Job);

public interface ITeamLabControlPlaneOperationService
{
    Task<IdempotencyBeginResult> SubmitRolloutPrepareAsync(Guid? apiTokenId, Guid actorUserId,
        string idempotencyKey, Guid rolloutId, Guid controlScopeId, CancellationToken cancellationToken);
    Task<IdempotencyBeginResult> SubmitRolloutSetAccessAsync(Guid? apiTokenId, Guid actorUserId,
        string idempotencyKey, Guid rolloutId, Guid controlScopeId, bool open, CancellationToken cancellationToken);
    Task<IdempotencyBeginResult> SubmitRolloutDrainAsync(Guid? apiTokenId, Guid actorUserId,
        string idempotencyKey, Guid rolloutId, Guid controlScopeId, CancellationToken cancellationToken);
    Task<IdempotencyBeginResult> SubmitRolloutPauseAsync(Guid? apiTokenId, Guid actorUserId,
        string idempotencyKey, Guid rolloutId, Guid controlScopeId, CancellationToken cancellationToken);
    Task<IdempotencyBeginResult> SubmitRolloutResumeAsync(Guid? apiTokenId, Guid actorUserId,
        string idempotencyKey, Guid rolloutId, Guid controlScopeId, CancellationToken cancellationToken);
}

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
            throw new ApiOperationTerminalException("teamlab_payload_invalid", "受保护的 TeamLab 操作负载无效。");
        }
    }
}

public sealed class TeamLabRuntimeOperationApplicationService(
    ITeamLabRuntimeOperationSubmissionStore submissions,
    TeamLabRuntimeOperationPayloadProtector protector) : ITeamLabControlPlaneOperationService
{
    public const string OperationKind = "teamlab.runtime.v1";

    public Task<IdempotencyBeginResult> SubmitCreateAsync(
        Guid? apiTokenId,
        Guid actorUserId,
        string idempotencyKey,
        string routeKey,
        Guid controlScopeId,
        CreateTeamLabRuntimeModel command,
        CancellationToken cancellationToken) =>
        SubmitAsync(apiTokenId, actorUserId, idempotencyKey, routeKey, TeamLabRuntimeOperationKind.Create,
            new TeamLabRuntimeOperationPayload(command, null, null) { ControlScopeId = controlScopeId }, cancellationToken);

    public Task<IdempotencyBeginResult> SubmitResetAsync(
        Guid? apiTokenId,
        Guid actorUserId,
        string idempotencyKey,
        string routeKey,
        Guid runtimeId,
        Guid controlScopeId,
        ResetTeamLabRuntimeModel command,
        CancellationToken cancellationToken) =>
        SubmitAsync(apiTokenId, actorUserId, idempotencyKey, routeKey, TeamLabRuntimeOperationKind.Reset,
            new TeamLabRuntimeOperationPayload(null, runtimeId, command) { ControlScopeId = controlScopeId }, cancellationToken);

    public Task<IdempotencyBeginResult> SubmitDestroyAsync(
        Guid? apiTokenId,
        Guid actorUserId,
        string idempotencyKey,
        string routeKey,
        Guid runtimeId,
        Guid controlScopeId,
        CancellationToken cancellationToken) =>
        SubmitAsync(apiTokenId, actorUserId, idempotencyKey, routeKey, TeamLabRuntimeOperationKind.Destroy,
            new TeamLabRuntimeOperationPayload(null, runtimeId, null) { ControlScopeId = controlScopeId }, cancellationToken);

    public Task<IdempotencyBeginResult> SubmitPauseAsync(
        Guid? apiTokenId,
        Guid actorUserId,
        string idempotencyKey,
        Guid runtimeId,
        Guid controlScopeId,
        CancellationToken cancellationToken) =>
        SubmitAsync(apiTokenId, actorUserId, idempotencyKey,
            $"POST:/api/open/v1/teamlab/runtimes/{runtimeId:D}/pause", TeamLabRuntimeOperationKind.RuntimePause,
            new TeamLabRuntimeOperationPayload(null, runtimeId, null) { ControlScopeId = controlScopeId }, cancellationToken);

    public Task<IdempotencyBeginResult> SubmitResumeAsync(
        Guid apiTokenId,
        Guid actorUserId,
        string idempotencyKey,
        Guid runtimeId,
        Guid controlScopeId,
        CancellationToken cancellationToken) =>
        SubmitAsync(apiTokenId, actorUserId, idempotencyKey,
            $"POST:/api/open/v1/teamlab/runtimes/{runtimeId:D}/resume", TeamLabRuntimeOperationKind.RuntimeResume,
            new TeamLabRuntimeOperationPayload(null, runtimeId, null) { ControlScopeId = controlScopeId }, cancellationToken);

    public Task<IdempotencyBeginResult> SubmitRolloutTargetLifecycleAsync(
        Guid? apiTokenId,
        Guid actorUserId,
        string idempotencyKey,
        Guid runtimePublicId,
        Guid rolloutId,
        Guid rolloutTargetId,
        Guid? controlScopeId,
        bool pause,
        CancellationToken cancellationToken)
    {
        var kind = pause ? TeamLabRuntimeOperationKind.RuntimePause : TeamLabRuntimeOperationKind.RuntimeResume;
        var action = pause ? "pause" : "resume";
        return SubmitAsync(apiTokenId, actorUserId, idempotencyKey,
            $"POST:/api/open/v1/teamlab/rollouts/{rolloutId:D}/targets/{rolloutTargetId:D}/{action}",
            kind,
            new TeamLabRuntimeOperationPayload(null, runtimePublicId, null)
            {
                ControlScopeId = controlScopeId,
                RolloutId = rolloutId,
                RolloutTargetId = rolloutTargetId
            }, cancellationToken);
    }

    public Task<IdempotencyBeginResult> SubmitRolloutTargetRestartAsync(
        Guid? apiTokenId,
        Guid actorUserId,
        string idempotencyKey,
        Guid runtimePublicId,
        Guid rolloutId,
        Guid rolloutTargetId,
        Guid? controlScopeId,
        CancellationToken cancellationToken) =>
        SubmitAsync(apiTokenId, actorUserId, idempotencyKey,
            $"POST:/api/open/v1/teamlab/rollouts/{rolloutId:D}/targets/{rolloutTargetId:D}/restart",
            TeamLabRuntimeOperationKind.Reset,
            new TeamLabRuntimeOperationPayload(null, runtimePublicId, null)
            {
                ControlScopeId = controlScopeId,
                RolloutId = rolloutId,
                RolloutTargetId = rolloutTargetId
            }, cancellationToken);

    public Task<IdempotencyBeginResult> SubmitTopologyCreateAsync(
        Guid apiTokenId,
        Guid actorUserId,
        string idempotencyKey,
        CreateTeamLabTopologyModel command,
        CancellationToken cancellationToken) =>
        SubmitAsync(apiTokenId, actorUserId, idempotencyKey,
            "POST:/api/open/v1/teamlab/topologies", TeamLabRuntimeOperationKind.TopologyCreate,
            new TeamLabRuntimeOperationPayload(null, null, null)
            {
                ControlScopeId = command.ControlScopeId,
                CreateTopology = command
            }, cancellationToken);

    public Task<IdempotencyBeginResult> SubmitTopologyUpdateAsync(
        Guid apiTokenId,
        Guid actorUserId,
        string idempotencyKey,
        Guid topologyId,
        Guid controlScopeId,
        UpdateTeamLabTopologyModel command,
        CancellationToken cancellationToken) =>
        SubmitAsync(apiTokenId, actorUserId, idempotencyKey,
            $"PUT:/api/open/v1/teamlab/topologies/{topologyId:D}", TeamLabRuntimeOperationKind.TopologyUpdate,
            new TeamLabRuntimeOperationPayload(null, null, null)
            {
                ControlScopeId = controlScopeId,
                TopologyId = topologyId,
                UpdateTopology = command
            }, cancellationToken);

    public Task<IdempotencyBeginResult> SubmitTopologyDeleteAsync(
        Guid apiTokenId,
        Guid actorUserId,
        string idempotencyKey,
        Guid topologyId,
        Guid controlScopeId,
        CancellationToken cancellationToken) =>
        SubmitAsync(apiTokenId, actorUserId, idempotencyKey,
            $"DELETE:/api/open/v1/teamlab/topologies/{topologyId:D}", TeamLabRuntimeOperationKind.TopologyDelete,
            new TeamLabRuntimeOperationPayload(null, null, null)
            {
                ControlScopeId = controlScopeId,
                TopologyId = topologyId
            }, cancellationToken);

    public Task<IdempotencyBeginResult> SubmitTopologyPublishAsync(
        Guid apiTokenId,
        Guid actorUserId,
        string idempotencyKey,
        Guid topologyId,
        Guid controlScopeId,
        PublishTeamLabTopologyModel command,
        CancellationToken cancellationToken) =>
        SubmitAsync(apiTokenId, actorUserId, idempotencyKey,
            $"POST:/api/open/v1/teamlab/topologies/{topologyId:D}/releases",
            TeamLabRuntimeOperationKind.TopologyPublish,
            new TeamLabRuntimeOperationPayload(null, null, null)
            {
                ControlScopeId = controlScopeId,
                TopologyId = topologyId,
                PublishTopology = command
            }, cancellationToken);

    public Task<IdempotencyBeginResult> SubmitAccessGrantCreateAsync(
        Guid apiTokenId,
        Guid actorUserId,
        string idempotencyKey,
        Guid runtimeId,
        Guid controlScopeId,
        TeamLabAccessGrantCreateModel command,
        CancellationToken cancellationToken) =>
        SubmitAsync(apiTokenId, actorUserId, idempotencyKey,
            $"POST:/api/open/v1/teamlab/runtimes/{runtimeId:D}/access-grants",
            TeamLabRuntimeOperationKind.AccessGrantCreate,
            new TeamLabRuntimeOperationPayload(null, runtimeId, null)
            {
                ControlScopeId = controlScopeId,
                CreateAccessGrant = command
            },
            cancellationToken);

    public Task<IdempotencyBeginResult> SubmitAccessGrantRevokeAsync(
        Guid apiTokenId,
        Guid actorUserId,
        string idempotencyKey,
        Guid runtimeId,
        Guid controlScopeId,
        Guid grantId,
        CancellationToken cancellationToken) =>
        SubmitAsync(apiTokenId, actorUserId, idempotencyKey,
            $"DELETE:/api/open/v1/teamlab/runtimes/{runtimeId:D}/access-grants/{grantId:D}",
            TeamLabRuntimeOperationKind.AccessGrantRevoke,
            new TeamLabRuntimeOperationPayload(null, runtimeId, null)
            {
                ControlScopeId = controlScopeId,
                AccessGrantId = grantId
            },
            cancellationToken);

    public Task<IdempotencyBeginResult> SubmitCaptureStartAsync(
        Guid apiTokenId,
        Guid actorUserId,
        string idempotencyKey,
        Guid runtimeId,
        Guid controlScopeId,
        CreateTeamLabCaptureModel command,
        CancellationToken cancellationToken) =>
        SubmitAsync(apiTokenId, actorUserId, idempotencyKey,
            $"POST:/api/open/v1/teamlab/runtimes/{runtimeId:D}/captures",
            TeamLabRuntimeOperationKind.CaptureStart,
            new TeamLabRuntimeOperationPayload(null, runtimeId, null)
            {
                ControlScopeId = controlScopeId,
                CreateCapture = command
            },
            cancellationToken);

    public Task<IdempotencyBeginResult> SubmitCaptureStopAsync(
        Guid apiTokenId,
        Guid actorUserId,
        string idempotencyKey,
        Guid runtimeId,
        Guid controlScopeId,
        Guid captureId,
        CancellationToken cancellationToken) =>
        SubmitAsync(apiTokenId, actorUserId, idempotencyKey,
            $"POST:/api/open/v1/teamlab/runtimes/{runtimeId:D}/captures/{captureId:D}/stop",
            TeamLabRuntimeOperationKind.CaptureStop,
            new TeamLabRuntimeOperationPayload(null, runtimeId, null)
            {
                ControlScopeId = controlScopeId,
                CaptureId = captureId
            },
            cancellationToken);

    public Task<IdempotencyBeginResult> SubmitRolloutCreateAsync(
        Guid apiTokenId,
        Guid actorUserId,
        string idempotencyKey,
        CreateTeamLabRolloutModel command,
        CancellationToken cancellationToken) =>
        SubmitAsync(apiTokenId, actorUserId, idempotencyKey,
            "POST:/api/open/v1/teamlab/rollouts", TeamLabRuntimeOperationKind.RolloutCreate,
            new TeamLabRuntimeOperationPayload(null, null, null)
            {
                ControlScopeId = command.ControlScopeId,
                CreateRollout = command
            }, cancellationToken);

    public Task<IdempotencyBeginResult> SubmitRolloutReplaceTargetsAsync(
        Guid apiTokenId,
        Guid actorUserId,
        string idempotencyKey,
        Guid rolloutId,
        ReplaceTeamLabRolloutTargetsModel command,
        Guid controlScopeId,
        CancellationToken cancellationToken) =>
        SubmitAsync(apiTokenId, actorUserId, idempotencyKey,
            $"PUT:/api/open/v1/teamlab/rollouts/{rolloutId:D}/targets",
            TeamLabRuntimeOperationKind.RolloutReplaceTargets,
            new TeamLabRuntimeOperationPayload(null, null, null)
            {
                ControlScopeId = controlScopeId,
                RolloutId = rolloutId,
                ReplaceRolloutTargets = command
            }, cancellationToken);

    public Task<IdempotencyBeginResult> SubmitRolloutPrepareAsync(
        Guid? apiTokenId,
        Guid actorUserId,
        string idempotencyKey,
        Guid rolloutId,
        Guid controlScopeId,
        CancellationToken cancellationToken) =>
        SubmitRolloutCommandAsync(apiTokenId, actorUserId, idempotencyKey, rolloutId, controlScopeId,
            "prepare", TeamLabRuntimeOperationKind.RolloutPrepare, cancellationToken);

    public Task<IdempotencyBeginResult> SubmitRolloutSetAccessAsync(
        Guid? apiTokenId,
        Guid actorUserId,
        string idempotencyKey,
        Guid rolloutId,
        Guid controlScopeId,
        bool open,
        CancellationToken cancellationToken) =>
        SubmitAsync(apiTokenId, actorUserId, idempotencyKey,
            $"POST:/api/open/v1/teamlab/rollouts/{rolloutId:D}/{(open ? "open-access" : "close-access")}",
            TeamLabRuntimeOperationKind.RolloutSetAccess,
            new TeamLabRuntimeOperationPayload(null, null, null)
            {
                ControlScopeId = controlScopeId,
                RolloutId = rolloutId,
                RolloutAccessOpen = open
            }, cancellationToken);

    public Task<IdempotencyBeginResult> SubmitRolloutDrainAsync(
        Guid? apiTokenId,
        Guid actorUserId,
        string idempotencyKey,
        Guid rolloutId,
        Guid controlScopeId,
        CancellationToken cancellationToken) =>
        SubmitRolloutCommandAsync(apiTokenId, actorUserId, idempotencyKey, rolloutId, controlScopeId,
            "drain", TeamLabRuntimeOperationKind.RolloutDrain, cancellationToken);

    public Task<IdempotencyBeginResult> SubmitRolloutRebuildTargetAsync(
        Guid apiTokenId,
        Guid actorUserId,
        string idempotencyKey,
        Guid rolloutId,
        Guid targetId,
        Guid controlScopeId,
        CancellationToken cancellationToken) =>
        SubmitAsync(apiTokenId, actorUserId, idempotencyKey,
            $"POST:/api/open/v1/teamlab/rollouts/{rolloutId:D}/targets/{targetId:D}/rebuild",
            TeamLabRuntimeOperationKind.RolloutRebuildTarget,
            new TeamLabRuntimeOperationPayload(null, null, null)
            {
                ControlScopeId = controlScopeId,
                RolloutId = rolloutId,
                RolloutTargetId = targetId
            }, cancellationToken);

    public Task<IdempotencyBeginResult> SubmitRolloutArchiveAsync(
        Guid apiTokenId,
        Guid actorUserId,
        string idempotencyKey,
        Guid rolloutId,
        Guid controlScopeId,
        CancellationToken cancellationToken) =>
        SubmitRolloutCommandAsync(apiTokenId, actorUserId, idempotencyKey, rolloutId, controlScopeId,
            "archive", TeamLabRuntimeOperationKind.RolloutArchive, cancellationToken);

    public Task<IdempotencyBeginResult> SubmitRolloutPauseAsync(
        Guid? apiTokenId,
        Guid actorUserId,
        string idempotencyKey,
        Guid rolloutId,
        Guid controlScopeId,
        CancellationToken cancellationToken) =>
        SubmitRolloutCommandAsync(apiTokenId, actorUserId, idempotencyKey, rolloutId, controlScopeId,
            "pause", TeamLabRuntimeOperationKind.RolloutPause, cancellationToken);

    public Task<IdempotencyBeginResult> SubmitRolloutResumeAsync(
        Guid? apiTokenId,
        Guid actorUserId,
        string idempotencyKey,
        Guid rolloutId,
        Guid controlScopeId,
        CancellationToken cancellationToken) =>
        SubmitRolloutCommandAsync(apiTokenId, actorUserId, idempotencyKey, rolloutId, controlScopeId,
            "resume", TeamLabRuntimeOperationKind.RolloutResume, cancellationToken);

    public Task<IdempotencyBeginResult> SubmitReleasePreparationAsync(
        Guid apiTokenId,
        Guid actorUserId,
        string idempotencyKey,
        Guid releaseId,
        Guid controlScopeId,
        CancellationToken cancellationToken) =>
        SubmitAsync(apiTokenId, actorUserId, idempotencyKey,
            $"POST:/api/open/v1/teamlab/preparations/releases/{releaseId:D}",
            TeamLabRuntimeOperationKind.ReleasePreparation,
            new TeamLabRuntimeOperationPayload(null, null, null)
            {
                ControlScopeId = controlScopeId,
                ReleaseId = releaseId
            }, cancellationToken);

    public Task<IdempotencyBeginResult> SubmitReleasePreparationReleaseAsync(
        Guid apiTokenId,
        Guid actorUserId,
        string idempotencyKey,
        Guid releaseId,
        Guid controlScopeId,
        CancellationToken cancellationToken) =>
        SubmitAsync(apiTokenId, actorUserId, idempotencyKey,
            $"DELETE:/api/open/v1/teamlab/preparations/releases/{releaseId:D}",
            TeamLabRuntimeOperationKind.ReleasePreparationRelease,
            new TeamLabRuntimeOperationPayload(null, null, null)
            {
                ControlScopeId = controlScopeId,
                ReleaseId = releaseId
            }, cancellationToken);

    public Task<IdempotencyBeginResult> SubmitWebhookCreateAsync(
        Guid apiTokenId,
        Guid actorUserId,
        string idempotencyKey,
        CreateTeamLabWebhookModel command,
        CancellationToken cancellationToken) =>
        SubmitAsync(apiTokenId, actorUserId, idempotencyKey,
            "POST:/api/open/v1/teamlab/webhooks",
            TeamLabRuntimeOperationKind.WebhookCreate,
            new TeamLabRuntimeOperationPayload(null, null, null)
            {
                ControlScopeId = command.ControlScopeId,
                CreateWebhook = command
            }, cancellationToken);

    public Task<IdempotencyBeginResult> SubmitWebhookRevokeAsync(
        Guid apiTokenId,
        Guid actorUserId,
        string idempotencyKey,
        Guid webhookId,
        Guid controlScopeId,
        CancellationToken cancellationToken) =>
        SubmitAsync(apiTokenId, actorUserId, idempotencyKey,
            $"DELETE:/api/open/v1/teamlab/webhooks/{webhookId:D}",
            TeamLabRuntimeOperationKind.WebhookRevoke,
            new TeamLabRuntimeOperationPayload(null, null, null)
            {
                ControlScopeId = controlScopeId,
                WebhookId = webhookId
            }, cancellationToken);

    public Task<IdempotencyBeginResult> SubmitWebhookReplayAsync(
        Guid apiTokenId,
        Guid actorUserId,
        string idempotencyKey,
        Guid webhookId,
        Guid controlScopeId,
        long? fromEventId,
        CancellationToken cancellationToken) =>
        SubmitAsync(apiTokenId, actorUserId, idempotencyKey,
            $"POST:/api/open/v1/teamlab/webhooks/{webhookId:D}/replay",
            TeamLabRuntimeOperationKind.WebhookReplay,
            new TeamLabRuntimeOperationPayload(null, null, null)
            {
                ControlScopeId = controlScopeId,
                WebhookId = webhookId,
                ReplayFromEventId = fromEventId
            }, cancellationToken);

    private Task<IdempotencyBeginResult> SubmitRolloutCommandAsync(
        Guid? apiTokenId,
        Guid actorUserId,
        string idempotencyKey,
        Guid rolloutId,
        Guid controlScopeId,
        string action,
        TeamLabRuntimeOperationKind kind,
        CancellationToken cancellationToken) =>
        SubmitAsync(apiTokenId, actorUserId, idempotencyKey,
            $"POST:/api/open/v1/teamlab/rollouts/{rolloutId:D}/{action}", kind,
            new TeamLabRuntimeOperationPayload(null, null, null)
            {
                ControlScopeId = controlScopeId,
                RolloutId = rolloutId
            }, cancellationToken);

    private Task<IdempotencyBeginResult> SubmitAsync(
        Guid? apiTokenId,
        Guid actorUserId,
        string idempotencyKey,
        string routeKey,
        TeamLabRuntimeOperationKind kind,
        TeamLabRuntimeOperationPayload payload,
        CancellationToken cancellationToken)
    {
        var normalizedKey = ExternalIdempotencyKey.Normalize(idempotencyKey);
        var scopeId = payload.ControlScopeId
            ?? throw new TeamLabApiContractException(
                "teamlab_scope_missing", "TeamLab 控制范围缺失。", 422);
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
            apiTokenId, actorUserId, scopeId, $"{routeKey.Trim()}#scope:{scopeId:D}", normalizedKey, requestHash,
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
            TeamLabRuntimeOperationKind.ReleasePreparation =>
                ("teamlab-release", payload.ReleaseId?.ToString("D")),
            TeamLabRuntimeOperationKind.WebhookCreate => ("teamlab-webhook", null),
            TeamLabRuntimeOperationKind.WebhookRevoke or TeamLabRuntimeOperationKind.WebhookReplay =>
                ("teamlab-webhook", payload.WebhookId?.ToString("D")),
            TeamLabRuntimeOperationKind.RolloutCreate => ("teamlab-rollout", null),
            TeamLabRuntimeOperationKind.RolloutReplaceTargets or
                TeamLabRuntimeOperationKind.RolloutPrepare or
                TeamLabRuntimeOperationKind.RolloutSetAccess or
                TeamLabRuntimeOperationKind.RolloutDrain or
                TeamLabRuntimeOperationKind.RolloutRebuildTarget or
                TeamLabRuntimeOperationKind.RolloutArchive or
                TeamLabRuntimeOperationKind.RolloutPause or
                TeamLabRuntimeOperationKind.RolloutResume =>
                ("teamlab-rollout", payload.RolloutId?.ToString("D")),
            _ => ("teamlab-runtime", payload.RuntimeId?.ToString("D"))
        };

}
