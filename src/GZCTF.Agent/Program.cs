using GZCTF.Agent.Models;
using GZCTF.Agent.Middlewares;
using GZCTF.Agent.Services;
using GZCTF.Agent.Services.Vm;
using GZCTF.Agent.Services.GuestControl;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using System.Net;

var builder = WebApplication.CreateBuilder(args);
var agentConfig = builder.Configuration.GetSection("Agent").Get<AgentConfig>() ?? new AgentConfig();

builder.WebHost.ConfigureKestrel(kestrel =>
{
    kestrel.ListenAnyIP(agentConfig.ListenPort);
    if (!agentConfig.GuestManagement.Enabled) return;
    var certificateAuthority = new GuestCertificateAuthority(Options.Create(agentConfig));
    kestrel.Listen(IPAddress.Parse(agentConfig.GuestManagement.HostAddress),
        agentConfig.GuestManagement.ListenPort,
        listener => listener.UseHttps(https =>
        {
            https.ServerCertificate = certificateAuthority.GetServerCertificate();
            https.ServerCertificateChain =
                new System.Security.Cryptography.X509Certificates.X509Certificate2Collection(
                    certificateAuthority.GetAuthorityCertificate());
            https.ClientCertificateMode = ClientCertificateMode.AllowCertificate;
            https.ClientCertificateValidation = static (_, _, _) => true;
        }));
});

builder.Services.Configure<AgentConfig>(builder.Configuration.GetSection("Agent"));
builder.Services.Configure<DockerConfig>(builder.Configuration.GetSection("Docker"));
builder.Services.Configure<KvmConfig>(builder.Configuration.GetSection("Kvm"));
builder.Services.Configure<AgentTeamLabConfig>(builder.Configuration.GetSection("TeamLab"));

builder.Services.AddSingleton<DockerService>();
builder.Services.AddSingleton<KvmService>();
builder.Services.AddSingleton<VmGuestAgentService>();
builder.Services.AddSingleton<IVmGuestAgentClient>(provider => provider.GetRequiredService<VmGuestAgentService>());
builder.Services.AddSingleton<VmBootstrapService>();
builder.Services.AddSingleton<VmImageBackingChainInspector>();
builder.Services.AddSingleton<HostCommandRunner>();
builder.Services.AddSingleton<AgentMaintenanceService>();
builder.Services.AddSingleton<AgentCapabilityService>();
builder.Services.AddSingleton<AgentOperationGate>();
builder.Services.AddSingleton<AgentResourceLock>();
builder.Services.AddSingleton<ImageTransferSingleFlight>();
builder.Services.AddSingleton<AgentImageStorageBudget>();
builder.Services.AddSingleton<AgentImageDownloadWriter>();
builder.Services.AddSingleton<GuestCertificateAuthority>();
builder.Services.AddSingleton<GuestEnrollmentStore>();
builder.Services.AddSingleton<GuestManagementNetworkService>();
builder.Services.AddSingleton<GuestEventIngestor>();
builder.Services.AddSingleton<AgentOciArtifactUploader>();
builder.Services.AddSingleton<AgentOperationReceiptStore>();
builder.Services.AddSingleton<VmScenarioArtifactService>();
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
app.UseMiddleware<AgentEndpointAuthenticationMiddleware>();
app.UseWebSockets();

app.MapControllers();

app.Run();
