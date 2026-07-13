using GZCTF.Agent.Models;
using GZCTF.Agent.Services;
using Microsoft.AspNetCore.Http.Features;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<AgentConfig>(builder.Configuration.GetSection("Agent"));
builder.Services.Configure<DockerConfig>(builder.Configuration.GetSection("Docker"));
builder.Services.Configure<KvmConfig>(builder.Configuration.GetSection("Kvm"));
builder.Services.Configure<AgentTeamLabConfig>(builder.Configuration.GetSection("TeamLab"));

builder.Services.AddSingleton<DockerService>();
builder.Services.AddSingleton<KvmService>();
builder.Services.AddSingleton<TeamLabCommandRunner>();
builder.Services.AddSingleton<TeamLabNetworkService>();
builder.Services.AddSingleton<AgentMaintenanceService>();
builder.Services.AddSingleton<AgentCapabilityService>();
builder.Services.AddSingleton<AgentOperationGate>();
builder.Services.AddSingleton<AgentResourceLock>();
builder.Services.AddSingleton<ImageTransferSingleFlight>();
builder.Services.AddHostedService<HeartbeatWorker>();

builder.Services.AddControllers();
builder.Services.AddHttpClient();

var app = builder.Build();

app.Use(async (context, next) =>
{
    var config = context.RequestServices.GetRequiredService<IConfiguration>();
    var expectedToken = config.GetSection("Agent:AuthToken").Get<string>() ?? "";
    if (string.IsNullOrEmpty(expectedToken) || expectedToken == "__local__")
    {
        context.Response.StatusCode = 401;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync("{\"message\":\"Agent auth not configured. Set Agent:AuthToken in appsettings.json\"}");
        return;
    }
    var authHeader = context.Request.Headers.Authorization.ToString().Replace("Bearer ", "").Trim();
    if (authHeader != expectedToken)
    {
        context.Response.StatusCode = 401;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync("{\"message\":\"Invalid auth token\"}");
        return;
    }
    await next();
});

app.MapControllers();

var agentConfig = builder.Configuration.GetSection("Agent").Get<AgentConfig>() ?? new();
var url = $"http://0.0.0.0:{agentConfig.ListenPort}";
app.Urls.Add(url);

app.Run();
