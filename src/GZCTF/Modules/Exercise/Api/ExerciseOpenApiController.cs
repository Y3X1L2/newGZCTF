using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Net.Mime;
using System.Security.Claims;
using GZCTF.Models;
using GZCTF.Models.Data;
using ExerciseFilter = GZCTF.Models.Request.Exercise.ExerciseFilter;
using GZCTF.Modules.Audit.Application;
using GZCTF.Modules.Audit.Contracts;
using GZCTF.Modules.Exercise.Application;
using GZCTF.Modules.Exercise.Contracts;
using GZCTF.Modules.Identity.Application;
using GZCTF.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GZCTF.Modules.Exercise.Api;

[ApiController]
[ApiExplorerSettings(GroupName = "open-v1")]
[Route("api/open/v1/exercises")]
[Produces(MediaTypeNames.Application.Json, "application/problem+json")]
public sealed class ExerciseOpenApiController(
    IExerciseService exerciseService,
    ExerciseExternalApplicationService mutations,
    IAuthorizationService authorization) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = "scope:" + ApiTokenScopes.ExercisesRead)]
    [ProducesResponseType(typeof(ExerciseExternalPageModel), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] string? search,
        [FromQuery] string? category,
        [FromQuery] string? difficulty,
        [FromQuery] string? tags,
        [FromQuery, Range(1, 100)] int limit = 50,
        CancellationToken cancellationToken = default)
    {
        await AuthorizeExerciseAsync("*");
        var filter = new ExerciseFilter
        {
            Search = search,
            Categories = ParseCategories(category),
            Difficulties = ParseDifficulties(difficulty),
            Tags = ParseStringArray(tags),
        };
        var exercises = await exerciseService.GetExerciseListAsync(filter, cancellationToken);
        return Ok(new ExerciseExternalPageModel { Items = exercises.Take(limit).Select(toSummary).ToArray() });
    }

    [HttpGet("{exerciseId:int}")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.ExercisesRead)]
    [ProducesResponseType(typeof(ExerciseExternalModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(
        int exerciseId,
        CancellationToken cancellationToken = default)
    {
        await AuthorizeExerciseAsync(exerciseId.ToString());
        var exercise = await exerciseService.GetExerciseByIdAsync(exerciseId, cancellationToken);
        if (exercise is null)
            throw new ExerciseApiContractException(
                "exercise_not_found", $"Exercise {exerciseId} not found.", 404);

        return Ok(toExternalModel(exercise));
    }

    [HttpPost("import")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.ExercisesWrite)]
    [ProducesResponseType(typeof(ApiOperationModel), StatusCodes.Status202Accepted)]
    public async Task<IActionResult> Import(
        [FromBody] ExerciseImportFromExternalModel model,
        [FromHeader(Name = "Idempotency-Key"), Required] string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        await AuthorizeExerciseAsync("*");
        var (tokenId, actorUserId) = GetActor();
        var normalized = model.Items.Select(NormalizeImportItem).ToArray();
        var result = await mutations.SubmitImportAsync(
            tokenId,
            new ActorContext(actorUserId, Role.Teacher, tokenId),
            idempotencyKey,
            normalized,
            "POST:/api/open/v1/exercises/import",
            cancellationToken);
        return AcceptedOperation(result);
    }

    [HttpPost]
    [Authorize(Policy = "scope:" + ApiTokenScopes.ExercisesWrite)]
    [ProducesResponseType(typeof(ApiOperationModel), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Create(
        [FromBody] ExerciseCreateModel model,
        [FromHeader(Name = "Idempotency-Key"), Required] string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        await AuthorizeExerciseAsync("*");
        var (tokenId, actorUserId) = GetActor();
        var result = await mutations.SubmitCreateAsync(
            tokenId,
            new ActorContext(actorUserId, Role.Teacher, tokenId),
            idempotencyKey,
            model,
            "POST:/api/open/v1/exercises",
            cancellationToken);
        return AcceptedOperation(result);
    }

    [HttpPut("{exerciseId:int}")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.ExercisesWrite)]
    [ProducesResponseType(typeof(ApiOperationModel), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Update(
        int exerciseId,
        [FromBody] ExerciseCreateModel model,
        [FromHeader(Name = "Idempotency-Key"), Required] string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        await AuthorizeExerciseAsync(exerciseId.ToString());
        var existing = await exerciseService.GetExerciseByIdAsync(exerciseId, cancellationToken);
        if (existing is null)
            throw new ExerciseApiContractException(
                "exercise_not_found", $"Exercise {exerciseId} not found.", 404);

        var (tokenId, actorUserId) = GetActor();
        var result = await mutations.SubmitUpdateAsync(
            exerciseId,
            tokenId,
            new ActorContext(actorUserId, Role.Teacher, tokenId),
            idempotencyKey,
            model,
            $"PUT:/api/open/v1/exercises/{exerciseId}",
            cancellationToken);
        return AcceptedOperation(result);
    }

    [HttpDelete("{exerciseId:int}")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.ExercisesDelete)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(
        int exerciseId,
        [FromHeader(Name = "Idempotency-Key"), Required] string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        await AuthorizeExerciseAsync(exerciseId.ToString());
        var existing = await exerciseService.GetExerciseByIdAsync(exerciseId, cancellationToken);
        if (existing is null)
            throw new ExerciseApiContractException(
                "exercise_not_found", $"Exercise {exerciseId} not found.", 404);
        var (tokenId, actorUserId) = GetActor();
        var result = await mutations.SubmitDeleteAsync(
            exerciseId,
            tokenId,
            new ActorContext(actorUserId, Role.Teacher, tokenId),
            idempotencyKey,
            $"DELETE:/api/open/v1/exercises/{exerciseId}",
            cancellationToken);
        return AcceptedOperation(result);
    }

    private static ExerciseExternalSummaryModel toSummary(ExerciseChallenge exercise) => new()
    {
        Id = exercise.Id,
        Title = exercise.Title,
        Category = exercise.Category,
        Type = exercise.Type,
        Difficulty = exercise.Difficulty,
        Credit = exercise.Credit,
        Tags = exercise.Tags ?? [],
        IsEnabled = exercise.IsEnabled,
    };

    private static ExerciseExternalSummaryModel toSummary(GZCTF.Models.Request.Exercise.ExerciseInfoModel exercise) => new()
    {
        Id = exercise.Id,
        Title = exercise.Title,
        Category = exercise.Category,
        Type = exercise.Type,
        Difficulty = exercise.Difficulty,
        Credit = exercise.Credit,
        Tags = exercise.Tags ?? [],
        IsEnabled = exercise.IsEnabled,
    };

    private static ExerciseExternalModel toExternalModel(ExerciseChallenge exercise) => new()
    {
        Id = exercise.Id,
        Title = exercise.Title,
        Content = exercise.Content,
        Category = exercise.Category,
        Type = exercise.Type,
        Difficulty = exercise.Difficulty,
        Credit = exercise.Credit,
        Tags = exercise.Tags ?? [],
        Hints = exercise.Hints ?? [],
        IsEnabled = exercise.IsEnabled,
        ContainerImage = exercise.ContainerImage,
        MemoryLimit = exercise.MemoryLimit,
        StorageLimit = exercise.StorageLimit,
        CPUCount = exercise.CPUCount,
        ExposePort = exercise.ExposePort,
        NetworkMode = exercise.NetworkMode,
        Environment = exercise.Environment,
        ImageTemplateId = exercise.ImageTemplateId,
        FlagTemplate = exercise.FlagTemplate,
        Attachment = toAttachmentModel(exercise.Attachment),
        Flags = (exercise.Flags ?? []).Select(toFlagInfoModel).ToList(),
    };

    private static ExerciseOpenApiFlagInfoModel toFlagInfoModel(FlagContext flag) => new()
    {
        Id = flag.Id,
        Flag = flag.Flag,
        OrderIndex = flag.OrderIndex,
        Description = flag.Description,
        ScoreMode = flag.ScoreMode,
        FixedScore = flag.FixedScore,
        MaxAttempts = flag.MaxAttempts,
        AttachmentHash = flag.AttachmentHash,
        AnswerType = flag.AnswerType,
        CustomName = flag.CustomName,
        Attachment = toAttachmentModel(flag.Attachment),
    };

    private static ExerciseOpenApiAttachmentModel? toAttachmentModel(Attachment? attachment) =>
        attachment?.Type == FileType.Remote && !string.IsNullOrEmpty(attachment.RemoteUrl)
            ? new ExerciseOpenApiAttachmentModel { RemoteUrl = attachment.RemoteUrl }
            : null;

    private static ChallengeCategory[]? ParseCategories(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return value.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => Enum.TryParse<ChallengeCategory>(s.Trim(), ignoreCase: true, out var cat) ? cat : (ChallengeCategory?)null)
            .Where(c => c.HasValue)
            .Select(c => c!.Value)
            .ToArray();
    }

    private static Difficulty[]? ParseDifficulties(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return value.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => Enum.TryParse<Difficulty>(s.Trim(), ignoreCase: true, out var d) ? d : (Difficulty?)null)
            .Where(d => d.HasValue)
            .Select(d => d!.Value)
            .ToArray();
    }

    private static string[]? ParseStringArray(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return value.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => !string.IsNullOrEmpty(s))
            .ToArray();
    }

    private static ExerciseImportItemModel NormalizeImportItem(ExerciseImportItemModel item)
    {
        if (item is null)
            throw new ExerciseApiContractException("exercise_import_item_invalid", "An import item cannot be null.", 422);
        var externalId = item.ExternalId?.Trim() ?? string.Empty;
        var title = item.Title?.Trim() ?? string.Empty;
        if (externalId.Length is < 1 or > 128)
            throw new ExerciseApiContractException("exercise_external_id_invalid", "External ID must contain between 1 and 128 characters.", 422);
        if (title.Length is < 1 or > 256)
            throw new ExerciseApiContractException("exercise_title_invalid", "Exercise title must contain between 1 and 256 characters.", 422);
        if (item.Content is null || item.Content.Length > 1_000_000)
            throw new ExerciseApiContractException("exercise_content_too_large", "Exercise content cannot exceed 1,000,000 characters.", 422);
        if (!Enum.IsDefined(item.Category) || !Enum.IsDefined(item.Type))
            throw new ExerciseApiContractException("exercise_enum_invalid", "Exercise category or type is invalid.", 422);
        if (item.Hints is { Count: > 100 } ||
            item.Hints?.Any(hint => hint is null || hint.Length > 4096) == true)
            throw new ExerciseApiContractException("exercise_hints_invalid", "An exercise may contain at most 100 hints of 4,096 characters each.", 422);
        if (item.Flags is { Count: > 100 })
            throw new ExerciseApiContractException("exercise_flags_invalid", "An exercise may contain at most 100 flags.", 422);
        if (item.Flags?.Any(f => string.IsNullOrWhiteSpace(f.Flag) || f.Flag.Length > Limits.MaxFlagLength) == true)
            throw new ExerciseApiContractException("exercise_flag_content_invalid", $"Each flag must be between 1 and {Limits.MaxFlagLength} characters.", 422);
        return item;
    }

    private AcceptedResult AcceptedOperation(IdempotencyBeginResult result)
    {
        var operation = ApiOperationModel.FromEntity(result.Operation);
        return Accepted($"/api/open/v1/operations/{operation.Id}", operation);
    }

    private async Task AuthorizeExerciseAsync(string resourceId)
    {
        var result = await authorization.AuthorizeAsync(
            User,
            null,
            new ApiResourceRequirement("exercise", resourceId, true));
        if (!result.Succeeded)
            throw new ExerciseApiContractException(
                "insufficient_permission",
                $"The token does not grant access to exercise resource {resourceId}.",
                403);
    }

    private (Guid TokenId, Guid ActorUserId) GetActor()
    {
        if (Guid.TryParse(User.FindFirstValue(ApiTokenClaimTypes.TokenId), out var tokenId) &&
            Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var actorUserId))
            return (tokenId, actorUserId);
        throw new ExerciseApiContractException(
            "authentication_required", "Authentication is required.", 401);
    }
}
