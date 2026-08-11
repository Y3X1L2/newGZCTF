using System.Security.Cryptography;
using System.Text.Json;
using GZCTF.Models;
using GZCTF.Models.Request.Game;
using GZCTF.Modules.Audit.Application;
using GZCTF.Modules.Exercise.Contracts;
using GZCTF.Modules.Identity.Application;
using GZCTF.Modules.Provisioning.Contracts;
using GZCTF.Modules.Provisioning.Domain;
using GZCTF.Services;

namespace GZCTF.Modules.Provisioning.Application;

public sealed record AcademicImportSubmission(
    Guid ApiTokenId,
    Guid ActorUserId,
    string RouteKey,
    string IdempotencyKey,
    string RequestHash,
    string ResourceType,
    string ResourceId,
    AcademicImportJob Job);

public interface IAcademicImportSubmissionStore
{
    Task<IdempotencyBeginResult> SubmitAsync(
        AcademicImportSubmission submission,
        CancellationToken cancellationToken);
}

public sealed record TrainingCourseImportPayload(IReadOnlyList<TrainingCourseImportModel> Items);
public sealed record TheoryQuestionImportPayload(IReadOnlyList<TheoryQuestionImportModel> Items);
public sealed record TheoryPaperImportPayload(int GameId, TheoryPaperImportModel Model);
public sealed record TeamImportPayload(IReadOnlyList<TeamImportModel> Items);

