using GZCTF.Agent.Models;
using GZCTF.Agent.Middlewares;
using GZCTF.Agent.Services;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;

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

builder.Services.AddControllers()
    .ConfigureApiBehaviorOptions(options =>
    {
        options.InvalidModelStateResponseFactory = context =>
        {
            var error = AgentCorrelationErrorMiddleware.CreateValidationError(
                context.HttpContext.Request,
                "The Agent request payload is invalid.");
            context.HttpContext.Response.Headers[AgentProtocolHeaders.ErrorCategory] = error.Category;
            context.HttpContext.Response.Headers[AgentProtocolHeaders.ErrorCode] = error.Code;
            context.HttpContext.Response.Headers[AgentProtocolHeaders.Retryable] = "false";
            return new BadRequestObjectResult(error);
        };
    });
builder.Services.AddHttpClient();

var app = builder.Build();

app.UseMiddleware<AgentCorrelationErrorMiddleware>();

app.Use(async (context, next) =>
{
    var config = context.RequestServices.GetRequiredService<IConfiguration>();
    var expectedToken = config.GetSection("Agent:AuthToken").Get<string>() ?? "";
    if (string.IsNullOrEmpty(expectedToken) || expectedToken == "__local__")
    {
        await AgentCorrelationErrorMiddleware.WriteAsync(context,
            new AgentErrorResponse(
                "Authorization", "auth.not_configured", "Agent authentication is not configured.", false,
                $"{context.Request.Method.ToLowerInvariant()}.auth", context.Response.Headers[AgentProtocolHeaders.CorrelationId]!),
            StatusCodes.Status401Unauthorized, context.RequestAborted);
        return;
    }
    var authHeader = context.Request.Headers.Authorization.ToString().Replace("Bearer ", "").Trim();
    if (authHeader != expectedToken)
    {
        await AgentCorrelationErrorMiddleware.WriteAsync(context,
            new AgentErrorResponse(
                "Authorization", "auth.forbidden", "Invalid Agent authentication token.", false,
                $"{context.Request.Method.ToLowerInvariant()}.auth", context.Response.Headers[AgentProtocolHeaders.CorrelationId]!),
            StatusCodes.Status401Unauthorized, context.RequestAborted);
        return;
    }
    await next();
});

app.MapControllers();

var agentConfig = builder.Configuration.GetSection("Agent").Get<AgentConfig>() ?? new();
var url = $"http://0.0.0.0:{agentConfig.ListenPort}";
app.Urls.Add(url);

app.Run();
