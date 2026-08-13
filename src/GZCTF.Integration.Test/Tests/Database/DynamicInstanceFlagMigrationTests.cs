using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Testcontainers.PostgreSql;
using Xunit;

namespace GZCTF.Integration.Test.Tests.Database;

public sealed class DynamicInstanceFlagMigrationTests : IAsyncLifetime
{
    private const string PreviousMigration = "20260721151047_CompletePhaseTwoInstanceReadiness";
    private const string CurrentMigration = "20260726083459_IsolateDynamicInstanceFlags";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("gzctf_dynamic_flag_isolation")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .WithCleanUp(true)
        .Build();

    public Task InitializeAsync() => _postgres.StartAsync();

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task Migration_DetachesOnlyDynamicContainerInstanceFlags()
    {
        int dynamicGameFlagId;
        int attachmentGameFlagId;
        int staticGameFlagId;
        int dynamicExerciseFlagId;
        int attachmentExerciseFlagId;
        int staticExerciseFlagId;
        int dynamicGameChallengeId;
        int attachmentGameChallengeId;
        int dynamicExerciseId;
        int attachmentExerciseId;
        var userId = Guid.CreateVersion7();

        await using (var context = CreateContext())
        {
            await context.Database.GetService<IMigrator>().MigrateAsync(PreviousMigration);
            await ExercisePoolMigrationTestCompatibility.AddCurrentExerciseColumnsAsync(context);

            var user = new UserInfo
            {
                Id = userId,
                UserName = "flagmigration",
                NormalizedUserName = "FLAGMIGRATION",
                Email = "dynamic-flag-migration@example.test",
                NormalizedEmail = "DYNAMIC-FLAG-MIGRATION@EXAMPLE.TEST"
            };
            var team = new Team { Name = "flag-migrate", Captain = user, CaptainId = user.Id };
            var game = new Game
            {
                Title = "dynamic-flag-migration",
                PublicKey = "public",
                PrivateKey = "private"
            };
            var participation = new Participation
            {
                Game = game,
                Team = team,
                Token = "migration-token",
                Status = ParticipationStatus.Accepted
            };

            var dynamicGame = CreateGameChallenge(game, "dynamic-game", ChallengeType.DynamicContainer);
            var attachmentGame = CreateGameChallenge(game, "attachment-game", ChallengeType.DynamicAttachment);
            var staticGame = CreateGameChallenge(game, "static-game", ChallengeType.StaticContainer);
            var dynamicGameFlag = AttachGameFlag(dynamicGame, "flag{dynamic-game}");
            var attachmentGameFlag = AttachGameFlag(attachmentGame, "flag{attachment-game}");
            var staticGameFlag = AttachGameFlag(staticGame, "flag{static-game}");

            var dynamicExercise = CreateExercise("dynamic-exercise", ChallengeType.DynamicContainer);
            var attachmentExercise = CreateExercise("attachment-exercise", ChallengeType.DynamicAttachment);
            var staticExercise = CreateExercise("static-exercise", ChallengeType.StaticContainer);
            var dynamicExerciseFlag = AttachExerciseFlag(dynamicExercise, "flag{dynamic-exercise}");
            var attachmentExerciseFlag = AttachExerciseFlag(attachmentExercise, "flag{attachment-exercise}");
            var staticExerciseFlag = AttachExerciseFlag(staticExercise, "flag{static-exercise}");

            context.AddRange(
                new GameInstance
                {
                    Participation = participation,
                    Challenge = dynamicGame,
                    FlagContext = dynamicGameFlag,
                    IsLoaded = true
                },
                new GameInstance
                {
                    Participation = participation,
                    Challenge = attachmentGame,
                    FlagContext = attachmentGameFlag,
                    IsLoaded = true
                },
                new ExerciseInstance
                {
                    User = user,
                    Exercise = dynamicExercise,
                    FlagContext = dynamicExerciseFlag,
                    IsLoaded = true
                },
                new ExerciseInstance
                {
                    User = user,
                    Exercise = attachmentExercise,
                    FlagContext = attachmentExerciseFlag,
                    IsLoaded = true
                });
            context.AddRange(staticGame, staticExercise);
            await context.SaveChangesAsync();

            dynamicGameFlagId = dynamicGameFlag.Id;
            attachmentGameFlagId = attachmentGameFlag.Id;
            staticGameFlagId = staticGameFlag.Id;
            dynamicExerciseFlagId = dynamicExerciseFlag.Id;
            attachmentExerciseFlagId = attachmentExerciseFlag.Id;
            staticExerciseFlagId = staticExerciseFlag.Id;
            dynamicGameChallengeId = dynamicGame.Id;
            attachmentGameChallengeId = attachmentGame.Id;
            dynamicExerciseId = dynamicExercise.Id;
            attachmentExerciseId = attachmentExercise.Id;

            Assert.Equal(dynamicGameChallengeId, dynamicGameFlag.ChallengeId);
            Assert.Equal(dynamicExerciseId, dynamicExerciseFlag.ExerciseId);

            await ExercisePoolMigrationTestCompatibility.RemoveCurrentExerciseColumnsAsync(context);
            await context.Database.GetService<IMigrator>().MigrateAsync(CurrentMigration);
        }

        await using var migrated = CreateContext();
        var flags = await migrated.FlagContexts
            .AsNoTracking()
            .Where(flag => new[]
            {
                dynamicGameFlagId,
                attachmentGameFlagId,
                staticGameFlagId,
                dynamicExerciseFlagId,
                attachmentExerciseFlagId,
                staticExerciseFlagId
            }.Contains(flag.Id))
            .ToDictionaryAsync(flag => flag.Id);

        Assert.Null(flags[dynamicGameFlagId].ChallengeId);
        Assert.Null(await GameChallengeCollectionId(migrated, dynamicGameFlagId));
        Assert.Equal(attachmentGameChallengeId, flags[attachmentGameFlagId].ChallengeId);
        Assert.Equal(attachmentGameChallengeId, await GameChallengeCollectionId(migrated, attachmentGameFlagId));
        Assert.NotNull(flags[staticGameFlagId].ChallengeId);
        Assert.NotNull(await GameChallengeCollectionId(migrated, staticGameFlagId));

        Assert.Null(flags[dynamicExerciseFlagId].ExerciseId);
        Assert.Equal(attachmentExerciseId, flags[attachmentExerciseFlagId].ExerciseId);
        Assert.NotNull(flags[staticExerciseFlagId].ExerciseId);

        Assert.Equal(dynamicGameFlagId, await migrated.GameInstances
            .Where(instance => instance.ChallengeId == dynamicGameChallengeId)
            .Select(instance => instance.FlagId)
            .SingleAsync());
        Assert.Equal(dynamicExerciseFlagId, await migrated.ExerciseInstances
            .Where(instance => instance.UserId == userId && instance.ExerciseId == dynamicExerciseId)
            .Select(instance => instance.FlagId)
            .SingleAsync());
    }

