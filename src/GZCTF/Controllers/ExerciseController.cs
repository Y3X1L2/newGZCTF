using GZCTF.Middlewares;
using GZCTF.Modules.Exercise.Application;
using GZCTF.Models.Request.Exercise;
using GZCTF.Models.Request.Game;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Controllers;

[RequireUser]
[ApiController]
[Route("api/[controller]")]
[ProducesResponseType(typeof(RequestResponse), StatusCodes.Status401Unauthorized)]
[ProducesResponseType(typeof(RequestResponse), StatusCodes.Status403Forbidden)]
public class ExerciseController(
    IExerciseService exerciseService,
    IExerciseManagementService managementService,
    UserManager<UserInfo> userManager) : ControllerBase
{
    private async Task<UserInfo> CurrentUser() =>
        await userManager.GetUserAsync(User) ?? throw new InvalidOperationException("Current user is missing.");

    [HttpGet]
    public async Task<IActionResult> GetExercises([FromQuery] ExerciseFilter? filter, CancellationToken token)
    {
        var user = await CurrentUser();
        var exercises = await exerciseService.GetExerciseListAsync(filter, token, user.Id, user.Role);
        return Ok(exercises);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetExercise(int id, CancellationToken token)
    {
        var user = await CurrentUser();
        var detail = await exerciseService.GetExerciseDetailAsync(user, id, token);
        if (detail is null)
            return NotFound();

        return Ok(detail);
    }

    [HttpGet("{id:int}/manage")]
    [RequireTeacher]
    public async Task<IActionResult> GetExerciseForManagement(int id, CancellationToken token)
    {
        var exercise = await managementService.GetExerciseForUpdateAsync(id, token);
        return exercise is null ? NotFound() : Ok(ExerciseManagementModel.FromExercise(exercise));
    }

    [HttpPost("{id:int}/flag")]
    public async Task<IActionResult> SubmitFlag(int id, [FromBody] FlagSubmitModel model, CancellationToken token)
    {
        var user = await CurrentUser();
        var (status, flagId) = await exerciseService.SubmitFlagAsync(
            user,
            id,
            model.Flag ?? string.Empty,
            model.FlagId,
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            token);
        return Ok(new { status, flagId });
    }

    [HttpPost("{id:int}/container")]
    public async Task<IActionResult> CreateContainer(int id, CancellationToken token)
    {
        var user = await CurrentUser();
        var result = await exerciseService.CreateContainerAsync(user, id, token);
        if (result is QueuedTaskResult<Container> queued)
            return Accepted(new { status = "queued", queue = queued.QueueStatus });
        if (result.Status == TaskStatus.NotFound)
            return NotFound();
        if (result.Status != TaskStatus.Success || result.Result is null)
            return BadRequest(new RequestResponse("练习容器创建失败，请稍后重试。"));

        return Ok(ContainerInfoModel.FromContainer(result.Result));
    }

    [HttpPost("{id:int}/container/extend")]
    public async Task<IActionResult> ExtendContainer(int id, CancellationToken token)
    {
        var result = await exerciseService.ExtendContainerAsync(await CurrentUser(), id, token);
        if (result.Status == TaskStatus.NotFound)
            return NotFound();
        if (result.Status != TaskStatus.Success || result.Result is null)
            return BadRequest(new RequestResponse("当前实例无法延期。"));
        return Accepted(result.Result);
    }

    [HttpDelete("{id:int}/container")]
    public async Task<IActionResult> DestroyContainer(int id, CancellationToken token)
    {
        var result = await exerciseService.DestroyContainerAsync(await CurrentUser(), id, token);
        if (result.Status == TaskStatus.NotFound)
            return NotFound();
        if (result.Status != TaskStatus.Success || result.Result is null)
            return BadRequest(new RequestResponse("当前实例无法销毁。"));
        return Accepted(result.Result);
    }

    [HttpPost("import")]
    [RequireTeacher]
    public async Task<IActionResult> ImportFromGame([FromBody] ExerciseImportFromGameModel model, CancellationToken token)
    {
        var exercises = await managementService.ImportFromGameAsync(model.GameId, model.ChallengeIds, token);
        return Ok(exercises);
    }

    [HttpPost("import/training")]
    [RequireTeacher]
    public async Task<IActionResult> ImportFromTraining([FromBody] ExerciseImportFromTrainingModel model, CancellationToken token)
    {
        var exercises = await managementService.ImportFromTrainingAsync(model.CourseId, model.ChallengeIds, token);
        return Ok(exercises);
    }

    [HttpPost]
    [RequireTeacher]
    public async Task<IActionResult> CreateExercise([FromBody] ExerciseCreateModel model, CancellationToken token)
    {
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
            FlagTemplate = model.FlagTemplate,
            IsEnabled = model.IsEnabled,
            NetworkMode = model.NetworkMode,
            Environment = model.Environment,
            ImageTemplateId = model.ImageTemplateId,
            SubmissionLimit = model.SubmissionLimit
        };

        var created = await managementService.CreateExerciseWithRelationsAsync(
            exercise, model.Flags, model.Attachment, token);
        return Ok(created);
    }

    [HttpPut("{id:int}")]
    [RequireTeacher]
    public async Task<IActionResult> UpdateExercise(int id, [FromBody] ExerciseCreateModel model, CancellationToken token)
    {
        var exercise = await managementService.UpdateExerciseWithRelationsAsync(
            new ExerciseChallenge
            {
                Id = id,
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
                FlagTemplate = model.FlagTemplate,
                IsEnabled = model.IsEnabled,
                NetworkMode = model.NetworkMode,
                Environment = model.Environment,
                ImageTemplateId = model.ImageTemplateId,
                SubmissionLimit = model.SubmissionLimit
            }, model.Flags, model.Attachment, token);

        return Ok(exercise);
    }

    [HttpDelete("{id:int}")]
    [RequireTeacher]
    public async Task<IActionResult> DeleteExercise(int id, CancellationToken token)
    {
        await managementService.RemoveExerciseAsync(id, token);
        return Ok();
    }
}
