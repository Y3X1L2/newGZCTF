using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using GZCTF.Models;
using GZCTF.Modules.Audit.Application;
using GZCTF.Modules.Audit.Domain;
using GZCTF.Modules.Ctf.Contracts;
using GZCTF.Modules.Ctf.Domain;
using GZCTF.Modules.Identity.Application;
using GZCTF.Utils;

namespace GZCTF.Modules.Ctf.Application;

public sealed record ChallengeMutationSubmission(
    Guid ApiTokenId,
    Guid ActorUserId,
    string RouteKey,
    string IdempotencyKey,
    string RequestHash,
    ChallengeMutationJob Job);

public interface IChallengeMutationSubmissionStore
{
    Task<IdempotencyBeginResult> SubmitAsync(
        ChallengeMutationSubmission submission,
        CancellationToken cancellationToken);
}

public interface IExternalChallengeCatalog
{
    Task<bool> GameExistsAsync(int gameId, CancellationToken cancellationToken);
    Task<IReadOnlySet<int>> FindReadyWindowsTemplateIdsAsync(
        IReadOnlyCollection<int> templateIds,
        CancellationToken cancellationToken);
    Task<IReadOnlyDictionary<string, int>> FindReadyDockerImagesAsync(
        IReadOnlyCollection<string> images,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<OpenChallengeSummaryModel>> ListAsync(
        int gameId,
        int limit,
        int? afterId,
        CancellationToken cancellationToken);
    Task<OpenChallengeModel?> FindAsync(
        int gameId,
        int challengeId,
        CancellationToken cancellationToken);
    Task<OpenAwdpServicePageModel> ListAwdpAsync(int gameId, int limit, int? afterId, CancellationToken cancellationToken);
    Task<OpenAwdpServiceModel?> FindAwdpAsync(int gameId, int serviceId, CancellationToken cancellationToken);
}

public sealed record ChallengeImportPayload(IReadOnlyList<OpenChallengeImportModel> Items);
public sealed record ChallengeDeletePayload(IReadOnlyList<int> ChallengeIds);
public sealed record AwdpImportPayload(IReadOnlyList<OpenAwdpServiceImportModel> Items);
public sealed record AwdpDeletePayload(IReadOnlyList<int> ServiceIds);

public sealed class ChallengeExternalApplicationService(
    IChallengeMutationSubmissionStore submissions,
    IExternalChallengeCatalog catalog)
{
    public const string OperationKind = "ctf.challenge-mutation.v1";
    public const int MaximumBatchSize = 100;

    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IdempotencyBeginResult> SubmitImportAsync(
        int gameId,
        Guid apiTokenId,
        ActorContext actor,
        string idempotencyKey,
        IReadOnlyList<OpenChallengeImportModel> items,
        string routeKey,
        CancellationToken cancellationToken)
    {
        var actorUserId = actor.UserId ?? throw new ChallengeApiContractException(
            "authentication_required", "Authentication is required.", 401);
        await RequireGameAsync(gameId, cancellationToken);
        if (items is null || items.Count is < 1 or > MaximumBatchSize)
            throw new ChallengeApiContractException(
                "challenge_batch_size_invalid",
                $"A challenge import must contain between 1 and {MaximumBatchSize} items.",
                422);

        var normalized = items.Select(Normalize).ToArray();
        var duplicateExternalId = normalized.GroupBy(item => item.ExternalId, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1)?.Key;
        if (duplicateExternalId is not null)
            throw new ChallengeApiContractException(
                "challenge_external_id_duplicate",
                $"External ID '{duplicateExternalId}' occurs more than once in the batch.",
                422);

        var windowsTemplateIds = normalized
            .Where(item => item.Environment == EnvironmentType.WindowsVM)
            .Select(item => item.ImageTemplateId!.Value)
            .Distinct()
            .ToArray();
        var readyWindowsTemplateIds = await catalog.FindReadyWindowsTemplateIdsAsync(
            windowsTemplateIds, cancellationToken);
        var invalidWindowsTemplateId = windowsTemplateIds.FirstOrDefault(
            templateId => !readyWindowsTemplateIds.Contains(templateId));
        if (invalidWindowsTemplateId > 0)
            throw new ChallengeApiContractException(
                "challenge_vm_template_invalid",
                $"Image template {invalidWindowsTemplateId} is not a ready Windows VM template.",
                422);

        var dockerImages = normalized
            .Where(item => item.Environment == EnvironmentType.Docker)
            .Select(item => item.ContainerImage!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var readyDockerImages = await catalog.FindReadyDockerImagesAsync(dockerImages, cancellationToken);
        var invalidDockerImage = dockerImages.FirstOrDefault(
            image => !readyDockerImages.ContainsKey(image));
        if (invalidDockerImage is not null)
        {
            throw new ChallengeApiContractException(
                "challenge_docker_image_unregistered",
                $"Docker image '{invalidDockerImage}' is not a ready platform image template.",
                422);
        }
        foreach (var item in normalized.Where(item => item.Environment == EnvironmentType.Docker))
            item.ImageTemplateId = readyDockerImages[item.ContainerImage!];

        var payload = new ChallengeImportPayload(normalized);
        return await SubmitAsync(
            gameId,
            apiTokenId,
            actorUserId,
            routeKey,
            idempotencyKey,
            ChallengeMutationKind.Import,
            payload,
            cancellationToken);
    }

    public async Task<IdempotencyBeginResult> SubmitDeleteAsync(
        int gameId,
        Guid apiTokenId,
        ActorContext actor,
        string idempotencyKey,
        IReadOnlyList<int> challengeIds,
        string routeKey,
        CancellationToken cancellationToken)
    {
        var actorUserId = actor.UserId ?? throw new ChallengeApiContractException(
            "authentication_required", "Authentication is required.", 401);
        await RequireGameAsync(gameId, cancellationToken);
        if (challengeIds is null)
            throw new ChallengeApiContractException(
                "challenge_delete_set_invalid", "Challenge IDs are required.", 422);
        var normalized = challengeIds.Distinct().Order().ToArray();
        if (normalized.Length is < 1 or > MaximumBatchSize || normalized.Any(id => id <= 0))
            throw new ChallengeApiContractException(
                "challenge_delete_set_invalid",
                $"A challenge deletion must contain between 1 and {MaximumBatchSize} positive IDs.",
                422);

        return await SubmitAsync(
            gameId,
            apiTokenId,
            actorUserId,
            routeKey,
            idempotencyKey,
            ChallengeMutationKind.Delete,
            new ChallengeDeletePayload(normalized),
            cancellationToken);
    }

    public async Task<IdempotencyBeginResult> SubmitAwdpImportAsync(
        int gameId, Guid apiTokenId, ActorContext actor, string idempotencyKey,
        IReadOnlyList<OpenAwdpServiceImportModel> items, string routeKey,
        CancellationToken cancellationToken)
    {
        var actorUserId = actor.UserId ?? throw new ChallengeApiContractException("authentication_required", "Authentication is required.", 401);
        await RequireGameAsync(gameId, cancellationToken);
        if (items is null || items.Count is < 1 or > MaximumBatchSize)
            throw new ChallengeApiContractException("awdp_batch_size_invalid", $"An AWDP import must contain between 1 and {MaximumBatchSize} items.", 422);
        var normalized = items.Select(NormalizeAwdp).ToArray();
        if (normalized.GroupBy(item => item.ExternalId, StringComparer.Ordinal).Any(group => group.Count() > 1))
            throw new ChallengeApiContractException("awdp_external_id_duplicate", "AWDP external IDs must be unique within a batch.", 422);
        var images = normalized.Select(item => item.ImageName).Distinct(StringComparer.Ordinal).ToArray();
        var ready = await catalog.FindReadyDockerImagesAsync(images, cancellationToken);
        var invalid = images.FirstOrDefault(image => !ready.ContainsKey(image));
        if (invalid is not null)
            throw new ChallengeApiContractException("awdp_docker_image_unregistered", $"Docker image '{invalid}' is not a ready platform image template.", 422);
        return await SubmitAsync(gameId, apiTokenId, actorUserId, routeKey, idempotencyKey,
            ChallengeMutationKind.ImportAwdp, new AwdpImportPayload(normalized), cancellationToken);
    }

    public async Task<IdempotencyBeginResult> SubmitAwdpDeleteAsync(
        int gameId, Guid apiTokenId, ActorContext actor, string idempotencyKey,
        IReadOnlyList<int> serviceIds, string routeKey, CancellationToken cancellationToken)
    {
        var actorUserId = actor.UserId ?? throw new ChallengeApiContractException("authentication_required", "Authentication is required.", 401);
        await RequireGameAsync(gameId, cancellationToken);
        var normalized = serviceIds?.Distinct().Order().ToArray() ?? [];
        if (normalized.Length is < 1 or > MaximumBatchSize || normalized.Any(id => id <= 0))
            throw new ChallengeApiContractException("awdp_delete_set_invalid", "AWDP service IDs are invalid.", 422);
        return await SubmitAsync(gameId, apiTokenId, actorUserId, routeKey, idempotencyKey,
            ChallengeMutationKind.DeleteAwdp, new AwdpDeletePayload(normalized), cancellationToken);
    }

    public async Task<OpenChallengePageModel> ListAsync(
        int gameId,
        int limit,
        string? after,
        CancellationToken cancellationToken)
    {
        await RequireGameAsync(gameId, cancellationToken);
        var normalizedLimit = Math.Clamp(limit, 1, 100);
        var afterId = DecodeCursor(after);
        var items = await catalog.ListAsync(gameId, normalizedLimit + 1, afterId, cancellationToken);
        var page = items.Take(normalizedLimit).ToArray();
        var nextCursor = items.Count > normalizedLimit ? EncodeCursor(page[^1].Id) : null;
        return new OpenChallengePageModel(page, nextCursor);
    }

    public async Task<OpenChallengeModel> GetAsync(
        int gameId,
        int challengeId,
        CancellationToken cancellationToken)
    {
        await RequireGameAsync(gameId, cancellationToken);
        return await catalog.FindAsync(gameId, challengeId, cancellationToken)
               ?? throw new ChallengeApiContractException(
                   "challenge_not_found", "The challenge was not found.", 404);
    }

    private async Task<IdempotencyBeginResult> SubmitAsync<TPayload>(
        int gameId,
        Guid apiTokenId,
        Guid actorUserId,
        string routeKey,
        string idempotencyKey,
        ChallengeMutationKind kind,
        TPayload payload,
        CancellationToken cancellationToken)
    {
        var normalizedKey = ExternalIdempotencyKey.Normalize(idempotencyKey);
        var payloadJson = JsonSerializer.Serialize(payload, JsonOptions);
        var requestHash = Convert.ToHexStringLower(SHA256.HashData(
            JsonSerializer.SerializeToUtf8Bytes(new { gameId, kind, payload }, JsonOptions)));
        var job = new ChallengeMutationJob
        {
            GameId = gameId,
            Kind = kind,
            PayloadJson = payloadJson
        };
        return await submissions.SubmitAsync(
            new ChallengeMutationSubmission(
                apiTokenId,
                actorUserId,
                routeKey.Trim(),
                normalizedKey,
                requestHash,
                job),
            cancellationToken);
    }

    private async Task RequireGameAsync(int gameId, CancellationToken cancellationToken)
    {
        if (gameId <= 0 || !await catalog.GameExistsAsync(gameId, cancellationToken))
            throw new ChallengeApiContractException(
                "game_not_found", "The game was not found.", 404);
    }

    private static OpenChallengeImportModel Normalize(OpenChallengeImportModel source)
    {
        if (source is null)
            throw Invalid("challenge_item_invalid", "A challenge item cannot be null.");
        var externalId = source.ExternalId?.Trim() ?? string.Empty;
        var title = source.Title?.Trim() ?? string.Empty;
        if (externalId.Length is < 1 or > 128)
            throw Invalid("challenge_external_id_invalid", "External ID must contain between 1 and 128 characters.");
        if (title.Length is < 1 or > 256)
            throw Invalid("challenge_title_invalid", "Challenge title must contain between 1 and 256 characters.");
        if (source.Content is null || source.Content.Length > 1_000_000)
            throw Invalid("challenge_content_too_large", "Challenge content cannot exceed 1,000,000 characters.");
        if (!Enum.IsDefined(source.Category) || !Enum.IsDefined(source.Type))
            throw Invalid("challenge_enum_invalid", "Challenge category or type is invalid.");
        if (source.Hints is { Count: > 100 } ||
            source.Hints?.Any(hint => hint is null || hint.Length > 4096) == true)
            throw Invalid("challenge_hints_invalid", "A challenge may contain at most 100 hints of 4,096 characters each.");
        if (source.Flags is null || source.Flags.Count > 100 || source.Flags.Any(flag => flag is null))
            throw Invalid("challenge_flags_invalid", "A challenge may contain at most 100 flags.");
        if (!Enum.IsDefined(source.NetworkMode))
            throw Invalid("challenge_network_mode_invalid", "Challenge network mode is invalid.");

        var environment = source.Type.IsContainer()
            ? source.Environment ?? EnvironmentType.Docker
            : EnvironmentType.None;
        if (!Enum.IsDefined(environment))
            throw Invalid("challenge_environment_invalid", "Challenge environment is invalid.");
        if (!source.Type.IsContainer() &&
            (source.Environment is not null and not EnvironmentType.None ||
             source.ContainerImage is not null || source.ExposePort is not null ||
             source.ImageTemplateId is not null || source.EnableTrafficCapture))
            throw Invalid(
                "challenge_environment_invalid",
                "Attachment challenges cannot declare a Docker or VM runtime.");
        if (source.Type.IsContainer() && environment == EnvironmentType.None)
            throw Invalid("challenge_environment_invalid", "Container challenges require a runtime environment.");
        if (environment == EnvironmentType.Docker &&
            (string.IsNullOrWhiteSpace(source.ContainerImage) || source.ExposePort is not (>= 1 and <= 65535) ||
             source.ImageTemplateId is not null))
            throw Invalid(
                "challenge_docker_config_invalid",
                "Docker challenges require containerImage and exposePort and cannot reference a VM template.");
        if (environment == EnvironmentType.WindowsVM &&
            (!source.ImageTemplateId.HasValue || source.ContainerImage is not null || source.ExposePort is not null))
            throw Invalid(
                "challenge_vm_config_invalid",
                "Windows VM challenges require imageTemplateId and cannot declare Docker image fields.");

        var flagTemplate = string.IsNullOrWhiteSpace(source.FlagTemplate) ? null : source.FlagTemplate.Trim();
        if (source.Type == ChallengeType.DynamicContainer &&
            (flagTemplate is null || !new DynamicFlagGenerator(flagTemplate).IsValid()))
            throw Invalid(
                "challenge_flag_template_invalid",
                "Dynamic container challenges require a valid flagTemplate.");
        if (source.Type != ChallengeType.DynamicContainer && source.IsEnabled && source.Flags.Count == 0)
            throw Invalid(
                "challenge_flags_required",
                "An enabled non-dynamic-container challenge requires at least one flag.");

        var flags = source.Flags.Select((flag, index) => NormalizeFlag(flag, index)).ToArray();
        var duplicateOrder = flags.GroupBy(flag => flag.OrderIndex).FirstOrDefault(group => group.Count() > 1)?.Key;
        if (duplicateOrder is not null)
            throw Invalid(
                "challenge_flag_order_duplicate",
                $"Flag order index {duplicateOrder} occurs more than once.");

        return new OpenChallengeImportModel
        {
            ExternalId = externalId,
            Title = title,
            Content = source.Content,
            Category = source.Category,
            Type = source.Type,
            Hints = source.Hints?.Select(hint => hint.Trim()).ToList(),
            IsEnabled = source.IsEnabled,
            DeadlineUtc = source.DeadlineUtc,
            SubmissionLimit = source.SubmissionLimit,
            OriginalScore = source.OriginalScore,
            MinScoreRate = source.MinScoreRate,
            Difficulty = source.Difficulty,
            DisableBloodBonus = source.DisableBloodBonus,
            FlagTemplate = flagTemplate,
            Environment = environment,
            ContainerImage = environment == EnvironmentType.Docker ? source.ContainerImage!.Trim() : null,
            ExposePort = environment == EnvironmentType.Docker ? source.ExposePort : null,
            ImageTemplateId = environment == EnvironmentType.WindowsVM ? source.ImageTemplateId : null,
            CPUCount = source.CPUCount,
            MemoryLimit = source.MemoryLimit,
            StorageLimit = source.StorageLimit,
            NetworkMode = source.NetworkMode,
            EnableTrafficCapture = environment == EnvironmentType.Docker && source.EnableTrafficCapture,
            FileName = string.IsNullOrWhiteSpace(source.FileName) ? null : source.FileName.Trim(),
            Flags = flags.ToList(),
            Attachment = NormalizeAttachment(source.Attachment)
        };
    }

    private static OpenAwdpServiceImportModel NormalizeAwdp(OpenAwdpServiceImportModel source)
    {
        if (source is null || string.IsNullOrWhiteSpace(source.ExternalId) || string.IsNullOrWhiteSpace(source.Name) ||
            string.IsNullOrWhiteSpace(source.ImageName))
            throw new ChallengeApiContractException("awdp_item_invalid", "AWDP externalId, name, and imageName are required.", 422);
        var flagTemplate = source.FlagTemplate?.Trim();
        if (string.IsNullOrWhiteSpace(flagTemplate) || !new DynamicFlagGenerator(flagTemplate).IsValid())
            throw new ChallengeApiContractException("awdp_flag_template_invalid", "A valid AWDP flagTemplate is required.", 422);
        if (!Enum.IsDefined(source.Category) || !Enum.IsDefined(source.Difficulty))
            throw new ChallengeApiContractException("awdp_enum_invalid", "AWDP category or difficulty is invalid.", 422);
        source.ExternalId = source.ExternalId.Trim();
        source.Name = source.Name.Trim();
        source.ImageName = source.ImageName.Trim();
        source.Content = source.Content?.Trim() ?? string.Empty;
        source.FlagTemplate = flagTemplate;
        source.Tags = source.Tags?.Where(tag => !string.IsNullOrWhiteSpace(tag)).Select(tag => tag.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList() ?? [];
        return source;
    }

    private static OpenChallengeFlagModel NormalizeFlag(OpenChallengeFlagModel source, int index)
    {
        var flag = source.Flag.Trim();
        if (flag.Length is < 1 or > Limits.MaxFlagLength)
            throw Invalid("challenge_flag_invalid", "Flag text is empty or too long.");
        if (!Enum.IsDefined(source.ScoreMode) || !Enum.IsDefined(source.AnswerType))
            throw Invalid("challenge_flag_enum_invalid", "Flag score mode or answer type is invalid.");
        if (source.ScoreMode == FlagScoreMode.FixedScore && source.FixedScore <= 0)
            throw Invalid("challenge_flag_score_invalid", "A fixed-score flag requires a positive fixedScore.");

        return new OpenChallengeFlagModel
        {
            Flag = flag,
            OrderIndex = source.OrderIndex > 0 ? source.OrderIndex : index,
            Description = string.IsNullOrWhiteSpace(source.Description) ? null : source.Description.Trim(),
            ScoreMode = source.ScoreMode,
            FixedScore = source.ScoreMode == FlagScoreMode.FixedScore ? source.FixedScore : 0,
            MaxAttempts = source.MaxAttempts,
            AttachmentHash = string.IsNullOrWhiteSpace(source.AttachmentHash)
                ? null
                : source.AttachmentHash.Trim(),
            AnswerType = source.AnswerType,
            CustomName = string.IsNullOrWhiteSpace(source.CustomName) ? null : source.CustomName.Trim(),
            Attachment = NormalizeAttachment(source.Attachment)
        };
    }

    private static OpenChallengeAttachmentModel? NormalizeAttachment(OpenChallengeAttachmentModel? source)
    {
        if (source is null)
            return null;
        var value = source.RemoteUrl.Trim();
        if (value.Length is < 1 or > 2048 ||
            !Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https"))
            throw Invalid(
                "challenge_attachment_url_invalid",
                "Remote attachment URLs must be absolute HTTP or HTTPS URLs no longer than 2,048 characters.");
        return new OpenChallengeAttachmentModel { RemoteUrl = value };
    }

    private static string EncodeCursor(int challengeId)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(bytes, challengeId);
        return WebEncoders.Base64UrlEncode(bytes);
    }

    private static int? DecodeCursor(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor))
            return null;
        try
        {
            var bytes = WebEncoders.Base64UrlDecode(cursor.Trim());
            if (bytes.Length != 4)
                throw new FormatException();
            var value = BinaryPrimitives.ReadInt32BigEndian(bytes);
            if (value <= 0)
                throw new FormatException();
            return value;
        }
        catch (FormatException)
        {
            throw new ChallengeApiContractException(
                "challenge_cursor_invalid", "The pagination cursor is invalid.", 400);
        }
    }

    private static ChallengeApiContractException Invalid(string code, string message) => new(code, message, 422);
}

public sealed class ChallengeApiContractException(string code, string message, int statusCode)
    : ApiContractException(code, message, statusCode);
