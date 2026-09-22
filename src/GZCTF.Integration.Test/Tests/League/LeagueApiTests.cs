using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using GZCTF.Extensions;
using GZCTF.Extensions.Startup;
using GZCTF.Infrastructure.Api;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Modules.Identity.Contracts;
using GZCTF.Modules.Identity.Infrastructure;
using GZCTF.Modules.Audit.Application;
using GZCTF.Modules.Audit.Infrastructure;
using GZCTF.Modules.League;
using GZCTF.Modules.League.Application;
using GZCTF.Modules.League.Contracts;
using GZCTF.Modules.League.Infrastructure;
using GZCTF.Modules.TeamLab.Contracts;
using GZCTF.Modules.TeamLab.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Extensions.Hosting;
using Xunit;

namespace GZCTF.Integration.Test.Tests.League;

public sealed class LeagueApiTests(LeagueDatabase database) : IClassFixture<LeagueDatabase>
{
    [Fact]
    public async Task Http_UsesRealIdentityAndProblemDetails_AndExportsFrontendContract()
    {
        var env = new LeagueTestEnvironment(database); await env.SeedAsync();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        { ApplicationName = typeof(Program).Assembly.FullName, EnvironmentName = Environments.Development });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders(); builder.Logging.AddConsole().SetMinimumLevel(LogLevel.Error);
        builder.Services.AddDbContext<AppDbContext>(o => o.UseNpgsql(database.Container.GetConnectionString()));
        builder.Services.AddIdentityCore<UserInfo>().AddEntityFrameworkStores<AppDbContext>();
        builder.Services.AddLocalization();
        builder.Services.AddSingleton<IDiagnosticContext>(new DiagnosticContext(new LoggerConfiguration().CreateLogger()));
        builder.Services.AddAuthentication("league-test").AddScheme<AuthenticationSchemeOptions, LeagueTestAuthentication>("league-test", _ => { });
        builder.Services.AddAuthorization();
        builder.Services.AddScoped<OperationalCorrelation>();
        builder.Services.AddScoped<IOperationalEventWriter, EfOperationalEventWriter>();
        builder.Services.AddScoped<AdminMutationAuditFilter>();
        builder.Services.AddControllers(o => o.Filters.AddService<AdminMutationAuditFilter>()).AddApplicationPart(typeof(Program).Assembly)
            .AddJsonOptions(o => o.JsonSerializerOptions.ConfigCustomSerializerOptions());
        builder.Services.AddScoped<ITeamMembershipQuery, EfTeamMembershipQuery>();
        builder.Services.AddScoped<ITeamLabReleaseCatalog, EfTeamLabReleaseCatalog>();
        builder.Services.AddLeagueModule(builder.Configuration);
        builder.Services.RemoveAll<ILeagueRuntimePort>();
        builder.Services.RemoveAll<ILeagueAttackAccessPort>();
        builder.Services.AddScoped<LeagueUnavailableTestProvider>();
        builder.Services.AddScoped<ILeagueRuntimePort>(provider =>
            provider.GetRequiredService<LeagueUnavailableTestProvider>());
        builder.Services.AddScoped<ILeagueAttackAccessPort>(provider =>
            provider.GetRequiredService<LeagueUnavailableTestProvider>());
        builder.Services.Configure<LeagueOptions>(o => o.Enabled = true);
        // No automatic background execution in this HTTP contract test.
        var worker = builder.Services.Single(x => x.ServiceType == typeof(IHostedService) && x.ImplementationType == typeof(LeagueLifecycleWorker));
        builder.Services.Remove(worker);
        builder.AddOpenApiServices();
        await using var app = builder.Build();
        app.UseMiddleware<ExternalApiExceptionHandler>(); app.UseAuthentication(); app.UseAuthorization();
        app.MapControllers(); app.MapOpenApiDocumentation(); await app.StartAsync();
        using var client = app.GetTestClient();
        var route = $"/api/league/matches/{env.MatchId:D}";
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(route)).StatusCode);
        client.DefaultRequestHeaders.Add("X-League-Test-User", env.First.UserId!.Value.ToString());
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/league/matches", new LeagueDraftModel("denied", null, null, 0))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(route + "/registrations", new LeagueRegisterModel(env.FirstTeamId))).StatusCode);
        client.DefaultRequestHeaders.Remove("X-League-Test-User"); client.DefaultRequestHeaders.Add("X-League-Test-User", env.Admin.UserId!.Value.ToString());
        var created = await client.PostAsJsonAsync("/api/league/matches", new LeagueDraftModel("HTTP draft", null, null, 0));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode); Assert.NotNull(created.Headers.Location);
        using var detail = JsonDocument.Parse(await (await client.GetAsync(route)).Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.Number, detail.RootElement.GetProperty("match").GetProperty("createdAt").ValueKind);
        var unavailable = await client.PostAsJsonAsync(route + "/prepare", new LeagueRevisionModel(detail.RootElement.GetProperty("match").GetProperty("revision").GetInt32()));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, unavailable.StatusCode);
        using var error = JsonDocument.Parse(await unavailable.Content.ReadAsStringAsync());
        Assert.Equal("league_dependency_unavailable", error.RootElement.GetProperty("code").GetString());
        await using (var auditDb = database.CreateContext())
            Assert.True(await auditDb.OperationalEvents.AnyAsync(x => x.ResourceType == "league-match" && x.ResourceId == env.MatchId.ToString()));
        var openapi = await client.GetStringAsync("/openapi/v1.json");
        using var document = JsonDocument.Parse(openapi);
        Assert.True(document.RootElement.GetProperty("paths").TryGetProperty("/api/league/matches", out _));
        if (Environment.GetEnvironmentVariable("LEAGUE_INTERNAL_OPENAPI_PATH") is { Length: > 0 } output)
            await File.WriteAllTextAsync(output, openapi);
    }
}

internal sealed class LeagueUnavailableTestProvider : ILeagueRuntimePort, ILeagueAttackAccessPort
{
    public bool IsAvailable => false;

    private static LeagueException Unavailable() =>
        new("league_dependency_unavailable", "联赛测试依赖未接入。", 503);

    public Task<LeaguePreparationResult> PrepareAsync(
        LeagueFrozenMatch match,
        IReadOnlyList<LeagueCoreReference> cores,
        CancellationToken cancellationToken) => throw Unavailable();

    public Task<LeagueEffectResult> OpenAccessAsync(
        LeagueFrozenMatch match,
        Guid operationId,
        IReadOnlyList<LeagueRuntimeBinding> bindings,
        CancellationToken cancellationToken) => throw Unavailable();

    public Task<LeagueEffectResult> CleanupAsync(
        LeagueFrozenMatch match,
        Guid operationId,
        CancellationToken cancellationToken) => throw Unavailable();

    public Task<LeagueAttackAccessGrant> CreateAsync(
        Guid matchId,
        Guid userId,
        CancellationToken cancellationToken) => throw Unavailable();

    public Task<LeagueAccessConfiguration> ConsumeAsync(
        Guid matchId,
        Guid grantId,
        string token,
        Guid userId,
        CancellationToken cancellationToken) => throw Unavailable();
}

public sealed class LeagueTestAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Guid.TryParse(Request.Headers["X-League-Test-User"], out var user))
            return Task.FromResult(AuthenticateResult.NoResult());
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, user.ToString())], Scheme.Name));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
    }
}
