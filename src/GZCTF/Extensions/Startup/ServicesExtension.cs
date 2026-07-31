using System.Net.Mime;
using GZCTF.Composition;
using GZCTF.Middlewares;
using GZCTF.Modules.Identity.Infrastructure;
using GZCTF.Modules.Audit.Infrastructure;
using GZCTF.Infrastructure.Api;
using GZCTF.Infrastructure.Cache;
using GZCTF.Infrastructure.Concurrency;
using GZCTF.Infrastructure.Telemetry;
using GZCTF.Modules.Runtime.Application;
using GZCTF.Modules.Runtime.Infrastructure;
using GZCTF.Models.Internal;
using GZCTF.Repositories;
using GZCTF.Repositories.Interface;
using GZCTF.Services;
using GZCTF.Services.Config;

using GZCTF.Services.Container;
using GZCTF.Services.CronJob;

using GZCTF.Services.Fleet;
using GZCTF.Services.Mail;
using GZCTF.Services.TeamLab;
using GZCTF.Services.Transfer;
using GZCTF.Services.Vm;
using GZCTF.Storage;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using NSwag;
using NSwag.Generation.Processors.Security;

namespace GZCTF.Extensions.Startup;

internal static class ServicesExtension
{
    extension(WebApplicationBuilder builder)
    {
        private void AddConfig<TConfig>()
            where TConfig : class
            => builder.Services.Configure<TConfig>(builder.Configuration.GetSection(typeof(TConfig).Name));

        internal void AddServiceConfigurations()
        {
            builder.AddConfig<EmailConfig>();
            builder.AddConfig<AccountPolicy>();
            builder.AddConfig<GlobalConfig>();
            builder.AddConfig<ManagedConfig>();
            builder.AddConfig<PortalSsoConfig>();
            builder.AddConfig<ContainerPolicy>();
            builder.AddConfig<ContainerProvider>();
            builder.AddConfig<TeamLabNetworkConfig>();
            builder.AddConfig<PublicUdpGatewayConfig>();
            builder.AddConfig<KvmSettings>();
            builder.AddConfig<GuacamoleSettings>();
            builder.AddConfig<DockerRegistrySettings>();
            builder.AddConfig<RuntimeSchedulingOptions>();

            builder.Services.Configure<RegistrySet<RegistryConfig>>(builder.Configuration.GetSection("Registries"));

            var oldConfig = builder.Configuration.GetSection(nameof(RegistryConfig)).Get<RegistryConfig>();
            if (!string.IsNullOrWhiteSpace(oldConfig?.ServerAddress))
                // Add old config to new config set
                builder.Services.Configure<RegistrySet<RegistryConfig>>(set =>
                {
                    if (!set.TryAdd(oldConfig.ServerAddress, oldConfig))
                        set[oldConfig.ServerAddress] = oldConfig;
                });

            var forwardedOptions =
                builder.Configuration.GetSection(nameof(ForwardedOptions)).Get<ForwardedOptions>();
            if (forwardedOptions is null)
                builder.Services.Configure<ForwardedHeadersOptions>(options =>
                {
                    options.ForwardedHeaders =
                        ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
                });
            else
                builder.Services.Configure<ForwardedHeadersOptions>(forwardedOptions.ToForwardedHeadersOptions);
        }

