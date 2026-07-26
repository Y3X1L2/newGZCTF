using GZCTF.Integration.Test.Base;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Repositories.Interface;
using GZCTF.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GZCTF.Integration.Test.Tests.Repository;

[Collection(nameof(IntegrationTestCollection))]
public class InstanceFlagIsolationTests(GZCTFApplicationFactory factory)
{
    [Fact]
    public async Task TrainingDynamicFlags_AreUniqueInstanceOwnedAndCrossSubmissionFails()
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var repository = scope.ServiceProvider.GetRequiredService<IExerciseInstanceRepository>();
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var firstUser = CreateUser($"df1{suffix}");
        var secondUser = CreateUser($"df2{suffix}");
        var exercise = new ExerciseChallenge
        {
            Title = $"dynamic-{suffix}",
            Content = "dynamic flag isolation",
            Type = ChallengeType.DynamicContainer,
            FlagTemplate = "flag{[TEAM_HASH]}",
            IsEnabled = true
        };

        context.AddRange(
            firstUser,
            secondUser,
            exercise,
            new ExerciseInstance { User = firstUser, Exercise = exercise },
            new ExerciseInstance { User = secondUser, Exercise = exercise });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var first = await repository.GetInstance(firstUser, exercise.Id);
        var second = await repository.GetInstance(secondUser, exercise.Id);

        Assert.NotNull(first?.FlagContext);
        Assert.NotNull(second?.FlagContext);
        Assert.NotEqual(first.FlagContext.Flag, second.FlagContext.Flag);
        Assert.DoesNotContain("TestTeamHash", first.FlagContext.Flag, StringComparison.Ordinal);
        Assert.DoesNotContain("TestTeamHash", second.FlagContext.Flag, StringComparison.Ordinal);
        Assert.Null(first.FlagContext.ExerciseId);
        Assert.Null(second.FlagContext.ExerciseId);
        Assert.False(context.Entry(first.Exercise).Collection(item => item.Flags).IsLoaded);

        var firstOwn = await repository.VerifyAnswer(firstUser, first, first.FlagContext.Flag, 0);
        var firstCross = await repository.VerifyAnswer(firstUser, first, second.FlagContext.Flag, 0);
        var secondOwn = await repository.VerifyAnswer(secondUser, second, second.FlagContext.Flag, 0);

        Assert.Equal(AnswerResult.Accepted, firstOwn.Status);
        Assert.Equal(AnswerResult.WrongAnswer, firstCross.Status);
        Assert.Equal(AnswerResult.Accepted, secondOwn.Status);
    }

    [Fact]
    public async Task StaticExercise_LoadsConfiguredFlagsWhileDynamicExerciseDoesNot()
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var repository = scope.ServiceProvider.GetRequiredService<IExerciseInstanceRepository>();
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var user = CreateUser($"sf{suffix}");
        var staticExercise = new ExerciseChallenge
        {
            Title = $"static-{suffix}",
            Content = "static flag projection",
            Type = ChallengeType.StaticContainer,
            IsEnabled = true,
            Flags =
            [
                new FlagContext { Flag = "flag{static-1}", OrderIndex = 1 },
                new FlagContext { Flag = "flag{static-2}", OrderIndex = 2 }
            ]
        };
        foreach (var flag in staticExercise.Flags)
            flag.Exercise = staticExercise;

        context.AddRange(
            user,
            staticExercise,
            new ExerciseInstance { User = user, Exercise = staticExercise, IsLoaded = true });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var instance = await repository.GetInstance(user, staticExercise.Id);

        Assert.NotNull(instance);
        Assert.True(context.Entry(instance.Exercise).Collection(item => item.Flags).IsLoaded);
        Assert.Equal(2, instance.Exercise.Flags.Count);
    }

    [Fact]
    public async Task GameDynamicFlag_IsInstanceOwnedAndStaticFlagsRemainLoadable()
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var repository = scope.ServiceProvider.GetRequiredService<IGameInstanceRepository>();
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var user = CreateUser($"gf{suffix}");
        var team = new Team { Name = $"g{suffix}", Captain = user, CaptainId = user.Id };
        var game = new Game
        {
            Title = $"game-{suffix}",
            PublicKey = "public",
            PrivateKey = "private"
        };
        var participation = new Participation
        {
            Game = game,
            Team = team,
            Token = $"token-{suffix}",
            Status = ParticipationStatus.Accepted
        };
        var dynamicChallenge = new GameChallenge
        {
            Game = game,
            Title = $"dynamic-{suffix}",
            Content = "dynamic game flag",
            Type = ChallengeType.DynamicContainer,
            FlagTemplate = "flag{[TEAM_HASH]}",
            IsEnabled = true
        };
        var staticChallenge = new GameChallenge
        {
            Game = game,
            Title = $"static-{suffix}",
            Content = "static game flags",
            Type = ChallengeType.StaticContainer,
            IsEnabled = true,
            Flags =
            [
                new FlagContext { Flag = "flag{game-static-1}", OrderIndex = 1 },
                new FlagContext { Flag = "flag{game-static-2}", OrderIndex = 2 }
            ]
        };
        foreach (var flag in staticChallenge.Flags)
            flag.Challenge = staticChallenge;

        context.AddRange(
            new GameInstance { Participation = participation, Challenge = dynamicChallenge },
            new GameInstance { Participation = participation, Challenge = staticChallenge, IsLoaded = true });
        await context.SaveChangesAsync();
        var participationId = participation.Id;
        context.ChangeTracker.Clear();
        participation = await context.Participations.SingleAsync(item => item.Id == participationId);

        var dynamicInstance = await repository.GetInstance(participation, dynamicChallenge.Id);
        var staticInstance = await repository.GetInstance(participation, staticChallenge.Id);

        Assert.NotNull(dynamicInstance?.FlagContext);
        Assert.Null(dynamicInstance.FlagContext.ChallengeId);
        Assert.Null(await context.FlagContexts
            .Where(flag => flag.Id == dynamicInstance.FlagId)
            .Select(flag => EF.Property<int?>(flag, "GameChallengeId"))
            .SingleAsync());
        Assert.False(context.Entry(dynamicInstance.Challenge).Collection(item => item.Flags).IsLoaded);

        Assert.NotNull(staticInstance);
        Assert.True(context.Entry(staticInstance.Challenge).Collection(item => item.Flags).IsLoaded);
        Assert.Equal(2, staticInstance.Challenge.Flags.Count);
    }

    private static UserInfo CreateUser(string userName) => new()
    {
        UserName = userName,
        NormalizedUserName = userName.ToUpperInvariant(),
        Email = $"{userName}@example.test",
        NormalizedEmail = $"{userName.ToUpperInvariant()}@EXAMPLE.TEST"
    };
}
