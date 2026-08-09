using GZCTF.Agent.Models;
using GZCTF.Agent.Middlewares;
using GZCTF.Agent.Services;
using GZCTF.Agent.Services.Observation;
using GZCTF.Agent.Services.RuntimeSignals;
using GZCTF.Agent.Services.TeamLab;
using GZCTF.Agent.Services.Vm;
using GZCTF.Agent.Services.GuestControl;
using GZCTF.Agent.Services.RemoteAccess;
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
builder.Services.AddSingleton<VmBootstrapService>();
builder.Services.AddSingleton<VmImageBackingChainInspector>();
builder.Services.AddSingleton<VmRuntimeReadinessCoordinator>();
builder.Services.AddHostedService(serviceProvider =>
    serviceProvider.GetRequiredService<VmRuntimeReadinessCoordinator>());
builder.Services.AddSingleton<TeamLabCommandRunner>();
builder.Services.AddSingleton<TeamLabCommandExecutor>();
builder.Services.AddSingleton<TeamLabBridgeService>();
builder.Services.AddSingleton<TeamLabRouterService>();
builder.Services.AddSingleton<TeamLabFirewallService>();
builder.Services.AddSingleton<TeamLabFabricRouteStore>();
builder.Services.AddSingleton<TeamLabFabricService>();
builder.Services.AddHostedService(serviceProvider => serviceProvider.GetRequiredService<TeamLabFabricService>());
builder.Services.AddSingleton<ObservationPointRegistry>();
builder.Services.AddSingleton<ObservationBatchSpool>();
builder.Services.AddHostedService(serviceProvider => serviceProvider.GetRequiredService<ObservationBatchSpool>());
builder.Services.AddSingleton<TeamLabPacketObserver>();
builder.Services.AddHostedService(serviceProvider => serviceProvider.GetRequiredService<TeamLabPacketObserver>());
builder.Services.AddSingleton<EndpointSensorChannelService>();
builder.Services.AddSingleton<PcapSegmentUploader>();
builder.Services.AddSingleton<TeamLabPcapService>();
builder.Services.AddSingleton<TeamLabRuntimeGenerationStore>();
builder.Services.AddSingleton<TeamLabContainerNetworkFinalizeService>();
builder.Services.AddSingleton<AgentRuntimeSignalJournal>();
builder.Services.AddSingleton<AgentRuntimeSignalPublisher>();
builder.Services.AddHostedService<AgentRuntimeSignalPublisherWorker>();
builder.Services.AddSingleton<TeamLabNetworkService>();
builder.Services.AddSingleton<AgentMaintenanceService>();
builder.Services.AddSingleton<AgentCapabilityService>();
builder.Services.AddSingleton<AgentOperationGate>();
builder.Services.AddSingleton<AgentResourceLock>();
builder.Services.AddSingleton<ImageTransferSingleFlight>();
builder.Services.AddSingleton<GuestCertificateAuthority>();
builder.Services.AddSingleton<GuestEnrollmentStore>();
builder.Services.AddSingleton<GuestManagementNetworkService>();
builder.Services.AddSingleton<GuestEventIngestor>();
builder.Services.AddSingleton<AgentOciArtifactUploader>();
builder.Services.AddSingleton<AgentOperationReceiptStore>();
builder.Services.AddSingleton<VmScenarioArtifactService>();
builder.Services.AddSingleton<TeamLabTerminalSessionRegistry>();
builder.Services.AddSingleton<RemoteAccessRelayService>();
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