        internal void AddCustomServices()
        {
            builder.Services.AddCaptchaService(builder.Configuration);
            builder.Services.AddContainerService(builder.Configuration);

            builder.Services.AddScoped<IConfigService, ConfigService>();
            builder.Services.AddScoped<ILogRepository, LogRepository>();
            builder.Services.AddScoped<IBlobRepository, BlobRepository>();
            builder.Services.AddScoped<IPostRepository, PostRepository>();
            builder.Services.AddScoped<IGameRepository, GameRepository>();
            builder.Services.AddScoped<ITeamRepository, TeamRepository>();
            builder.Services.AddScoped<IContainerRepository, ContainerRepository>();
            builder.Services.AddScoped<IGameEventRepository, GameEventRepository>();
            builder.Services.AddScoped<ICheatInfoRepository, CheatInfoRepository>();
            builder.Services.AddScoped<IGameNoticeRepository, GameNoticeRepository>();
            builder.Services.AddScoped<ISubmissionRepository, SubmissionRepository>();
            builder.Services.AddScoped<IGameInstanceRepository, GameInstanceRepository>();
            builder.Services.AddScoped<IGameChallengeRepository, GameChallengeRepository>();
            builder.Services.AddScoped<IExerciseInstanceRepository, ExerciseInstanceRepository>();
            builder.Services.AddScoped<IExerciseChallengeRepository, ExerciseChallengeRepository>();
            builder.Services.AddScoped<IParticipationRepository, ParticipationRepository>();
            builder.Services.AddScoped<IDivisionRepository, DivisionRepository>();
            builder.Services.AddScoped<IAwdpRepository, AwdpRepository>();
            builder.Services.AddScoped<DockerImageRegistryService>();
            builder.Services.AddScoped<VmImageRegistryService>();
            builder.Services.AddScoped<VmArtifactStore>();

            builder.Services.AddScoped<AwdpScriptRunner>();
            builder.Services.AddScoped<AwdpInstanceService>();
            builder.Services.AddScoped<AwdpCheckerService>();
            builder.Services.AddScoped<AwdpScoreService>();
            builder.Services.AddScoped<AwdpPatchService>();
            builder.Services.AddSingleton<AwdpRoundService>();
            builder.Services.AddHostedService(sp => sp.GetRequiredService<AwdpRoundService>());

            builder.Services.AddScoped<ExcelHelper>();
            builder.Services.AddScoped<GameExportService>();
            builder.Services.AddScoped<GameImportService>();

            builder.Services.AddChannel<Submission>();
            builder.Services.AddSingleton<CachePolicyCatalog>();
            builder.Services.AddScoped<IProjectionRevisionStore, ProjectionRevisionStore>();
            builder.Services.AddScoped<IPlatformCache, PlatformCache>();
            builder.Services.AddSingleton<IMailSender, MailSender>();

            builder.Services.AddSingleton<ImageStorage>();
            builder.Services.AddSingleton<ContainerOrchestrator>();
            builder.Services.AddSingleton<IVirtualMachineProvider, KvmProvider>();
            builder.Services.AddScoped<LocalImageImporter>();
            builder.Services.AddScoped<IArchiveExtractor, ArchiveExtractor>();

            builder.Services.AddScoped<GamePhaseService>();
            builder.Services.AddScoped<TheoryExamService>();
            builder.Services.AddScoped<PortalSsoService>();

            builder.Services.AddHostedService<FlagChecker>();

            // Fleet control-plane services
            builder.Services.AddScoped<INodeRepository, NodeRepository>();
            builder.Services.AddScoped<NodeDeployService>();
            builder.Services.AddScoped<NodeCapacitySnapshotService>();
            builder.Services.AddScoped<NodeEligibilityEvaluator>();
            builder.Services.AddScoped<RuntimeQueueSelector>();
            builder.Services.AddScoped<RuntimeAdmissionPolicy>();
            builder.Services.AddScoped<TeamLabPhysicalPlacementService>();
            builder.Services.AddScoped<FleetCapacityReservationService>();
            builder.Services.AddScoped<DeploymentQueueService>();
            builder.Services.AddScoped<DeploymentQueueViewService>();
            builder.Services.AddScoped<DeploymentExecutionContextAccessor>();
            builder.Services.AddScoped<DeploymentExecutionService>();
            builder.Services.AddScoped<RuntimeTicketLifecycleDispatcher>();
            builder.Services.AddScoped<RuntimeSchedulingService>();
            builder.Services.AddScoped<RuntimeFactReconciliationService>();
            builder.Services.AddSingleton<RuntimeExecutionService>();
            builder.Services.AddSingleton<NodeDispatchLimiter>();
            builder.Services.AddSingleton<ImageDistributionCoordinator>();
            builder.Services.AddScoped<ImageDistributionService>();
            builder.Services.AddHostedService<ImageDistributionWorker>();
            builder.Services.AddHostedService<ImageDistributionReconcileService>();
            builder.Services.AddHostedService<FleetHealthCheckService>();
            builder.Services.AddHostedService<RuntimeSchedulingWorker>();
            builder.Services.AddHostedService<RuntimeRecoveryWorker>();
            builder.Services.AddHostedService<RuntimeExecutionWorker>();
            builder.Services.AddHostedService<RuntimeTelemetrySnapshotWorker>();

#pragma warning disable EXTEXP0001
            builder.Services.AddTransient<AgentTelemetryHandler>();
            builder.Services.AddHttpClient("Agent", client =>
                {
                    client.Timeout = TimeSpan.FromMinutes(10);
                })
                .AddHttpMessageHandler<AgentTelemetryHandler>()
                .RemoveAllResilienceHandlers();
#pragma warning restore EXTEXP0001
            builder.Services.AddSingleton<AgentClient>();
            builder.Services.AddScoped<FleetVmService>();
            builder.Services.AddSingleton<VmCredentialService>();
            builder.Services.AddSingleton<GuacamoleService>();
            builder.Services.AddSingleton<GuacamoleRemoteSessionService>();
            builder.Services.AddHostedService<VmReadyService>();
            builder.Services.AddScoped<NodeTunnelService>();
            builder.Services.AddScoped<IPublicUdpGatewayProvider, PublicUdpGatewayProvider>();

            builder.Services.AddSingleton<RedisDistributedLeaseProvider>();
            builder.Services.AddSingleton<LocalDevelopmentLeaseProvider>();
            builder.Services.AddSingleton<IDistributedLeaseProvider>(serviceProvider =>
                serviceProvider.GetRequiredService<IOptions<RedisRuntimeOptions>>().Value.Mode ==
                RedisRuntimeMode.Distributed
                    ? serviceProvider.GetRequiredService<RedisDistributedLeaseProvider>()
                    : serviceProvider.GetRequiredService<LocalDevelopmentLeaseProvider>());

            // Nginx proxy: port allocation service (Redis-backed with local fallback)
            builder.Services.AddSingleton<IPortAllocationService, PortAllocationService>();
            builder.Services.AddSingleton<NginxSyncService>();
            builder.Services.AddSingleton<INginxProxySyncService>(sp => sp.GetRequiredService<NginxSyncService>());
            builder.Services.AddHostedService(sp => sp.GetRequiredService<NginxSyncService>());
            builder.Services.AddHostedService<PortLeaseRefreshService>();

            builder.Services.AddHostedService<CronJobService>();
            builder.Services.AddHostedService<LocalNodeRegistrar>();
            builder.Services.AddHostedService<LocalNodeMetricsService>();
            builder.Services.AddPlatformModules(builder.Configuration);
        }

