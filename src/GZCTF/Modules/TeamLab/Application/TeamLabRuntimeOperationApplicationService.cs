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
    ResetTeamLabRuntimeModel? Reset);

public sealed record TeamLabRuntimeOperationSubmission(
    Guid ApiTokenId,
    Guid ActorUserId,
    string RouteKey,
    string IdempotencyKey,
    string RequestHash,
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

    private Task<IdempotencyBeginResult> SubmitAsync(
        Guid apiTokenId,
        Guid actorUserId,
        string idempotencyKey,
        string routeKey,
        TeamLabRuntimeOperationKind kind,
        TeamLabRuntimeOperationPayload payload,
        CancellationToken cancellationToken)
    {
        var normalizedKey = NormalizeIdempotencyKey(idempotencyKey);
        var payloadBytes = JsonSerializer.SerializeToUtf8Bytes(new { kind, payload });
        var requestHash = Convert.ToHexStringLower(SHA256.HashData(payloadBytes));
        var job = new TeamLabRuntimeOperationJob
        {
            Kind = kind,
            RuntimePublicId = payload.RuntimeId,
            ProtectedPayload = protector.Protect(payload),
            PayloadHash = $"sha256:{requestHash}"
        };
        return submissions.SubmitAsync(new TeamLabRuntimeOperationSubmission(
            apiTokenId, actorUserId, routeKey.Trim(), normalizedKey, requestHash, job), cancellationToken);
    }

    private static string NormalizeIdempotencyKey(string value)
    {
        var normalized = value.Trim();
        if (normalized.Length is < 1 or > 128 || normalized.Any(character =>
                !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_' and not '.'))
            throw new IdempotencyValidationException(
                string.IsNullOrEmpty(normalized) ? "idempotency_key_required" : "idempotency_key_invalid",
                "Idempotency-Key must contain 1-128 ASCII letters, digits, '-', '_' or '.'.");
        return normalized;
    }
}
