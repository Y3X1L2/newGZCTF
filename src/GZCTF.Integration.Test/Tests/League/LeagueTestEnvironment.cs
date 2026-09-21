using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Modules.Identity.Application;
using GZCTF.Modules.Identity.Infrastructure;
using GZCTF.Modules.League.Application;
using GZCTF.Modules.League.Contracts;
using GZCTF.Modules.League.Domain;
using GZCTF.Modules.TeamLab.Domain;
using GZCTF.Modules.TeamLab.Infrastructure;
using GZCTF.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Testcontainers.PostgreSql;
using Xunit;

namespace GZCTF.Integration.Test.Tests.League;

public sealed class LeagueDatabase : IAsyncLifetime
{
    public PostgreSqlContainer Container { get; } = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("league_lxy").WithUsername("postgres").WithPassword("postgres").Build();
    public AppDbContext CreateContext() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseNpgsql(Container.GetConnectionString()).Options) { SuppressProjectionRevisionBumps = true };
    public async Task InitializeAsync()
    {
        await Container.StartAsync();
        await using var db = CreateContext();
        await db.Database.MigrateAsync();
    }
    public Task DisposeAsync() => Container.DisposeAsync().AsTask();
}

// Shared T0 development scenarios: Pending, one failed team, both ready. Never registered in the main application.
public sealed class LeagueTestProviders : ILeagueRuntimePort, ILeagueFlagPort, ILeagueCoinPort
{
    public bool IsAvailable { get; set; } = true;
    public LeagueProgressState PreparationState { get; set; } = LeagueProgressState.Ready;
    public bool BindingConfirmed { get; set; } = true;
    public bool AccessClosed { get; set; } = true;
    public bool Retryable { get; set; } = true;
    public bool Throw { get; set; }
    public LeagueEffectResult OpenResult { get; set; } = new(true);
    public LeagueEffectResult CleanupResult { get; set; } = new(true);
    public HashSet<Guid> Initializations { get; } = [];
    public List<Guid> PreparationCalls { get; } = [];
    public List<Guid> StartCalls { get; } = [];
    public List<Guid> CleanupCalls { get; } = [];
    private readonly Dictionary<(Guid, int), Guid> _runtimes = [];
    private readonly Dictionary<(Guid, int), Guid> _materials = [];
    public Task InitializeAsync(LeagueFrozenMatch match, CancellationToken cancellationToken)
    { Initializations.Add(match.PreparationId); return Task.CompletedTask; }
    public Task<IReadOnlyList<LeagueCoreReference>> PrepareAsync(LeagueFrozenMatch match, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<LeagueCoreReference>>(match.Teams.Select(x =>
        {
            if (!_materials.TryGetValue((match.PreparationId, x.TeamId), out var id))
                _materials[(match.PreparationId, x.TeamId)] = id = Guid.NewGuid();
            return new LeagueCoreReference(x.TeamId, "core", id);
        }).ToArray());
    public Task<bool> ConfirmBindingsAsync(LeagueFrozenMatch match, IReadOnlyList<LeagueRuntimeBinding> bindings, CancellationToken cancellationToken) => Task.FromResult(BindingConfirmed);
    public Task<LeaguePreparationResult> PrepareAsync(LeagueFrozenMatch match, IReadOnlyList<LeagueCoreReference> cores, CancellationToken cancellationToken)
    {
        PreparationCalls.Add(match.PreparationId);
        if (Throw) throw new InvalidOperationException("Provider exception must not be exposed");
        var progress = match.Teams.Select((x, index) =>
        {
            if (!_runtimes.TryGetValue((match.PreparationId, x.TeamId), out var id))
                _runtimes[(match.PreparationId, x.TeamId)] = id = Guid.NewGuid();
            var state = index == 0 ? LeagueProgressState.Ready : PreparationState;
            return new LeagueTeamPreparation(x.TeamId, state, new(x.TeamId, id, 1), state == LeagueProgressState.Ready,
                state == LeagueProgressState.Ready, state == LeagueProgressState.Ready, AccessClosed,
                state == LeagueProgressState.Failed ? LeagueFailure.EnvironmentFailed : LeagueFailure.None, Retryable);
        }).ToArray();
        return Task.FromResult(new LeaguePreparationResult(match.PreparationId, progress));
    }
    public Task<LeagueEffectResult> OpenAccessAsync(LeagueFrozenMatch match, Guid operationId, IReadOnlyList<LeagueRuntimeBinding> bindings, CancellationToken cancellationToken)
    { StartCalls.Add(operationId); return Task.FromResult(OpenResult); }
    public Task<LeagueEffectResult> CleanupAsync(LeagueFrozenMatch match, Guid operationId, CancellationToken cancellationToken)
    { CleanupCalls.Add(operationId); return Task.FromResult(CleanupResult); }
}

internal sealed class LeagueTestEnvironment(LeagueDatabase database)
{
    public LeagueTestProviders Providers { get; } = new();
    public ActorContext Admin { get; private set; } = null!;
    public ActorContext First { get; private set; } = null!;
    public ActorContext Second { get; private set; } = null!;
    public ActorContext Outsider { get; private set; } = null!;
    public int FirstTeamId { get; private set; }
    public int SecondTeamId { get; private set; }
    public Guid MatchId { get; private set; }
    public Guid ReleaseId { get; private set; }
    public LeagueMatchService Service(AppDbContext db, bool enabled = true) => new(db, new(db), new EfTeamMembershipQuery(db),
        new EfTeamLabReleaseCatalog(db), Providers, Providers, Providers, Options.Create(new LeagueOptions { Enabled = enabled }));
    public async Task SeedAsync()
    {
        await using var db = database.CreateContext();
        UserInfo User(Role role) => new() { UserName = Guid.NewGuid().ToString("N")[..12], Role = role };
        var admin = User(Role.Admin); var first = User(Role.Student); var second = User(Role.Student); var outsider = User(Role.Student);
        db.AddRange(admin, first, second, outsider);
        var a = new Team { Name = "first-" + first.Id.ToString("N")[..8], CaptainId = first.Id, Members = [first] };
        var b = new Team { Name = "second-" + second.Id.ToString("N")[..8], CaptainId = second.Id, Members = [second] };
        db.AddRange(a, b);
        var topology = new TeamLabTopology { Name = "League test", OwnerUserId = admin.Id };
        var release = new TeamLabTopologyRelease { Topology = topology, Version = 1, CanonicalJson = "{}", ContentHash = "league-test" };
        db.Add(release); await db.SaveChangesAsync();
        Admin = new(admin.Id, admin.Role); First = new(first.Id, first.Role); Second = new(second.Id, second.Role); Outsider = new(outsider.Id, outsider.Role);
        FirstTeamId = a.Id; SecondTeamId = b.Id; ReleaseId = release.Id;
        var detail = await Service(db).CreateAsync(new("League test", topology.PublicId, release.Id, 100), Admin, default);
        MatchId = detail.Match.Id;
    }
    public async Task ConfigureAsync()
    {
        await using var db = database.CreateContext(); var service = Service(db);
        await service.RegisterAsync(MatchId, FirstTeamId, First, default);
        await service.RegisterAsync(MatchId, SecondTeamId, Second, default);
        await service.ReviewAsync(MatchId, FirstTeamId, LeagueRegistrationState.Approved, Admin, default);
        var detail = await service.ReviewAsync(MatchId, SecondTeamId, LeagueRegistrationState.Approved, Admin, default);
        await service.SelectTeamsAsync(MatchId, new(detail.Match.Revision, FirstTeamId, SecondTeamId), Admin, default);
    }
    public async Task<LeagueMatchDetail> PrepareAsync()
    {
        await using var db = database.CreateContext(); var service = Service(db);
        var detail = await service.GetAsync(MatchId, Admin, default);
        return await service.PrepareAsync(MatchId, detail.Match.Revision, Admin, default);
    }
    public async Task ProcessAsync()
    {
        await using var db = database.CreateContext();
        await new LeagueLifecycleService(db, new(db), Providers, Providers, Providers).ProcessAsync(MatchId, default);
    }
    public async Task<LeagueMatchDetail> DetailAsync(ActorContext? actor = null)
    { await using var db = database.CreateContext(); return await Service(db).GetAsync(MatchId, actor ?? Admin, default); }
    public async Task RunAsync()
    {
        await ConfigureAsync(); await PrepareAsync(); await ProcessAsync();
        await using var db = database.CreateContext(); await Service(db).StartAsync(MatchId, Admin, default);
        await ProcessAsync();
    }
    public async Task<LeagueKnockoutCommand> KnockoutAsync(bool first = true)
    {
        var detail = await DetailAsync();
        await using var db = database.CreateContext();
        var frozen = await Service(db).GetFrozenAsync(MatchId, default);
        return new(MatchId, Guid.NewGuid(), (first ? First : Second).UserId!.Value,
            first ? FirstTeamId : SecondTeamId, frozen.PreparationId,
            detail.Preparation.Single(x => x.TeamId == (first ? SecondTeamId : FirstTeamId)).Binding!);
    }
}