        internal void AddWebServices()
        {
            builder.Services.AddRouting(options => options.LowercaseUrls = true);
            builder.Services.AddRateLimiter(RateLimiter.ConfigureRateLimiter);
            builder.Services.AddResponseCompression(options =>
            {
                options.Providers.Add<ZStandardCompressionProvider>();
                options.Providers.Add<BrotliCompressionProvider>();
                options.Providers.Add<GzipCompressionProvider>();
                options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(
                    [
                        // See others in ResponseCompressionDefaults.MimeTypes
                        MediaTypeNames.Application.Pdf
                    ]
                );
                options.EnableForHttps = true;
            });

            builder.Services.AddControllersWithViews()
                .AddMvcOptions(options => options.Filters.AddService<AdminMutationAuditFilter>())
                .ConfigureApiBehaviorOptions(options =>
            {
                options.InvalidModelStateResponseFactory = InvalidModelStateHandler;
            }).AddDataAnnotationsLocalization(options =>
            {
                options.DataAnnotationLocalizerProvider = (_, factory) =>
                    factory.Create(typeof(Resources.Program));
            }).AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.ConfigCustomSerializerOptions();
            });
            builder.Services.AddHttpClient("GuacamoleClient", client =>
            {
                client.DefaultRequestHeaders.Accept.Clear();
                client.DefaultRequestHeaders.Accept.Add(
                    new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
            });
            builder.Services.AddHttpClient("PortalSso", client =>
            {
                client.DefaultRequestHeaders.Accept.Clear();
                client.DefaultRequestHeaders.Accept.Add(
                    new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
            });

            builder.Services.AddResponseCaching();
        }

        internal void AddOpenApiServices()
        {
            if (builder.Environment.IsDevelopment())
            {
                builder.Services.AddOpenApiDocument(settings =>
                {
                    settings.DocumentName = "v1";
                    settings.Version = "v1";
                    settings.Title = "YINYU CTF Platform API";
                    settings.Description = "YINYU CTF Platform internal API document";
                    settings.UseControllerSummaryAsTagDescription = true;
                    settings.SchemaSettings.TypeMappers.Add(new OpenApiDateTimeOffsetToUIntMapper());
                    settings.SchemaSettings.TypeMappers.Add(new OpenApiIPAddressToStringMapper());
                    settings.SchemaSettings.ReflectionService = new GenericsSystemTextJsonReflectionService();
                });
            }

            builder.Services.AddOpenApiDocument(settings =>
            {
                settings.DocumentName = "open-v1";
                settings.ApiGroupNames = ["open-v1"];
                settings.Version = "v1";
                settings.Title = "YINYU CTF Platform Open API";
                settings.Description = "YINYU CTF Platform external API contract";
                settings.UseControllerSummaryAsTagDescription = true;
                settings.SchemaSettings.TypeMappers.Add(new OpenApiDateTimeOffsetToUIntMapper());
                settings.SchemaSettings.TypeMappers.Add(new OpenApiIPAddressToStringMapper());
                settings.SchemaSettings.ReflectionService = new GenericsSystemTextJsonReflectionService();
                settings.AddSecurity(ApiTokenDefaults.Scheme, new OpenApiSecurityScheme
                {
                    Type = OpenApiSecuritySchemeType.Http,
                    Scheme = "bearer",
                    BearerFormat = "GZCTF API token",
                    Description = "Scoped GZCTF API token"
                });
                settings.OperationProcessors.Add(
                    new AspNetCoreOperationSecurityScopeProcessor(ApiTokenDefaults.Scheme));
                settings.OperationProcessors.Add(new OpenApiContractOperationProcessor());
            });
        }
    }
}