    private AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql($"{_postgres.GetConnectionString()};Include Error Detail=true")
            .Options;
        return new AppDbContext(options) { SuppressProjectionRevisionBumps = true };
    }

    private static GameChallenge CreateGameChallenge(Game game, string title, ChallengeType type) => new()
    {
        Game = game,
        Title = title,
        Content = title,
        Type = type,
        IsEnabled = true
    };

    private static ExerciseChallenge CreateExercise(string title, ChallengeType type) => new()
    {
        Title = title,
        Content = title,
        Type = type,
        IsEnabled = true
    };

    private static FlagContext AttachGameFlag(GameChallenge challenge, string value)
    {
        var flag = new FlagContext { Flag = value, Challenge = challenge };
        challenge.Flags.Add(flag);
        return flag;
    }

    private static FlagContext AttachExerciseFlag(ExerciseChallenge exercise, string value)
    {
        var flag = new FlagContext { Flag = value, Exercise = exercise };
        exercise.Flags.Add(flag);
        return flag;
    }

    private static Task<int?> GameChallengeCollectionId(AppDbContext context, int flagId) =>
        context.FlagContexts
            .Where(flag => flag.Id == flagId)
            .Select(flag => EF.Property<int?>(flag, "GameChallengeId"))
            .SingleAsync();
}
