using System.Text.Json;
using GZCTF.Models.Request.Game;
using GZCTF.Modules.Audit.Application;
using GZCTF.Modules.Exercise.Contracts;
using GZCTF.Modules.Provisioning.Application;
using GZCTF.Modules.Provisioning.Contracts;
using GZCTF.Modules.Provisioning.Domain;
using GZCTF.Modules.Runtime.Domain;
using GZCTF.Modules.Theory.Application;
using GZCTF.Modules.Training.Domain;
using GZCTF.Services;
using GZCTF.Services.Fleet;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Modules.Provisioning.Infrastructure;

public sealed class AcademicImportOperationHandler(
    AppDbContext context,
    TheoryExamService theoryService,
    ITheoryQuestionCatalog questionCatalog,
    TheoryStatisticsProjectionService statistics,
    ImageDistributionService imageDistribution) : IApiOperationHandler
{
    const int MaxCaptainTeams = 3;

    public string Kind => AcademicImportApplicationService.OperationKind;

    public async Task ExecuteAsync(Guid operationId, string leaseOwner, CancellationToken cancellationToken)
    {
        var job = await context.AcademicImportJobs.SingleOrDefaultAsync(
            item => item.OperationId == operationId, cancellationToken)
            ?? throw new ApiOperationTerminalException(
                "academic_import_job_not_found", "The persisted academic import payload was not found.");
        if (job.ResultJson is not null)
            return;
        if (string.IsNullOrWhiteSpace(job.PayloadJson))
            throw new ApiOperationTerminalException(
                "academic_import_payload_missing", "The persisted academic import payload is unavailable.");

        var operation = await context.ApiOperations.AsNoTracking().SingleAsync(
            item => item.Id == operationId, cancellationToken);
        var actorUserId = operation.ActorUserId
            ?? throw new ApiOperationTerminalException(
                "academic_import_actor_missing", "The academic import actor is unavailable.");

        try
        {
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            var result = job.Kind switch
            {
                AcademicImportKind.TrainingCourses => await ImportTrainingCoursesAsync(
                    job.PayloadJson, actorUserId, cancellationToken),
                AcademicImportKind.TheoryQuestions => await ImportTheoryQuestionsAsync(
                    job.PayloadJson, cancellationToken),
                AcademicImportKind.TheoryPaper => await ImportTheoryPaperAsync(
                    job.PayloadJson, cancellationToken),
                AcademicImportKind.Teams => await ImportTeamsAsync(
                    job.PayloadJson, actorUserId, cancellationToken),
                _ => throw new ApiOperationTerminalException(
                    "academic_import_kind_invalid", "The academic import kind is invalid.")
            };

            job.ResultJson = JsonSerializer.Serialize(
                new AcademicImportResult(result), AcademicImportApplicationService.JsonOptions);
            job.PayloadJson = null;
            job.CompletedAt = DateTimeOffset.UtcNow;
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (JsonException)
        {
            throw new ApiOperationTerminalException(
                "academic_import_payload_invalid", "The persisted academic import payload is invalid.");
        }
    }

    public async Task OnTerminalFailureAsync(Guid operationId, CancellationToken cancellationToken)
    {
        var job = await context.AcademicImportJobs.SingleOrDefaultAsync(
            item => item.OperationId == operationId, cancellationToken);
        if (job is null)
            return;
        job.PayloadJson = null;
        await context.SaveChangesAsync(cancellationToken);
    }

    async Task<List<AcademicImportResultItem>> ImportTrainingCoursesAsync(
        string payloadJson,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        var payload = Deserialize<TrainingCourseImportPayload>(payloadJson);
        var actor = await context.Users.SingleOrDefaultAsync(user => user.Id == actorUserId, cancellationToken);
        if (actor is null || actor.Role < Role.Teacher)
            throw Terminal("training_import_actor_invalid", "The training course owner is unavailable or is not a teacher.");

        List<AcademicImportResultItem> result = [];
        foreach (var model in payload.Items)
        {
            var now = DateTimeOffset.UtcNow;
            var course = new TrainingCourse
            {
                Title = model.Title,
                Slug = string.IsNullOrWhiteSpace(model.Slug) ? model.Title : model.Slug.Trim(),
                Summary = model.Summary.Trim(),
                Description = model.Description,
                Tags = model.Tags.Where(tag => !string.IsNullOrWhiteSpace(tag))
                    .Select(tag => tag.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Take(16).ToList(),
                EnrollmentPolicy = model.EnrollmentPolicy,
                Status = TrainingCourseStatus.Draft,
                CreatedById = actor.Id,
                UpdatedById = actor.Id,
                CreatedAt = now,
                UpdatedAt = now
            };
            course.Teachers.Add(new TrainingCourseTeacher
            {
                TeacherId = actor.Id,
                Teacher = actor,
                Role = TrainingCourseTeacherRole.Owner,
                AssignedById = actor.Id,
                AssignedAt = now
            });
            context.TrainingCourses.Add(course);
            await context.SaveChangesAsync(cancellationToken);
            result.Add(Created(model.ExternalId, "training-course", course.Id));

            var chapters = new Dictionary<string, TrainingCourseChapter>(StringComparer.Ordinal);
            foreach (var chapterModel in model.Chapters)
            {
                var chapter = new TrainingCourseChapter
                {
                    CourseId = course.Id,
                    Title = chapterModel.Title,
                    Summary = chapterModel.Summary.Trim(),
                    Content = chapterModel.Content,
                    ContentType = chapterModel.ContentType,
                    CompletionPolicy = chapterModel.CompletionPolicy,
                    VideoProvider = chapterModel.VideoProvider,
                    VideoUrl = string.IsNullOrWhiteSpace(chapterModel.VideoUrl) ? null : chapterModel.VideoUrl.Trim(),
                    Order = chapterModel.Order,
                    IsPublished = chapterModel.IsPublished,
                    CreatedById = actor.Id,
                    UpdatedById = actor.Id,
                    CreatedAt = now,
                    UpdatedAt = now
                };
                chapters.Add(chapterModel.ExternalId, chapter);
                context.TrainingCourseChapters.Add(chapter);
            }
            await context.SaveChangesAsync(cancellationToken);
            foreach (var chapterModel in model.Chapters)
            {
                var chapter = chapters[chapterModel.ExternalId];
                if (chapterModel.ParentExternalId is not null)
                    chapter.ParentId = chapters[chapterModel.ParentExternalId].Id;
                result.Add(Created(chapterModel.ExternalId, "training-chapter", chapter.Id));
            }
            await context.SaveChangesAsync(cancellationToken);

            var courseQuestions = new Dictionary<string, TrainingCourseTheoryQuestion>(StringComparer.Ordinal);
            foreach (var questionModel in model.TheoryQuestions)
            {
                RequireValidQuestion(questionModel);
                var question = new TrainingCourseTheoryQuestion
                {
                    CourseId = course.Id,
                    Type = questionModel.Type,
                    BankName = questionModel.BankName,
                    Title = questionModel.Title,
                    Content = questionModel.Content,
                    Options = questionModel.Options,
                    AnswerIndexes = questionModel.AnswerIndexes,
                    CreatedById = actor.Id,
                    UpdatedById = actor.Id,
                    CreatedAt = now,
                    UpdatedAt = now
                };
                courseQuestions.Add(questionModel.ExternalId, question);
                context.TrainingCourseTheoryQuestions.Add(question);
            }
            await context.SaveChangesAsync(cancellationToken);
            foreach (var (externalId, question) in courseQuestions)
                result.Add(Created(externalId, "training-theory-question", question.Id));

            HashSet<int> templateIds = [];
            foreach (var exerciseModel in model.Exercises)
            {
                var template = await ResolveImageTemplateAsync(exerciseModel, cancellationToken);
                if (template is not null)
                {
                    exerciseModel.ImageTemplateId = template.Id;
                    templateIds.Add(template.Id);
                    if (!await context.TrainingCourseImageTemplateBindings.AnyAsync(binding =>
                            binding.CourseId == course.Id && binding.ImageTemplateId == template.Id,
                            cancellationToken))
                    {
                        context.TrainingCourseImageTemplateBindings.Add(new TrainingCourseImageTemplateBinding
                        {
                            CourseId = course.Id,
                            ImageTemplateId = template.Id,
                            AddedById = actor.Id,
                            AddedAt = now
                        });
                    }
                }

                var exercise = CreateTrainingExercise(course.Id, exerciseModel);
                context.ExerciseChallenges.Add(exercise);
                await context.SaveChangesAsync(cancellationToken);
                context.TrainingCourseChallenges.Add(new TrainingCourseChallenge
                {
                    CourseId = course.Id,
                    ExerciseChallengeId = exercise.Id,
                    Order = exerciseModel.Order,
                    IsRequired = exerciseModel.IsRequired,
                    DisplayTitle = string.IsNullOrWhiteSpace(exerciseModel.DisplayTitle)
                        ? null : exerciseModel.DisplayTitle.Trim(),
                    CreatedById = actor.Id,
                    CreatedAt = now
                });
                if (exerciseModel.ChapterExternalId is not null)
                {
                    context.TrainingCourseChapterChallenges.Add(new TrainingCourseChapterChallenge
                    {
                        CourseId = course.Id,
                        ChapterId = chapters[exerciseModel.ChapterExternalId].Id,
                        ExerciseChallengeId = exercise.Id,
                        Order = exerciseModel.Order
                    });
                }
                await context.SaveChangesAsync(cancellationToken);
                result.Add(Created(exerciseModel.ExternalId, "training-exercise", exercise.Id));
            }

            foreach (var paperModel in model.TheoryPapers)
            {
                var paper = new TrainingCourseChapterTheoryPaper
                {
                    CourseId = course.Id,
                    ChapterId = chapters[paperModel.ChapterExternalId].Id,
                    Title = paperModel.Title,
                    Description = paperModel.Description.Trim(),
                    PassRate = Math.Clamp(paperModel.PassRate, 1, 100),
                    AllowRetake = paperModel.AllowRetake,
                    ShowCorrectAnswerAfterSubmit = paperModel.ShowCorrectAnswerAfterSubmit,
                    IsPublished = paperModel.Publish,
                    PublishedAt = paperModel.Publish ? now : null,
                    UpdatedById = actor.Id,
                    CreatedAt = now,
                    UpdatedAt = now
                };
                paper.Questions = paperModel.Questions.Select((questionModel, index) =>
                {
                    RequireValidQuestion(questionModel, questionModel.Score);
                    return new TrainingCourseChapterTheoryQuestion
                    {
                        SourceQuestionId = questionModel.SourceQuestionExternalId is null
                            ? null : courseQuestions[questionModel.SourceQuestionExternalId].Id,
                        Type = questionModel.Type,
                        Title = questionModel.Title,
                        Content = questionModel.Content,
                        Options = questionModel.Options,
                        AnswerIndexes = TheoryExamService.NormalizeIndexes(questionModel.AnswerIndexes),
                        Score = questionModel.Score,
                        Order = questionModel.Order > 0 ? questionModel.Order : index + 1
                    };
                }).ToList();
                context.TrainingCourseChapterTheoryPapers.Add(paper);
                await context.SaveChangesAsync(cancellationToken);
                result.Add(Created(paperModel.ExternalId, "training-theory-paper", paper.Id));
            }

            if (model.Publish)
            {
                course.Status = TrainingCourseStatus.Published;
                course.PublishedAt = now;
                course.UpdatedAt = now;
                await context.SaveChangesAsync(cancellationToken);
            }

            foreach (var templateId in templateIds)
                await imageDistribution.DistributeTemplateAsync(
                    templateId, ImageDistributionReferenceKey.TrainingCourse(course.Id), cancellationToken);
        }
        return result;
    }

    async Task<List<AcademicImportResultItem>> ImportTheoryQuestionsAsync(
        string payloadJson,
        CancellationToken cancellationToken)
    {
        var payload = Deserialize<TheoryQuestionImportPayload>(payloadJson);
        List<(TheoryQuestionImportModel Model, TheoryQuestionBankItem Item)> imported = [];
        foreach (var model in payload.Items)
        {
            RequireValidQuestion(model);
            var item = theoryService.ToBankQuestion(model);
            context.TheoryQuestionBankItems.Add(item);
            imported.Add((model, item));
        }
        await context.SaveChangesAsync(cancellationToken);
        foreach (var (model, item) in imported)
            await questionCatalog.SetTagsAsync(item, model.Tags, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        return imported.Select(pair => Created(pair.Model.ExternalId, "theory-question", pair.Item.Id)).ToList();
    }

    async Task<List<AcademicImportResultItem>> ImportTheoryPaperAsync(
        string payloadJson,
        CancellationToken cancellationToken)
    {
        var payload = Deserialize<TheoryPaperImportPayload>(payloadJson);
        var game = await context.Games.SingleOrDefaultAsync(item => item.Id == payload.GameId, cancellationToken);
        if (game is null)
            throw Terminal("game_not_found", "The game was not found.");
        if (!TheoryExamService.IsTheoryGame(game))
            throw Terminal("theory_game_required", "The target game is not a theory or mixed game.");

        foreach (var question in payload.Model.Questions)
            RequireValidQuestion(question, question.Score);
        var sourceIds = payload.Model.Questions.Where(question => question.SourceQuestionId.HasValue)
            .Select(question => question.SourceQuestionId!.Value).Distinct().ToArray();
        if (sourceIds.Length > 0)
        {
            var existingIds = await context.TheoryQuestionBankItems
                .Where(question => sourceIds.Contains(question.Id))
                .Select(question => question.Id).ToArrayAsync(cancellationToken);
            if (sourceIds.Except(existingIds).Any())
                throw Terminal("theory_source_question_not_found", "A source theory question was not found.");
        }

        var paper = await context.TheoryPapers.Include(item => item.Questions)
            .SingleOrDefaultAsync(item => item.GameId == payload.GameId, cancellationToken);
        if (paper is not null && await context.TheoryAnswerSheets.AnyAsync(sheet =>
                sheet.PaperId == paper.Id && sheet.Status == TheoryAnswerSheetStatus.Submitted,
                cancellationToken))
            throw Terminal("theory_paper_has_submissions", "A paper with submitted answer sheets cannot be replaced.");

        paper ??= new TheoryPaper { GameId = payload.GameId, CreatedAt = DateTimeOffset.UtcNow };
        if (paper.Questions.Count > 0)
            context.TheoryPaperQuestions.RemoveRange(paper.Questions);
        paper.Questions = payload.Model.Questions.Select((question, index) => new TheoryPaperQuestion
        {
            SourceQuestionId = question.SourceQuestionId,
            Type = question.Type,
            Title = question.Title,
            Content = question.Content,
            Options = question.Options,
            AnswerIndexes = TheoryExamService.NormalizeIndexes(question.AnswerIndexes),
            Score = question.Score,
            Order = question.Order > 0 ? question.Order : index + 1
        }).ToList();
        paper.Title = payload.Model.Title.Trim();
        paper.Description = payload.Model.Description.Trim();
        paper.IsPublished = payload.Model.Publish;
        paper.PublishedAt = payload.Model.Publish ? paper.PublishedAt ?? DateTimeOffset.UtcNow : null;
        paper.UpdatedAt = DateTimeOffset.UtcNow;
        if (paper.Id == 0)
            context.TheoryPapers.Add(paper);
        await context.SaveChangesAsync(cancellationToken);
        await statistics.InvalidateAsync(payload.GameId, cancellationToken);
        return [Created(payload.GameId.ToString(), "theory-paper", paper.Id)];
    }

    async Task<List<AcademicImportResultItem>> ImportTeamsAsync(
        string payloadJson,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        var payload = Deserialize<TeamImportPayload>(payloadJson);
        var actorRole = await context.Users.AsNoTracking()
            .Where(user => user.Id == actorUserId)
            .Select(user => (Role?)user.Role)
            .SingleOrDefaultAsync(cancellationToken);
        if (actorRole is null || actorRole < Role.Admin)
            throw Terminal("team_import_admin_required", "Team imports require an administrator-owned token.");

        var references = payload.Items.SelectMany(item => item.Members.Append(item.Captain)).ToArray();
        var userIds = references.Where(item => item.UserId.HasValue).Select(item => item.UserId!.Value).Distinct().ToArray();
        var normalizedNames = references.Where(item => !string.IsNullOrWhiteSpace(item.UserName))
            .Select(item => item.UserName!.ToUpperInvariant()).Distinct(StringComparer.Ordinal).ToArray();
        var users = await context.Users.Where(user =>
                userIds.Contains(user.Id) || normalizedNames.Contains(user.NormalizedUserName!))
            .ToArrayAsync(cancellationToken);
        var usersById = users.ToDictionary(user => user.Id);
        var usersByName = users.Where(user => user.NormalizedUserName is not null)
            .ToDictionary(user => user.NormalizedUserName!, StringComparer.Ordinal);
        UserInfo Resolve(ExternalUserReferenceModel reference)
        {
            UserInfo? user = null;
            if (reference.UserId.HasValue)
                usersById.TryGetValue(reference.UserId.Value, out user);
            if (user is null && reference.UserName is not null)
                usersByName.TryGetValue(reference.UserName.ToUpperInvariant(), out user);
            if (user is null || reference.UserId.HasValue && user.Id != reference.UserId.Value ||
                reference.UserName is not null && !string.Equals(
                    user.NormalizedUserName, reference.UserName.ToUpperInvariant(), StringComparison.Ordinal))
                throw Terminal("team_user_not_found", "A referenced team user was not found or did not match.");
            return user;
        }

        var captains = payload.Items.Select(item => Resolve(item.Captain)).ToArray();
        foreach (var group in captains.GroupBy(captain => captain.Id))
        {
            var existing = await context.Teams.CountAsync(team => team.CaptainId == group.Key, cancellationToken);
            if (existing + group.Count() > MaxCaptainTeams)
                throw Terminal("team_captain_limit_exceeded", "A captain cannot own more than three teams.");
        }

        List<(TeamImportModel Model, Team Team)> imported = [];
        for (var index = 0; index < payload.Items.Count; index++)
        {
            var model = payload.Items[index];
            var captain = captains[index];
            var team = new Team
            {
                Name = model.Name,
                Bio = model.Bio,
                Locked = model.Locked,
                CaptainId = captain.Id,
                Captain = captain
            };
            team.Members.Add(captain);
            foreach (var member in model.Members.Select(Resolve))
                team.Members.Add(member);
            context.Teams.Add(team);
            imported.Add((model, team));
        }
        await context.SaveChangesAsync(cancellationToken);
        return imported.Select(pair => Created(pair.Model.ExternalId, "team", pair.Team.Id)).ToList();
    }

    async Task<ImageTemplate?> ResolveImageTemplateAsync(
        TrainingExerciseImportModel model,
        CancellationToken cancellationToken)
    {
        if (!model.Type.IsContainer())
            return null;
        if (model.Environment == EnvironmentType.Docker)
        {
            var image = model.ContainerImage!.Trim();
            var template = await context.ImageTemplates.AsNoTracking()
                .Where(item => item.ImageType == ImageType.Docker && item.Status == ImageStatus.Ready &&
                               item.RegistryUrl == image &&
                               (!model.ImageTemplateId.HasValue || item.Id == model.ImageTemplateId.Value))
                .OrderBy(item => item.Id)
                .FirstOrDefaultAsync(cancellationToken);
            return template ?? throw Terminal(
                "training_docker_image_unregistered", $"Docker image '{image}' is not a ready image template.");
        }
        if (model.Environment == EnvironmentType.WindowsVM)
        {
            var template = await context.ImageTemplates.AsNoTracking().SingleOrDefaultAsync(item =>
                item.Id == model.ImageTemplateId && item.OSType == OSType.Windows &&
                item.ImageType != ImageType.Docker && item.Status == ImageStatus.Ready,
                cancellationToken);
            return template ?? throw Terminal(
                "training_vm_template_invalid", $"VM template {model.ImageTemplateId} is not ready.");
        }
        throw Terminal("training_environment_invalid", "Container training exercises require Docker or WindowsVM.");
    }

    void RequireValidQuestion(TheoryQuestionEditModel model, int? score = null)
    {
        if (theoryService.NormalizeAndValidate(model, score) is { } error)
            throw Terminal("theory_question_invalid", error);
    }

    static ExerciseChallenge CreateTrainingExercise(int courseId, TrainingExerciseImportModel model) => new()
    {
        Title = model.Title,
        Content = model.Content,
        Category = model.Category,
        Type = model.Environment == EnvironmentType.WindowsVM ? ChallengeType.StaticContainer : model.Type,
        Difficulty = model.Difficulty,
        Credit = model.Credit,
        Tags = model.Tags ?? [],
        Hints = model.Hints,
        IsEnabled = model.IsEnabled,
        ContainerImage = model.ContainerImage?.Trim(),
        MemoryLimit = model.MemoryLimit,
        StorageLimit = model.StorageLimit,
        CPUCount = model.CPUCount,
        ExposePort = model.ExposePort,
        NetworkMode = model.NetworkMode,
        Environment = model.Environment,
        ImageTemplateId = model.ImageTemplateId,
        FlagTemplate = string.IsNullOrWhiteSpace(model.FlagTemplate) ? null : model.FlagTemplate.Trim(),
        TrainingCourseId = courseId,
        PoolSource = ExercisePoolSource.Training,
        Attachment = CreateAttachment(model.Attachment),
        Flags = model.Flags?.Select(flag => new FlagContext
        {
            Flag = flag.Flag.Trim(),
            OrderIndex = flag.OrderIndex,
            Description = flag.Description,
            ScoreMode = flag.ScoreMode,
            FixedScore = flag.FixedScore,
            MaxAttempts = flag.MaxAttempts,
            AttachmentHash = flag.AttachmentHash,
            AnswerType = flag.AnswerType,
            CustomName = flag.CustomName,
            Attachment = CreateAttachment(flag.Attachment)
        }).ToList() ?? []
    };

    static Attachment? CreateAttachment(ExerciseOpenApiAttachmentModel? model) => model is null
        ? null
        : new Attachment { Type = FileType.Remote, RemoteUrl = model.RemoteUrl.Trim() };

    static T Deserialize<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, AcademicImportApplicationService.JsonOptions) ?? throw new JsonException();

    static AcademicImportResultItem Created(string externalId, string resourceType, int resourceId) =>
        new(externalId, resourceType, resourceId.ToString(), "created");

    static ApiOperationTerminalException Terminal(string code, string message) => new(code, message);
}
