using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;
using GZCTF.Modules.TeamLab.Api;
using System.Net;
using System.Text.Json.Nodes;
using GZCTF.Infrastructure.Concurrency;
using GZCTF.Integration.Test.Base;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Modules.Penetration.Application;
using GZCTF.Modules.Penetration.Domain;
using GZCTF.Modules.TeamLab;
using GZCTF.Modules.TeamLab.Application;
using GZCTF.Modules.TeamLab.Domain;
using GZCTF.Modules.TeamLab.Infrastructure.Netlab;
using GZCTF.Services.Config;
using GZCTF.Repositories;
using GZCTF.Repositories.Interface;
using GZCTF.Utils;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Newtonsoft.Json;
using Testcontainers.PostgreSql;
using Xunit;

namespace GZCTF.Integration.Test.Tests.Runtime;

public sealed class NetlabCompetitionTests
{
    [Fact]
    public async Task UpgradeAndLifecycle_KeepHistoryAndDelegateCreationRebuildAndCleanup()
    {
        var connectionString = System.Environment.GetEnvironmentVariable("GZCTF_TEST_DATABASE");
        await using var postgres = connectionString is null ? new PostgreSqlBuilder("postgres:16-alpine").Build() : null;
        if (connectionString is null)
        {
            await postgres!.StartAsync();
            connectionString = postgres.GetConnectionString();
        }
        await using var database = await IsolatedPostgresDatabase.CreateAsync(connectionString);
        await using var db = database.CreateContext();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20261017184129_AddUserDeletionTombstone");
        var owner = new UserInfo { UserName = "scene-owner", Role = Role.Teacher };
        var team = new Team { Name = "队伍一", Captain = owner, Members = [owner] };
        var game = new Game { Title = "组网比赛", Owner = owner, GameType = GameType.Penetration,
            EndTimeUtc = DateTimeOffset.UtcNow.AddDays(1) };
        var topology = new TeamLabTopology { Name = "历史场景", OwnerUserId = owner.Id };
        db.AddRange(owner, team, game, topology);
        await db.SaveChangesAsync();
        var now = DateTimeOffset.UtcNow;
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "PenetrationGameLabBindings" ("GameId","TopologyId","MaxResetCount","ObjectiveRevision","CreatedAt","UpdatedAt")
            VALUES ({game.Id},{topology.Id},1,0,{now},{now})
            """);
        await migrator.MigrateAsync();
        Assert.False(db.Database.HasPendingModelChanges());
        var settings = await db.PenetrationGameLabBindings.SingleAsync();
        Assert.Equal(topology.Id, settings.TopologyId);
        Assert.Null(settings.BlueprintId);
        settings.BlueprintId = "scene";
        settings.BlueprintVersionId = "version";
        db.Participations.Add(new Participation { Game = game, Team = team, Status = ParticipationStatus.Accepted });
        db.PenetrationObjectives.AddRange(
            new PenetrationObjective { GameId = game.Id, Key = "web", TopologyAssetKey = "web", Title = "门户", IsDynamic = true },
            new PenetrationObjective { GameId = game.Id, Key = "domain", TopologyAssetKey = "domain", Title = "域控", IsDynamic = true });
        await db.SaveChangesAsync();

        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(server => server.Listen(IPAddress.Loopback, 0));
        builder.Services.AddTeamLabModule();
        await using var upstream = builder.Build();
        List<JsonObject> creates = [], actions = [];
        var state = "running";
        var operationState = "running";
        var interruptReset = true;
        var resources = new { cpu = 1, memoryMiB = 512, diskGiB = 8 };
        var assets = new[] {
            new { id = "web", name = "门户", templateId = "container", resources, interfaces = Array.Empty<object>() },
            new { id = "domain", name = "域控", templateId = "vm", resources, interfaces = Array.Empty<object>() }
        };
        var spec = new { assets, networks = Array.Empty<object>() };
        upstream.Run(async http =>
        {
            Assert.False(http.Request.Headers.ContainsKey("X-Netlab-Context"));
            object response = http.Request.Path.Value switch
            {
                "/api/v1/blueprints/scene" => new { id = "scene", name = "场景模板", projectId = "test", latestVersionId = "version",
                    latestVersion = 2, assetCount = 2, networkCount = 0, createdAt = now, updatedAt = now },
                "/api/v1/blueprint-versions/version" => new { id = "version", blueprintId = "scene", version = 2,
                    assetCount = 2, networkCount = 0, createdAt = now, spec, view = new { }, sourceEnvironmentId = "environment", sourceRevision = 1 },
                "/api/v1/templates" => new object[] {
                    new { id = "container", name = "Linux", kind = "container", os = "linux", version = 1, source = "", resources },
                    new { id = "vm", name = "Windows", kind = "vm", os = "windows", version = 1, source = "", resources }
                },
                "/api/v1/environments/environment" => new { id = "environment", name = "队伍环境", projectId = "test",
                    externalReference = $"game:{game.Id}/team:{team.Id}", revision = 7, status = "running", view = new { }, createdAt = now, updatedAt = now,
                    spec = new { networks = Array.Empty<object>(), assets = new[] { new { id = "web", name = "门户", templateId = "container",
                        resources, interfaces = Array.Empty<object>(), parameters = new { GZCTF_FLAG = "private-flag" } } } },
                    permissions = new[] { "read", "compose", "operate", "session", "observe", "access" } },
                "/api/v1/environments/environment/state" => new { id = "environment", status = state, revision = 7, assets = Array.Empty<object>(), updatedAt = now },
                "/api/v1/operations/rebuild" => new { id = "rebuild", scopeKind = "environment", scopeId = "environment",
                    kind = "rebuild", state = operationState, phase = "execute", completed = 0, total = 1, retryable = false, createdAt = now, updatedAt = now },
                _ => new object()
            };
            if (http.Request.Method == "POST")
            {
                var body = (JsonObject)(await JsonNode.ParseAsync(http.Request.Body))!;
                if (http.Request.Path == "/api/v1/environments")
                {
                    creates.Add(body);
                    response = new { id = "environment", projectId = body["projectId"]!.GetValue<string>(), name = "组网比赛 · 队伍一",
                        revision = 1, status = "deploying", spec, view = new { }, createdAt = now, updatedAt = now };
                }
                else if (http.Request.Path == "/api/v1/environments/environment/actions")
                {
                    actions.Add(body);
                    if (body["action"]!.GetValue<string>() == "rebuild" && interruptReset)
                    {
                        interruptReset = false;
                        http.Response.StatusCode = 503;
                        await http.Response.WriteAsJsonAsync(new { detail = "upstream interrupted" });
                        return;
                    }
                    response = new { id = "rebuild", scopeKind = "environment", scopeId = "environment",
                        kind = body["action"]!.GetValue<string>(), state = "running", phase = "execute", completed = 0, total = 1, retryable = false, createdAt = now, updatedAt = now };
                }
                else throw new InvalidOperationException("Unexpected lifecycle endpoint");
            }
            else if (http.Request.Path == "/api/v1/environments")
                response = state == "destroyed" ? Array.Empty<object>() : new[] { new { id = "environment", projectId = "test", externalReference = $"game:{game.Id}/team:{team.Id}", name = "组网比赛 · 队伍一",
                    revision = 7, status = state, assetCount = 2, networkCount = 0, createdAt = now, updatedAt = now } };
            http.Response.ContentType = "application/json";
            await http.Response.WriteAsync(JsonConvert.SerializeObject(response));
        });
        await upstream.StartAsync();
        var protection = new EphemeralDataProtectionProvider();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["Netlab:Endpoint"] = upstream.Urls.Single() + "/", ["Netlab:ProjectId"] = "test",
            ["Netlab:Credential"] = protection.CreateProtector("Netlab.ServiceCredential").Protect(Guid.NewGuid().ToString())
        }).Build();
        var clients = upstream.Services.GetRequiredService<IHttpClientFactory>();
        var connection = new NetlabConnection(configuration, null!, protection, clients);
        var client = new NetlabClient(connection, clients);
        var leases = new LocalDevelopmentLeaseProvider();
        var config = Mock.Of<IConfigService>(x => x.GetXorKey() == new byte[32]);
        var objectives = new PenetrationObjectiveService(db, client, connection, null!, leases, null!, null!,
            NullLogger<PenetrationObjectiveService>.Instance, config);
        var scenes = new PenetrationTeamLabAdapter(db, client, connection, null!, objectives, leases);
        var deployed = await scenes.DeployTeamAsync(game.Id, team.Id, default);
        Assert.Equal("environment", deployed.RuntimePublicId);
        Assert.True((await scenes.DeployTeamAsync(game.Id, team.Id, default)).Reused);
        var create = Assert.Single(creates);
        Assert.Equal("test", create["projectId"]!.GetValue<string>());
        var webFlag = create["spec"]!["assets"]![0]!["parameters"]!["GZCTF_FLAG"]!.GetValue<string>();
        Assert.StartsWith("flag{", webFlag);
        Assert.NotNull(create["spec"]!["assets"]![1]!["guest"]!["files"]![@"C:\ProgramData\GZCTF\Runtime\secrets\GZCTF_FLAG"]);
        Assert.NotEqual(webFlag, (await objectives.BuildOverlaysAsync(game.Id, team.Id + 1, 2, default))["web"]["GZCTF_FLAG"]);
        Assert.Null((await db.PenetrationTeamRuntimeBindings.SingleAsync()).RuntimeId);
        Assert.Empty(await db.DeploymentQueueTickets.ToArrayAsync());

        await Assert.ThrowsAsync<TeamLabApiContractException>(() => scenes.ResetTeamAsync(game.Id, team.Id, owner.Id, false, default));
        await scenes.ResetTeamAsync(game.Id, team.Id, owner.Id, false, default);
        Assert.Equal(actions[0]["clientRequestId"]!.GetValue<string>(), actions[1]["clientRequestId"]!.GetValue<string>());
        Assert.Equal(7, actions[1]["expectedRevision"]!.GetValue<int>());
        Assert.True((await scenes.ResetTeamAsync(game.Id, team.Id, owner.Id, false, default)).Reused);
        Assert.Equal(2, actions.Count);
        operationState = "succeeded";
        await Assert.ThrowsAsync<TeamLabApiContractException>(() => scenes.ResetTeamAsync(game.Id, team.Id, owner.Id, false, default));
        await Assert.ThrowsAsync<TeamLabApiContractException>(() => scenes.EnsureTeamDrainedBeforeDeleteAsync(team.Id, default));
        Assert.Equal("destroy", actions[^1]["action"]!.GetValue<string>());
        state = "destroyed";
        await scenes.EnsureTeamDrainedBeforeDeleteAsync(team.Id, default);
        await scenes.EnsureGameDrainedBeforeDeleteAsync(game.Id, default);
        Assert.Equal(1, await db.PenetrationResetRecords.CountAsync());
        var completed = await scenes.GetGameTeamLabAsync(game.Id, default);
        Assert.Equal("completed", completed.Rollout!.Status);
        Assert.Equal(1, completed.Rollout.Counts.Destroyed);

        game.StartTimeUtc = now.AddHours(-1);
        settings.AccessOpen = true;
        var participation = await db.Participations.SingleAsync();
        participation.Members.Add(new UserParticipation(owner, game, team) { Participation = participation });
        await db.SaveChangesAsync();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped(_ => database.CreateContext());
        services.AddIdentityCore<UserInfo>().AddEntityFrameworkStores<AppDbContext>();
        services.AddSingleton(connection);
        services.AddSingleton(client);
        services.AddScoped<IParticipationRepository>(s => new ParticipationRepository(null!, null!, s.GetRequiredService<AppDbContext>()));
        services.AddScoped<NetlabWorkspaceAccess>();
        await using var provider = services.BuildServiceProvider();
        await using var accessScope = provider.CreateAsyncScope();
        var access = accessScope.ServiceProvider.GetRequiredService<NetlabWorkspaceAccess>();
        scenes = new PenetrationTeamLabAdapter(db, client, connection, access, objectives, leases);
        Assert.Equal("scene", (await scenes.BindAsync(game.Id, "scene", owner.Id, default)).TopologyId);
        Assert.Equal("version", (await scenes.ActivateReleaseAsync(game.Id, "version", owner.Id, default)).ActiveReleaseId);
        var teacher = await access.AuthorizeAsync(owner, null, default);
        Assert.False(teacher.Administrator);
        Assert.Equal(game.Id, Assert.Single(teacher.Games));
        await Assert.ThrowsAsync<TeamLabApiContractException>(() => access.AuthorizeAsync(new UserInfo(), null, default));
        var player = await access.AuthorizeAsync(owner, game.Id, default);
        Assert.Equal("environment", player.EnvironmentId);
        Assert.True(player.CanRead("test", "environment", null));
        Assert.False(player.CanRead("test", "other-team", null));
        var http = new DefaultHttpContext { RequestServices = accessScope.ServiceProvider,
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, owner.Id.ToString())], "test")) };
        http.Response.Body = new MemoryStream();
        var controller = new NetlabWorkspaceController(accessScope.ServiceProvider.GetRequiredService<UserManager<UserInfo>>(),
            connection, access, new NetlabProxy(connection, clients), client) { ControllerContext = new ControllerContext { HttpContext = http } };
        http.Request.Method = "POST";
        await Assert.ThrowsAsync<TeamLabApiContractException>(() => controller.GameProxy(game.Id, "api/v1/environments/other-team/actions", default));
        await Assert.ThrowsAsync<TeamLabApiContractException>(() => controller.GameProxy(game.Id, "api/v1/environments/environment/actions", default));
        http.Request.Method = "GET";
        await controller.GameProxy(game.Id, "api/v1/environments/environment", default);
        var visible = Encoding.UTF8.GetString(((MemoryStream)http.Response.Body).ToArray());
        Assert.DoesNotContain("private-flag", visible);
        Assert.DoesNotContain("compose", visible);
        Assert.Contains("session", visible);
        http.Response.Body.SetLength(0);
        await controller.GameProxy(game.Id, "api/v1/environments/environment/assets/web/files", default);
        Assert.Equal(StatusCodes.Status200OK, http.Response.StatusCode);
        http.Response.Body.SetLength(0);
        http.Request.Method = "POST";
        var payload = JsonConvert.SerializeObject(new { name = "新场景", projectId = "foreign", externalReference = "other-user", spec });
        http.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(payload));
        http.Request.ContentLength = http.Request.Body.Length;
        await controller.AdminProxy("api/v1/environments", default);
        Assert.Equal("test", creates[^1]["projectId"]!.GetValue<string>());
        Assert.Equal($"user:{owner.Id:D}", creates[^1]["externalReference"]!.GetValue<string>());

        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var session = access.RunSessionAsync(owner, game.Id, player,
            cancellation => Task.Delay(Timeout.Infinite, cancellation), deadline.Token);
        settings.AccessOpen = false;
        await db.SaveChangesAsync();
        var revoked = await Assert.ThrowsAsync<TeamLabApiContractException>(() => session);
        Assert.Contains("环境尚未开放", revoked.Message);
    }
}
