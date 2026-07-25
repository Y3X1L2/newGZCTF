using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Net.Mime;
using System.Security.Claims;
using GZCTF.Modules.Audit.Application;
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
    IExerciseManagementService managementService) : ControllerBase
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
        _ = GetActor();
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
        _ = GetActor();
        var exercise = await exerciseService.GetExerciseByIdAsync(exerciseId, cancellationToken);
        if (exercise is null)
            throw new ExerciseApiContractException(
                "exercise_not_found", $"Exercise {exerciseId} not found.", 404);

        return Ok(toExternalModel(exercise));
    }

    [HttpPost("import")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.ExercisesWrite)]
    [ProducesResponseType(typeof(ExerciseImportResult), StatusCodes.Status201Created)]
    public async Task<IActionResult> Import(
        [FromBody] ExerciseImportFromExternalModel model,
        [FromHeader(Name = "Idempotency-Key"), Required] string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        _ = GetActor();
        ValidateIdempotencyKey(idempotencyKey);
        var normalized = model.Items.Select(NormalizeImportItem).ToArray();
        var imported = new List<ExerciseImportResultItem>();
        foreach (var item in normalized)
        {
            var exercise = new ExerciseChallenge
            {
                Title = item.Title,
                Content = item.Content,
                Category = item.Category,
                Type = item.Type,
                Difficulty = item.Difficulty,
                Credit = item.Credit,
                Tags = item.Tags ?? [],
                Hints = item.Hints,
                IsEnabled = item.IsEnabled,
            };
            var created = await managementService.CreateExerciseAsync(exercise, cancellationToken);
            imported.Add(new ExerciseImportResultItem { ExternalId = item.ExternalId, ExerciseId = created.Id, Title = created.Title });
        }

        return CreatedAtAction(nameof(List), new { }, new ExerciseImportResult(imported, []));
    }

    [HttpPost]
    [Authorize(Policy = "scope:" + ApiTokenScopes.ExercisesWrite)]
    [ProducesResponseType(typeof(ExerciseExternalModel), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Create(
        [FromBody] ExerciseCreateModel model,
        CancellationToken cancellationToken = default)
    {
        _ = GetActor();
        var exercise = new ExerciseChallenge
        {
            Title = model.Title,
            Content = model.Content,
            Category = model.Category,
            Type = model.Type,
            Difficulty = model.Difficulty,
            Credit = model.Credit,
            Tags = model.Tags ?? [],
            Hints = model.Hints,
            ContainerImage = model.ContainerImage,
            MemoryLimit = model.MemoryLimit,
            StorageLimit = model.StorageLimit,
            CPUCount = model.CPUCount,
            ExposePort = model.ExposePort,
            NetworkMode = model.NetworkMode,
            FlagTemplate = model.FlagTemplate,
            Environment = model.Environment,
            ImageTemplateId = model.ImageTemplateId,
            IsEnabled = model.IsEnabled,
        };
        var created = await managementService.CreateExerciseAsync(exercise, cancellationToken);
        return CreatedAtAction(nameof(Get), new { exerciseId = created.Id }, toExternalModel(created));
    }

    [HttpPut("{exerciseId:int}")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.ExercisesWrite)]
    [ProducesResponseType(typeof(ExerciseExternalModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Update(
        int exerciseId,
        [FromBody] ExerciseCreateModel model,
        CancellationToken cancellationToken = default)
    {
        _ = GetActor();
        var existing = await exerciseService.GetExerciseByIdAsync(exerciseId, cancellationToken);
        if (existing is null)
            throw new ExerciseApiContractException(
                "exercise_not_found", $"Exercise {exerciseId} not found.", 404);

        existing.Title = model.Title;
        existing.Content = model.Content;
        existing.Category = model.Category;
        existing.Type = model.Type;
        existing.Difficulty = model.Difficulty;
        existing.Credit = model.Credit;
        existing.Tags = model.Tags ?? [];
        existing.Hints = model.Hints;
        existing.ContainerImage = model.ContainerImage;
        existing.MemoryLimit = model.MemoryLimit;
        existing.StorageLimit = model.StorageLimit;
        existing.CPUCount = model.CPUCount;
        existing.ExposePort = model.ExposePort;
        existing.NetworkMode = model.NetworkMode;
        existing.FlagTemplate = model.FlagTemplate;
        existing.Environment = model.Environment;
        existing.ImageTemplateId = model.ImageTemplateId;
        existing.IsEnabled = model.IsEnabled;

        var updated = await managementService.UpdateExerciseAsync(existing, cancellationToken);
        return Ok(toExternalModel(updated));
    }

    [HttpDelete("{exerciseId:int}")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.ExercisesDelete)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(
        int exerciseId,
        CancellationToken cancellationToken = default)
    {
        _ = GetActor();
        var existing = await exerciseService.GetExerciseByIdAsync(exerciseId, cancellationToken);
        if (existing is null)
            throw new ExerciseApiContractException(
                "exercise_not_found", $"Exercise {exerciseId} not found.", 404);
        if (existing.TrainingCourseId is not null)
            throw new ExerciseApiContractException(
                "exercise_in_use", $"Exercise {exerciseId} is referenced by a training course and cannot be deleted.", 422);

        await managementService.RemoveExerciseAsync(exerciseId, cancellationToken);
        return NoContent();
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
    };

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

    private static void ValidateIdempotencyKey(string key) =>
        ExternalIdempotencyKey.Normalize(key);

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
        return item;
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