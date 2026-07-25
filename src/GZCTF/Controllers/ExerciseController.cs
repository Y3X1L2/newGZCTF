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
        var exercises = await exerciseService.GetExerciseListAsync(filter, token);
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

    [HttpPost("{id:int}/flag")]
    public async Task<IActionResult> SubmitFlag(int id, [FromBody] FlagSubmitModel model, CancellationToken token)
    {
        var user = await CurrentUser();
        var (status, _) = await exerciseService.SubmitFlagAsync(user, id, model.Flag ?? string.Empty, model.FlagId, token);
        return Ok(new { status });
    }

    [HttpPost("{id:int}/container")]
    public async Task<IActionResult> CreateContainer(int id, CancellationToken token)
    {
        var user = await CurrentUser();
        var result = await exerciseService.CreateContainerAsync(user, id, token);
        if (result.Status == TaskStatus.Faulted)
            return BadRequest();

        return Ok(result);
    }

    [HttpPost("import")]
    public async Task<IActionResult> ImportFromGame([FromBody] ExerciseImportFromGameModel model, CancellationToken token)
    {
        var exercises = await managementService.ImportFromGameAsync(model.GameId, model.ChallengeIds, token);
        return Ok(exercises);
    }

    [HttpPost]
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
            IsEnabled = true
        };

        var created = await managementService.CreateExerciseAsync(exercise, token);
        return Ok(created);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> UpdateExercise(int id, [FromBody] ExerciseCreateModel model, CancellationToken token)
    {
        var exercise = await managementService.UpdateExerciseAsync(
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
                IsEnabled = true
            }, token);

        return Ok(exercise);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteExercise(int id, CancellationToken token)
    {
        await managementService.RemoveExerciseAsync(id, token);
        return Ok();
    }
}