public sealed class AcademicImportApplicationService(
    IAcademicImportSubmissionStore submissions,
    TheoryExamService theoryService)
{
    public const string OperationKind = "academic.import.v1";
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public Task<IdempotencyBeginResult> SubmitTrainingCoursesAsync(
        Guid apiTokenId,
        Guid actorUserId,
        string idempotencyKey,
        TrainingCourseImportBatchModel model,
        CancellationToken cancellationToken)
    {
        ValidateBatch(model.Items, 50, "training_course");
        foreach (var item in model.Items)
            NormalizeCourse(item);
        return SubmitAsync(apiTokenId, actorUserId, "POST:/api/open/v1/training/courses/import",
            idempotencyKey, "training-course", "*", AcademicImportKind.TrainingCourses, null,
            new TrainingCourseImportPayload(model.Items), cancellationToken);
    }

    public Task<IdempotencyBeginResult> SubmitTheoryQuestionsAsync(
        Guid apiTokenId,
        Guid actorUserId,
        string idempotencyKey,
        TheoryQuestionImportBatchModel model,
        CancellationToken cancellationToken)
    {
        ValidateBatch(model.Items, 1000, "theory_question");
        foreach (var item in model.Items)
            ValidateQuestion(item);
        return SubmitAsync(apiTokenId, actorUserId, "POST:/api/open/v1/theory/questions/import",
            idempotencyKey, "theory-bank", "*", AcademicImportKind.TheoryQuestions, null,
            new TheoryQuestionImportPayload(model.Items), cancellationToken);
    }

    public Task<IdempotencyBeginResult> SubmitTheoryPaperAsync(
        int gameId,
        Guid apiTokenId,
        Guid actorUserId,
        string idempotencyKey,
        TheoryPaperImportModel model,
        CancellationToken cancellationToken)
    {
        if (gameId <= 0)
            throw Invalid("game_not_found", "The game was not found.", 404);
        model.Title = RequireText(model.Title, 256, "theory_paper_title_invalid", "Paper title");
        if (model.Questions.Count is < 1 or > 1000)
            throw Invalid("theory_paper_questions_invalid", "A paper requires between 1 and 1,000 questions.");
        foreach (var question in model.Questions)
            ValidateQuestion(question, question.Score);
        return SubmitAsync(apiTokenId, actorUserId, $"PUT:/api/open/v1/theory/games/{gameId}/paper",
            idempotencyKey, "game", gameId.ToString(), AcademicImportKind.TheoryPaper, gameId,
            new TheoryPaperImportPayload(gameId, model), cancellationToken);
    }

    public Task<IdempotencyBeginResult> SubmitTeamsAsync(
        Guid apiTokenId,
        Guid actorUserId,
        string idempotencyKey,
        TeamImportBatchModel model,
        CancellationToken cancellationToken)
    {
        ValidateBatch(model.Items, 200, "team");
        foreach (var item in model.Items)
        {
            item.Name = RequireText(item.Name, Limits.MaxTeamNameLength, "team_name_invalid", "Team name");
            ValidateUserReference(item.Captain, "captain");
            foreach (var member in item.Members)
                ValidateUserReference(member, "member");
        }
        return SubmitAsync(apiTokenId, actorUserId, "POST:/api/open/v1/teams/import",
            idempotencyKey, "team", "*", AcademicImportKind.Teams, null,
            new TeamImportPayload(model.Items), cancellationToken);
    }

    async Task<IdempotencyBeginResult> SubmitAsync<TPayload>(
        Guid apiTokenId,
        Guid actorUserId,
        string routeKey,
        string idempotencyKey,
        string resourceType,
        string resourceId,
        AcademicImportKind kind,
        int? targetId,
        TPayload payload,
        CancellationToken cancellationToken)
    {
        var normalizedKey = ExternalIdempotencyKey.Normalize(idempotencyKey);
        var payloadJson = JsonSerializer.Serialize(payload, JsonOptions);
        var requestHash = Convert.ToHexStringLower(SHA256.HashData(
            JsonSerializer.SerializeToUtf8Bytes(new { kind, targetId, payload }, JsonOptions)));
        return await submissions.SubmitAsync(new AcademicImportSubmission(
            apiTokenId, actorUserId, routeKey, normalizedKey, requestHash, resourceType, resourceId,
            new AcademicImportJob { Kind = kind, TargetId = targetId, PayloadJson = payloadJson }),
            cancellationToken);
    }

    void NormalizeCourse(TrainingCourseImportModel course)
    {
        course.ExternalId = NormalizeExternalId(course.ExternalId);
        course.Title = RequireText(course.Title, 128, "training_course_title_invalid", "Course title");
        if (!Enum.IsDefined(course.EnrollmentPolicy))
            throw Invalid("training_course_enrollment_policy_invalid", "Course enrollment policy is invalid.");
        NormalizeExternalIds(course.Chapters, "training_chapter");
        NormalizeExternalIds(course.Exercises, "training_exercise");
        NormalizeExternalIds(course.TheoryQuestions, "training_theory_question");
        NormalizeExternalIds(course.TheoryPapers, "training_theory_paper");

        var chapterIds = course.Chapters.Select(item => item.ExternalId).ToHashSet(StringComparer.Ordinal);
        foreach (var chapter in course.Chapters)
        {
            chapter.Title = RequireText(chapter.Title, 128, "training_chapter_title_invalid", "Chapter title");
            chapter.ParentExternalId = NormalizeOptionalExternalId(chapter.ParentExternalId);
            if (chapter.ParentExternalId is not null && !chapterIds.Contains(chapter.ParentExternalId))
                throw Invalid("training_chapter_parent_not_found", $"Chapter '{chapter.ExternalId}' references an unknown parent.");
            if (!Enum.IsDefined(chapter.ContentType) || !Enum.IsDefined(chapter.VideoProvider))
                throw Invalid("training_chapter_enum_invalid", "Chapter content type or video provider is invalid.");
            if (chapter.VideoProvider == TrainingCourseVideoProvider.LocalFile)
                throw Invalid("training_chapter_local_video_unsupported", "Open API imports only support external chapter videos.");
            if (chapter.VideoProvider == TrainingCourseVideoProvider.ExternalUrl)
                ValidateHttpUrl(chapter.VideoUrl, "training_chapter_video_url_invalid");
        }
        RejectChapterCycles(course.Chapters);

        foreach (var exercise in course.Exercises)
        {
            exercise.ChapterExternalId = NormalizeOptionalExternalId(exercise.ChapterExternalId);
            if (exercise.ChapterExternalId is not null && !chapterIds.Contains(exercise.ChapterExternalId))
                throw Invalid("training_exercise_chapter_not_found", $"Exercise '{exercise.ExternalId}' references an unknown chapter.");
            ValidateExercise(exercise);
        }

        foreach (var question in course.TheoryQuestions)
            ValidateQuestion(question);
        var questionIds = course.TheoryQuestions.Select(item => item.ExternalId).ToHashSet(StringComparer.Ordinal);
        foreach (var paper in course.TheoryPapers)
        {
            paper.ChapterExternalId = NormalizeExternalId(paper.ChapterExternalId);
            if (!chapterIds.Contains(paper.ChapterExternalId))
                throw Invalid("training_theory_paper_chapter_not_found", $"Paper '{paper.ExternalId}' references an unknown chapter.");
            paper.Title = RequireText(paper.Title, 128, "training_theory_paper_title_invalid", "Paper title");
            if (paper.Publish && paper.Questions.Count == 0)
                throw Invalid("training_theory_paper_empty", "A published training paper requires at least one question.");
            foreach (var question in paper.Questions)
            {
                question.SourceQuestionExternalId = NormalizeOptionalExternalId(question.SourceQuestionExternalId);
                if (question.SourceQuestionExternalId is not null && !questionIds.Contains(question.SourceQuestionExternalId))
                    throw Invalid("training_theory_question_not_found", "A training paper references an unknown course question.");
                ValidateQuestion(question, question.Score);
            }
        }
        var duplicatePaperChapter = course.TheoryPapers
            .GroupBy(paper => paper.ChapterExternalId, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1)?.Key;
        if (duplicatePaperChapter is not null)
            throw Invalid("training_theory_paper_chapter_duplicate",
                $"Chapter '{duplicatePaperChapter}' has more than one theory paper.");
        if (course.Publish && course.Chapters.Count == 0)
            throw Invalid("training_course_empty", "A published course requires at least one chapter.");
    }

    void ValidateQuestion(TheoryQuestionEditModel model, int? score = null)
    {
        if (!Enum.IsDefined(model.Type))
            throw Invalid("theory_question_type_invalid", "Theory question type is invalid.");
        if (theoryService.NormalizeAndValidate(model, score) is { } error)
            throw Invalid("theory_question_invalid", error);
    }

    static void ValidateExercise(TrainingExerciseImportModel model)
    {
        model.Title = RequireText(model.Title, 256, "training_exercise_title_invalid", "Exercise title");
        if (!Enum.IsDefined(model.Type) || !Enum.IsDefined(model.Category) || !Enum.IsDefined(model.Environment))
            throw Invalid("training_exercise_enum_invalid", "Exercise type, category, or environment is invalid.");
        if (model.Type == ChallengeType.DynamicAttachment)
            throw Invalid("training_exercise_type_invalid", "Training imports do not support dynamic attachments.");
        if (!model.Type.IsContainer() &&
            (model.Environment != EnvironmentType.None || model.ImageTemplateId.HasValue ||
             !string.IsNullOrWhiteSpace(model.ContainerImage) || model.ExposePort.HasValue))
            throw Invalid("training_exercise_runtime_invalid", "Attachment exercises cannot declare a runtime.");
        if (model.Type.IsContainer() && model.Environment is not (EnvironmentType.Docker or EnvironmentType.WindowsVM))
            throw Invalid("training_exercise_environment_required", "Container training exercises require Docker or WindowsVM.");
        if (model.Type == ChallengeType.DynamicContainer && model.Environment == EnvironmentType.WindowsVM)
            throw Invalid("training_exercise_vm_dynamic_unsupported", "Windows VM training exercises use static container mode.");
        if (model.Type.IsContainer() && model.Environment == EnvironmentType.Docker &&
            string.IsNullOrWhiteSpace(model.ContainerImage))
            throw Invalid("training_exercise_image_required", "Docker training exercises require containerImage.");
        if (model.Environment == EnvironmentType.Docker && model.ExposePort is not (>= 1 and <= 65535))
            throw Invalid("training_exercise_port_invalid", "Docker training exercises require exposePort between 1 and 65535.");
        if (model.Environment == EnvironmentType.WindowsVM && !model.ImageTemplateId.HasValue)
            throw Invalid("training_exercise_template_required", "Windows VM training exercises require imageTemplateId.");
        if (model.Type == ChallengeType.DynamicContainer &&
            (string.IsNullOrWhiteSpace(model.FlagTemplate) || !new DynamicFlagGenerator(model.FlagTemplate).IsValid()))
            throw Invalid("training_exercise_flag_template_invalid", "Dynamic training exercises require a valid flagTemplate.");
        if (model.IsEnabled && !model.Type.IsDynamic() && (model.Flags is null || model.Flags.Count == 0))
            throw Invalid("training_exercise_flags_required", "Enabled static training exercises require at least one flag.");
        if (model.Flags?.Any(flag => string.IsNullOrWhiteSpace(flag.Flag) || flag.Flag.Length > Limits.MaxFlagLength) == true)
            throw Invalid("training_exercise_flag_invalid", "A training exercise flag is empty or too long.");
        ValidateRemoteAttachment(model.Attachment);
        if (model.Flags is not null)
            foreach (var flag in model.Flags)
                ValidateRemoteAttachment(flag.Attachment);
    }

    static void ValidateRemoteAttachment(ExerciseOpenApiAttachmentModel? attachment)
    {
        if (attachment is null)
            return;
        var value = attachment.RemoteUrl?.Trim() ?? string.Empty;
        if (value.Length is < 1 or > 2048 || !Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https"))
            throw Invalid("training_attachment_url_invalid", "Remote attachments require an absolute HTTP or HTTPS URL.");
        attachment.RemoteUrl = value;
    }

    static void ValidateHttpUrl(string? value, string code)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length is < 1 or > 1024 || !Uri.TryCreate(normalized, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https"))
            throw Invalid(code, "The URL must be an absolute HTTP or HTTPS URL.");
    }

    static void ValidateUserReference(ExternalUserReferenceModel reference, string label)
    {
        if (reference is null || reference.UserId is null && string.IsNullOrWhiteSpace(reference.UserName))
            throw Invalid("team_user_reference_invalid", $"Each team {label} requires userId or userName.");
        reference.UserName = string.IsNullOrWhiteSpace(reference.UserName) ? null : reference.UserName.Trim();
    }

    static void ValidateBatch<T>(IReadOnlyList<T> items, int maximum, string prefix)
        where T : IExternalImportItemModel
    {
        if (items is null || items.Count is < 1 || items.Count > maximum || items.Any(item => item is null))
            throw Invalid($"{prefix}_batch_invalid", $"The import requires between 1 and {maximum} items.");
        NormalizeExternalIds(items, prefix);
    }

    static void NormalizeExternalIds<T>(IReadOnlyList<T> items, string prefix)
        where T : IExternalImportItemModel
    {
        foreach (var item in items)
            item.ExternalId = NormalizeExternalId(item.ExternalId);
        var duplicate = items.GroupBy(item => item.ExternalId, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1)?.Key;
        if (duplicate is not null)
            throw Invalid($"{prefix}_external_id_duplicate", $"External ID '{duplicate}' occurs more than once.");
    }

    static string NormalizeExternalId(string? value)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length is < 1 or > 128)
            throw Invalid("external_id_invalid", "External IDs must contain between 1 and 128 characters.");
        return normalized;
    }

    static string? NormalizeOptionalExternalId(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : NormalizeExternalId(value);

    static string RequireText(string? value, int maximum, string code, string label)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length is < 1 || normalized.Length > maximum)
            throw Invalid(code, $"{label} must contain between 1 and {maximum} characters.");
        return normalized;
    }

    static void RejectChapterCycles(IReadOnlyList<TrainingChapterImportModel> chapters)
    {
        var parents = chapters.ToDictionary(item => item.ExternalId, item => item.ParentExternalId, StringComparer.Ordinal);
        foreach (var chapter in chapters)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var current = chapter.ExternalId;
            while (parents.TryGetValue(current, out var parent) && parent is not null)
            {
                if (!seen.Add(current))
                    throw Invalid("training_chapter_cycle", "Training chapter parent references contain a cycle.");
                current = parent;
            }
        }
    }

    static AcademicImportApiContractException Invalid(string code, string message, int statusCode = 422) =>
        new(code, message, statusCode);
}

public sealed class AcademicImportApiContractException(string code, string message, int statusCode)
    : ApiContractException(code, message, statusCode);
