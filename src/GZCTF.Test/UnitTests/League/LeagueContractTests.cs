using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading.Tasks;
using GZCTF.Extensions;
using GZCTF.Modules.League.Application;
using GZCTF.Modules.League.Contracts;
using GZCTF.Modules.League.Infrastructure;
using Xunit;

namespace GZCTF.Test.UnitTests.League;

public sealed class LeagueContractTests
{
    [Fact]
    public async Task DefaultProviders_CannotFabricateSuccessfulOperations()
    {
        var provider = new UnavailableLeagueProviders();
        Assert.False(provider.IsAvailable);
        var error = await Assert.ThrowsAsync<LeagueException>(() => provider.CleanupAsync(null!, Guid.NewGuid(), default));
        Assert.Equal(503, error.StatusCode); Assert.Equal("league_dependency_unavailable", error.Code);
        Assert.False(new LeagueOptions().Enabled);
    }

    [Fact]
    public async Task FrontendExamples_UseTheSameDtosAndSerializationAsTheServer()
    {
        var matchId = Guid.Parse("10000000-0000-4000-8000-000000000001");
        var op = Guid.Parse("10000000-0000-4000-8000-000000000002");
        var user = Guid.Parse("10000000-0000-4000-8000-000000000003");
        var other = Guid.Parse("10000000-0000-4000-8000-000000000004");
        var time = DateTimeOffset.FromUnixTimeMilliseconds(1790000000000);
        var examples = new Dictionary<string, LeagueMatchDetail>();
        foreach (var state in new[] { LeagueProgressState.Pending, LeagueProgressState.Failed, LeagueProgressState.Ready })
        {
            var ready = state == LeagueProgressState.Ready;
            var failed = state == LeagueProgressState.Failed;
            examples[state.ToString().ToLowerInvariant()] = new(
                new(matchId, "双港调度争夺（开发样例）", ready ? LeagueMatchState.Ready : LeagueMatchState.Preparing, 8, time, null, null),
                Guid.Parse("20000000-0000-4000-8000-000000000001"), Guid.Parse("20000000-0000-4000-8000-000000000002"), 100, 7,
                [new(11, "示例队一", LeagueRegistrationState.Approved, true, 1, [user]), new(22, "示例队二", LeagueRegistrationState.Approved, true, 2, [other])],
                [new(11, LeagueProgressState.Ready, new(11, Guid.Parse("30000000-0000-4000-8000-000000000001"), 1), true, true, true, true),
                 new(22, state, ready ? new(22, Guid.Parse("30000000-0000-4000-8000-000000000002"), 1) : null, ready, ready, ready, true,
                     failed ? LeagueFailure.EnvironmentFailed : LeagueFailure.None)],
                new(op, failed ? LeagueProgressState.Failed : ready ? LeagueProgressState.Ready : LeagueProgressState.Running,
                    failed ? LeagueFailure.EnvironmentFailed : LeagueFailure.None, true, 1), null,
                ready ? ["start", "abort"] : failed ? ["abort", "retry"] : ["abort"]);
        }
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };
        options.ConfigCustomSerializerOptions();
        options.TypeInfoResolverChain.Add(new DefaultJsonTypeInfoResolver());
        var json = JsonSerializer.Serialize(examples, options).Replace("\r\n", "\n") + "\n";
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))) directory = directory.Parent;
        Assert.NotNull(directory);
        var path = Path.Combine(directory.FullName, "docs", "development", "league", "phase1-examples.json");
        if (Environment.GetEnvironmentVariable("UPDATE_LEAGUE_EXAMPLES") == "1") await File.WriteAllTextAsync(path, json);
        Assert.Equal(json, (await File.ReadAllTextAsync(path)).Replace("\r\n", "\n"));
        var roundTrip = JsonSerializer.Deserialize<Dictionary<string, LeagueMatchDetail>>(json, options)!;
        Assert.Equal(2, roundTrip["ready"].Preparation.Count(x => x.State == LeagueProgressState.Ready));
        Assert.Null(roundTrip["pending"].Match.StartedAt);
        Assert.Equal(LeagueFailure.EnvironmentFailed, roundTrip["failed"].Operation!.Failure);
    }
}
